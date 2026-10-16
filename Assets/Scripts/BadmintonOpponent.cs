using System;
using UnityEngine;

namespace ViveShuttleVR
{
    public enum OpponentDifficulty { Easy, Club, Advanced }
    public enum OpponentServePlanStatus { Pending, Success, Failed }

    public struct OpponentAbility
    {
        public float reaction, runSpeed, reach, contactHeight, racketSpeed, returnDuration, missChance;
        public static OpponentAbility For(OpponentDifficulty difficulty)
        {
            // Gameplay tuning, NOT measured motion data from professional players.
            if (difficulty == OpponentDifficulty.Advanced)
                return new OpponentAbility { reaction=.10f, runSpeed=3.7f, reach=1.02f, contactHeight=2.8f,
                    racketSpeed=23f, returnDuration=1.35f, missChance=.02f };
            if (difficulty == OpponentDifficulty.Club)
                return new OpponentAbility { reaction=.18f, runSpeed=2.7f, reach=.88f, contactHeight=2.55f,
                    racketSpeed=18f, returnDuration=1.5f, missChance=.07f };
            return new OpponentAbility { reaction=.28f, runSpeed=1.7f, reach=.72f, contactHeight=2.3f,
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
    [DefaultExecutionOrder(200)]
    public sealed class BadmintonOpponent : MonoBehaviour
    {
        // Measured imported 1.75 m rig: right shoulder ~= (-.14,1.36,-.06),
        // total arm length .386 m. Grip is .476 m below the racket head, so
        // preparing the HEAD at hand height puts the grip impossibly low.
        public static readonly Vector3 ReadyHeadLocal=new Vector3(-.20f,1.67f,-.28f);
        public static readonly Quaternion ReadyHeadRotation=Quaternion.Euler(0f,180f,0f);
        public Vector3 FeetLocal { get { return feet; } }
        public bool Tracking { get { return tracking; } }
        public bool HasIntercept { get { return intercept.valid; } }
        public int ReturnAttempts { get; private set; }
        public int ObservedShots { get { return shotNumber; } }
        public bool EffortActive { get { return tracking && effortOnly; } }
        public bool EffortSwingActive { get { return effortSwingTime>=0f && effortSwingTime<.42f; } }
        public int EffortSwings { get; private set; }
        public float LastAimError { get; private set; }
        public string LastDecision { get; private set; }
        public ShuttlePhysicsSettings PhysicsSettings { get { return strictSettings; } }
        public Vector3 RacketHeadWorld { get { return racket != null ? racket.position : transform.position; } }
        public Quaternion RacketHeadRotation { get { return racket != null ? racket.rotation : transform.rotation; } }
        public bool RegulationServePlanning { get { return serviceStatus==OpponentServePlanStatus.Pending && serviceSearch!=null; } }
        public AthletePresentation Presentation { get { return athlete; } }
        public Vector3 ServePosition { get { return session.court.TransformPoint(feet+new Vector3(-.20f,1.64f,-.28f)); } }

        private BadmintonSession session;
        private ShuttlePhysicsSettings strictSettings;
        private OpponentAbility ability;
        private OpponentIntercept intercept;
        private Vector3 feet, moveGoal, returnTarget;
        private float reactionRemaining, interceptRemaining, walkPhase, swingRecovery;
        private bool tracking, planned, intentionallyMiss;
        private bool effortOnly, effortStarted;
        private float effortElapsed, effortSwingTime=-1f;
        private Vector3 effortAimLocal, effortSwingAim, effortSwingFrom;
        private ShuttleState observedState;
        private int shotNumber;
        private RacketContact plannedContact;
        private ReturnSearch returnSearch;
        private ReturnSearch serviceSearch;
        private OpponentServePlanStatus serviceStatus=OpponentServePlanStatus.Failed;
        private ShuttleState serviceIncoming;
        private RacketContact serviceContact;
        private Vector3 serviceTarget,serviceShoulder;
        private bool serviceRight;
        private float serviceReach;
        private int servicePosture,serviceSteps;
        private Transform avatar, head, racket, upperArm, forearm, leftLeg, rightLeg;
        private AthletePresentation athlete;
        private Vector3 presentationFeet;
        private Vector3 shoulderOffset=new Vector3(-.143f,1.36f,-.059f);
        private float armLength=.386f;
        private TextMesh nameplate;
        private Material shirtMaterial, skinMaterial, darkMaterial, stringsMaterial;
        private Material[] racketAppearanceMaterials;
        private Mesh[] racketMeshes;
        private OpponentDifficulty displayedDifficulty;
        private bool hasDisplayedDifficulty;

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

        // Dead-ball setup only. The far-side player's own right is court -X.
        // Never use this API to chase a live shuttle or to move the user's head.
        public void PositionForService(bool serverRight)
        {
            if (session==null || (session.shuttle!=null && session.shuttle.Flying)) return;
            ResetRally();
            feet=new Vector3(serverRight ? -1.295f : 1.295f,0f,7.1f);
            moveGoal=presentationFeet=feet;
            UpdateVisuals(0f);
            if (athlete!=null) athlete.ResetPose();
            SamplePresentation(0f);
        }

        // Synchronous test/authoring convenience. Live VR uses Begin + one
        // Advance per rendered frame during the service preparation interval.
        public bool TryPrepareRegulationServe(bool serverRight,Vector3 targetWorld,
            out ShuttleState state,out RacketContact contact)
        {
            state=new ShuttleState();
            contact=new RacketContact();
            if (!BeginRegulationServePlanning(serverRight,targetWorld)) return false;
            for (int i=0;i<1085 && serviceStatus==OpponentServePlanStatus.Pending;i++) StepRegulationServePlanning();
            return ReadRegulationServeResult(out state,out contact)==OpponentServePlanStatus.Success;
        }

        public void CancelRegulationServePlanning()
        {
            serviceSearch=null;
            serviceStatus=OpponentServePlanStatus.Failed;
            serviceContact=new RacketContact();
            serviceIncoming=new ShuttleState();
        }

        public bool BeginRegulationServePlanning(bool serverRight,Vector3 targetWorld)
        {
            CancelRegulationServePlanning();
            if (session==null || racket==null || !Finite(targetWorld) ||
                (session.shuttle!=null && session.shuttle.Flying))
            { LastDecision="SERVICE INPUT OR LIVE BALL"; return false; }
            Vector3 target=session.court.InverseTransformPoint(targetWorld);
            // rightSide in IsInServiceCourt means court +X, not player-local
            // right. An AI right serve therefore lands in the near +X box.
            if (!BadmintonCourt.IsInServiceCourt(target,false,serverRight,false) ||
                target.y<.35f || target.y>2.2f)
            {
                LastDecision="SERVICE TARGET OUTSIDE RECEIVER BOX";
                Debug.LogWarning("[AI Service Planner] Invalid target="+target+"; serverRight="+serverRight);
                return false;
            }
            PositionForService(serverRight);
            CacheAnatomy();
            ability=OpponentAbility.For(session.Difficulty);
            strictSettings=CreateStrictSettings(session.PhysicsSettings);
            // Cork centre 1.04 m; the rendered shuttle's furthest point is
            // < .10 m from that centre, so its ENTIRE body is below 1.15 m.
            // A low forward head, with roll chosen about the real face normal,
            // places the grip near the shoulder without stretching the arm.
            // A two-bone arm has an INNER unreachable radius as well as an
            // outer one. Head-to-shoulder distance here is ~.564 m; subtracting
            // the .476 m grip lever leaves >=.088 m, safely beyond the measured
            // upper/lower-arm length difference. The previous -.40 Z pose could
            // put the grip only .008 m from the shoulder: impossible to fold.
            Vector3 headLocal=feet+new Vector3(-.20f,1.04f,-.52f);
            serviceIncoming=ShuttleState.Launch(session.court.TransformPoint(headLocal),Vector3.down*.25f);
            serviceTarget=targetWorld;
            serviceShoulder=ShoulderWorld;
            serviceReach=armLength-.035f;
            serviceRight=serverRight;
            servicePosture=0;
            serviceStatus=OpponentServePlanStatus.Pending;
            LastAimError=float.PositiveInfinity;
            LastDecision="PREPARING REGULATION SERVE";
            BeginServePosture();
            return true;
        }

        private void BeginServePosture()
        {
            // The short legal arc is attempted first: high arcs previously
            // saturated Easy's physical racket cap and wasted VR preparation.
            OpponentAbility serveAbility=ability;
            serveAbility.returnDuration=servicePosture==1?1.45f:1.15f;
            // Angle the held shuttle with feathers AWAY from the approaching
            // racket, still presenting the cork first. The chosen pre-impact
            // orientation is passed intact to Flight and never replaced later.
            Vector3 tail=servicePosture==2 ? session.court.up :
                (InitialControl(serviceIncoming,serviceTarget,serveAbility.returnDuration,strictSettings)-serviceIncoming.velocity).normalized;
            serviceIncoming.orientation=Quaternion.FromToRotation(Vector3.up,tail);
            serviceSearch=new ReturnSearch(serviceIncoming,serviceTarget,session.court,strictSettings,
                serveAbility,serviceShoulder,serviceReach);
            serviceSteps=0;
        }

        public OpponentServePlanStatus AdvanceRegulationServePlanning(out ShuttleState state,out RacketContact contact)
        {
            // Pausing freezes the search. ResetRally/point end/new incoming
            // contact cancel it; no background thread touches Unity transforms.
            if (session!=null && session.Running) StepRegulationServePlanning();
            return ReadRegulationServeResult(out state,out contact);
        }

        private OpponentServePlanStatus ReadRegulationServeResult(out ShuttleState state,out RacketContact contact)
        {
            state=serviceStatus==OpponentServePlanStatus.Success?serviceIncoming:new ShuttleState();
            contact=serviceStatus==OpponentServePlanStatus.Success?serviceContact:new RacketContact();
            return serviceStatus;
        }

        private void StepRegulationServePlanning()
        {
            if (serviceStatus!=OpponentServePlanStatus.Pending || serviceSearch==null) return;
            if (session==null || (session.shuttle!=null && session.shuttle.Flying))
            { CancelRegulationServePlanning(); return; }
            // Exactly one incremental Newton/prediction step per rendered frame.
            serviceSearch.Step();
            serviceSteps++;
            LastAimError=serviceSearch.Error;
            if (!serviceSearch.Done && serviceSteps<360) return;
            Vector3 landing=Vector3.zero;
            string flightFailure="NOT SOLVED";
            bool legal=serviceSearch.Success && ValidateRegulationServe(serviceIncoming,serviceSearch.Contact,serviceRight,
                session.court,strictSettings,out landing,out flightFailure);
            if (legal)
            {
                serviceContact=serviceSearch.Contact;
                racket.SetPositionAndRotation(serviceContact.center,serviceContact.faceRotation);
                if (athlete!=null) athlete.AlignRacketArm(racket,1f);
                // Validate the actual pose after IK without stretching bones or
                // moving the physically solved contact. A failed pose can only
                // try another legal setup, never be reported as a successful hit.
                legal=Vector3.Distance(GripPosition(serviceContact),ShoulderWorld)<=armLength-.025f &&
                    (athlete==null || Vector3.Distance(athlete.rightHand.position,GripPosition(serviceContact))<.025f);
                if (!legal) flightFailure="ACTUAL ARM POSE";
            }
            if (legal)
            {
                serviceStatus=OpponentServePlanStatus.Success;
                swingRecovery=.22f;
                LastDecision="REGULATION SERVE";
                return;
            }
            ImpactResult diagnostic=RacketImpact.Resolve(serviceIncoming,serviceSearch.Contact,strictSettings);
            Debug.LogWarning("[AI Service Planner] Candidate="+servicePosture+"; right="+serviceRight+
                "; target="+session.court.InverseTransformPoint(serviceTarget).ToString("F3")+
                "; solved="+serviceSearch.Success+"; failure="+serviceSearch.FailureReason+
                "; flight="+flightFailure+"; landing="+landing.ToString("F3")+
                "; error="+serviceSearch.Error.ToString("F4")+"; duration="+serviceSearch.Duration.ToString("F2")+
                "; steps="+serviceSteps+"; racket="+serviceSearch.Contact.centerVelocity.magnitude.ToString("F3")+
                "; outgoing="+session.court.InverseTransformDirection(diagnostic.outgoingVelocity).ToString("F3")+
                "; gripDistance="+Vector3.Distance(GripPosition(serviceSearch.Contact),ShoulderWorld).ToString("F3")+
                "; armLimit="+serviceReach.ToString("F3"));
            if (++servicePosture<3) { BeginServePosture(); return; }
            CancelRegulationServePlanning();
            LastDecision="SERVICE UNAVAILABLE";
        }

        public static bool ValidateRegulationServe(ShuttleState incoming,RacketContact contact,bool serverRight,
            Transform court,ShuttlePhysicsSettings settings,out Vector3 landingLocal)
        {
            string reason;
            return ValidateRegulationServe(incoming,contact,serverRight,court,settings,out landingLocal,out reason);
        }

        private static bool ValidateRegulationServe(ShuttleState incoming,RacketContact contact,bool serverRight,
            Transform court,ShuttlePhysicsSettings settings,out Vector3 landingLocal,out string reason)
        {
            landingLocal=Vector3.zero;
            reason="INVALID INPUT";
            if (court==null || settings==null || settings.seatedAssist || !Finite(incoming.position)) return false;
            Vector3 origin=court.InverseTransformPoint(incoming.position);
            reason="SERVICE ORIGIN OR WHOLE HEIGHT";
            if (origin.y+.10f>=1.15f || origin.y<.20f ||
                !BadmintonCourt.IsInServiceCourt(origin,true,!serverRight,false)) return false;
            ImpactResult impact=RacketImpact.Resolve(incoming,contact,settings);
            Vector3 launch=court.InverseTransformDirection(impact.outgoingVelocity);
            reason="IMPACT OR UPWARD FLIGHT";
            if (!impact.hit || !Finite(launch) || launch.y<=0f || launch.z>=-.1f) return false;
            ShuttleState flight=incoming;
            flight.velocity=impact.outgoingVelocity;
            flight.angularVelocity=impact.angularVelocity;
            bool crossed=false;
            for (float t=0f;t<6f;t+=ShuttlePhysics.Step)
            {
                Vector3 previous=court.InverseTransformPoint(flight.position);
                ShuttlePhysics.Integrate(ref flight,settings,ShuttlePhysics.Step);
                Vector3 local=court.InverseTransformPoint(flight.position);
                reason="NONFINITE FLIGHT";
                if (!Finite(local)) return false;
                Vector3 crossing;
                if (ShuttleMath.NetCrossing(previous,local,out crossing))
                {
                    reason="NET CLEARANCE";
                    if (crossed || crossing.y<BadmintonCourt.NetHeightAt(crossing.x)+.035f ||
                        Mathf.Abs(crossing.x)>BadmintonCourt.SinglesHalfWidth) return false;
                    crossed=true;
                }
                if (local.y<=ShuttlePhysicsSettings.CorkRadius)
                {
                    float fraction=Mathf.InverseLerp(previous.y,local.y,ShuttlePhysicsSettings.CorkRadius);
                    landingLocal=Vector3.Lerp(previous,local,fraction);
                    bool legal=crossed && BadmintonCourt.IsInServiceCourt(landingLocal,false,serverRight,false);
                    reason=legal?"NONE":"DIAGONAL LANDING";
                    return legal;
                }
                reason="COURT EXIT";
                if (local.y>12f || local.z<BadmintonCourt.NearBaseline-.05f ||
                    local.z>BadmintonCourt.FarBaseline+.05f || Mathf.Abs(local.x)>BadmintonCourt.SinglesHalfWidth+.05f)
                    return false;
            }
            reason="FLIGHT TIME LIMIT";
            return false;
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
            CancelRegulationServePlanning();
            tracking=planned=intentionallyMiss=false;
            effortOnly=effortStarted=false;
            effortElapsed=0f;
            effortSwingTime=-1f;
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
                racket.localPosition=ReadyHeadLocal;
                racket.localRotation=ReadyHeadRotation;
            }
            LastDecision="READY";
            UpdateVisuals(0f);
            presentationFeet=feet;
            if (athlete!=null) athlete.ResetPose();
            SamplePresentation(0f);
        }

        // Animation is sampled once per frame after all shuttle substeps. Its
        // IK follows the existing racket; it never drives physical contact.
        public void SamplePresentation(float dt)
        {
            if (athlete==null || session==null || (dt>0f && !session.Running)) return;
            Vector3 velocity=dt>0f ? (feet-presentationFeet)/dt : Vector3.zero;
            presentationFeet=feet;
            Vector3 lookTarget=tracking ? observedState.position : session.playerCamera.transform.position;
            float swing=EffortSwingActive ? Mathf.Sin(Mathf.PI*Mathf.Clamp01(effortSwingTime/.42f)) :
                (tracking && planned ? Mathf.InverseLerp(.35f,0f,interceptRemaining) : Mathf.Clamp01(swingRecovery/.18f));
            athlete.SamplePose(Mathf.Max(0f,dt),velocity,lookTarget,racket,swing);
            // Locomotion may move the shoulder slightly. Keep non-contact
            // presentation in anatomical reach, then align IK even at 120 Hz.
            if (LastDecision!="RETURN" || swingRecovery<=0f) ConstrainVisualGrip();
            athlete.AlignRacketArm(racket,swing);
        }

        private Vector3 ShoulderWorld
        {
            get { return athlete!=null && athlete.rightUpperArm!=null ? athlete.rightUpperArm.position :
                session.court.TransformPoint(feet+shoulderOffset); }
        }

        private void CacheAnatomy()
        {
            if (athlete==null || athlete.rightUpperArm==null) return;
            shoulderOffset=avatar.InverseTransformPoint(athlete.rightUpperArm.position);
            armLength=Vector3.Distance(athlete.rightUpperArm.position,athlete.rightForearm.position)+
                Vector3.Distance(athlete.rightForearm.position,athlete.rightHand.position);
        }

        private void ConstrainVisualGrip()
        {
            if (racket==null || session==null) return;
            Vector3 grip=racket.TransformPoint(RacketSpecification.GripCenter-RacketSpecification.HeadCenter);
            Vector3 shoulder=ShoulderWorld;
            racket.position+=shoulder+Vector3.ClampMagnitude(grip-shoulder,Mathf.Max(.1f,armLength-.008f))-grip;
        }

        public static Quaternion OrientTowardShoulder(Vector3 normal,Vector3 head,Vector3 shoulder)
        {
            // Roll about a FIXED face normal minimizes shoulder-to-grip distance.
            // The full contact model is still evaluated with this orientation.
            Vector3 shaft=Vector3.ProjectOnPlane(head-shoulder,normal);
            if (shaft.sqrMagnitude<.000001f) shaft=Vector3.ProjectOnPlane(Vector3.up,normal);
            if (shaft.sqrMagnitude<.000001f) shaft=Vector3.ProjectOnPlane(Vector3.right,normal);
            return Quaternion.LookRotation(normal,shaft.normalized);
        }

        public static Vector3 GripPosition(RacketContact contact)
        { return contact.center+contact.faceRotation*(RacketSpecification.GripCenter-RacketSpecification.HeadCenter); }

        private void LateUpdate()
        {
            if (session!=null && session.Running && session.IsAiMatch) SamplePresentation(Time.deltaTime);
        }

        public void ResetMatch()
        {
            shotNumber=0;
            ReturnAttempts=0;
            EffortSwings=0;
            ResetRally();
        }

        public void BeginTracking(ShuttleState incoming)
        {
            if (session == null) return;
            CancelRegulationServePlanning();
            ability=OpponentAbility.For(session.Difficulty);
            strictSettings=CreateStrictSettings(session.PhysicsSettings);
            tracking=true;
            planned=false;
            intercept=new OpponentIntercept();
            reactionRemaining=ability.reaction;
            moveGoal=feet;
            shotNumber++;
            // Deterministic timing mistakes, not a decision to stop trying.
            // A missed read still causes pursuit and a bounded recovery swing.
            uint hash=unchecked((uint)(shotNumber*747796405+289133645));
            hash=(hash^(hash>>16))*2246822519u;
            intentionallyMiss=shotNumber>1 && (hash%10000u)/10000f < ability.missChance;
            PlanIncoming(incoming,ability.reaction);
        }

        public void OnIncomingDeflected(ShuttleState incoming)
        {
            if (session==null) return;
            // A legal tape contact changes the trajectory, not the striker.
            // Keep the current stroke number and its sampled mistake decision.
            PlanIncoming(incoming,Mathf.Min(ability.reaction,.12f));
        }

        private void PlanIncoming(ShuttleState incoming,float delay)
        {
            tracking=true;
            planned=false;
            effortOnly=effortStarted=false;
            effortElapsed=0f;
            effortSwingTime=-1f;
            observedState=incoming;
            returnSearch=null;
            intercept=new OpponentIntercept();
            reactionRemaining=delay;
            moveGoal=feet;
            LastAimError=float.PositiveInfinity;
            LastDecision="READING SHOT";
            if (intentionallyMiss) { SwitchToEffort(incoming,"CHASE / MISREAD SHOT"); return; }
            CacheAnatomy();
            returnTarget=session.GetOpponentTarget(session.Difficulty==OpponentDifficulty.Easy ? 0 : shotNumber);
            // Predict without moving: decision computation is spread over the
            // reaction interval, while the body still observes that full delay.
            ShuttleState afterReaction=ShuttlePhysics.Predict(incoming,delay,strictSettings);
            intercept=FindInterceptCore(afterReaction,feet,session.court,strictSettings,ability,
                true,shoulderOffset,Mathf.Max(.1f,armLength-.065f),returnTarget);
            if (!intercept.valid)
            {
                SwitchToEffort(incoming,"CHASE / OUT OF REACH");
                return;
            }
            moveGoal=intercept.feetLocal;
            interceptRemaining=delay+intercept.time;
            Vector3 contactFeet=StepFeet(feet,moveGoal,ability.runSpeed,intercept.time);
            returnSearch=new ReturnSearch(intercept.state,returnTarget,session.court,strictSettings,ability,
                session.court.TransformPoint(contactFeet+shoulderOffset),Mathf.Max(.1f,armLength-.045f));
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
                SwitchToEffort(observedState,"CHASE / NO CONTROLLED RETURN");
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
            observedState=next;
            if (effortOnly) { TickEffort(next,dt); return false; }
            reactionRemaining-=dt;
            if (intercept.valid) interceptRemaining-=dt;
            if (reactionRemaining>0f) { UpdateVisuals(dt); return false; }
            if (intercept.valid)
            {
                feet=StepFeet(feet,moveGoal,ability.runSpeed,dt);
            }
            UpdateVisuals(dt);
            if (!planned)
            {
                if (interceptRemaining<.02f) SwitchToEffort(next,"CHASE / LATE PLAN");
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
                if (Vector3.Distance(GripPosition(contact),ShoulderWorld)>armLength-.025f)
                {
                    SwitchToEffort(next,"CHASE / WRIST OUT OF REACH");
                    return false;
                }
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
                SwitchToEffort(next,"CHASE / MISSED CONTACT");
            }
            return false;
        }

        // Faulted trajectories can advance the visible effort without ever
        // producing a hit. Flight owns legal contacts, points and rally lifetime.
        public void ContinueEffort(ShuttleState incoming,float dt)
        {
            if (!tracking || session==null || dt<=0f) return;
            if (!effortOnly) SwitchToEffort(incoming,"CHASE / DEFLECTED SHOT");
            observedState=incoming;
            TickEffort(incoming,Mathf.Min(dt,.08f));
        }

        public void OnPointEnded()
        {
            CancelRegulationServePlanning();
            tracking=planned=effortOnly=false;
            returnSearch=null;
            intercept=new OpponentIntercept();
            reactionRemaining=0f;
            moveGoal=feet;
            // Do not teleport the body or start a fake late hit. An already
            // started swing may finish through Idle while the next point waits.
        }

        private void SwitchToEffort(ShuttleState incoming,string decision)
        {
            tracking=true;
            effortOnly=true;
            planned=false;
            returnSearch=null;
            intercept=new OpponentIntercept();
            LastDecision=decision;
            observedState=incoming;
            Vector3 local=session.court.InverseTransformPoint(incoming.position);
            Vector3 velocity=session.court.InverseTransformDirection(incoming.velocity);
            // An urgent visual reach can begin sooner than a controlled return
            // decision. It has NO contact authority and retains difficulty order.
            float airTime=TimeToFloor(local.y,velocity.y);
            reactionRemaining=Mathf.Min(Mathf.Max(0f,reactionRemaining),
                Mathf.Max(.008f,Mathf.Min(ability.reaction*.5f,airTime*.20f)));
            UpdateEffortGoal(local,velocity);
        }

        private static float TimeToFloor(float height,float verticalSpeed)
        {
            float h=Mathf.Max(0f,height-.025f);
            return Mathf.Max(0f,(verticalSpeed+Mathf.Sqrt(verticalSpeed*verticalSpeed+2f*ShuttlePhysics.Gravity*h))/ShuttlePhysics.Gravity);
        }

        private void UpdateEffortGoal(Vector3 local,Vector3 velocity)
        {
            // Cheap reactive pursuit, not another iterative flight solve. The
            // feet stay inside their court even when the shuttle is wide/short.
            float lookAhead=Mathf.Min(.16f,TimeToFloor(local.y,velocity.y)*.25f);
            effortAimLocal=local+velocity*lookAhead;
            moveGoal=new Vector3(Mathf.Clamp(effortAimLocal.x,-2.45f,2.45f),0f,
                Mathf.Clamp(effortAimLocal.z+.32f,ShuttleMath.NetZ+.30f,10.15f));
        }

        private void TickEffort(ShuttleState incoming,float dt)
        {
            Vector3 local=session.court.InverseTransformPoint(incoming.position);
            Vector3 velocity=session.court.InverseTransformDirection(incoming.velocity);
            reactionRemaining-=dt;
            UpdateEffortGoal(local,velocity);
            if (reactionRemaining<=0f)
            {
                feet=StepFeet(feet,moveGoal,ability.runSpeed,dt);
                effortElapsed+=dt;
                float planarDistance=new Vector2(local.x-feet.x,local.z-feet.z).magnitude;
                if (!effortStarted && (TimeToFloor(local.y,velocity.y)<.24f ||
                    planarDistance<ability.reach+.25f || effortElapsed>.28f)) StartEffortSwing();
            }
            UpdateVisuals(dt);
        }

        private void StartEffortSwing()
        {
            effortStarted=true;
            effortSwingTime=0f;
            EffortSwings++;
            effortSwingFrom=racket!=null ? racket.localPosition : ReadyHeadLocal;
            Vector3 relative=effortAimLocal-feet;
            Vector3 horizontal=Vector3.ClampMagnitude(new Vector3(relative.x,0f,relative.z),ability.reach);
            effortSwingAim=new Vector3(horizontal.x,Mathf.Clamp(relative.y,.30f,ability.contactHeight),horizontal.z);
            LastDecision="RECOVERY SWING / NO CONTACT";
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
            return FindInterceptCore(incoming,feetLocal,court,settings,ability,false,Vector3.zero,0f,Vector3.zero);
        }

        private static OpponentIntercept FindInterceptCore(ShuttleState incoming, Vector3 feetLocal, Transform court,
            ShuttlePhysicsSettings settings, OpponentAbility ability,bool anatomical,Vector3 shoulder,float reachLength,Vector3 target)
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
                    Mathf.Abs(crossing.x)<=BadmintonCourt.DoublesHalfWidth &&
                    crossing.y<BadmintonCourt.NetHeightAt(crossing.x)+.018f) return result;
                if (local.y<.35f || local.z>10.5f || Mathf.Abs(local.x)>3.5f) return result;
                float time=i*predictionStep;
                if (time<.16f || local.z<=ShuttleMath.NetZ+.30f || local.z>10.15f ||
                    Mathf.Abs(local.x)>2.55f || local.y>.98f*ability.contactHeight || local.y<.65f) continue;
                Vector3 goal=Vector3.zero,possible=Vector3.zero;
                float bestCost=float.PositiveInfinity;
                Vector3 normal=anatomical ? (InitialControl(state,target,ability.returnDuration,settings)-state.velocity).normalized : Vector3.up;
                // A horizontal racket needs room BESIDE the shoulder. A fixed
                // body-under-head stance can be impossible even with optimal
                // roll. Search a small, allocation-free forehand/backhand grid.
                for (int stance=0;stance<(anatomical?9:1);stance++)
                {
                    float offsetX=anatomical ? (stance%3==0?.50f:stance%3==1?-.22f:.14f) : 0f;
                    float offsetZ=anatomical ? (stance/3==0?.18f:stance/3==1?.42f:-.06f) : .32f;
                    Vector3 candidateGoal=new Vector3(Mathf.Clamp(local.x+offsetX,-2.4f,2.4f),0f,
                        Mathf.Clamp(local.z+offsetZ,ShuttleMath.NetZ+.35f,10.15f));
                    Vector3 candidateFeet=StepFeet(feetLocal,candidateGoal,ability.runSpeed,Mathf.Max(0f,time-.03f));
                    float reach=new Vector2(local.x-candidateFeet.x,local.z-candidateFeet.z).magnitude;
                    if (reach>ability.reach-.08f) continue;
                    Vector3 body=StepFeet(feetLocal,candidateGoal,ability.runSpeed,time);
                    float cost=Vector3.Distance(feetLocal,body)+stance*.001f;
                    if (anatomical)
                    {
                        Vector3 shoulderWorld=court.TransformPoint(body+shoulder);
                        Quaternion orientation=OrientTowardShoulder(normal,state.position,shoulderWorld);
                        Vector3 grip=state.position+orientation*(RacketSpecification.GripCenter-RacketSpecification.HeadCenter);
                        float gripReach=Vector3.Distance(grip,shoulderWorld);
                        if (gripReach>reachLength) continue;
                        cost+=gripReach*.3f;
                    }
                    if (cost>=bestCost) continue;
                    bestCost=cost; goal=candidateGoal; possible=candidateFeet;
                }
                if (float.IsPositiveInfinity(bestCost)) continue;
                // Refine only the first viable candidate, not every anatomically
                // rejected point: BeginTracking must not contain many replays.
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
            public string FailureReason { get; private set; }
            public float Duration { get { return duration; } }
            private readonly ShuttleState incoming;
            private readonly Vector3 target;
            private readonly Transform court;
            private readonly ShuttlePhysicsSettings settings;
            private readonly OpponentAbility ability;
            private readonly Vector3 shoulder;
            private readonly float armReach;
            private int durationIndex,iteration,phase,lineAttempt;
            private float duration,currentError,lineScale;
            private Vector3 control,correction,basePoint,dx,dy,dz;
            private RacketContact currentContact;
            private const float Epsilon=.16f;

            public ReturnSearch(ShuttleState state,Vector3 destination,Transform reference,
                ShuttlePhysicsSettings model,OpponentAbility capabilities)
                : this(state,destination,reference,model,capabilities,Vector3.zero,0f) { }

            public ReturnSearch(ShuttleState state,Vector3 destination,Transform reference,
                ShuttlePhysicsSettings model,OpponentAbility capabilities,Vector3 shoulderWorld,float anatomicalReach)
            {
                incoming=state; target=destination; court=reference;
                settings=model; ability=capabilities;
                shoulder=shoulderWorld; armReach=anatomicalReach;
                Error=float.PositiveInfinity;
                FailureReason="NOT FINISHED";
                BeginDuration();
            }

            private void BeginDuration()
            {
                if (durationIndex>=3) { Done=true; return; }
                duration=ReturnDuration(ability,durationIndex);
                // Cheap ballistic/drag seed only. Unlike SolveLaunch this has no
                // hidden synchronous iterative flight predictions at creation.
                control=InitialControl(incoming,target,duration,settings);
                phase=iteration=lineAttempt=0;
            }

            private void NextDuration() { durationIndex++; BeginDuration(); }

            public void Step()
            {
                if (Done) return;
                if (phase==4)
                {
                    bool reachable=armReach<=0f || Vector3.Distance(GripPosition(currentContact),shoulder)<=armReach;
                    if (reachable && ClearsNet(incoming,currentContact,target,duration,court,settings))
                    {
                        Contact=currentContact; Error=currentError; Done=Success=true; FailureReason="NONE";
                    }
                    else { FailureReason=reachable?"NET OR TARGET":"ARM REACH"; NextDuration(); }
                    return;
                }
                Vector3 query=control;
                if (phase==1) query+=Vector3.right*Epsilon;
                if (phase==2) query+=Vector3.up*Epsilon;
                if (phase==3) query+=Vector3.forward*Epsilon;
                if (phase==5) query+=correction*lineScale;
                RacketContact candidate;
                ShuttleState predicted;
                if (!Evaluate(incoming,query,duration,settings,ability,shoulder,armReach>0f,out candidate,out predicted))
                {
                    FailureReason="NO PHYSICAL CONTACT";
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
                    if (Mathf.Abs(determinant)<1e-12f) { FailureReason="SINGULAR CONTROL"; NextDuration(); return; }
                    correction=new Vector3(Vector3.Dot(rhs,Vector3.Cross(b,c)),
                        Vector3.Dot(a,Vector3.Cross(rhs,c)),Vector3.Dot(a,Vector3.Cross(b,rhs)))/determinant;
                    correction=Vector3.ClampMagnitude(correction,12f);
                    if (!Finite(correction)) { FailureReason="NONFINITE CONTROL"; NextDuration(); return; }
                    lineScale=1f; lineAttempt=0; phase=5;
                    return;
                }
                if (phase==5)
                {
                    if (distance<currentError-.000001f)
                    {
                        control=query;
                        iteration++;
                        if (iteration>=12 && distance>=.065f) { FailureReason="CONTROL ITERATION LIMIT"; NextDuration(); return; }
                        AcceptBase(candidate,predicted.position,distance);
                    }
                    else
                    {
                        lineScale*=.5f;
                        lineAttempt++;
                        if (lineAttempt>=5) { FailureReason="CONTROL LINE SEARCH"; NextDuration(); }
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
            ShuttlePhysicsSettings settings, OpponentAbility ability,Vector3 shoulder,bool anatomical,
            out RacketContact contact, out ShuttleState predicted)
        {
            Vector3 impulse=control-incoming.velocity;
            Vector3 normal=impulse.normalized;
            float speed=Vector3.Dot(incoming.velocity,normal)+impulse.magnitude/
                (1f+.78f)*(1f+settings.mass/settings.RacketContactMass(Vector3.zero,Vector3.forward));
            speed=Mathf.Clamp(speed,-2f,ability.racketSpeed);
            contact=new RacketContact { point=incoming.position, center=incoming.position,
                centerVelocity=normal*speed, surfaceNormal=normal,
                faceRotation=anatomical ? OrientTowardShoulder(normal,incoming.position,shoulder) :
                    Quaternion.LookRotation(normal,Mathf.Abs(normal.y)>.95f?Vector3.forward:Vector3.up),
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

        private static Vector3 InitialControl(ShuttleState incoming,Vector3 target,float duration,ShuttlePhysicsSettings settings)
        {
            Vector3 control=(target-incoming.position)/duration+Vector3.up*ShuttlePhysics.Gravity*duration*.5f;
            return control*Mathf.Exp(Mathf.Min(1.1f,settings.LinearDragFactor*Vector3.Distance(incoming.position,target)*.35f));
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
            stringsMaterial=Material(new Color(.82f,.85f,.80f));
            racketAppearanceMaterials=new Material[7];
            for(int i=0;i<racketAppearanceMaterials.Length;i++)
                racketAppearanceMaterials[i]=RacketAppearance.Create((RacketMaterialPart)i);
            if (session.athletePrefab!=null)
            {
                athlete=Instantiate(session.athletePrefab,avatar,false);
                athlete.name="Animated badminton athlete";
            }
            else
            {
                // Primitive fallback keeps physics-only test/older scenes usable.
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
                Segment(idleArm,new Vector3(.24f,1.38f,0f),new Vector3(.36f,.96f,-.16f),.065f);
            }
            racket=new GameObject("AI racket contact head").transform;
            racket.SetParent(avatar,false);
            Transform racketModel=new GameObject("AI shared racket model").transform;
            racketModel.SetParent(racket,false);
            racketModel.localPosition=-RacketSpecification.HeadCenter;
            RacketGeometry.Build(racketModel,racketAppearanceMaterials[(int)RacketMaterialPart.Frame],
                racketAppearanceMaterials[(int)RacketMaterialPart.BlueGraphics],
                racketAppearanceMaterials[(int)RacketMaterialPart.Strings],
                racketAppearanceMaterials[(int)RacketMaterialPart.Grip],
                racketAppearanceMaterials[(int)RacketMaterialPart.WhiteGraphics],
                racketAppearanceMaterials[(int)RacketMaterialPart.OrangeGraphics],
                racketAppearanceMaterials[(int)RacketMaterialPart.Stencil]);
            MeshFilter[] generated=racketModel.GetComponentsInChildren<MeshFilter>();
            racketMeshes=new Mesh[generated.Length];
            for (int i=0;i<generated.Length;i++) racketMeshes[i]=generated[i].sharedMesh;
            nameplate=new GameObject("AI difficulty label").AddComponent<TextMesh>();
            nameplate.transform.SetParent(avatar,false);
            // Beside the head: a floating label above it overlaps the distant
            // scoreboard when viewed from the stationary practice position.
            nameplate.transform.localPosition=new Vector3(-.82f,1.72f,-.08f);
            nameplate.fontSize=48;
            nameplate.characterSize=.045f;
            nameplate.anchor=TextAnchor.MiddleCenter;
            nameplate.alignment=TextAlignment.Center;
            nameplate.color=Color.white;
            GameLocalization.ApplyFont(nameplate);
        }

        private void UpdateVisuals(float dt)
        {
            if (avatar==null || session==null) return;
            avatar.localPosition=feet;
            bool moving=tracking && reactionRemaining<=0f && (intercept.valid || effortOnly) && Vector3.Distance(feet,moveGoal)>.05f;
            walkPhase+=dt*(moving?10f:1.8f);
            if (leftLeg!=null) leftLeg.localRotation=Quaternion.Euler(moving?Mathf.Sin(walkPhase)*24f:0f,0f,0f);
            if (rightLeg!=null) rightLeg.localRotation=Quaternion.Euler(moving?-Mathf.Sin(walkPhase)*24f:0f,0f,0f);
            swingRecovery=Mathf.Max(0f,swingRecovery-dt);
            Vector3 idle=ReadyHeadLocal;
            if (EffortSwingActive)
            {
                effortSwingTime+=dt;
                float phase=Mathf.Clamp01(effortSwingTime/.42f);
                Vector3 follow=effortSwingAim+new Vector3(effortSwingAim.x<0f?-.18f:.18f,.06f,-.16f);
                Vector3 aim=phase<.55f ? Vector3.Lerp(effortSwingFrom,effortSwingAim,Mathf.SmoothStep(0f,1f,phase/.55f)) :
                    Vector3.Lerp(effortSwingAim,follow,Mathf.SmoothStep(0f,1f,(phase-.55f)/.45f));
                Vector3 flat=Vector3.ClampMagnitude(new Vector3(aim.x,0f,aim.z),ability.reach);
                aim=new Vector3(flat.x,Mathf.Clamp(aim.y,.30f,ability.contactHeight),flat.z);
                racket.localPosition=Vector3.MoveTowards(racket.localPosition,aim,Mathf.Min(8f,ability.racketSpeed)*dt);
                Quaternion swingRotation=Quaternion.Euler(-20f+55f*phase,180f+75f*(phase-.5f),-35f+70f*phase);
                racket.localRotation=Quaternion.RotateTowards(racket.localRotation,swingRotation,720f*dt);
            }
            else if (tracking && effortOnly)
            {
                racket.localPosition=Vector3.MoveTowards(racket.localPosition,idle,Mathf.Min(8f,ability.racketSpeed)*dt);
                racket.localRotation=Quaternion.RotateTowards(racket.localRotation,ReadyHeadRotation,360f*dt);
            }
            else if (intercept.valid && tracking && planned)
            {
                float swing=Mathf.InverseLerp(.35f,0f,interceptRemaining);
                float u2=swing*swing,u3=u2*swing;
                // Hermite approach ends with the same head velocity used by the
                // impact model, rather than a visually unrelated linear lerp.
                racket.position=(2f*u3-3f*u2+1f)*avatar.TransformPoint(idle)+
                    (-2f*u3+3f*u2)*intercept.state.position+
                    (u3-u2)*plannedContact.centerVelocity*.35f;
                racket.rotation=Quaternion.Slerp(session.court.rotation*ReadyHeadRotation,plannedContact.faceRotation,swing*swing*(3f-2f*swing));
            }
            else if (swingRecovery<=0f)
            {
                racket.localPosition=Vector3.Lerp(racket.localPosition,idle,Mathf.Min(1f,dt*9f+.01f));
                racket.localRotation=Quaternion.Slerp(racket.localRotation,ReadyHeadRotation,Mathf.Min(1f,dt*9f+.01f));
            }
            if (LastDecision!="RETURN" || swingRecovery<=0f) ConstrainVisualGrip();
            Vector3 hand=avatar.InverseTransformPoint(racket.TransformPoint(RacketSpecification.GripCenter-RacketSpecification.HeadCenter));
            Vector3 shoulder=new Vector3(-.23f,1.42f,0f);
            Vector3 elbow=Vector3.Lerp(shoulder,hand,.50f)+new Vector3(-.07f,-.12f,.04f);
            if (upperArm!=null) Segment(upperArm,shoulder,elbow,.065f);
            if (forearm!=null) Segment(forearm,elbow,hand,.054f);
            if (!hasDisplayedDifficulty || displayedDifficulty!=session.Difficulty)
            {
                displayedDifficulty=session.Difficulty;
                hasDisplayedDifficulty=true;
                nameplate.text="AI / "+GameLocalization.Difficulty(displayedDifficulty);
            }
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
            foreach (Material material in new[] { shirtMaterial,skinMaterial,darkMaterial,stringsMaterial })
                if (material!=null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            if(racketAppearanceMaterials!=null)
                foreach(Material material in racketAppearanceMaterials)
                    if(material!=null) { if(Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        }
    }
}
