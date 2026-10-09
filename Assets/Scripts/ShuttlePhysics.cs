using System;
using UnityEngine;

namespace ViveShuttleVR
{
    [Serializable]
    public sealed class ShuttlePhysicsSettings
    {
        [Header("SI units - tunable feather-shuttle approximation")]
        [Range(.0045f,.006f)] public float mass = .0054f;
        [Range(.055f,.075f)] public float skirtDiameter = .066f;
        [Range(.3f,.9f)] public float dragCoefficient = .5f;
        [Range(.8f,1.5f)] public float airDensity = 1.225f;
        public Vector3 wind = Vector3.zero;
        [Range(.3f,.95f)] public float restitution = .82f;
        [Range(.02f,.5f)] public float stringFriction = .30f;
        [Header("ASTROX 100 ZZ - published mass, estimated grip inertia")]
        public RacketWeightClass racketWeightClass = RacketWeightClass.FourU;
        public bool useRacketSpecification = true;
        [SerializeField] private RacketSpecification racketModel = new RacketSpecification();
        [Tooltip("Legacy scalar fallback only when useRacketSpecification is disabled.")]
        [Range(.02f,.2f)] public float effectiveRacketMass = .09f;
        public RacketSpecification RacketModel
        {
            get
            {
                if (racketModel == null) racketModel = new RacketSpecification();
                racketModel.weightClass = racketWeightClass;
                return racketModel;
            }
        }
        public float RacketContactMass(Vector3 headLocalPoint, Vector3 headLocalNormal)
        {
            return useRacketSpecification ? RacketModel.EffectiveContactMass(headLocalPoint,headLocalNormal) :
                Mathf.Clamp(effectiveRacketMass,.02f,.2f);
        }
        [Header("Reduced-order string bed - lbs is a tuning input, not force in newtons")]
        [Range(18f,32f)] public float stringTensionLbs = 25f;
        public const float CorkRadius = .013f;
        public float InertiaScale { get { return Mathf.Max(.001f,mass)/.005f; } }
        // Coefficients are game-model calibrations, not a measured commercial racket.
        public float StringbedStiffness { get { return 45000f*Mathf.Pow(Mathf.Clamp(stringTensionLbs,18f,32f)/25f,1.15f); } }
        public float TangentialStiffness { get { return StringbedStiffness*.22f; } }
        public float StaticFriction { get { return Mathf.Max(0f,stringFriction)*1.5f; } }
        public float KineticFriction { get { return Mathf.Max(0f,stringFriction); } }
        public float SweetSpotFalloff { get { return .65f*Mathf.Pow(Mathf.Clamp(stringTensionLbs,18f,32f)/25f,.7f); } }
        public float RestitutionAtSpeed(float closing)
        {
            float tensionFactor=Mathf.Lerp(1.025f,.975f,Mathf.InverseLerp(18f,32f,stringTensionLbs));
            float speedCorrection=Mathf.Lerp(.025f,-.015f,Mathf.InverseLerp(2f,30f,closing));
            return Mathf.Clamp(restitution*tensionFactor+speedCorrection,.05f,.95f);
        }
        [Header("Seated assistance - never auto-aims or guarantees net clearance")]
        public bool seatedAssist = true;
        [Range(1f,1.8f)] public float swingGain = 1.45f;
        [Range(0f,.04f)] public float contactMargin = .025f;
        public float LinearDragFactor
        {
            get { return .5f * Mathf.Max(.1f,airDensity) * Mathf.Max(.05f,dragCoefficient) * Mathf.PI * skirtDiameter * skirtDiameter * .25f / Mathf.Max(.001f,mass); }
        }
        public float ContactMargin { get { return seatedAssist ? contactMargin : .003f; } }
    }

    public struct ShuttleState
    {
        public Vector3 position, velocity, angularVelocity;
        // Local +Y is the feather/skirt axis, from cork toward feathers.
        public Quaternion orientation;
        public static ShuttleState Launch(Vector3 position, Vector3 velocity)
        {
            return new ShuttleState { position=position, velocity=velocity, angularVelocity=Vector3.zero,
                orientation=velocity.sqrMagnitude>.001f ? Quaternion.FromToRotation(Vector3.up,-velocity.normalized) : Quaternion.identity };
        }
    }

    public static class ShuttlePhysics
    {
        public const float Gravity = 9.81f;
        public const float Step = 1f / 360f;
        public const float TransverseInertia = .000006f;
        public const float AxialInertia = .0000012f;

