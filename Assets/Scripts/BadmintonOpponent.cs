using System;
using UnityEngine;

namespace ViveShuttleVR
{
    public enum OpponentDifficulty { Easy, Club, Advanced }

    public struct OpponentAbility
    {
        public float reaction, runSpeed, reach, contactHeight, racketSpeed, returnDuration, missChance;
        public static OpponentAbility For(OpponentDifficulty difficulty)
        {
            // Gameplay tuning, NOT measured motion data from professional players.
            if (difficulty == OpponentDifficulty.Advanced)
                return new OpponentAbility { reaction=.13f, runSpeed=3.7f, reach=1.02f, contactHeight=2.8f,
                    racketSpeed=23f, returnDuration=1.35f, missChance=.02f };
            if (difficulty == OpponentDifficulty.Club)
                return new OpponentAbility { reaction=.25f, runSpeed=2.7f, reach=.88f, contactHeight=2.55f,
                    racketSpeed=18f, returnDuration=1.5f, missChance=.07f };
            return new OpponentAbility { reaction=.42f, runSpeed=1.7f, reach=.72f, contactHeight=2.3f,
                racketSpeed=15f, returnDuration=1.65f, missChance=.16f };
        }
    }

    public struct OpponentIntercept
    {
        public bool valid;
        public float time;
        public ShuttleState state;
        public Vector3 feetLocal;
    }

    /// <summary>
    /// Predictive, speed-limited practice opponent. It observes the shuttle after
    /// a reaction delay, runs to a reachable interception, and chooses a racket
    /// pose/speed using the same contact and flight model as the human player.
    /// Only shot selection is assisted: the outgoing shuttle is never replaced
    /// with a target-solver velocity. This is not a human biomechanics model.
    /// </summary>
    public sealed class BadmintonOpponent : MonoBehaviour
    {
        public Vector3 FeetLocal { get { return feet; } }
        public bool Tracking { get { return tracking; } }
        public bool HasIntercept { get { return intercept.valid; } }
        public int ReturnAttempts { get; private set; }
        public float LastAimError { get; private set; }
        public string LastDecision { get; private set; }
        public ShuttlePhysicsSettings PhysicsSettings { get { return strictSettings; } }
        public Vector3 RacketHeadWorld { get { return racket != null ? racket.position : transform.position; } }
        public Vector3 ServePosition { get { return session.court.TransformPoint(feet+new Vector3(.23f,1.55f,-.38f)); } }

        private BadmintonSession session;
        private ShuttlePhysicsSettings strictSettings;
        private OpponentAbility ability;
        private OpponentIntercept intercept;
        private Vector3 feet, moveGoal, returnTarget;
        private float reactionRemaining, interceptRemaining, walkPhase, swingRecovery;
        private bool tracking, planned, intentionallyMiss;
        private int shotNumber;
        private RacketContact plannedContact;
        private ReturnSearch returnSearch;
        private Transform avatar, head, racket, upperArm, forearm, leftLeg, rightLeg;
        private TextMesh nameplate;
        private Material shirtMaterial, skinMaterial, darkMaterial, racketMaterial, stringsMaterial;
        private Mesh[] racketMeshes;

        public void Initialize(BadmintonSession owner)
        {
            session=owner;
            transform.SetParent(owner.court,false);
            transform.localPosition=Vector3.zero;
            transform.localRotation=Quaternion.identity;
            if (avatar == null) BuildVisuals();
            ResetRally();
        }

        public void SetVisible(bool visible)
        {
            if (avatar != null) avatar.gameObject.SetActive(visible);
        }

        public void PrepareServe()
        {
            if (racket==null || session==null) return;
            racket.position=ServePosition;
            racket.rotation=session.court.rotation*Quaternion.Euler(0f,180f,0f);
            swingRecovery=.22f;
            LastDecision="PRACTICE SERVE";
        }

