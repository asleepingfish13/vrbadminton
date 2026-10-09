using UnityEngine;

namespace ViveShuttleVR
{
    public struct RacketContact
    {
        public Vector3 point, center, centerVelocity, angularVelocity;
        public Quaternion faceRotation;
        // The swept geometry supplies the face side occupied BEFORE contact.
        // Never infer this side from a later tracking-velocity sample.
        public Vector3 surfaceNormal;
        public float recentPeakSpeed;
        public Vector3 PointVelocity { get { return centerVelocity + Vector3.Cross(angularVelocity,point-center); } }
        public Vector3 Normal { get { return faceRotation*Vector3.forward; } }
    }

    public struct ImpactResult
    {
        public bool hit;
        public Vector3 outgoingVelocity, angularVelocity;
        public Vector3 incomingVelocity, resolvedNormal;
        public float swingSpeed, normalSwing, brushSpeed, sliceRatio, quality, normalizedOffset, closingSpeed;
        // Reduced-order contact telemetry, not a measured real string-bed map.
        public float contactDuration, maxIndentation, slidingFraction, wrapAngle, axialSpin;
        public float effectiveRacketMass;
    }

    public static class RacketImpact
    {
        private const int MaxContactSteps = 64;
        private const float MaxContactDuration = .005f;

