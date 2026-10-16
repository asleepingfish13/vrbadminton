using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using Object = UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    public static class BadmintonRegression
    {
        private static int checks;
        private static readonly ShuttlePhysicsSettings strict = new ShuttlePhysicsSettings { seatedAssist = false };
        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("[Shuttle Test] " + message);
        }

        [MenuItem("Tools/Vive Shuttle VR/Run Regression Checks")]
        public static void Run()
        {
            checks = 0;
            checks += ChineseLocalizationRegression.Run();
            checks += SinglesMatchRulesRegression.Run();
            checks += StandardSinglesSessionRegression.Run();
            checks += RegulationFlightRegression.Run();
            checks += RegulationServeRegression.Run();
            checks += StandardRallyIntegrationRegression.Run();
            EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
            BadmintonProjectBuilder.ValidateXR();
            BadmintonSession session = Object.FindObjectOfType<BadmintonSession>();
            Require(session != null && session.rackets.Length == 2, "Two rackets and manager required");
            Require(session.mode == PracticeMode.SinglesMatch && session.Difficulty == OpponentDifficulty.Easy,
                "Saved scene must default to regulation singles with Easy AI");
            session.playerCamera.GetComponent<TrackedPoseDriver>().enabled = false;
            Require(session.simulatePlayerHeight && Mathf.Abs(session.simulatedStature-1.75f)<.0001f,
                "Scene must default to requested 175 cm simulated stature");
            Require(Mathf.Abs(session.VirtualEyeHeight-1.63f)<.0001f,
                "175 cm stature must map to the documented 163 cm virtual eye height");
            Require(session.compactSeatedTargets,"Saved scene must default to the compact seated target profile");
            // Legacy lifecycle fixtures target the original repeatable reach ring.
            // Mixed-feed coverage below exercises the new runtime default separately.
            session.mode = PracticeMode.Training;
            session.feedPattern = FeedPattern.Fixed;
            session.simulatePlayerHeight = false;
            session.compactSeatedTargets = false;
            foreach (Transform item in Object.FindObjectsOfType<Transform>())
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject) == 0, "Missing script: " + item.name);

            checks += RegulationCourtRegression.Run(session.court);
            checks += AthleteRegression.Run(session);
            checks += RacketSpecificationRegression.Run();
            checks += RacketAppearanceRegression.Run(session);
            checks += ShuttleHandRegression.Run(session);
            checks += HandServeRegression.Run(session);
            checks += OpponentRegression.Run();
            TestFeeds();
            TestPlayerHeight(session);
            TestSweeps();
            TestGripAlignment(session);
            TestImpacts();
            TestBackwardHitRegression(session);
            TestFlight();
            checks += SpinFlightRegression.Run();
            TestClassifier();
            checks += EquipmentPhysicsRegression.Run(session);
            checks += PracticeFeedRegression.Run(session);
            checks += PracticeControlRegression.Run(session);
            checks += OpponentIntegrationRegression.Run(session);
            session.shuttle.Stop();
            session.mode = PracticeMode.Training;
            session.feedPattern = FeedPattern.Fixed;
            session.simulatePlayerHeight = false;
            session.playerCamera.transform.SetPositionAndRotation(new Vector3(1.4f,1.2f,-.6f),Quaternion.Euler(8f,40f,0f));
            session.Recenter();
            TestSession(session);
            TestFrameRates(session);
            TestDrills(session);
            TestAlignment(session);
            Debug.Log("[Shuttle Test] PASS " + checks + " checks: XR and BWF-dimension court, ASTROX published weight/tension and estimated geometry/inertia, multi-difficulty physical AI, safe pause controls, varied stationary feeds and repeatable feeder, bidirectional swept contact, grip-speed response, forward-swing regression, mass-scaled passive energy, 25-lb equipment defaults and guarded adjustments, finite string/cork compliance and signed slice spin, sweet spot, stationary blocks, drag/tumble stability, technique features and real impacts, legal landing, rounds, explicit 175 cm virtual stature without XR pose/scale edits, real-height drill mode and first-trigger alignment.");
            EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
        }

        private static void TestFeeds()
        {
            foreach (float height in new[] { .95f, 1.25f, 1.6f })
            {
                Vector3 source = new Vector3(0f, 1.5f, 8.6f);
                Vector3 target = new Vector3(.22f, height-.18f, .62f);
                Vector3 launch = ShuttleMath.SolveLaunch(source, target, 1.75f);
                Require(Vector3.Distance(ShuttleMath.Predict(source, launch, 1.75f), target)<.01f, "Feed misses seated height " + height);
                Require(ClearsNet(source, launch), "Feed hits net");
            }
            Vector3 netSource = new Vector3(0f, 1.5f, 4.8f);
            Vector3 netTarget = new Vector3(.22f, 1.34f, 3.02f);
            Vector3 netLaunch = ShuttleMath.SolveLaunch(netSource, netTarget, .85f);
            Require(Vector3.Distance(ShuttleMath.Predict(netSource, netLaunch, .85f), netTarget)<.01f, "Net-touch feed misses reach ring");
            Require(ClearsNet(netSource, netLaunch), "Net-touch feed hits net");
            Vector3 overheadSource = new Vector3(0f, 1.5f, 8.6f);
            Vector3 overheadTarget = new Vector3(.22f, 1.97f, 1.82f);
            Vector3 overheadLaunch = ShuttleMath.SolveLaunch(overheadSource, overheadTarget, 1.75f);
            Require(Vector3.Distance(ShuttleMath.Predict(overheadSource, overheadLaunch, 1.75f), overheadTarget)<.01f, "Overhead feed misses reach ring");
            Require(ClearsNet(overheadSource, overheadLaunch), "Overhead feed hits net");
        }

        private struct TrackedTransformSnapshot
        {
            public Vector3 position, localPosition, localScale, worldScale;
            public Quaternion rotation, localRotation;
            public TrackedTransformSnapshot(Transform transform)
            {
                position=transform.position; rotation=transform.rotation;
                localPosition=transform.localPosition; localRotation=transform.localRotation;
                localScale=transform.localScale; worldScale=transform.lossyScale;
            }
            public void RequireUnchanged(Transform transform,string label)
            {
                Require(Vector3.Distance(transform.position,position)<.00001f && Quaternion.Angle(transform.rotation,rotation)<.01f,
                    label+" world pose changed during height calibration");
                Require(Vector3.Distance(transform.localPosition,localPosition)<.00001f && Quaternion.Angle(transform.localRotation,localRotation)<.01f,
                    label+" tracked local pose changed during height calibration");
                Require(Vector3.Distance(transform.localScale,localScale)<.00001f && Vector3.Distance(transform.lossyScale,worldScale)<.00001f,
                    label+" scale changed during height calibration");
            }
        }

        private static void TestPlayerHeight(BadmintonSession session)
        {
            Transform head=session.playerCamera.transform;
            Transform origin=head.parent;
            Require(origin!=null,"Height fixture needs the tracked XR origin");
            Vector3 savedHeadPosition=head.position;
            Quaternion savedHeadRotation=head.rotation;
            bool savedSimulation=session.simulatePlayerHeight;
            float savedStature=session.simulatedStature;
            PracticeMode savedMode=session.mode;
            PracticeDrill savedDrill=session.drill;
            FeedPattern savedPattern=session.feedPattern;
            float savedReach=session.seatedReach;
            var originPose=new TrackedTransformSnapshot(origin);
            var gripPoses=new TrackedTransformSnapshot[session.rackets.Length];
            var racketHeadPoses=new TrackedTransformSnapshot[session.rackets.Length];
            for(int i=0;i<session.rackets.Length;i++)
            {
                gripPoses[i]=new TrackedTransformSnapshot(session.rackets[i].transform);
                racketHeadPoses[i]=new TrackedTransformSnapshot(session.rackets[i].head);
            }
            try
            {
                session.shuttle.Stop();
                session.mode=PracticeMode.Training;
                session.feedPattern=FeedPattern.Fixed;
                session.seatedReach=.62f;
                session.simulatedStature=1.75f;
                Require(Mathf.Abs(BadmintonSession.SimulatedEyeBelowCrown-.12f)<.0001f,
                    "Simulated stature-to-eye offset must remain explicit");
                foreach(bool simulate in new[]{false,true})
                foreach(float realEye in new[]{1.25f,1.60f,1.85f})
                {
                    session.simulatePlayerHeight=simulate;
                    head.SetPositionAndRotation(new Vector3(1.4f,realEye,-.6f),Quaternion.Euler(8f,40f,0f));
                    var headPose=new TrackedTransformSnapshot(head);
                    Require(session.Recenter(),"Height-mode recenter failed");
                    float expectedEye=simulate?1.63f:realEye;
                    float expectedFloor=simulate?realEye-1.63f:0f;
                    foreach(PracticeDrill drill in new[]{PracticeDrill.Rally,PracticeDrill.NetTouch,PracticeDrill.Overhead})
                    {
                        session.SetDrill(drill);
                        Require(session.drill==drill,"Height fixture could not switch paused training drill");
                        string label="height mode="+simulate+" real eye="+realEye+" drill="+drill;
                        Vector3 localEye=session.court.InverseTransformPoint(head.position);
                        float playerZ=drill==PracticeDrill.NetTouch?2.4f:drill==PracticeDrill.Overhead?1.2f:0f;
                        float offsetY=drill==PracticeDrill.NetTouch?-.16f:drill==PracticeDrill.Overhead?.22f:-.18f;
                        Require(Mathf.Abs(localEye.y-expectedEye)<.0001f,label+" has incorrect virtual eye height");
                        Require(Mathf.Abs(localEye.x)<.0001f && Mathf.Abs(localEye.z-playerZ)<.0001f,
                            label+" has incorrect stationary court alignment");
                        Require(Mathf.Abs(session.court.position.y-expectedFloor)<.0001f,label+" has incorrect floor offset");
                        Require(Mathf.Abs(session.reachMarker.localPosition.y-(expectedEye+offsetY))<.0001f &&
                            Mathf.Abs(session.reachMarker.localPosition.z-playerZ-.62f)<.0001f,
                            label+" reachable target ignores virtual eye height");
                        Vector3 worldReach=session.reachMarker.position-head.position;
                        Require(Mathf.Abs(worldReach.y-offsetY)<.0001f &&
                            Mathf.Abs(Vector3.Dot(worldReach,session.court.forward)-.62f)<.0001f,
                            label+" shifted physical hand reach with virtual stature");
                        headPose.RequireUnchanged(head,"Camera / "+label);
                        originPose.RequireUnchanged(origin,"XR origin / "+label);
                        for(int i=0;i<session.rackets.Length;i++)
                        {
                            gripPoses[i].RequireUnchanged(session.rackets[i].transform,"Controller "+i+" / "+label);
                            racketHeadPoses[i].RequireUnchanged(session.rackets[i].head,"Racket head "+i+" / "+label);
                        }
                        checks += RegulationCourtRegression.VerifyWorldDimensions(session.court);
                    }
                }
                session.simulatedStature=.5f;
                Require(Mathf.Abs(session.VirtualEyeHeight-1.1f)<.0001f,"Simulated eye minimum guard failed");
                session.simulatedStature=3f;
                Require(Mathf.Abs(session.VirtualEyeHeight-2f)<.0001f,"Simulated eye maximum guard failed");
            }
            finally
            {
                session.shuttle.Stop();
                head.SetPositionAndRotation(savedHeadPosition,savedHeadRotation);
                session.simulatePlayerHeight=savedSimulation;
                session.simulatedStature=savedStature;
                session.mode=savedMode;
                session.drill=savedDrill;
                session.feedPattern=savedPattern;
                session.seatedReach=savedReach;
                session.Recenter();
            }
        }

        private static void TestSweeps()
        {
            Vector3 contact;
            float fraction;
            Require(ShuttleMath.SweptFaceHit(Vector3.forward, Vector3.back, Vector3.zero, Vector3.zero, Quaternion.identity, .025f, out contact, out fraction), "Fast shuttle tunneling");
            Require(Mathf.Abs(fraction-.5f)<.0001f && contact.magnitude<.0001f, "Sweep contact time incorrect");
            Require(ShuttleMath.SweptFaceHit(Vector3.zero, Vector3.zero, Vector3.back, Vector3.forward, Quaternion.identity, out contact), "Fast moving racket tunneling");
            Require(ShuttleMath.SweptFaceHit(Vector3.back, Vector3.forward, Vector3.zero, Vector3.zero, Quaternion.identity, out contact), "Back racket face does not hit");
            Require(!ShuttleMath.SweptFaceHit(new Vector3(.4f,0,1), new Vector3(.4f,0,-1), Vector3.zero, Vector3.zero, Quaternion.identity, out contact), "False racket hit outside string bed");
            foreach (float sign in new[] { -1f, 1f })
            {
                Require(!ShuttleMath.SweptFaceHit(Vector3.forward*.01f*sign, Vector3.forward*.03f*sign, Vector3.zero, Vector3.zero, Quaternion.identity, out contact), "Receding shuttle bounced from tolerance zone");
                Require(!ShuttleMath.SweptFaceHit(Vector3.forward*.02f*sign, Vector3.forward*.02f*sign+Vector3.right*.01f, Vector3.zero, Vector3.zero, Quaternion.identity, out contact), "Parallel shuttle bounced from tolerance zone");
                Require(ShuttleMath.SweptFaceHit(Vector3.forward*.06f*sign, Vector3.forward*.02f*sign, Vector3.zero, Vector3.zero, Quaternion.identity, out contact), "Approaching tolerance-zone contact rejected");
            }
            Require(ShuttleMath.CrossesNet(new Vector3(0,1,3), new Vector3(0,1,4)), "Low shot must hit net");
            Require(!ShuttleMath.CrossesNet(new Vector3(0,2,3), new Vector3(0,2,4)), "High shot should clear net");
            Require(!ShuttleMath.IsInOpponentCourt(new Vector3(3,0,7)), "Wide shot must be out");
            Require(!ShuttleMath.IsInOpponentCourt(new Vector3(0,0,BadmintonCourt.FarBaseline+.1f)), "Long shot must be out");
        }

        private static RacketContact Contact(Vector3 position, Vector3 velocity, Vector3 normal)
        {
            return new RacketContact { point=position, center=position, centerVelocity=velocity,
                faceRotation=Quaternion.LookRotation(normal.normalized, Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up))>.95f ? Vector3.forward : Vector3.up),
                recentPeakSpeed=velocity.magnitude };
        }

        private static void TestGripAlignment(BadmintonSession session)
        {
            Quaternion baseline=RacketController.GripToRacketRotation;
            Require(Vector3.Angle(baseline*Vector3.up,Vector3.forward)<.01f, "Racket shaft does not follow grip +Z");
            Require(Vector3.Angle(baseline*Vector3.forward,Vector3.right)<.01f, "Racket face not aligned to forehand grip +X");
            Require(Vector3.Distance(RacketController.ViveLowerHandleMountOffset,new Vector3(0f,0f,-.08f))<.00001f,
                "Vive lower-handle preset must be an explicit 8 cm heelward translation, not a face rotation");
            Require(session.rackets[0].hand!=session.rackets[1].hand,
                "Mounting checks need both left- and right-hand tracked grip frames");
            MethodInfo samplePose=typeof(RacketController).GetMethod("SampleTrackedPose",BindingFlags.Instance|BindingFlags.NonPublic);
            MethodInfo resetMotion=typeof(RacketController).GetMethod("ResetMotion",BindingFlags.Instance|BindingFlags.NonPublic);
            Require(samplePose!=null && resetMotion!=null,"Tracked-pose motion sampling entry points are missing");
            foreach (RacketController racket in session.rackets)
            {
                racket.ApplyGripAlignment();
                Require(Vector3.Distance(racket.controllerMountOffset,RacketController.ViveLowerHandleMountOffset)<.00001f,
                    "Saved " + racket.hand + " scene must use the Vive lower-handle mounting preset");
                Require(racket.gripPositionOffset==Vector3.zero && racket.gripRotationOffsetEuler==Vector3.zero,
                    "Saved " + racket.hand + " fine calibration must remain neutral and separate from the mounting preset");
                Require(Vector3.Distance(racket.modelGripCenter,RacketSpecification.GripCenter)<.0001f,
                    "Controller grip anchor differs from the shared racket model");
                Require(Vector3.Distance(racket.visual.transform.InverseTransformPoint(racket.head.position),RacketSpecification.HeadCenter)<.0001f,
                    "Tracked collision head differs from the rendered ASTROX-size geometry");
                Vector3 modelGrip=racket.visual.transform.TransformPoint(racket.modelGripCenter);
                Vector3 trackedGrip=racket.transform.TransformPoint(racket.GripAnchorLocal);
                Require(Vector3.Distance(modelGrip,trackedGrip)<.0001f, "Model handle centre is not anchored to controller lower grip");
                Require(Vector3.Angle(racket.visual.transform.localRotation*Vector3.up,
                    Quaternion.Euler(racket.gripRotationOffsetEuler)*Vector3.forward)<.01f, "Saved racket tilt survived grip alignment");

                Vector3 savedPosition=racket.transform.localPosition;
                Quaternion savedRotation=racket.transform.localRotation;
                Vector3 savedMount=racket.controllerMountOffset;
                Vector3 savedFinePosition=racket.gripPositionOffset;
                Vector3 savedFineRotation=racket.gripRotationOffsetEuler;
                try
                {
                    foreach(float yaw in new[]{0f,73f,210f})
                    foreach(bool fineAdjustment in new[]{false,true})
                    {
                        string label=racket.hand+" / yaw "+yaw+" / fine "+fineAdjustment;
                        racket.controllerMountOffset=RacketController.ViveLowerHandleMountOffset;
                        racket.gripPositionOffset=fineAdjustment?new Vector3(.01f,-.015f,.02f):Vector3.zero;
                        racket.gripRotationOffsetEuler=fineAdjustment?new Vector3(5f,-3f,2f):Vector3.zero;
                        racket.ApplyGripAlignment();
                        resetMotion.Invoke(racket,null);
                        Vector3 gripFrom=new Vector3(.18f,1.12f,.24f);
                        Vector3 gripTo=gripFrom+new Vector3(.005f,.002f,.01f);
                        Quaternion poseFrom=Quaternion.Euler(20f,yaw,-12f);
                        Quaternion poseTo=poseFrom*Quaternion.AngleAxis(10f,Vector3.up);
                        float frame=1f/90f;
                        Quaternion modelRotation=Quaternion.Euler(racket.gripRotationOffsetEuler)*baseline;
                        Vector3 localHead=new Vector3(0f,0f,-.08f)+racket.gripPositionOffset+
                            modelRotation*(RacketSpecification.HeadCenter-RacketSpecification.GripCenter);
                        if(!fineAdjustment)
                            Require(Mathf.Abs(localHead.z-.396f)<.00001f,
                                "Lower mounting must shorten the tracked-origin head lever from 47.6 to 39.6 cm / "+label);
                        Vector3 expectedFrom=racket.transform.parent.TransformPoint(gripFrom+poseFrom*localHead);
                        Vector3 expectedTo=racket.transform.parent.TransformPoint(gripTo+poseTo*localHead);
                        samplePose.Invoke(racket,new object[]{gripFrom,poseFrom,frame});
                        Require(!racket.HasMotionSample && racket.HeadVelocity==Vector3.zero,
                            "First pose after mount reset must not sweep a shuttle / "+label);
                        samplePose.Invoke(racket,new object[]{gripTo,poseTo,frame});
                        Require(racket.HasMotionSample && Mathf.Abs(racket.MotionDeltaTime-frame)<.00001f,
                            "Continuous tracked pose did not produce a valid motion segment / "+label);
                        Require(Vector3.Distance(racket.PreviousHead,expectedFrom)<.00001f &&
                            Vector3.Distance(racket.CurrentHead,expectedTo)<.00001f,
                            "Collision sweep does not use shifted world-space head endpoints / "+label);
                        Require(Vector3.Distance(racket.HeadVelocity,(expectedTo-expectedFrom)/frame)<.0001f,
                            "Wrist/translation velocity omitted or doubled the lower mounting lever / "+label);
                        Require(Vector3.Distance(racket.head.position,racket.visual.transform.TransformPoint(RacketSpecification.HeadCenter))<.00001f,
                            "Collision head and visible string bed separated after mounting / "+label);
                        Vector3 expectedAnchor=racket.transform.TransformPoint(new Vector3(0f,0f,-.08f)+racket.gripPositionOffset);
                        Require(Vector3.Distance(racket.visual.transform.TransformPoint(racket.modelGripCenter),expectedAnchor)<.00001f,
                            "Mounting direction failed to rotate with the tracked hand / "+label);
                        Vector3 savedVelocity=racket.HeadVelocity;
                        racket.ApplyGripAlignment();
                        Require(racket.HasMotionSample && Vector3.Distance(racket.HeadVelocity,savedVelocity)<.00001f,
                            "An unchanged alignment reset valid swing velocity / "+label);

                        // Changes are calibration, not real swings: discard the old endpoints
                        // and speed history, then prime the new geometry for one pose sample.
                        foreach(bool changeFineOffset in new[]{false,true})
                        {
                            if(changeFineOffset) racket.gripPositionOffset+=Vector3.right*.015f;
                            else racket.controllerMountOffset+=Vector3.back*.015f;
                            racket.ApplyGripAlignment();
                            Require(!racket.HasMotionSample && racket.HeadVelocity==Vector3.zero &&
                                racket.AngularVelocity==Vector3.zero && racket.MotionDeltaTime==0f && racket.RecentPeakSpeed==0f,
                                "Mount/fine calibration injected phantom swing speed / "+label);
                            samplePose.Invoke(racket,new object[]{gripTo,poseTo,frame});
                            Require(!racket.HasMotionSample && racket.HeadVelocity==Vector3.zero,
                                "First pose after changed calibration swept the old head location / "+label);
                            samplePose.Invoke(racket,new object[]{gripTo,poseTo,frame});
                            Require(racket.HasMotionSample && racket.HeadVelocity==Vector3.zero && racket.AngularVelocity==Vector3.zero,
                                "Stationary remounted racket must resume with zero speed / "+label);
                        }

                        racket.controllerMountOffset=Vector3.zero;
                        racket.gripPositionOffset=Vector3.zero;
                        racket.gripRotationOffsetEuler=Vector3.zero;
                        racket.ApplyGripAlignment();
                        Require(Vector3.Distance(racket.visual.transform.TransformPoint(racket.modelGripCenter),racket.transform.position)<.00001f,
                            "Clearing the mounting preset must restore the original tracked grip anchor / "+label);
                    }
                }
                finally
                {
                    racket.controllerMountOffset=savedMount;
                    racket.gripPositionOffset=savedFinePosition;
                    racket.gripRotationOffsetEuler=savedFineRotation;
                    racket.transform.localPosition=savedPosition;
                    racket.transform.localRotation=savedRotation;
                    racket.ApplyGripAlignment();
                    resetMotion.Invoke(racket,null);
                }
            }
            float headLever=(RacketSpecification.HeadCenter-RacketSpecification.GripCenter).magnitude;
            Vector3 wristTip=RacketController.VelocityAtPoint(Vector3.zero,Vector3.up*10f,Vector3.forward*headLever);
            Require(Vector3.Distance(wristTip,Vector3.right*(10f*headLever))<.0001f, "Wrist angular motion missing at racket head");
            Vector3 translated=RacketController.VelocityAtPoint(Vector3.forward*2f,Vector3.up*10f,Vector3.forward*headLever);
            Require(Vector3.Distance(translated,new Vector3(10f*headLever,0f,2f))<.0001f, "Grip and head velocities were not composed");
            Vector3 angular=RacketController.AngularVelocityFromRotation(Quaternion.identity,Quaternion.AngleAxis(90f,Vector3.right),.5f);
            Require(Vector3.Distance(angular,Vector3.right*Mathf.PI)<.0001f, "Angular velocity must be in radians per second");
            angular=RacketController.AngularVelocityFromRotation(Quaternion.Euler(0f,179f,0f),Quaternion.Euler(0f,-179f,0f),.02f);
            Require(Vector3.Distance(angular,Vector3.up*(100f*Mathf.Deg2Rad))<.001f, "Angular velocity took long path through quaternion wrap");
            Require(RacketController.AngularVelocityFromRotation(Quaternion.identity,Quaternion.identity,0f)==Vector3.zero, "Zero-duration angular sample was not rejected");
        }

        private static void TestImpacts()
        {
            ShuttleState incoming = ShuttleState.Launch(Vector3.zero, Vector3.back*6f);
            ShuttlePhysicsSettings assisted = new ShuttlePhysicsSettings();
            foreach (ShuttlePhysicsSettings settings in new[] { strict, assisted })
            {
                float previous = -1f;
                foreach (float speed in new[] { 0f, .5f, 1f, 2f, 3f, 5f, 8f, 12f })
                {
                    ImpactResult result = RacketImpact.Resolve(incoming, Contact(Vector3.zero, Vector3.forward*speed, Vector3.forward), settings);
                    Require(result.hit && result.outgoingVelocity.z>0f, "Square contact must return shuttle at swing " + speed);
                    Require(result.outgoingVelocity.magnitude>previous+.1f, "Higher racket speed must produce higher outgoing speed");
                    Require(Mathf.Abs(result.swingSpeed-speed)<.0001f, "Reported swing speed must remain measured, not assisted");
                    previous = result.outgoingVelocity.magnitude;
                    Debug.Log("[Shuttle Test] Speed assist=" + settings.seatedAssist + " racket=" + speed.ToString("F1") + "m/s outgoing=" + previous.ToString("F3") + "m/s");
                }
            }
            foreach (float speed in new[] { 0f, .3f, 1f, 1.5f })
            {
                RacketContact contact = Contact(Vector3.zero, Vector3.forward*speed, Vector3.forward);
                ImpactResult a = RacketImpact.Resolve(incoming, contact, strict);
                ImpactResult b = RacketImpact.Resolve(incoming, contact, assisted);
                Require(Vector3.Distance(a.outgoingVelocity,b.outgoingVelocity)<.0001f, "Assist changed gentle touch");
            }
            RacketContact strongContact = Contact(Vector3.zero, Vector3.forward*6f, Vector3.forward);
            Require(RacketImpact.Resolve(incoming,strongContact,assisted).outgoingVelocity.magnitude>
                RacketImpact.Resolve(incoming,strongContact,strict).outgoingVelocity.magnitude, "Strong seated assist has no effect");

            ImpactResult centered = RacketImpact.Resolve(incoming, strongContact, strict);
            RacketContact offCenter = strongContact;
            offCenter.point += Vector3.right*(RacketSpecification.HeadHalfWidth*.86f);
            ImpactResult edge = RacketImpact.Resolve(incoming, offCenter, strict);
            Require(edge.hit && edge.quality<centered.quality && edge.outgoingVelocity.magnitude<centered.outgoingVelocity.magnitude, "Off-center contact should lose string-bed energy");
            offCenter.point = Vector3.right*.25f;
            Require(!RacketImpact.Resolve(incoming,offCenter,strict).hit, "Far outside racket accepted");
            ImpactResult block = RacketImpact.Resolve(incoming, Contact(Vector3.zero,Vector3.zero,Vector3.forward), strict);
            Require(block.hit && block.outgoingVelocity.z>0f && block.swingSpeed==0f, "Stationary racket cannot block");
            Require(Mathf.Abs(block.outgoingVelocity.y)<.0001f, "Square horizontal contact received artificial auto-lift");
            RacketContact wrist = Contact(Vector3.zero,Vector3.zero,Vector3.forward);
            wrist.point=Vector3.right*.1f;
            wrist.angularVelocity=Vector3.down*20f;
            Require(Vector3.Distance(wrist.PointVelocity,Vector3.forward*2f)<.0001f, "Angular wrist velocity not included at contact point");
            Require(RacketImpact.Resolve(incoming,wrist,strict).swingSpeed>1.99f, "Wrist-only contact has no swing speed");
            Require(!RacketImpact.Resolve(ShuttleState.Launch(Vector3.zero,Vector3.zero),Contact(Vector3.zero,Vector3.zero,Vector3.forward),strict).hit, "No relative motion must not count as hit");
            ShuttleState spinning=ShuttleState.Launch(Vector3.zero,Vector3.back);
            spinning.orientation=Quaternion.FromToRotation(Vector3.up,Vector3.right);
            spinning.angularVelocity=Vector3.up*100f;
            Require(!RacketImpact.Resolve(spinning,Contact(Vector3.zero,Vector3.zero,Vector3.forward),strict).hit, "Separating cork surface counted as impact");
            spinning.angularVelocity=Vector3.down*100f;
            Require(RacketImpact.Resolve(spinning,Contact(Vector3.zero,Vector3.zero,Vector3.forward),strict).hit, "Approaching spinning cork surface rejected");

            // A stationary passive racket cannot create translational + rotational
            // energy. This catches an omitted cork lever in the normal effective mass.
            int passiveHits = 0;
            foreach (float tilt in new[] { 0f, 25f, 60f, 90f, 135f })
            foreach (float incidence in new[] { -55f, -20f, 0f, 30f, 65f })
            foreach (float friction in new[] { 0f, .18f, .5f })
            foreach (float spin in new[] { 0f, 40f })
            {
                ShuttleState state = ShuttleState.Launch(Vector3.zero, Quaternion.Euler(incidence,0f,0f)*(Vector3.back*18f));
                state.orientation=Quaternion.Euler(tilt,15f,10f);
                state.angularVelocity=new Vector3(spin,spin*.3f,-spin*.2f);
                ShuttlePhysicsSettings settings = new ShuttlePhysicsSettings { seatedAssist=false, stringFriction=friction };
                ImpactResult result=RacketImpact.Resolve(state,Contact(Vector3.zero,Vector3.zero,Vector3.forward),settings);
                Require(Finite(result.outgoingVelocity)&&Finite(result.angularVelocity), "Passive impact produced nonfinite result");
                if (!result.hit) continue;
                passiveHits++;
                float before=KineticEnergy(state.velocity,state.angularVelocity,state.orientation,settings.mass);
                float after=KineticEnergy(result.outgoingVelocity,result.angularVelocity,state.orientation,settings.mass);
                Require(after<=before+.0001f, "Passive impact added energy: tilt=" + tilt + " incidence=" + incidence + " friction=" + friction + " spin=" + spin + " before=" + before + " after=" + after);
            }
            Require(passiveHits>100, "Passive-energy sweep did not exercise enough contacts");
        }

        private static void TestBackwardHitRegression(BadmintonSession session)
        {
            const float frame=.011f;
            Vector3 poseVelocity=RacketController.HeadVelocityFromPoses(Vector3.back*.03f,Vector3.forward*.03f,frame);
            Require(Vector3.Distance(poseVelocity,Vector3.forward*(.06f/frame))<.0001f, "Pose-segment forward velocity has wrong sign or scale");
            Require(RacketController.HeadVelocityFromPoses(Vector3.zero,Vector3.forward,0f)==Vector3.zero, "Invalid pose duration produces velocity");
            Require(RacketController.HeadVelocityFromPoses(Vector3.one,Vector3.one,frame)==Vector3.zero, "Stationary pose produces velocity");

            Vector3 gripPosition=new Vector3(.18f,1.12f,.24f);
            float headLever=(RacketSpecification.HeadCenter-RacketSpecification.GripCenter).magnitude;
            Vector3 headOffset=Vector3.forward*headLever;
            Vector3 wristHeadFrom=gripPosition+headOffset;
            Vector3 wristHeadTo=gripPosition+Quaternion.Euler(0f,10f,0f)*headOffset;
            Vector3 wristVelocity=RacketController.HeadVelocityFromPoses(wristHeadFrom,wristHeadTo,frame);
            Require(Mathf.Abs(wristVelocity.magnitude-2f*headLever*Mathf.Sin(5f*Mathf.Deg2Rad)/frame)<.0002f, "Fixed-grip wrist rotation is missing from pose head speed");

            // Reproduction: the pose swept +6 cm while an asynchronous SDK sample
            // reported -7 m/s. Choosing the face from that velocity made -10.43 m/s
            // backwards output. Geometry must choose the side, never velocity.
            Vector3 referenceOutput=Vector3.zero;
            foreach (float yaw in new[] { 0f,124f,166f,270f })
            {
                Quaternion worldRotation=Quaternion.Euler(0f,yaw,0f);
                Vector3 worldOrigin=new Vector3(.73f,1.31f,-.42f);
                Vector3 forward=worldRotation*Vector3.forward;
                Vector3 ballFrom=worldOrigin;
                Vector3 ballTo=worldOrigin-forward*(2f*frame);
                Vector3 racketFrom=worldOrigin-forward*.03f;
                Vector3 racketTo=worldOrigin+forward*.03f;
                Vector3 point, normal;
                float fraction;
                Require(ShuttleMath.SweptFaceHit(ballFrom,ballTo,racketFrom,racketTo,worldRotation,worldRotation,
                    strict.ContactMargin,out point,out fraction,out normal), "Forward pose sweep missed at court yaw " + yaw);
                Require(Vector3.Dot(normal,forward)>.999f, "Contact side changed with world/court yaw " + yaw);
                RacketContact contact=Contact(point,-forward*7f,forward);
                contact.center=Vector3.Lerp(racketFrom,racketTo,fraction);
                contact.surfaceNormal=normal;
                ShuttleState incoming=ShuttleState.Launch(point,-forward*2f);
                Require(!RacketImpact.Resolve(incoming,contact,strict).hit, "Mismatched separating velocity flipped contact side and returned backwards at yaw " + yaw);
                contact.centerVelocity=RacketController.HeadVelocityFromPoses(racketFrom,racketTo,frame);
                ImpactResult impact=RacketImpact.Resolve(incoming,contact,strict);
                Require(impact.hit && Vector3.Dot(impact.outgoingVelocity,forward)>5f, "Forward swept racket failed to return forward at yaw " + yaw);
                Require(Vector3.Distance(contact.centerVelocity,worldRotation*poseVelocity)<.0002f, "Pose velocity used local rather than world axes at yaw " + yaw);
                Vector3 localOutput=Quaternion.Inverse(worldRotation)*impact.outgoingVelocity;
                if (yaw==0f) referenceOutput=localOutput;
                else Require(Vector3.Distance(localOutput,referenceOutput)<.001f, "Impact response changes when court is rotated to yaw " + yaw);

                Vector3 rotatedWristFrom=worldOrigin+worldRotation*wristHeadFrom;
                Vector3 rotatedWristTo=worldOrigin+worldRotation*wristHeadTo;
                Require(Vector3.Distance(RacketController.HeadVelocityFromPoses(rotatedWristFrom,rotatedWristTo,frame),
                    worldRotation*wristVelocity)<.0002f, "Fixed-grip wrist sample lost world-space covariance at yaw " + yaw);

                // The back face must remain usable; this fix is not a forced
                // court-forward clamp. A shuttle arriving from -Z rebounds to -Z.
                Require(ShuttleMath.SweptFaceHit(worldOrigin-forward*.08f,worldOrigin+forward*.08f,
                    worldOrigin,worldOrigin,worldRotation,worldRotation,strict.ContactMargin,out point,out fraction,out normal),
                    "Back-face geometry no longer contacts at yaw " + yaw);
                Require(Vector3.Dot(normal,-forward)>.999f, "Back-face contact has front-face normal at yaw " + yaw);
                contact=Contact(point,Vector3.zero,forward);
                contact.center=worldOrigin;
                contact.surfaceNormal=normal;
                impact=RacketImpact.Resolve(ShuttleState.Launch(point,forward*6f),contact,strict);
                Require(impact.hit && Vector3.Dot(impact.outgoingVelocity,forward)<0f, "Genuine back-face block was forced forward at yaw " + yaw);

                contact=Contact(worldOrigin,Vector3.zero,forward);
                contact.surfaceNormal=forward;
                impact=RacketImpact.Resolve(ShuttleState.Launch(worldOrigin,-forward*6f),contact,strict);
                Require(impact.hit && Vector3.Dot(impact.outgoingVelocity,forward)>0f, "Signed front-face stationary block failed at yaw " + yaw);
                Require(!RacketImpact.Resolve(ShuttleState.Launch(worldOrigin,forward*6f),contact,strict).hit,
                    "A departing shuttle flipped the geometry-signed face at yaw " + yaw);
            }
            Debug.Log("[Shuttle Test] Backward-hit reproduction fixed: pose swing=" + poseVelocity.z.ToString("F3")+
                "m/s, outgoing=" + referenceOutput.z.ToString("F3") + "m/s; asynchronous -7m/s sample rejected.");

            foreach (float side in new[] { -1f,1f })
            {
                Vector3 ball=new Vector3(.1f,0f,.06f)*side;
                Quaternion endRotation=Quaternion.Euler(0f,-90f,0f);
                Vector3 point, normal;
                float fraction;
                Require(ShuttleMath.SweptFaceHit(ball,ball,Vector3.zero,Vector3.zero,Quaternion.identity,endRotation,
                    strict.ContactMargin,out point,out fraction,out normal), "Rotating face missed a stationary shuttle on side " + side);
                Require(fraction>0f && fraction<1f, "Rotating-face contact time must lie inside pose segment");
                Vector3 expected=Quaternion.Slerp(Quaternion.identity,endRotation,fraction)*Vector3.forward*side;
                Require(Vector3.Dot(normal,expected)>.999f, "Rotating contact normal was not evaluated at time of impact");
                Require(Vector3.Dot(normal,Vector3.forward*side)>0f, "Rotating face chose the far side after crossing the shuttle");
            }

            TestForwardFlightPipeline(session,poseVelocity,frame);
        }

        private static void TestForwardFlightPipeline(BadmintonSession session, Vector3 poseVelocity, float frame)
        {
            // Exercise the actual ShuttleFlight -> swept geometry -> signed impact
            // path, not just the pure resolver. Restore all injected tracking state.
            RacketController racket=Array.Find(session.rackets,item=>item.CanStrike);
            Require(racket!=null,"Forward-hit pipeline requires a striking racket, not the free holding hand");
            string[] properties={ "IsTracked","HasMotionSample","PreviousHead","CurrentHead","PreviousRotation",
                "HeadVelocity","AngularVelocity","MotionDeltaTime","RecentPeakSpeed" };
            object[] saved=new object[properties.Length];
            bool[] tracked=new bool[session.rackets.Length];
            Quaternion savedHeadRotation=racket.head.rotation;
            for (int i=0;i<properties.Length;i++) saved[i]=typeof(RacketController).GetProperty(properties[i]).GetValue(racket,null);
            for (int i=0;i<session.rackets.Length;i++)
            {
                tracked[i]=session.rackets[i].IsTracked;
                SetRacketProperty(session.rackets[i],"IsTracked",false);
            }
            try
            {
                session.SetDrill(PracticeDrill.Rally);
                foreach (float yaw in new[] { 0f,124f,166f,270f })
                foreach (bool mismatched in new[] { false,true })
                {
                    session.playerCamera.transform.SetPositionAndRotation(new Vector3(.3f,1.25f,-.2f),Quaternion.Euler(0f,yaw,0f));
                    Require(session.Recenter(), "Forward-flight test recenter failed");
                    session.StartPractice(XRNode.RightHand);
                    Vector3 point=session.court.TransformPoint(new Vector3(.22f,1.4f,.62f));
                    Vector3 forward=session.court.forward;
                    Quaternion face=session.court.rotation;
                    racket.head.rotation=face;
                    SetRacketProperty(racket,"IsTracked",true);
                    SetRacketProperty(racket,"HasMotionSample",true);
                    SetRacketProperty(racket,"PreviousHead",point-forward*.03f);
                    SetRacketProperty(racket,"CurrentHead",point+forward*.03f);
                    SetRacketProperty(racket,"PreviousRotation",face);
                    SetRacketProperty(racket,"HeadVelocity",mismatched ? -forward*7f : face*poseVelocity);
                    SetRacketProperty(racket,"AngularVelocity",Vector3.zero);
                    SetRacketProperty(racket,"MotionDeltaTime",frame);
                    SetRacketProperty(racket,"RecentPeakSpeed",poseVelocity.magnitude);
                    session.shuttle.Launch(point,-forward*2f);
                    session.shuttle.AdvanceFrame(frame);
                    if (mismatched)
                        Require(!session.shuttle.Returned && session.Hits==0, "Live collision accepted contradictory separating velocity at yaw " + yaw);
                    else
                        Require(session.shuttle.Returned && session.Hits==1 && Vector3.Dot(session.shuttle.Velocity,forward)>4f,
                            "Live forward collision flew backwards or missed at yaw " + yaw);
                }
            }
            finally
            {
                racket.head.rotation=savedHeadRotation;
                for (int i=0;i<properties.Length;i++) SetRacketProperty(racket,properties[i],saved[i]);
                for (int i=0;i<session.rackets.Length;i++) SetRacketProperty(session.rackets[i],"IsTracked",tracked[i]);
                session.Recenter();
            }
        }

        private static void SetRacketProperty(RacketController racket, string name, object value)
        {
            typeof(RacketController).GetProperty(name).GetSetMethod(true).Invoke(racket,new[] { value });
        }

        private static float KineticEnergy(Vector3 velocity, Vector3 spin, Quaternion orientation, float mass)
        {
            Vector3 tail=orientation*Vector3.up;
            Vector3 axial=tail*Vector3.Dot(spin,tail);
            float inertiaScale=mass/.005f;
            return .5f*mass*velocity.sqrMagnitude+.5f*inertiaScale*
                (ShuttlePhysics.AxialInertia*axial.sqrMagnitude+ShuttlePhysics.TransverseInertia*(spin-axial).sqrMagnitude);
        }

        private static void TestFlight()
        {
            foreach (Vector3 velocity in new[] { new Vector3(0f,-4f,40f), new Vector3(.5f,2f,4f) })
            {
                ShuttleState start=ShuttleState.Launch(new Vector3(0f,2f,.6f),velocity);
                start.orientation=Quaternion.Euler(75f,20f,15f);
                start.angularVelocity=new Vector3(35f,12f,-6f);
                ShuttleState normal=IntegrateDuration(start,.75f,ShuttlePhysics.Step);
                ShuttleState fine=IntegrateDuration(start,.75f,ShuttlePhysics.Step*.5f);
                Require(Vector3.Distance(normal.position,fine.position)<.06f, "Flight position sensitive to substep size");
                Require(Vector3.Distance(normal.velocity,fine.velocity)<.15f, "Flight velocity sensitive to substep size");
                Require(Finite(normal.position)&&Finite(normal.velocity)&&Finite(normal.angularVelocity), "Flight contains nonfinite values");
                Require(Mathf.Abs(Quaternion.Dot(normal.orientation,normal.orientation)-1f)<.001f, "Orientation lost normalization");
            }
            ShuttleState fast=ShuttleState.Launch(Vector3.up*2f,Vector3.forward*70f);
            ShuttleState later=ShuttlePhysics.Predict(fast,.25f,strict);
            Require(later.velocity.z<fast.velocity.z*.65f && later.velocity.z>0f, "Shuttle does not exhibit strong speed-dependent air drag");
            ShuttleState flip=ShuttleState.Launch(Vector3.up*2f,Vector3.back*10f);
            flip.velocity=Vector3.forward*10f; // Feather direction must not teleport on impact.
            Vector3 initialTail=flip.orientation*Vector3.up;
            ShuttlePhysics.Integrate(ref flip,strict,ShuttlePhysics.Step);
            Require(Vector3.Angle(initialTail,flip.orientation*Vector3.up)<10f, "Feathers snapped instantly after return");
            flip=ShuttlePhysics.Predict(flip,.8f,strict);
            Require(Vector3.Dot(flip.orientation*Vector3.up,-flip.velocity.normalized)>.65f, "Shuttle failed to turn cork-first");
        }

        private static ShuttleState IntegrateDuration(ShuttleState state, float duration, float step)
        {
            for (float t=0f;t<duration-.000001f;)
            {
                float dt=Mathf.Min(step,duration-t);
                ShuttlePhysics.Integrate(ref state,strict,dt);
                t+=dt;
            }
            return state;
        }

        private static void TestClassifier()
        {
            ShotContext overhead=new ShotContext { position=new Vector3(0f,ShuttleMath.NetHeight+.67f,1.82f), eye=new Vector3(0f,ShuttleMath.NetHeight+.45f,1.2f),
                courtForward=Vector3.forward, distanceToNet=1.78f, heightOverNet=.67f };
            ShotContext net=new ShotContext { position=new Vector3(0f,ShuttleMath.NetHeight+.04f,3.02f), eye=new Vector3(0f,ShuttleMath.NetHeight+.20f,2.4f),
                courtForward=Vector3.forward, distanceToNet=.58f, heightOverNet=.04f };
            ImpactResult feature=new ImpactResult { hit=true, quality=1f, outgoingVelocity=new Vector3(0f,-8f,25f), normalSwing=8f, swingSpeed=9f };
            Require(ShotClassifier.Classify(feature,overhead).technique==ShotTechnique.Smash, "Smash feature label");
            feature.outgoingVelocity=new Vector3(0f,-1f,7f); feature.normalSwing=1f; feature.swingSpeed=1.2f;
            Require(ShotClassifier.Classify(feature,overhead).technique==ShotTechnique.Drop, "Drop feature label");
            feature.outgoingVelocity=new Vector3(0f,.8f,3f);
            Require(ShotClassifier.Classify(feature,net).technique==ShotTechnique.NetShot, "Net-shot feature label");
            feature.brushSpeed=2f; feature.sliceRatio=2f; feature.angularVelocity=Vector3.right*30f;
            Require(ShotClassifier.Classify(feature,net).technique==ShotTechnique.SpinningNet, "Spinning-net feature label");
            feature.normalizedOffset=1.1f;
            Require(ShotClassifier.Classify(feature,net).technique==ShotTechnique.Mishit, "Off-center warning label");
            feature.hit=false;
            Require(ShotClassifier.Classify(feature,net).technique==ShotTechnique.Unknown, "Miss mislabeled as a stroke");

            // These are actual contact outputs, not manually assigned shot labels.
            CheckPhysicalTechnique(ShotTechnique.Smash, overhead, new Vector3(0f,-2f,-6f), new Vector3(0f,-.35f,1f), new Vector3(0f,-3.5f,10f));
            CheckPhysicalTechnique(ShotTechnique.Drop, overhead, new Vector3(0f,-2f,-6f), new Vector3(0f,.1f,1f), Vector3.forward*.6f);
            CheckPhysicalTechnique(ShotTechnique.NetShot, net, new Vector3(0f,-1f,-2f), new Vector3(0f,.25f,1f), Vector3.forward*.5f);
            CheckPhysicalTechnique(ShotTechnique.SpinningNet, net, new Vector3(0f,-.5f,-2f), Vector3.forward, new Vector3(2f,0f,.5f));
            CheckLegalTechnique(ShotTechnique.Smash,overhead,new Vector3(0f,-2f,-6f));
            CheckLegalTechnique(ShotTechnique.Drop,overhead,new Vector3(0f,-2f,-6f));
            CheckLegalTechnique(ShotTechnique.NetShot,net,new Vector3(0f,-1f,-2f));
            CheckLegalTechnique(ShotTechnique.SpinningNet,net,new Vector3(0f,-.5f,-2f));
        }

        private static void CheckLegalTechnique(ShotTechnique expected, ShotContext context, Vector3 incomingVelocity)
        {
            ShuttleState incoming=ShuttleState.Launch(context.position,incomingVelocity);
            foreach (float angle in new[] { -30f,-25f,-20f,-15f,-12f,-9f,-6f,-3f,0f,3f,6f,9f,12f,15f,20f,25f,30f,40f,50f,60f })
            foreach (float speed in new[] { .25f,.5f,1f,2f,3f,4f,6f,8f,10f,12f,16f,20f,24f })
            {
                Vector3 normal=new Vector3(0f,Mathf.Sin(angle*Mathf.Deg2Rad),Mathf.Cos(angle*Mathf.Deg2Rad));
                Vector3 racketVelocity=normal*speed+(expected==ShotTechnique.SpinningNet ? Vector3.right*2f : Vector3.zero);
                ImpactResult impact=RacketImpact.Resolve(incoming,Contact(context.position,racketVelocity,normal),strict);
                ShotReadout readout=ShotClassifier.Classify(impact,context);
                if (!impact.hit || readout.technique!=expected) continue;
                ShuttleState state=incoming;
                state.velocity=impact.outgoingVelocity;
                state.angularVelocity=impact.angularVelocity;
                Vector3 landing;
                float clearance;
                if (!LegalFlight(state,null,strict,out landing,out clearance)) continue;
                Require(clearance>.018f && ShuttleMath.IsInOpponentCourt(landing), "Legal " + expected + " fixture did not clear net");
                Debug.Log("[Shuttle Test] Legal " + expected + ": face=" + angle + "deg racket=" + impact.swingSpeed.ToString("F2") +
                    "m/s outgoing=" + readout.speed.ToString("F2") + "m/s launch=" + readout.launchAngle.ToString("F1") +
                    "deg net clearance=" + clearance.ToString("F3") + "m landing=" + landing);
                return;
            }
            Require(false,"No legal physical " + expected + " example in bounded angle/speed sample for this drill");
        }

        private static void CheckPhysicalTechnique(ShotTechnique expected, ShotContext context, Vector3 incomingVelocity, Vector3 normal, Vector3 racketVelocity)
        {
            ShuttleState incoming=ShuttleState.Launch(context.position,incomingVelocity);
            ImpactResult impact=RacketImpact.Resolve(incoming,Contact(context.position,racketVelocity,normal),strict);
            ShotReadout readout=ShotClassifier.Classify(impact,context);
            Require(impact.hit, "Physical " + expected + " example missed");
            Require(readout.technique==expected, "Physical " + expected + " classified as " + readout.technique + "; outgoing=" + impact.outgoingVelocity + "; spin=" + impact.angularVelocity.magnitude);
            Require(readout.confidence>=0f && readout.confidence<=1f, "Technique confidence outside range");
            Debug.Log("[Shuttle Test] Physical " + expected + ": racket=" + impact.swingSpeed.ToString("F2") + "m/s outgoing=" +
                readout.speed.ToString("F2") + "m/s angle=" + readout.launchAngle.ToString("F1") + " spin=" + readout.spinRpm.ToString("F0") + "rpm");
        }

        private static void TestSession(BadmintonSession session)
        {
            session.SetDrill(PracticeDrill.Rally);
            session.playerCamera.transform.SetPositionAndRotation(new Vector3(1.4f,1.2f,-.6f),Quaternion.Euler(8f,40f,0f));
            session.Recenter();
            Vector3 eyeLocal=session.court.InverseTransformPoint(session.playerCamera.transform.position);
            Require(Mathf.Abs(eyeLocal.x)<.001f && Mathf.Abs(eyeLocal.z)<.001f, "Recenter must retain headset world pose");
            Require(Mathf.Abs(session.reachMarker.localPosition.z-.62f)<.001f, "Reach marker too far");
            session.StartPractice(XRNode.RightHand);
            Require(session.Running, "Practice did not start");
            session.TogglePause();
            Require(!session.Running, "Pause failed");
            session.StartPractice(XRNode.RightHand);

            MethodInfo serve=typeof(BadmintonSession).GetMethod("Serve",BindingFlags.Instance|BindingFlags.NonPublic);
            Require(serve!=null, "Feeder method missing");
            for (int i=0;i<20;i++)
            {
                serve.Invoke(session,null);
                Require(session.shuttle.Flying && session.Feeds==i+1, "Serve lifecycle failed");
                Require(session.shuttle.ReturnShot(session.court.forward*3f,session.court.forward), "Return rejected");
                Require(!session.shuttle.ReturnShot(session.court.forward*3f,session.court.forward), "Double hit counted");
                session.shuttle.Stop();
                // This loop tests scoring/round bookkeeping only. Real flight is below.
                session.OnFinished("LAND",session.targetMarker.localPosition,true);
            }
            Require(session.Score==1100 && session.Hits==20 && session.Combo==20, "20 target hits score incorrectly");
            serve.Invoke(session,null);
            Require(!session.Running && session.Feeds==20, "Round must stop after 20 feeds");
            session.Recenter();
            Require(!session.shuttle.Flying && !session.Running && session.Score==0 && session.Feeds==0, "Reset leaked flight/score");

            session.StartPractice(XRNode.RightHand);
            serve.Invoke(session,null);
            for (int i=0;i<157;i++) session.shuttle.AdvanceFrame(1f/90f);
            Require(session.shuttle.Flying && !session.shuttle.Returned, "Feed ended before reaching player");
            Vector3 localArrival=session.court.InverseTransformPoint(session.shuttle.transform.position);
            Require(Vector3.Distance(localArrival,session.reachMarker.localPosition)<.08f, "Live integration missed reach ring");
            session.TogglePause();
            Vector3 pausedPosition=session.shuttle.transform.position;
            session.shuttle.AdvanceFrame(.06f);
            Require(session.shuttle.transform.position==pausedPosition, "Paused shuttle moved");
            session.StartPractice(XRNode.RightHand);
            // Test-only fixture selection: find a physical, aimed stroke that
            // legally clears the net. Gameplay never searches or replaces velocity.
            RacketContact legalContact;
            Require(FindLegalContact(session.shuttle.State,session.court,session.PhysicsSettings,out legalContact), "No physical legal-return fixture found");
            Require(session.shuttle.ReturnShot(legalContact), "Integrated physical return failed");
            for (int i=0;i<900 && session.shuttle.Flying;i++) session.shuttle.AdvanceFrame(1f/90f);
            Require(!session.shuttle.Flying && session.Combo==1 && session.Score>=15, "Physical return did not score a legal landing");
            Require(!float.IsNaN(session.LastNetClearance) && session.LastNetClearance>0f, "Net clearance telemetry missing");
            Require(session.LastShot.speed>0f, "Physical shot telemetry missing");

            session.Recenter();
            session.StartPractice(XRNode.LeftHand);
            serve.Invoke(session,null);
            for (int i=0;i<900 && session.shuttle.Flying;i++) session.shuttle.AdvanceFrame(1f/90f);
            Require(!session.shuttle.Flying && session.Score==0 && session.Combo==0, "Unhit feed did not resolve as a miss");
            session.Recenter();
        }

        private static bool FindLegalContact(ShuttleState incoming, Transform court, ShuttlePhysicsSettings settings, out RacketContact contact)
        {
            contact=new RacketContact();
            foreach (float angle in new[] { 10f,20f,30f,40f,50f,60f })
            foreach (float speed in new[] { 2f,4f,6f,8f,12f,16f })
            {
                Vector3 normal=court.TransformDirection(new Vector3(0f,Mathf.Sin(angle*Mathf.Deg2Rad),Mathf.Cos(angle*Mathf.Deg2Rad)));
                RacketContact candidate=Contact(incoming.position,normal*speed,normal);
                ImpactResult impact=RacketImpact.Resolve(incoming,candidate,settings);
                if (!impact.hit) continue;
                ShuttleState test=incoming;
                test.velocity=impact.outgoingVelocity;
                test.angularVelocity=impact.angularVelocity;
                Vector3 landing;
                float clearance;
                if (LegalFlight(test,court,settings,out landing,out clearance))
                {
                    contact=candidate;
                    Debug.Log("[Shuttle Test] Legal physical return: face elevation=" + angle + "deg racket=" + speed + "m/s outgoing=" + impact.outgoingVelocity.magnitude.ToString("F2") + "m/s landing=" + landing);
                    return true;
                }
            }
            return false;
        }

        private static bool LegalFlight(ShuttleState state, Transform court, ShuttlePhysicsSettings settings, out Vector3 landing, out float clearance)
        {
            landing=state.position;
            clearance=float.NaN;
            for (int i=0;i<3600;i++)
            {
                Vector3 before=court!=null ? court.InverseTransformPoint(state.position) : state.position;
                ShuttlePhysics.Integrate(ref state,settings,ShuttlePhysics.Step);
                Vector3 after=court!=null ? court.InverseTransformPoint(state.position) : state.position;
                Vector3 crossing;
                if (ShuttleMath.NetCrossing(before,after,out crossing))
                {
                    clearance=crossing.y-BadmintonCourt.NetHeightAt(crossing.x);
                    if (Mathf.Abs(crossing.x)>BadmintonCourt.SinglesHalfWidth || clearance<=.018f) return false;
                }
                if (after.y>.025f) continue;
                landing=Vector3.Lerp(before,after,Mathf.Clamp01((before.y-.025f)/Mathf.Max(.00001f,before.y-after.y)));
                return !float.IsNaN(clearance) && ShuttleMath.IsInOpponentCourt(landing);
            }
            return false;
        }

        private static void TestFrameRates(BadmintonSession session)
        {
            ShuttleState reference=new ShuttleState();
            Vector3 referenceLanding=Vector3.zero;
            foreach (int fps in new[] { 90,72,120 })
            {
                session.Recenter();
                session.StartPractice(XRNode.RightHand);
                session.shuttle.Launch(session.court.TransformPoint(new Vector3(.22f,1.4f,.62f)),
                    session.court.TransformDirection(new Vector3(0f,-1f,-4f)));
                RacketContact contact;
                Require(FindLegalContact(session.shuttle.State,session.court,session.PhysicsSettings,out contact), "FPS test has no legal stroke fixture");
                Require(session.shuttle.ReturnShot(contact), "FPS fixture contact failed");
                for (float t=0f;t<.4f-.000001f;)
                {
                    float dt=Mathf.Min(1f/fps,.4f-t);
                    session.shuttle.AdvanceFrame(dt);
                    t+=dt;
                }
                Require(session.shuttle.Flying, "FPS fixture landed before comparison time");
                if (fps==90) reference=session.shuttle.State;
                else
                {
                    Require(Vector3.Distance(session.shuttle.State.position,reference.position)<.03f, "Flight position differs at " + fps + "fps");
                    Require(Vector3.Distance(session.shuttle.Velocity,reference.velocity)<.08f, "Flight speed differs at " + fps + "fps");
                }
                for (int i=0;i<fps*10 && session.shuttle.Flying;i++) session.shuttle.AdvanceFrame(1f/fps);
                Require(!session.shuttle.Flying && session.Combo==1, "Legal flight failed at " + fps + "fps");
                if (fps==90) referenceLanding=session.landingMarker.localPosition;
                else Require(Vector3.Distance(session.landingMarker.localPosition,referenceLanding)<.05f, "Landing differs at " + fps + "fps");
            }
            session.Recenter();
        }

        private static void TestDrills(BadmintonSession session)
        {
            Require(!session.simulatePlayerHeight,"Real-floor drill fixture must explicitly disable simulated stature");
            session.Recenter();
            Vector3 eyeBefore=session.playerCamera.transform.position;
            Quaternion headBefore=session.playerCamera.transform.rotation;
            session.SetDrill(PracticeDrill.NetTouch);
            Vector3 eye=session.court.InverseTransformPoint(eyeBefore);
            Require(session.drill==PracticeDrill.NetTouch && Mathf.Abs(eye.z-2.4f)<.001f && Mathf.Abs(eye.y-eyeBefore.y)<.001f, "Net drill must retain real floor/eye height");
            Vector3 netReach=session.reachMarker.localPosition;
            Require(Mathf.Abs(Mathf.Abs(netReach.x)-session.feedSide)<.001f && Mathf.Abs(netReach.y-(eyeBefore.y-.16f))<.001f && Mathf.Abs(netReach.z-3.02f)<.001f, "Net drill reachable target follows real eye height");
            Require(session.playerCamera.transform.position==eyeBefore && Quaternion.Angle(session.playerCamera.transform.rotation,headBefore)<.01f, "Drill moved XR head pose");
            session.StartPractice(XRNode.RightHand);
            session.SetDrill(PracticeDrill.Overhead);
            Require(session.drill==PracticeDrill.NetTouch && session.Running, "Active drill switch reset the court");
            bool assistBefore=session.PhysicsSettings.seatedAssist;
            session.ToggleAssist();
            Require(session.PhysicsSettings.seatedAssist==assistBefore, "Assist changed while playing");
            MethodInfo serve=typeof(BadmintonSession).GetMethod("Serve",BindingFlags.Instance|BindingFlags.NonPublic);
            serve.Invoke(session,null);
            for (int i=0;i<76;i++) session.shuttle.AdvanceFrame(1f/90f);
            Require(session.shuttle.Flying && Vector3.Distance(session.court.InverseTransformPoint(session.shuttle.transform.position),session.reachMarker.localPosition)<.08f, "Live net drill feed missed ring");
            session.TogglePause();
            Vector3 pausedPosition=session.shuttle.transform.position;
            Quaternion pausedCourt=session.court.rotation;
            session.ToggleAssist();
            Require(session.PhysicsSettings.seatedAssist==assistBefore, "Paused in-flight assist changed the launched model");
            session.SetDrill(PracticeDrill.Overhead);
            Require(session.drill==PracticeDrill.NetTouch && session.shuttle.Flying && !session.Running &&
                session.shuttle.transform.position==pausedPosition && Quaternion.Angle(session.court.rotation,pausedCourt)<.001f,
                "Paused in-flight drill switch changed or erased the active shuttle");
            Require(session.Recenter(), "Explicit reset before settings adjustment failed");
            session.ToggleAssist();
            Require(session.PhysicsSettings.seatedAssist!=assistBefore, "Assist toggle after explicit reset failed");
            session.ToggleAssist();
            session.SetDrill(PracticeDrill.Overhead);
            eye=session.court.InverseTransformPoint(eyeBefore);
            Require(Mathf.Abs(eye.z-1.2f)<.001f && Mathf.Abs(eye.y-eyeBefore.y)<.001f, "Overhead drill must retain real floor/eye height");
            Require(Mathf.Abs(session.reachMarker.localPosition.y-(eyeBefore.y+.22f))<.001f && Mathf.Abs(session.reachMarker.localPosition.z-1.82f)<.001f && !session.shuttle.Flying && session.Feeds==0, "Overhead drill reset/reachable target");
            session.SetDrill(PracticeDrill.Rally);
            session.Recenter();
        }

        private static void TestAlignment(BadmintonSession session)
        {
            MethodInfo serve=typeof(BadmintonSession).GetMethod("Serve",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach (float yaw in new[] { -135f,-90f,-30f,0f,75f,170f })
            {
                session.playerCamera.transform.SetPositionAndRotation(new Vector3(1.2f,1.25f,-.7f),Quaternion.Euler(0f,yaw-90f,0f));
                Require(session.Recenter(), "Pre-start recenter failed");
                session.playerCamera.transform.rotation=Quaternion.Euler(15f,yaw,0f);
                Vector3 eyeBefore=session.playerCamera.transform.position;
                Quaternion headBefore=session.playerCamera.transform.rotation;
                session.StartPractice(XRNode.RightHand);
                Vector3 desiredForward=Vector3.ProjectOnPlane(session.playerCamera.transform.forward,Vector3.up).normalized;
                Vector3 feederDirection=Vector3.ProjectOnPlane(session.court.TransformPoint(new Vector3(0f,1.5f,8.6f))-eyeBefore,Vector3.up).normalized;
                Require(Vector3.Angle(feederDirection,desiredForward)<.05f, "Feeder is not straight ahead at yaw " + yaw);
                Require(session.playerCamera.transform.position==eyeBefore && Quaternion.Angle(session.playerCamera.transform.rotation,headBefore)<.01f, "Alignment moved the XR camera");
                serve.Invoke(session,null);
                session.TogglePause();
                Quaternion courtBeforeResume=session.court.rotation;
                session.playerCamera.transform.rotation=Quaternion.Euler(0f,yaw+70f,0f);
                session.StartPractice(XRNode.RightHand);
                Require(Quaternion.Angle(session.court.rotation,courtBeforeResume)<.01f && session.Feeds==1 && session.shuttle.Flying, "Resume rotated court or erased active shot");
            }
            session.Recenter();
            Quaternion beforeVertical=session.court.rotation;
            session.playerCamera.transform.rotation=Quaternion.Euler(90f,60f,0f);
            Require(!session.Recenter() && Quaternion.Angle(session.court.rotation,beforeVertical)<.01f, "Vertical look reset arbitrary world heading");
        }

        private static bool ClearsNet(Vector3 position, Vector3 velocity)
        {
            ShuttleState state=ShuttleState.Launch(position,velocity);
            for (int i=0;i<3600;i++)
            {
                Vector3 before=state.position;
                ShuttlePhysics.Integrate(ref state,strict,ShuttlePhysics.Step);
                if (ShuttleMath.CrossesNet(before,state.position)) return false;
                if (state.position.y<0f) break;
            }
            return true;
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x)&&!float.IsNaN(value.y)&&!float.IsNaN(value.z)&&
                !float.IsInfinity(value.x)&&!float.IsInfinity(value.y)&&!float.IsInfinity(value.z);
        }
    }
}