        public static ShuttlePhysicsSettings CreateStrictSettings(ShuttlePhysicsSettings source)
        {
            // Serialization preserves future equipment fields as well as the
            // current string-bed settings. Human comfort speed gain is excluded.
            var copy=JsonUtility.FromJson<ShuttlePhysicsSettings>(JsonUtility.ToJson(source));
            copy.seatedAssist=false;
            return copy;
        }

        public void ResetRally()
        {
            tracking=planned=intentionallyMiss=false;
            returnSearch=null;
            intercept=new OpponentIntercept();
            reactionRemaining=interceptRemaining=swingRecovery=0f;
            // A between-point reset is explicit; within a rally only MoveTowards
            // changes the feet and no interception teleports the body.
            feet=new Vector3(0f,0f,7.1f);
            moveGoal=feet;
            if (session != null)
            {
                ability=OpponentAbility.For(session.Difficulty);
                strictSettings=CreateStrictSettings(session.PhysicsSettings);
            }
            if (racket != null)
            {
                racket.localPosition=new Vector3(.36f,1.20f,-.42f);
                racket.localRotation=Quaternion.Euler(0f,180f,-30f);
            }
            LastDecision="READY";
            UpdateVisuals(0f);
        }

        public void BeginTracking(ShuttleState incoming)
        {
            if (session == null) return;
            ability=OpponentAbility.For(session.Difficulty);
            strictSettings=CreateStrictSettings(session.PhysicsSettings);
            tracking=true;
            planned=false;
            intercept=new OpponentIntercept();
            reactionRemaining=ability.reaction;
            moveGoal=feet;
            shotNumber++;
            // Deterministic mistake schedule for reproducible tests. It changes
            // the decision to attempt, never bends a shuttle already in flight.
            uint hash=unchecked((uint)(shotNumber*747796405+289133645));
            hash=(hash^(hash>>16))*2246822519u;
            intentionallyMiss=shotNumber>1 && (hash%10000u)/10000f < ability.missChance;
            LastDecision="READING SHOT";
            if (intentionallyMiss) { returnSearch=null; return; }
            // Predict without moving: decision computation is spread over the
            // reaction interval, while the body still observes that full delay.
            ShuttleState afterReaction=ShuttlePhysics.Predict(incoming,ability.reaction,strictSettings);
            intercept=FindIntercept(afterReaction,feet,session.court,strictSettings,ability);
            if (!intercept.valid)
            {
                tracking=false;
                LastDecision="OUT OF REACH";
                return;
            }
            moveGoal=intercept.feetLocal;
            interceptRemaining=ability.reaction+intercept.time;
            returnTarget=session.GetOpponentTarget(session.Difficulty==OpponentDifficulty.Easy ? 0 : shotNumber);
            returnSearch=new ReturnSearch(intercept.state,returnTarget,session.court,strictSettings,ability);
        }

        // Called ONCE per rendered/simulated frame by ShuttleFlight, never once
        // per physics substep. One step contains at most one 0.35-2.75 s flight
        // prediction; no synchronous Newton search runs in a VR frame.
        public void AdvancePlanning()
        {
            if (!tracking || returnSearch==null || returnSearch.Done) return;
            returnSearch.Step();
            LastAimError=returnSearch.Error;
            if (!returnSearch.Done) return;
            if (!returnSearch.Success)
            {
                tracking=false;
                LastDecision="NO CONTROLLED RETURN";
                return;
            }
            plannedContact=returnSearch.Contact;
            planned=true;
            LastDecision="RETURN PLANNED";
        }

