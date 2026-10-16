using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SpatialTracking;

namespace ViveShuttleVR.Editor
{
    public static class RegulationServeRegression
    {
        private static int checks;

        public static int Run()
        {
            EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
            BadmintonSession session=UnityEngine.Object.FindObjectOfType<BadmintonSession>();
            if (session==null) throw new InvalidOperationException("Regulation service scene has no session");
            TrackedPoseDriver driver=session.playerCamera.GetComponent<TrackedPoseDriver>();
            bool enabled=driver!=null && driver.enabled;
            try
            {
                if (driver!=null) driver.enabled=false;
                if (!session.Recenter()) throw new InvalidOperationException("Regulation service recenter failed");
                return Run(session);
            }
            finally { if (driver!=null) driver.enabled=enabled; }
        }

        public static int Run(BadmintonSession session)
        {
            checks=0;
            Require(session!=null && session.opponent!=null && session.shuttle!=null,"Missing playable AI scene");
            Vector3 courtPosition=session.court.position;
            Quaternion courtRotation=session.court.rotation;
            Vector3 headPosition=session.playerCamera.transform.position;
            Quaternion headRotation=session.playerCamera.transform.rotation;
            OpponentDifficulty difficulty=session.Difficulty;
            ShuttlePhysicsSettings physics=session.physics;
            int playerPoints=session.PlayerPoints,opponentPoints=session.OpponentPoints;
            double maxMs=0,totalMs=0;
            int cases=0;
            try
            {
                session.shuttle.Stop();
                session.physics=BadmintonOpponent.CreateStrictSettings(new ShuttlePhysicsSettings());
                foreach (float yaw in new[] { 0f,135f })
                foreach (OpponentDifficulty level in Enum.GetValues(typeof(OpponentDifficulty)))
                foreach (bool right in new[] { true,false })
                {
                    session.court.SetPositionAndRotation(new Vector3(1.4f,.35f,-.8f),Quaternion.Euler(0f,yaw,0f));
                    session.Difficulty=level;
                    session.opponent.ResetRally();
                    int attempts=session.opponent.ReturnAttempts,observed=session.opponent.ObservedShots;
                    Vector3 targetLocal=new Vector3((right?1.295f:-1.295f)+.14f,1.67f,.44f);
                    Vector3 target=session.court.TransformPoint(targetLocal);
                    ShuttleState state;
                    RacketContact contact;
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    bool prepared=session.opponent.TryPrepareRegulationServe(right,target,out state,out contact);
                    watch.Stop();
                    double ms=watch.Elapsed.TotalMilliseconds;
                    totalMs+=ms; maxMs=Math.Max(maxMs,ms); cases++;
                    string label=level+" right="+right+" yaw="+yaw;
                    Require(prepared,"Physical service planner failed: "+label+" decision="+
                        session.opponent.LastDecision+" error="+session.opponent.LastAimError);
                    Require(!session.shuttle.Flying,"Preparing a service launched the shuttle itself: "+label);
                    Require(Vector3.Distance(session.opponent.FeetLocal,new Vector3(right?-1.295f:1.295f,0f,7.1f))<.0001f,
                        "Wrong player's own service side: "+label);
                    Require(!session.opponent.PhysicsSettings.seatedAssist,"Service used a powered assist");
                    Require(contact.centerVelocity.magnitude<=OpponentAbility.For(level).racketSpeed+.0001f,"Racket speed cap exceeded");
                    Require(Vector3.Distance(contact.point,state.position)<.00001f &&
                        Vector3.Distance(contact.center,state.position)<.00001f,"Service is not a real central cork contact");
                    Vector3 local=session.court.InverseTransformPoint(state.position);
                    Require(local.y+.10f<1.15f,"The whole shuttle is not below 1.15 m");
                    Require(session.StandardShuttleTop(state)<1.15f,"Rendered feather tips exceed the legal service height");
                    Require(Vector3.Dot(state.orientation*Vector3.up,contact.surfaceNormal)>0f,
                        "Feathers face the approaching racket instead of presenting the cork first");
                    ImpactResult impact=RacketImpact.Resolve(state,contact,session.opponent.PhysicsSettings);
                    Require(impact.hit,"Strict contact did not resolve");
                    Vector3 velocity=session.court.InverseTransformDirection(impact.outgoingVelocity);
                    Require(velocity.y>0f && velocity.z<0f,"Service does not initially rise toward receiver");
                    Vector3 landing;
                    Require(BadmintonOpponent.ValidateRegulationServe(state,contact,right,session.court,
                        session.opponent.PhysicsSettings,out landing),"Actual physical service flight is illegal: "+label);
                    Require(BadmintonCourt.IsInServiceCourt(landing,false,right,false),"Diagonal singles service landing failed");
                    VerifyUnreturnedFlight(state,impact,target,session.court,session.opponent.PhysicsSettings,right,label);
                    AthletePresentation athlete=session.opponent.Presentation;
                    Require(athlete!=null,"Actual service scene has no human athlete");
                    float length=Vector3.Distance(athlete.rightUpperArm.position,athlete.rightForearm.position)+
                        Vector3.Distance(athlete.rightForearm.position,athlete.rightHand.position);
                    Vector3 grip=BadmintonOpponent.GripPosition(contact);
                    Require(Vector3.Distance(grip,athlete.rightUpperArm.position)<=length-.02f,
                        "Service grip is outside anatomical reach: "+label);
                    Require(Vector3.Distance(athlete.rightHand.position,grip)<.025f,"Service hand floats away from physical grip");
                    Require(Vector3.Distance(session.opponent.RacketHeadWorld,contact.center)<.00001f &&
                        Quaternion.Angle(session.opponent.RacketHeadRotation,contact.faceRotation)<.01f,"Visible racket differs from physical action");
                    Require(session.opponent.ReturnAttempts==attempts && session.opponent.ObservedShots==observed,
                        "Preparation registered a rally hit or new incoming shot");
                    Require(session.PlayerPoints==playerPoints && session.OpponentPoints==opponentPoints,"Preparation awarded a point");
                    Require(session.playerCamera.transform.position==headPosition &&
                        session.playerCamera.transform.rotation==headRotation,"Service moved the headset");
                    Debug.Log("[Regulation Service] "+label+" racket="+contact.centerVelocity.magnitude.ToString("F2")+
                        "m/s; outgoing="+velocity+"; landing="+landing+"; planMs="+ms.ToString("F2"));
                }
                TestSafeFailure(session);
                TestIncrementalPlanning(session);
            }
            finally
            {
                session.shuttle.Stop();
                session.physics=physics;
                session.Difficulty=difficulty;
                session.court.SetPositionAndRotation(courtPosition,courtRotation);
                session.opponent.ResetRally();
            }
            Debug.Log("[Regulation Service] PASS "+checks+" checks across "+cases+
                " physical services; mean prepareMs="+(totalMs/cases).ToString("F2")+"; max="+maxMs.ToString("F2")+
                ". Pre-point synchronous preparation, not a continuous 90 FPS timing claim.");
            return checks;
        }