        public static void Integrate(ref ShuttleState state, ShuttlePhysicsSettings settings, float dt)
        {
            if (dt<=0f) return;
            Vector3 previousVelocity = state.velocity;
            Vector3 relative = state.velocity - settings.wind;
            float speed = relative.magnitude;
            Vector3 tail = state.orientation * Vector3.up;
            Vector3 desiredTail = speed>.02f ? -relative/speed : tail;
            float cosine = Mathf.Clamp(Vector3.Dot(tail,desiredTail),-1f,1f);
            float broadside = 1f-cosine*cosine;
            Vector3 axialSpin = tail * Vector3.Dot(state.angularVelocity,tail);
            Vector3 tumble = state.angularVelocity-axialSpin;
            float dragFactor = settings.LinearDragFactor * (1f+1.2f*broadside+.15f*Mathf.Clamp01(tumble.magnitude/100f));

            // Split gravity / analytic drag update is stable even at smash speed.
            Vector3 velocity = state.velocity + Vector3.down * Gravity * (dt*.5f);
            relative = velocity-settings.wind;
            velocity = settings.wind + relative/(1f+dragFactor*relative.magnitude*dt);
            // Small, transient effect while spin is transverse to the flight.
            // No tennis-style persistent or exaggerated sideways curvature.
            Vector3 magnus = Vector3.ClampMagnitude(Vector3.Cross(state.angularVelocity,relative)*.0007f,1.5f);
            velocity += (Vector3.down*Gravity*.5f+magnus)*dt;
            state.velocity = velocity;
            state.position += (previousVelocity+velocity)*(.5f*dt);

            Vector3 turnAxis = Vector3.Cross(tail,desiredTail);
            float angle = Mathf.Atan2(turnAxis.magnitude,cosine);
            if (cosine<-.999f && turnAxis.sqrMagnitude<.000001f)
                turnAxis = state.orientation*Vector3.right; // Exactly reversed after a square contact.
            if (turnAxis.sqrMagnitude>.000001f) turnAxis.Normalize();
            float stiffness = Mathf.Min(4000f,60f+speed*speed*6f)/settings.InertiaScale;
            tumble += turnAxis*angle*stiffness*dt;
            tumble *= Mathf.Exp(-(18f+Mathf.Min(speed,40f)*.6f)*dt);
            // A feather shuttle's handed skirt tends toward its natural axial spin.
            float axial = Mathf.Lerp(Vector3.Dot(axialSpin,tail),Mathf.Min(speed*4f,95f),1f-Mathf.Exp(-(12f+.4f*speed)*dt));
            // Explicit reduced-order aero orientation: tumble controls the tail
            // axis, then axial spin rolls about the resulting axis. This excludes
            // full rigid-body gyroscopic precession / feather deformation. Using
            // one large world-angle step instead would numerically couple fast
            // axial roll into the tail response and delay the cork-first flip.
            tumble=Vector3.ClampMagnitude(Vector3.ProjectOnPlane(tumble,tail),350f);
            axial=Mathf.Clamp(axial,-800f,800f);
            float tiltRadians=tumble.magnitude*dt;
            Quaternion orientation=state.orientation;
            if (tiltRadians>.000001f)
                orientation=Quaternion.AngleAxis(tiltRadians*Mathf.Rad2Deg,tumble.normalized)*orientation;
            Vector3 newTail=(orientation*Vector3.up).normalized;
            if (Mathf.Abs(axial*dt)>.000001f)
                orientation=Quaternion.AngleAxis(axial*dt*Mathf.Rad2Deg,newTail)*orientation;
            state.orientation=orientation.normalized;
            newTail=(state.orientation*Vector3.up).normalized;
            // Re-express spin using the new tail, so the next step does not
            // reinterpret old-axis roll as fresh tumble. Projection only removes
            // round-off energy; it never amplifies the transverse component.
            state.angularVelocity=Vector3.ProjectOnPlane(tumble,newTail)+newTail*axial;
        }

        public static ShuttleState Predict(ShuttleState state, float duration, ShuttlePhysicsSettings settings)
        {
            for (float t=0f;t<duration-.000001f;)
            {
                float dt=Mathf.Min(Step,duration-t);
                Integrate(ref state,settings,dt);
                t+=dt;
            }
            return state;
        }
    }
}