        public bool TryContact(ShuttleState before, ShuttleState next, float dt, out RacketContact contact)
        {
            contact=new RacketContact();
            if (!tracking || session == null || dt<=0f) return false;
            dt=Mathf.Min(dt,.08f);
            reactionRemaining-=dt;
            if (intercept.valid) interceptRemaining-=dt;
            if (reactionRemaining>0f) { UpdateVisuals(dt); return false; }
            if (intentionallyMiss)
            {
                tracking=false;
                LastDecision="LATE REACTION";
                return false;
            }
            if (intercept.valid)
            {
                feet=StepFeet(feet,moveGoal,ability.runSpeed,dt);
            }
            UpdateVisuals(dt);
            if (!planned)
            {
                if (interceptRemaining<.02f) { tracking=false; LastDecision="PLANNING TOO LATE"; }
                return false;
            }
            if (!intercept.valid || interceptRemaining>.035f) return false;
            Vector3 local=session.court.InverseTransformPoint(next.position);
            Vector3 projected=new Vector3(local.x,0f,local.z);
            bool closeToPlanned=(next.position-intercept.state.position).sqrMagnitude<.15f*.15f;
            bool reachable=Vector3.Distance(projected,feet)<=ability.reach &&
                local.y>=.40f && local.y<=ability.contactHeight && local.z>ShuttleMath.NetZ+.22f;
            if (closeToPlanned && reachable && interceptRemaining<=dt*.55f)
            {
                // Flight resolves this contact at next.position, retaining its
                // actual incoming spin/orientation; no launch-state substitution.
                contact=plannedContact;
                contact.point=contact.center=next.position;
                ImpactResult physical=RacketImpact.Resolve(next,contact,strictSettings);
                if (physical.hit && session.court.InverseTransformDirection(physical.outgoingVelocity).z<-.15f)
                {
                    ReturnAttempts++;
                    tracking=false;
                    swingRecovery=.18f;
                    LastDecision="RETURN";
                    if (racket != null) racket.SetPositionAndRotation(next.position,contact.faceRotation);
                    return true;
                }
            }
            if (interceptRemaining<-.08f)
            {
                tracking=false;
                LastDecision="MISSED CONTACT";
            }
            return false;
        }

        public void Idle(float dt)
        {
            // Root may call this while the player receives. No unrequested court
            // movement, time advancement, or hit happens through this method.
            if (!tracking) UpdateVisuals(Mathf.Clamp(dt,0f,.08f));
        }

        public static Vector3 StepFeet(Vector3 current, Vector3 goal, float speed, float dt)
        {
            goal.y=current.y=0f;
            goal.x=Mathf.Clamp(goal.x,-2.45f,2.45f);
            goal.z=Mathf.Clamp(goal.z,ShuttleMath.NetZ+.30f,10.15f);
            return Vector3.MoveTowards(current,goal,Mathf.Max(0f,speed)*Mathf.Max(0f,dt));
        }

        public static OpponentIntercept FindIntercept(ShuttleState incoming, Vector3 feetLocal, Transform court,
            ShuttlePhysicsSettings settings, OpponentAbility ability)
        {
            var result=new OpponentIntercept();
            ShuttleState state=incoming;
            const float predictionStep=1f/180f;
            for (int i=1;i<=540;i++)
            {
                Vector3 previous=court.InverseTransformPoint(state.position);
                ShuttlePhysics.Integrate(ref state,settings,predictionStep);
                Vector3 local=court.InverseTransformPoint(state.position);
                Vector3 crossing;
                if (ShuttleMath.NetCrossing(previous,local,out crossing) &&
                    Mathf.Abs(crossing.x)<=3.05f && crossing.y<1.575f) return result;
                if (local.y<.35f || local.z>10.5f || Mathf.Abs(local.x)>3.5f) return result;
                float time=i*predictionStep;
                if (time<.16f || local.z<=ShuttleMath.NetZ+.30f || local.z>10.15f ||
                    Mathf.Abs(local.x)>2.55f || local.y>.98f*ability.contactHeight || local.y<.65f) continue;
                Vector3 goal=new Vector3(Mathf.Clamp(local.x,-2.4f,2.4f),0f,
                    Mathf.Clamp(local.z+.32f,ShuttleMath.NetZ+.35f,10.15f));
                Vector3 possible=StepFeet(feetLocal,goal,ability.runSpeed,Mathf.Max(0f,time-.03f));
                float reach=new Vector2(local.x-possible.x,local.z-possible.z).magnitude;
                if (reach>ability.reach-.08f) continue;
                // Re-evaluate with the same small substep used by live flight.
                ShuttleState precise=ShuttlePhysics.Predict(incoming,time,settings);
                local=court.InverseTransformPoint(precise.position);
                if (new Vector2(local.x-possible.x,local.z-possible.z).magnitude>ability.reach-.06f) continue;
                result.valid=true;
                result.time=time;
                result.state=precise;
                result.feetLocal=goal;
                return result;
            }
            return result;
        }