        private static void VerifyUnreturnedFlight(ShuttleState state,ImpactResult impact,Vector3 target,
            Transform court,ShuttlePhysicsSettings settings,bool right,string label)
        {
            state.velocity=impact.outgoingVelocity;
            state.angularVelocity=impact.angularVelocity;
            float closest=float.PositiveInfinity;
            bool crossed=false,landed=false;
            // Independent full-flight replay at the runtime's 360 Hz step.
            for (int i=0;i<2160;i++)
            {
                Vector3 before=court.InverseTransformPoint(state.position);
                ShuttlePhysics.Integrate(ref state,settings,ShuttlePhysics.Step);
                Vector3 local=court.InverseTransformPoint(state.position);
                closest=Mathf.Min(closest,Vector3.Distance(state.position,target));
                Vector3 crossing;
                if (ShuttleMath.NetCrossing(before,local,out crossing))
                {
                    Require(crossing.y>BadmintonCourt.NetHeightAt(crossing.x)+.018f,"Service struck net: "+label);
                    crossed=true;
                }
                if (local.y<=ShuttlePhysicsSettings.CorkRadius)
                {
                    Vector3 landing=Vector3.Lerp(before,local,Mathf.InverseLerp(before.y,local.y,ShuttlePhysicsSettings.CorkRadius));
                    Require(crossed && BadmintonCourt.IsInServiceCourt(landing,false,right,false),"Replay landed outside service court: "+label);
                    landed=true;
                    break;
                }
            }
            Require(landed,"Physical service never landed: "+label);
            Require(closest<.08f,"Physical serve missed the player's reachable reception target: "+closest+" "+label);
        }

        private static void TestSafeFailure(BadmintonSession session)
        {
            ShuttleState state;
            RacketContact contact;
            Vector3 feet=session.opponent.FeetLocal;
            Require(!session.opponent.TryPrepareRegulationServe(true,new Vector3(float.NaN,0f,0f),out state,out contact),
                "NaN service target accepted");
            Require(!session.opponent.TryPrepareRegulationServe(true,session.court.TransformPoint(new Vector3(-1.3f,1.6f,.44f)),out state,out contact),
                "Wrong diagonal receiver court accepted");
            Require(!session.opponent.TryPrepareRegulationServe(true,session.court.TransformPoint(new Vector3(1.3f,1.6f,2.5f)),out state,out contact),
                "Target in the forbidden short-service area accepted");
            Require(session.opponent.FeetLocal==feet,"Rejected invalid input changed dead-ball stance");
            Vector3 target=session.court.TransformPoint(new Vector3(1.435f,1.67f,.44f));
            Require(session.opponent.TryPrepareRegulationServe(true,target,out state,out contact),"Safety fixture did not prepare");
            ShuttleState high=state;
            high.position+=Vector3.up*.06f;
            Vector3 landing;
            Require(!BadmintonOpponent.ValidateRegulationServe(high,contact,true,session.court,session.opponent.PhysicsSettings,out landing),
                "Whole-shuttle height fault was accepted");
            var assisted=BadmintonOpponent.CreateStrictSettings(session.opponent.PhysicsSettings);
            assisted.seatedAssist=true;
            Require(!BadmintonOpponent.ValidateRegulationServe(state,contact,true,session.court,assisted,out landing),
                "Powered human comfort assist accepted for AI service");
            session.shuttle.Launch(state.position,Vector3.down);
            feet=session.opponent.FeetLocal;
            session.opponent.PositionForService(false);
            Require(session.opponent.FeetLocal==feet,"PositionForService teleported during live play");
            Require(!session.opponent.TryPrepareRegulationServe(false,target,out state,out contact),"New service replaced a live shuttle");
            Require(contact.centerVelocity==Vector3.zero,"Failed service returned a fake usable action");
            session.shuttle.Stop();
        }