        public static ImpactResult Resolve(ShuttleState incoming, RacketContact contact, ShuttlePhysicsSettings settings)
        {
            Vector3 rawPointVelocity=contact.PointVelocity;
            // The assist is explicitly a powered racket-speed multiplier. It is
            // disabled in strict mode and leaves gentle net touches unchanged.
            float gain=settings.seatedAssist ? Mathf.Lerp(1f,settings.swingGain,
                Mathf.InverseLerp(1.5f,5f,rawPointVelocity.magnitude)) : 1f;
            Vector3 tail=(incoming.orientation*Vector3.up).normalized;
            bool geometryNormal=contact.surfaceNormal.sqrMagnitude>.5f;
            Vector3 normal=geometryNormal ? contact.surfaceNormal.normalized : contact.Normal.normalized;
            Vector3 racketVelocity=rawPointVelocity*gain;
            if (!geometryNormal && Vector3.Dot(incoming.velocity-racketVelocity,normal)>0f) normal=-normal;
            Vector3 initialNormal=normal;
            // Rounded cork: the string force acts on the near spherical surface,
            // not only at the cork centre. An oblique surface can generate axial
            // spin; no technique label or court direction supplies an extra torque.
            Vector3 corkCenterArm=-tail*.022f;
            Vector3 corkArm=corkCenterArm-normal*ShuttlePhysicsSettings.CorkRadius;
            Vector3 relative=incoming.velocity+Vector3.Cross(incoming.angularVelocity,corkArm)-racketVelocity;
            float closing=-Vector3.Dot(relative,normal);
            Vector3 local=Quaternion.Inverse(contact.faceRotation)*(contact.point-contact.center);
            float radius=settings.RacketModel.NormalizedFaceRadius(new Vector2(local.x,local.y));
            float quality=Mathf.Clamp01(1f-settings.SweetSpotFalloff*radius*radius);
            ImpactResult result=new ImpactResult {
                incomingVelocity=incoming.velocity, resolvedNormal=normal,
                swingSpeed=rawPointVelocity.magnitude, closingSpeed=closing,
                normalizedOffset=radius, quality=quality,
                normalSwing=Mathf.Abs(Vector3.Dot(rawPointVelocity,normal)),
                brushSpeed=Vector3.ProjectOnPlane(rawPointVelocity,normal).magnitude
            };
            result.sliceRatio=result.brushSpeed/(result.normalSwing+.25f);
            if (closing<.05f || radius>1.35f || !Finite(relative) || !Finite(normal) ||
                !Finite(incoming.angularVelocity) || !Finite(contact.angularVelocity) || normal.sqrMagnitude<.5f)
                return result;

            float mass=Mathf.Max(.001f,settings.mass);
            // Published total frame weight is not a point mass at the strings.
            // Estimate I/r^2 about the grip, including lower torsional support for
            // lateral mishits; the scalar is frozen during this brief contact.
            float racketMass=settings.RacketContactMass(local,Quaternion.Inverse(contact.faceRotation)*normal);
            result.effectiveRacketMass=racketMass;
            float inverseMass=1f/mass+1f/racketMass;
            float inertiaScale=Mathf.Max(.2f,settings.InertiaScale);
            float normalInverseMass=DirectionalInverseMass(normal,corkArm,tail,inverseMass,inertiaScale);
            float effectiveMass=1f/normalInverseMass;
            float stiffness=Mathf.Clamp(settings.StringbedStiffness*Mathf.Lerp(.45f,1f,quality),1000f,120000f);
            float restitution=Mathf.Clamp(settings.RestitutionAtSpeed(closing)*Mathf.Lerp(.48f,1f,quality),.1f,.98f);
            float logRestitution=Mathf.Log(restitution);
            float dampingRatio=-logRestitution/Mathf.Sqrt(Mathf.PI*Mathf.PI+logRestitution*logRestitution);
            float damping=2f*dampingRatio*Mathf.Sqrt(stiffness*effectiveMass);
            float angularFrequency=Mathf.Sqrt(stiffness/effectiveMass);
            float halfPeriod=Mathf.PI/(angularFrequency*Mathf.Sqrt(1f-dampingRatio*dampingRatio));
            float durationLimit=Mathf.Clamp(halfPeriod*1.35f,.0002f,MaxContactDuration);
            float dt=durationLimit/MaxContactSteps;
            float tangentStiffness=Mathf.Clamp(settings.TangentialStiffness*Mathf.Lerp(.45f,1f,quality),100f,50000f);
            float tangentDamping=.1f*2f*Mathf.Sqrt(tangentStiffness*effectiveMass);
            float staticFriction=Mathf.Max(0f,settings.StaticFriction);
            float kineticFriction=Mathf.Clamp(settings.KineticFriction,0f,staticFriction);

            Vector3 velocity=incoming.velocity;
            Vector3 spin=incoming.angularVelocity;
            // Equal-and-opposite impulse into the racket's finite effective mass.
            // This recoil is a local contact state, not a movement of the XR hand.
            Vector3 recoil=Vector3.zero;
            Vector3 elasticShear=Vector3.zero;
            Quaternion corkMaterialTurn=Quaternion.identity;
            float indentation=0f;
            float suppliedWork=0f;
            float initialEnergy=KineticEnergy(velocity,spin,tail,mass,inertiaScale);
            int slidingSteps=0, contactSteps=0;
            float wristSpeed=contact.angularVelocity.magnitude;
            Vector3 wristAxis=wristSpeed>.00001f ? contact.angularVelocity/wristSpeed : Vector3.up;

            for (int i=0;i<MaxContactSteps;i++)
            {
                float elapsed=i*dt;
                Quaternion wristTurn=wristSpeed>.00001f ?
                    Quaternion.AngleAxis(wristSpeed*elapsed*Mathf.Rad2Deg,wristAxis) : Quaternion.identity;
                normal=wristTurn*initialNormal;
                corkArm=corkCenterArm-normal*ShuttlePhysicsSettings.CorkRadius;
                racketVelocity=(contact.centerVelocity+
                    Vector3.Cross(contact.angularVelocity,wristTurn*(contact.point-contact.center)))*gain;
                relative=velocity+Vector3.Cross(spin,corkArm)-(racketVelocity+recoil);
                float normalSpeed=Vector3.Dot(relative,normal);
                normalInverseMass=DirectionalInverseMass(normal,corkArm,tail,inverseMass,inertiaScale);

                // Implicit Kelvin-Voigt spring/damper: stable at smash speeds.
                // Unilateral strings can push, never pull a departing shuttle.
                float normalImpulse=dt*(stiffness*indentation-(stiffness*dt+damping)*normalSpeed)/
                    (1f+dt*(stiffness*dt+damping)*normalInverseMass);
                if (normalImpulse<=0f && normalSpeed>=0f) break;
                normalImpulse=Mathf.Max(0f,normalImpulse);
                Vector3 impulse=normal*normalImpulse;
                ApplyImpulse(impulse,corkArm,tail,mass,racketMass,inertiaScale,ref velocity,ref spin,ref recoil);
                suppliedWork+=Vector3.Dot(impulse,racketVelocity);

                // Elastic tangential string displacement stores shear while the
                // cork sticks. Above static friction it slips at kinetic friction.
                // Solve the full 2D tangent effective-mass matrix, including spin.
                relative=velocity+Vector3.Cross(spin,corkArm)-(racketVelocity+recoil);
                Vector3 tangentVelocity=Vector3.ProjectOnPlane(relative,normal);
                elasticShear=Vector3.ProjectOnPlane(elasticShear,normal);
                Vector3 tangentX=wristTurn*(contact.faceRotation*Vector3.right);
                tangentX=Vector3.ProjectOnPlane(tangentX,normal).normalized;
                if (tangentX.sqrMagnitude<.5f)
                    tangentX=Vector3.Cross(normal,Mathf.Abs(normal.y)<.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 tangentY=Vector3.Cross(normal,tangentX);
                Vector3 responseX=VelocityResponse(tangentX,corkArm,tail,inverseMass,inertiaScale);
                Vector3 responseY=VelocityResponse(tangentY,corkArm,tail,inverseMass,inertiaScale);
                float coefficient=dt*(tangentStiffness*dt+tangentDamping);
                float a=1f+coefficient*Vector3.Dot(tangentX,responseX);
                float b=coefficient*Vector3.Dot(tangentX,responseY);
                float d=1f+coefficient*Vector3.Dot(tangentY,responseY);
                Vector3 rhs=-dt*(tangentStiffness*elasticShear+
                    (tangentStiffness*dt+tangentDamping)*tangentVelocity);
                float rhsX=Vector3.Dot(rhs,tangentX), rhsY=Vector3.Dot(rhs,tangentY);
                float determinant=Mathf.Max(.000001f,a*d-b*b);
                Vector3 tangentImpulse=tangentX*((d*rhsX-b*rhsY)/determinant)+
                    tangentY*((a*rhsY-b*rhsX)/determinant);
                bool sliding=tangentImpulse.magnitude>staticFriction*normalImpulse+.0000001f;
                if (sliding)
                {
                    // Use current slip, never a stroke label, to select friction.
                    Vector3 direction=tangentVelocity.sqrMagnitude>.0000001f ?
                        -tangentVelocity.normalized : tangentImpulse.normalized;
                    tangentImpulse=direction*(kineticFriction*normalImpulse);
                    // A kinetic step cannot reverse slip by overshooting zero.
                    if (tangentVelocity.sqrMagnitude>.0000001f)
                    {
                        float stopImpulse=tangentVelocity.magnitude/
                            DirectionalInverseMass(direction,corkArm,tail,inverseMass,inertiaScale);
                        tangentImpulse=Vector3.ClampMagnitude(tangentImpulse,stopImpulse);
                    }
                    slidingSteps++;
                }
                ApplyImpulse(tangentImpulse,corkArm,tail,mass,racketMass,inertiaScale,ref velocity,ref spin,ref recoil);
                suppliedWork+=Vector3.Dot(tangentImpulse,racketVelocity);
                relative=velocity+Vector3.Cross(spin,corkArm)-(racketVelocity+recoil);
                Vector3 tangentAfter=Vector3.ProjectOnPlane(relative,normal);
                if (sliding)
                {
                    // Plastic slip resets the elastic part; sliding distance is
                    // not accumulated into an unbounded spring for the next step.
                    elasticShear=-(tangentImpulse/dt+tangentDamping*tangentAfter)/tangentStiffness;
                    float maxShear=staticFriction*normalImpulse/(dt*tangentStiffness);
                    elasticShear=Vector3.ClampMagnitude(elasticShear,maxShear);
                }
                else elasticShear+=tangentAfter*dt;
                indentation=Mathf.Clamp(indentation-Vector3.Dot(relative,normal)*dt,0f,.06f);
                result.maxIndentation=Mathf.Max(result.maxIndentation,indentation);
                contactSteps++;
                result.contactDuration=contactSteps*dt;

                // The force solver freezes the COM/cork axis during this ~1 ms
                // event. This material rotation is only a contact-sweep estimate;
                // flight owns the actual rigid-body orientation integration.
                float turn=spin.magnitude*dt;
                if (turn>.0000001f)
                    corkMaterialTurn=(Quaternion.AngleAxis(turn*Mathf.Rad2Deg,spin.normalized)*corkMaterialTurn).normalized;
                Vector3 materialContact=Quaternion.Inverse(corkMaterialTurn)*
                    (-normal+elasticShear/ShuttlePhysicsSettings.CorkRadius).normalized;
                result.wrapAngle=Mathf.Max(result.wrapAngle,Vector3.Angle(-initialNormal,materialContact));
                if (indentation<=.0000001f && Vector3.Dot(relative,normal)>=0f) break;
            }

            if (contactSteps==0) return result;
            // Conservation audit: a stationary passive racket cannot create
            // translational + rotational energy. A moving racket supplies only
            // its impulse work; finite racket recoil consumes part of that work.
            // Discard residual internal strain at separation and suppress any
            // numerical leakage from split normal/tangent/rotating-frame solves.
            float energyBudget=Mathf.Max(0f,initialEnergy+suppliedWork-.5f*racketMass*recoil.sqrMagnitude);
            float finalEnergy=KineticEnergy(velocity,spin,tail,mass,inertiaScale);
            if (finalEnergy>energyBudget+.000001f && finalEnergy>.000001f)
            {
                float scale=Mathf.Sqrt(energyBudget/finalEnergy);
                velocity*=scale;
                spin*=scale;
            }
            result.outgoingVelocity=Vector3.ClampMagnitude(velocity,150f);
            // Axial spin has far less inertia than end-over-end tumble. A single
            // magnitude cap would erase fast slice spin to retain a small wobble.
            float axial=Vector3.Dot(spin,tail);
            result.angularVelocity=tail*Mathf.Clamp(axial,-800f,800f)+
                Vector3.ClampMagnitude(spin-tail*axial,350f);
            result.slidingFraction=(float)slidingSteps/contactSteps;
            result.wrapAngle=Mathf.Clamp(result.wrapAngle,0f,180f);
            result.axialSpin=Vector3.Dot(result.angularVelocity,tail);
            result.hit=Finite(result.outgoingVelocity)&&Finite(result.angularVelocity);
            return result;
        }

        private static void ApplyImpulse(Vector3 impulse, Vector3 arm, Vector3 tail, float mass,
            float racketMass, float inertiaScale, ref Vector3 velocity, ref Vector3 spin, ref Vector3 recoil)
        {
            velocity+=impulse/mass;
            spin+=InverseInertia(Vector3.Cross(arm,impulse),tail,inertiaScale);
            recoil-=impulse/racketMass;
        }

        private static Vector3 VelocityResponse(Vector3 impulseDirection, Vector3 arm, Vector3 tail,
            float inverseMass, float inertiaScale)
        {
            return impulseDirection*inverseMass+
                Vector3.Cross(InverseInertia(Vector3.Cross(arm,impulseDirection),tail,inertiaScale),arm);
        }

        private static float DirectionalInverseMass(Vector3 direction, Vector3 arm, Vector3 tail,
            float inverseMass, float inertiaScale)
        {
            return Mathf.Max(.0001f,Vector3.Dot(direction,VelocityResponse(direction,arm,tail,inverseMass,inertiaScale)));
        }

        private static Vector3 InverseInertia(Vector3 torque, Vector3 tail, float scale)
        {
            Vector3 axial=tail*Vector3.Dot(torque,tail);
            return axial/(ShuttlePhysics.AxialInertia*scale)+
                (torque-axial)/(ShuttlePhysics.TransverseInertia*scale);
        }

        private static float KineticEnergy(Vector3 velocity, Vector3 spin, Vector3 tail, float mass, float scale)
        {
            float axial=Vector3.Dot(spin,tail);
            return .5f*mass*velocity.sqrMagnitude+.5f*scale*
                (ShuttlePhysics.AxialInertia*axial*axial+
                 ShuttlePhysics.TransverseInertia*(spin-tail*axial).sqrMagnitude);
        }

        private static bool Finite(Vector3 vector)
        {
            return !float.IsNaN(vector.x)&&!float.IsNaN(vector.y)&&!float.IsNaN(vector.z)&&
                !float.IsInfinity(vector.x)&&!float.IsInfinity(vector.y)&&!float.IsInfinity(vector.z);
        }
    }
}