        public static bool TryPlanReturn(ShuttleState incoming, Vector3 target, Transform court,
            ShuttlePhysicsSettings settings, OpponentAbility ability, out RacketContact contact, out float error)
        {
            // Test/authoring convenience only. Runtime consumes exactly one
            // ReturnSearch.Step per frame through AdvancePlanning instead.
            var search=new ReturnSearch(incoming,target,court,settings,ability);
            for (int i=0;i<360 && !search.Done;i++) search.Step();
            contact=search.Contact;
            error=search.Error;
            return search.Success;
        }

        public static float ReturnDuration(OpponentAbility ability, int variant)
        {
            // A longer high arc can require MORE energy, not less. A shorter
            // legal arc is tried before asking an underpowered racket for lift.
            return Mathf.Clamp(ability.returnDuration+(variant==1 ? -.20f : variant==2 ? .15f : 0f),.8f,2.75f);
        }

        private sealed class ReturnSearch
        {
            public bool Done { get; private set; }
            public bool Success { get; private set; }
            public RacketContact Contact { get; private set; }
            public float Error { get; private set; }
            private readonly ShuttleState incoming;
            private readonly Vector3 target;
            private readonly Transform court;
            private readonly ShuttlePhysicsSettings settings;
            private readonly OpponentAbility ability;
            private int durationIndex,iteration,phase,lineAttempt;
            private float duration,currentError,lineScale;
            private Vector3 control,correction,basePoint,dx,dy,dz;
            private RacketContact currentContact;
            private const float Epsilon=.16f;

            public ReturnSearch(ShuttleState state,Vector3 destination,Transform reference,
                ShuttlePhysicsSettings model,OpponentAbility capabilities)
            {
                incoming=state; target=destination; court=reference;
                settings=model; ability=capabilities;
                Error=float.PositiveInfinity;
                BeginDuration();
            }

            private void BeginDuration()
            {
                if (durationIndex>=3) { Done=true; return; }
                duration=ReturnDuration(ability,durationIndex);
                // Cheap ballistic/drag seed only. Unlike SolveLaunch this has no
                // hidden synchronous iterative flight predictions at creation.
                control=(target-incoming.position)/duration+Vector3.up*ShuttlePhysics.Gravity*duration*.5f;
                control*=Mathf.Exp(Mathf.Min(1.1f,settings.LinearDragFactor*Vector3.Distance(incoming.position,target)*.35f));
                phase=iteration=lineAttempt=0;
            }

            private void NextDuration() { durationIndex++; BeginDuration(); }