        private static void TestIncrementalPlanning(BadmintonSession session)
        {
            bool running=session.Running;
            float mass=session.physics.mass;
            try
            {
                session.shuttle.Stop();
                session.Difficulty=OpponentDifficulty.Easy;
                SetRunning(session,false);
                Vector3 target=session.court.TransformPoint(new Vector3(1.435f,1.67f,.44f));
                Require(session.opponent.BeginRegulationServePlanning(true,target),"Incremental service did not begin");
                Require(session.opponent.RegulationServePlanning,"Pending service is not visible to session");
                Vector3 feet=session.opponent.FeetLocal,head=session.opponent.RacketHeadWorld;
                float error=session.opponent.LastAimError;
                ShuttleState state;
                RacketContact contact;
                for (int i=0;i<12;i++)
                {
                    Require(session.opponent.AdvanceRegulationServePlanning(out state,out contact)==OpponentServePlanStatus.Pending,
                        "Paused incremental service advanced");
                    Require(contact.centerVelocity==Vector3.zero && session.opponent.FeetLocal==feet &&
                        session.opponent.RacketHeadWorld==head && session.opponent.LastAimError==error,"Pause changed service computation/pose");
                }
                // Preparation owns an immutable physical snapshot until the
                // next point, even if a caller changes the source settings.
                session.physics.mass+=.0002f;
                Require(Mathf.Abs(session.opponent.PhysicsSettings.mass-mass)<.0000001f,"Service did not snapshot equipment");
                session.physics.mass=mass;
                SetRunning(session,true);
                OpponentServePlanStatus status=OpponentServePlanStatus.Pending;
                int frames=0;
                double maxMs=0,totalMs=0;
                while (status==OpponentServePlanStatus.Pending && frames<1085)
                {
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    status=session.opponent.AdvanceRegulationServePlanning(out state,out contact);
                    watch.Stop();
                    maxMs=Math.Max(maxMs,watch.Elapsed.TotalMilliseconds);
                    totalMs+=watch.Elapsed.TotalMilliseconds;
                    session.opponent.SamplePresentation(1f/90f);
                    Require(session.opponent.FeetLocal==feet && !session.shuttle.Flying,"Planning moved the service feet or launched itself");
                    frames++;
                }
                Require(status==OpponentServePlanStatus.Success,"Incremental service did not physically solve");
                Require(!session.opponent.RegulationServePlanning && frames>1,"Service planning was still synchronous/pending");
                Require(maxMs<8.0,"One incremental service frame exceeded 8 ms: "+maxMs.ToString("F2"));
                Debug.Log("[Regulation Service Budget] frames="+frames+"; totalMs="+totalMs.ToString("F2")+
                    "; maxFrameMs="+maxMs.ToString("F2")+"; one physical prediction step per frame.");
                Require(session.opponent.BeginRegulationServePlanning(false,session.court.TransformPoint(new Vector3(-1.155f,1.67f,.44f))),
                    "Cancellation fixture failed");
                session.opponent.OnPointEnded();
                Require(!session.opponent.RegulationServePlanning &&
                    session.opponent.AdvanceRegulationServePlanning(out state,out contact)==OpponentServePlanStatus.Failed &&
                    contact.centerVelocity==Vector3.zero,"Point end exposed an obsolete service plan");
                Require(session.opponent.BeginRegulationServePlanning(true,target),"Reset fixture failed");
                session.opponent.ResetRally();
                Require(!session.opponent.RegulationServePlanning &&
                    session.opponent.AdvanceRegulationServePlanning(out state,out contact)==OpponentServePlanStatus.Failed,
                    "Rally reset left service planning alive");
            }
            finally
            {
                session.physics.mass=mass;
                session.opponent.CancelRegulationServePlanning();
                SetRunning(session,running);
            }
        }

        private static void SetRunning(BadmintonSession session,bool value)
        {
            typeof(BadmintonSession).GetProperty("Running",BindingFlags.Instance|BindingFlags.Public).
                GetSetMethod(true).Invoke(session,new object[] { value });
        }

        private static void Require(bool condition,string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("[Regulation Service] "+message);
        }
    }
}