            public void Step()
            {
                if (Done) return;
                if (phase==4)
                {
                    if (ClearsNet(incoming,currentContact,target,duration,court,settings))
                    {
                        Contact=currentContact; Error=currentError; Done=Success=true;
                    }
                    else NextDuration();
                    return;
                }
                Vector3 query=control;
                if (phase==1) query+=Vector3.right*Epsilon;
                if (phase==2) query+=Vector3.up*Epsilon;
                if (phase==3) query+=Vector3.forward*Epsilon;
                if (phase==5) query+=correction*lineScale;
                RacketContact candidate;
                ShuttleState predicted;
                if (!Evaluate(incoming,query,duration,settings,ability,out candidate,out predicted))
                {
                    NextDuration();
                    return;
                }
                float distance=Vector3.Distance(target,predicted.position);
                if (distance<Error) { Error=distance; Contact=candidate; }
                if (phase==0) { AcceptBase(candidate,predicted.position,distance); return; }
                if (phase==1) { dx=(predicted.position-basePoint)/Epsilon; phase=2; return; }
                if (phase==2) { dy=(predicted.position-basePoint)/Epsilon; phase=3; return; }
                if (phase==3)
                {
                    dz=(predicted.position-basePoint)/Epsilon;
                    Vector3 delta=target-basePoint;
                    // Damped least squares remains well-defined when a racket
                    // speed cap makes one Jacobian direction nearly singular.
                    const float damping=.00002f;
                    Vector3 a=new Vector3(Vector3.Dot(dx,dx)+damping,Vector3.Dot(dx,dy),Vector3.Dot(dx,dz));
                    Vector3 b=new Vector3(a.y,Vector3.Dot(dy,dy)+damping,Vector3.Dot(dy,dz));
                    Vector3 c=new Vector3(a.z,b.z,Vector3.Dot(dz,dz)+damping);
                    Vector3 rhs=new Vector3(Vector3.Dot(dx,delta),Vector3.Dot(dy,delta),Vector3.Dot(dz,delta));
                    float determinant=Vector3.Dot(a,Vector3.Cross(b,c));
                    if (Mathf.Abs(determinant)<1e-12f) { NextDuration(); return; }
                    correction=new Vector3(Vector3.Dot(rhs,Vector3.Cross(b,c)),
                        Vector3.Dot(a,Vector3.Cross(rhs,c)),Vector3.Dot(a,Vector3.Cross(b,rhs)))/determinant;
                    correction=Vector3.ClampMagnitude(correction,12f);
                    if (!Finite(correction)) { NextDuration(); return; }
                    lineScale=1f; lineAttempt=0; phase=5;
                    return;
                }
                if (phase==5)
                {
                    if (distance<currentError-.000001f)
                    {
                        control=query;
                        iteration++;
                        if (iteration>=12 && distance>=.065f) { NextDuration(); return; }
                        AcceptBase(candidate,predicted.position,distance);
                    }
                    else
                    {
                        lineScale*=.5f;
                        lineAttempt++;
                        if (lineAttempt>=5) NextDuration();
                    }
                }
            }

            private void AcceptBase(RacketContact candidate,Vector3 point,float distance)
            {
                currentContact=candidate; basePoint=point; currentError=distance;
                phase=distance<.065f ? 4 : 1;
            }
        }

        private static bool Evaluate(ShuttleState incoming, Vector3 control, float duration,
            ShuttlePhysicsSettings settings, OpponentAbility ability, out RacketContact contact, out ShuttleState predicted)
        {
            Vector3 impulse=control-incoming.velocity;
            Vector3 normal=impulse.normalized;
            float speed=Vector3.Dot(incoming.velocity,normal)+impulse.magnitude/
                (1f+.78f)*(1f+settings.mass/settings.RacketContactMass(Vector3.zero,Vector3.forward));
            speed=Mathf.Clamp(speed,-2f,ability.racketSpeed);
            contact=new RacketContact { point=incoming.position, center=incoming.position,
                centerVelocity=normal*speed, surfaceNormal=normal,
                faceRotation=Quaternion.LookRotation(normal,Mathf.Abs(normal.y)>.95f?Vector3.forward:Vector3.up),
                recentPeakSpeed=Mathf.Abs(speed) };
            predicted=incoming;
            if (normal.sqrMagnitude<.5f) return false;
            ImpactResult impact=RacketImpact.Resolve(incoming,contact,settings);
            if (!impact.hit) return false;
            predicted.velocity=impact.outgoingVelocity;
            predicted.angularVelocity=impact.angularVelocity;
            predicted=ShuttlePhysics.Predict(predicted,duration,settings);
            return Finite(predicted.position) && Finite(predicted.velocity);
        }

        public static bool ClearsNet(ShuttleState incoming, RacketContact contact, Vector3 target, float duration,
            Transform court, ShuttlePhysicsSettings settings)
        {
            ImpactResult impact=RacketImpact.Resolve(incoming,contact,settings);
            if (!impact.hit) return false;
            ShuttleState state=incoming;
            state.velocity=impact.outgoingVelocity;
            state.angularVelocity=impact.angularVelocity;
            bool crossed=false;
            for (float t=0f;t<duration-.000001f;)
            {
                float dt=Mathf.Min(ShuttlePhysics.Step,duration-t);
                Vector3 previous=court.InverseTransformPoint(state.position);
                ShuttlePhysics.Integrate(ref state,settings,dt);
                Vector3 local=court.InverseTransformPoint(state.position);
                if (local.y<.10f || Mathf.Abs(local.x)>2.59f || local.z>10.3f || local.z<-3.1f) return false;
                Vector3 crossing;
                if (ShuttleMath.NetCrossing(previous,local,out crossing))
                {
                    if (crossing.y<1.60f || Mathf.Abs(crossing.x)>2.59f) return false;
                    crossed=true;
                }
                t+=dt;
            }
            return crossed && Vector3.Distance(state.position,target)<.08f;
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
                !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }

        private void BuildVisuals()
        {
            avatar=new GameObject("Visible AI player").transform;
            avatar.SetParent(transform,false);
            shirtMaterial=Material(new Color(.12f,.42f,.76f));
            skinMaterial=Material(new Color(.69f,.49f,.34f));
            darkMaterial=Material(new Color(.09f,.12f,.17f));
            racketMaterial=Material(new Color(.65f,.20f,.16f));
            stringsMaterial=Material(new Color(.82f,.85f,.80f));
            Primitive("Jersey",PrimitiveType.Capsule,new Vector3(0f,1.16f,0f),new Vector3(.43f,.39f,.27f),shirtMaterial,avatar);
            Primitive("Shorts",PrimitiveType.Cube,new Vector3(0f,.78f,0f),new Vector3(.38f,.30f,.25f),darkMaterial,avatar);
            head=Primitive("Head",PrimitiveType.Sphere,new Vector3(0f,1.72f,0f),new Vector3(.25f,.29f,.25f),skinMaterial,avatar);
            Primitive("Face direction",PrimitiveType.Cube,new Vector3(0f,1.73f,-.127f),new Vector3(.13f,.035f,.025f),darkMaterial,avatar);
            leftLeg=Primitive("Left leg",PrimitiveType.Capsule,new Vector3(-.13f,.40f,0f),new Vector3(.14f,.34f,.15f),skinMaterial,avatar);
            rightLeg=Primitive("Right leg",PrimitiveType.Capsule,new Vector3(.13f,.40f,0f),new Vector3(.14f,.34f,.15f),skinMaterial,avatar);
            Primitive("Left shoe",PrimitiveType.Cube,new Vector3(-.14f,.065f,-.08f),new Vector3(.19f,.13f,.32f),stringsMaterial,avatar);
            Primitive("Right shoe",PrimitiveType.Cube,new Vector3(.14f,.065f,-.08f),new Vector3(.19f,.13f,.32f),stringsMaterial,avatar);
            upperArm=Primitive("Racket upper arm",PrimitiveType.Cylinder,Vector3.zero,Vector3.one,skinMaterial,avatar);
            forearm=Primitive("Racket forearm",PrimitiveType.Cylinder,Vector3.zero,Vector3.one,skinMaterial,avatar);
            Transform idleArm=Primitive("Free arm",PrimitiveType.Cylinder,Vector3.zero,Vector3.one,skinMaterial,avatar);
            Segment(idleArm,new Vector3(-.24f,1.38f,0f),new Vector3(-.36f,.96f,-.16f),.065f);
            racket=new GameObject("AI racket contact head").transform;
            racket.SetParent(avatar,false);
            Transform racketModel=new GameObject("AI shared racket model").transform;
            racketModel.SetParent(racket,false);
            racketModel.localPosition=-RacketSpecification.HeadCenter;
            RacketGeometry.Build(racketModel,racketMaterial,shirtMaterial,stringsMaterial,darkMaterial);
            MeshFilter[] generated=racketModel.GetComponentsInChildren<MeshFilter>();
            racketMeshes=new Mesh[generated.Length];
            for (int i=0;i<generated.Length;i++) racketMeshes[i]=generated[i].sharedMesh;
            nameplate=new GameObject("AI difficulty label").AddComponent<TextMesh>();
            nameplate.transform.SetParent(avatar,false);
            nameplate.transform.localPosition=new Vector3(0f,2.20f,0f);
            nameplate.font=Resources.GetBuiltinResource<Font>("Arial.ttf");
            nameplate.GetComponent<Renderer>().sharedMaterial=nameplate.font.material;
            nameplate.fontSize=48;
            nameplate.characterSize=.018f;
            nameplate.anchor=TextAnchor.MiddleCenter;
            nameplate.alignment=TextAlignment.Center;
            nameplate.color=Color.white;
        }

        private void UpdateVisuals(float dt)
        {
            if (avatar==null || session==null) return;
            avatar.localPosition=feet;
            bool moving=tracking && intercept.valid && Vector3.Distance(feet,moveGoal)>.05f;
            walkPhase+=dt*(moving?10f:1.8f);
            leftLeg.localRotation=Quaternion.Euler(moving?Mathf.Sin(walkPhase)*24f:0f,0f,0f);
            rightLeg.localRotation=Quaternion.Euler(moving?-Mathf.Sin(walkPhase)*24f:0f,0f,0f);
            swingRecovery=Mathf.Max(0f,swingRecovery-dt);
            Vector3 idle=new Vector3(.36f,1.20f,-.42f);
            if (intercept.valid && tracking && planned)
            {
                float swing=Mathf.InverseLerp(.35f,0f,interceptRemaining);
                float u2=swing*swing,u3=u2*swing;
                // Hermite approach ends with the same head velocity used by the
                // impact model, rather than a visually unrelated linear lerp.
                racket.position=(2f*u3-3f*u2+1f)*avatar.TransformPoint(idle)+
                    (-2f*u3+3f*u2)*intercept.state.position+
                    (u3-u2)*plannedContact.centerVelocity*.35f;
                racket.rotation=Quaternion.Slerp(session.court.rotation,plannedContact.faceRotation,swing*swing*(3f-2f*swing));
            }
            else if (swingRecovery<=0f)
            {
                racket.localPosition=Vector3.Lerp(racket.localPosition,idle,Mathf.Min(1f,dt*9f+.01f));
                racket.localRotation=Quaternion.Slerp(racket.localRotation,Quaternion.Euler(0f,180f,-30f),Mathf.Min(1f,dt*9f+.01f));
            }
            Vector3 hand=avatar.InverseTransformPoint(racket.TransformPoint(new Vector3(0f,-.47f,0f)));
            Vector3 shoulder=new Vector3(.23f,1.42f,0f);
            Vector3 elbow=Vector3.Lerp(shoulder,hand,.50f)+new Vector3(.07f,-.12f,.04f);
            Segment(upperArm,shoulder,elbow,.065f);
            Segment(forearm,elbow,hand,.054f);
            nameplate.text="AI / "+session.Difficulty.ToString().ToUpperInvariant();
        }

        private static Material Material(Color color)
        {
            var material=new Material(Shader.Find("Standard"));
            material.color=color;
            material.SetFloat("_Glossiness",.2f);
            return material;
        }

        private static Transform Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale,
            Material material, Transform parent)
        {
            GameObject part=GameObject.CreatePrimitive(type);
            part.name=name;
            part.transform.SetParent(parent,false);
            part.transform.localPosition=position;
            part.transform.localScale=scale;
            part.GetComponent<Renderer>().sharedMaterial=material;
            Collider collider=part.GetComponent<Collider>();
            collider.enabled=false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            return part.transform;
        }

        private static void Segment(Transform segment, Vector3 a, Vector3 b, float radius)
        {
            segment.localPosition=(a+b)*.5f;
            segment.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
            segment.localScale=new Vector3(radius*2f,Vector3.Distance(a,b)*.5f,radius*2f);
        }

        private void OnDestroy()
        {
            if (racketMeshes!=null)
                foreach (Mesh mesh in racketMeshes)
                    if (mesh!=null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            foreach (Material material in new[] { shirtMaterial,skinMaterial,darkMaterial,racketMaterial,stringsMaterial })
                if (material!=null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        }
    }
}
