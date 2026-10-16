using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace ViveShuttleVR.Editor
{
    public static class OpponentIntegrationRegression
    {
        private static int checks;
        private static float maximumContactArmExcess,maximumContactHandGap;

        public static void RunStandalone()
        {
            EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
            BadmintonSession session=UnityEngine.Object.FindObjectOfType<BadmintonSession>();
            if (session==null) throw new InvalidOperationException("AI integration scene has no session");
            session.playerCamera.GetComponent<TrackedPoseDriver>().enabled=false;
            Run(session);
        }

        public static int Run(BadmintonSession session)
        {
            checks=0;
            maximumContactArmExcess=maximumContactHandGap=0f;
            Transform head=session.playerCamera.transform;
            Vector3 headBefore=head.position;
            Quaternion rotationBefore=head.rotation;
            PracticeMode modeBefore=session.mode;
            PracticeDrill drillBefore=session.drill;
            OpponentDifficulty difficultyBefore=session.Difficulty;
            ShuttlePhysicsSettings physicsBefore=session.physics;
            RacketController[] racketsBefore=session.rackets;
            bool heightBefore=session.simulatePlayerHeight;
            bool compactBefore=session.compactSeatedTargets;
            float statureBefore=session.simulatedStature;
            try
            {
                session.mode=PracticeMode.AiRally;
                session.drill=PracticeDrill.Rally;
                session.physics=BadmintonOpponent.CreateStrictSettings(new ShuttlePhysicsSettings());
                // Live player contacts below are explicit physical test fixtures,
                // not sampled XR devices. Never install a human aim solver in game.
                session.rackets=new RacketController[0];
                TestPointRules();
                TestPresentation(session);
                foreach (bool compact in new[] { false,true })
                foreach (bool height in new[] { false,true })
                foreach (OpponentDifficulty difficulty in Enum.GetValues(typeof(OpponentDifficulty)))
                foreach (int frameRate in new[] { 72,90,120 })
                foreach (float yaw in new[] { 0f,135f })
                {
                    session.compactSeatedTargets=compact;
                    session.simulatePlayerHeight=height;
                    session.simulatedStature=1.75f;
                    session.Difficulty=difficulty;
                    head.SetPositionAndRotation(new Vector3(1.4f,height?1.25f:1.65f,-.8f),Quaternion.Euler(0f,yaw,0f));
                    RunLiveRally(session,yaw,frameRate);
                }
                TestDeflection(session);
                TestEfforts(session);
                TestMatchCompletion(session);
            }
            finally
            {
                session.shuttle.Stop();
                session.mode=modeBefore;
                session.drill=drillBefore;
                session.Difficulty=difficultyBefore;
                session.physics=physicsBefore;
                session.rackets=racketsBefore;
                session.simulatePlayerHeight=heightBefore;
                session.compactSeatedTargets=compactBefore;
                session.simulatedStature=statureBefore;
                head.SetPositionAndRotation(headBefore,rotationBefore);
                session.Recenter();
            }
            Debug.Log("[Opponent Integration] PASS "+checks+" checks: 72 live cases across 3 difficulties, 72/90/120 FPS, compact/legacy targets, simulated/real eye height and yaw; physical exchanges, bounded chase and miss swings, pause, hit guards, deflection replan, stationary camera, scoring.");
            Debug.Log("[Opponent Anatomy] Actual physical contact maximum grip-over-arm="+maximumContactArmExcess.ToString("F3")+
                "m; sampled visible hand gap="+maximumContactHandGap.ToString("F3")+"m. Diagnostic only: no arm stretching or impact relocation.");
            return checks;
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("[Opponent Integration] "+message);
        }

        private static void Serve(BadmintonSession session)
        {
            MethodInfo serve=typeof(BadmintonSession).GetMethod("Serve",BindingFlags.Instance|BindingFlags.NonPublic);
            Require(serve!=null,"Session feeder entry point missing");
            serve.Invoke(session,null);
        }

        private static void AdvanceFrame(BadmintonSession session,float dt)
        {
            session.shuttle.AdvanceFrame(dt);
            session.opponent.SamplePresentation(dt);
        }

        private static void TestPresentation(BadmintonSession session)
        {
            Require(session.athletePrefab!=null,"Playable scene has no animated athlete prefab");
            Require(session.Recenter(),"Presentation recenter failed");
            session.StartPractice(XRNode.RightHand);
            AthletePresentation athlete=session.opponent.Presentation;
            Require(athlete!=null,"AI used primitive fallback despite the assigned athlete prefab");
            SkinnedMeshRenderer[] skins=athlete.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(skins.Length>0,"Athlete does not contain a skinned character");
            foreach (SkinnedMeshRenderer skin in skins)
                Require(skin.sharedMesh!=null && skin.bones.Length>0,"Athlete skin has no mesh/bones");
            Vector3 feet=session.opponent.FeetLocal,racket=session.opponent.RacketHeadWorld;
            Quaternion racketRotation=session.opponent.transform.Find("Visible AI player/AI racket contact head").rotation;
            session.opponent.SamplePresentation(1f/90f);
            Require(session.opponent.FeetLocal==feet && session.opponent.RacketHeadWorld==racket &&
                session.opponent.transform.Find("Visible AI player/AI racket contact head").rotation==racketRotation,
                "Presentation IK moved physical feet/racket");
            Transform racketHead=session.opponent.transform.Find("Visible AI player/AI racket contact head");
            float armLength,gripDistance,handGap;
            MeasureAnatomy(athlete,racketHead,out armLength,out gripDistance,out handGap);
            Require(session.court.InverseTransformPoint(athlete.rightUpperArm.position).x<feet.x &&
                session.court.InverseTransformPoint(athlete.GripTarget).x<feet.x,"Ready grip crossed to the athlete's opposite side");
            Require(gripDistance<armLength-.015f && handGap<.025f,"Ready racket grip is outside the real rig's arm reach");
            session.opponent.PrepareServe();
            session.opponent.SamplePresentation(1f/90f);
            MeasureAnatomy(athlete,racketHead,out armLength,out gripDistance,out handGap);
            Require(gripDistance<armLength-.015f && handGap<.025f,"Practice serve grip is outside the real rig's arm reach");
            Debug.Log("[Opponent Anatomy] Ready/serve are on anatomical right; arm="+armLength.ToString("F3")+
                "m serveGripDistance="+gripDistance.ToString("F3")+"m handGap="+handGap.ToString("F3")+"m");
            session.opponent.ResetRally();
            Transform[] bones=skins[0].bones;
            var positions=new Vector3[bones.Length];
            var rotations=new Quaternion[bones.Length];
            for (int i=0;i<bones.Length;i++) { positions[i]=bones[i].localPosition; rotations[i]=bones[i].localRotation; }
            session.TogglePause();
            for (int frame=0;frame<30;frame++) session.opponent.SamplePresentation(1f/90f);
            for (int i=0;i<bones.Length;i++)
                Require(bones[i].localPosition==positions[i] && bones[i].localRotation==rotations[i],"Paused character animation advanced");
            session.Recenter();
        }

        private static void MeasureAnatomy(AthletePresentation athlete,Transform racketHead,
            out float armLength,out float gripDistance,out float handGap)
        {
            Vector3 grip=racketHead.TransformPoint(RacketSpecification.GripCenter-RacketSpecification.HeadCenter);
            armLength=Vector3.Distance(athlete.rightUpperArm.position,athlete.rightForearm.position)+
                Vector3.Distance(athlete.rightForearm.position,athlete.rightHand.position);
            gripDistance=Vector3.Distance(athlete.rightUpperArm.position,grip);
            handGap=Vector3.Distance(athlete.rightHand.position,grip);
        }

        private static void RunLiveRally(BadmintonSession session, float yaw,int frameRate)
        {
            float frameDt=1f/frameRate;
            Require(session.Recenter(),"Recenter failed at yaw="+yaw);
            session.opponent.ResetMatch();
            session.StartPractice(XRNode.RightHand);
            Serve(session);
            Require(session.Running && session.shuttle.Flying && !session.shuttle.Returned && session.Feeds==1,
                "AI practice starter did not launch");
            Require(Vector3.Distance(session.shuttle.State.position,session.opponent.ServePosition)<.001f,
                "AI practice starter is not at its visible hand/racket");
            Vector3 courtPosition=session.court.position;
            Quaternion courtRotation=session.court.rotation;
            Vector3 headPosition=session.playerCamera.transform.position;
            Quaternion headRotation=session.playerCamera.transform.rotation;
            float duration=session.LastFeedProfile.duration;
            for (float time=0f;time<duration-.000001f;)
            {
                float dt=Mathf.Min(frameDt,duration-time);
                AdvanceFrame(session,dt);
                time+=dt;
            }
            Require(session.shuttle.Flying && !session.shuttle.Returned,"AI starter struck the net or ended before player");
            Require(Vector3.Distance(session.shuttle.State.position,session.court.TransformPoint(session.LastFeedProfile.targetLocal))<.025f,
                "AI starter does not reach the stationary player");

            RacketContact first=PlayerFixture(session);
            var beginTimer=System.Diagnostics.Stopwatch.StartNew();
            Require(session.shuttle.ReturnShot(first),"Physical first player contact failed");
            beginTimer.Stop();
            Require(session.shuttle.Returned && session.RallyStrokes==1 && session.Hits==1 && session.opponent.Tracking,
                "Player hit did not hand receive turn to AI");
            Require(!session.shuttle.ReturnShot(first),"Player double-hit accepted");
            Require(!session.shuttle.OpponentReturn(first),"Opponent hit accepted before crossing net");

            Vector3 reactionFeet=session.opponent.FeetLocal;
            int attemptsBeforeReaction=session.opponent.ReturnAttempts;
            int reactionFrames=Mathf.Max(1,Mathf.FloorToInt(OpponentAbility.For(session.Difficulty).reaction*frameRate*.5f));
            for (int frame=0;frame<reactionFrames;frame++) AdvanceFrame(session,frameDt);
            Require(session.opponent.FeetLocal==reactionFeet && session.opponent.ReturnAttempts==attemptsBeforeReaction,
                "AI moved/hit before its reaction delay");
            session.TogglePause();
            ShuttleState paused=session.shuttle.State;
            Vector3 pausedFeet=session.opponent.FeetLocal;
            int pausedStrokes=session.RallyStrokes;
            float pausedError=session.opponent.LastAimError;
            string pausedDecision=session.opponent.LastDecision;
            for (int frame=0;frame<frameRate;frame++) AdvanceFrame(session,frameDt);
            Require(session.shuttle.State.position==paused.position && session.shuttle.State.velocity==paused.velocity &&
                session.shuttle.State.orientation==paused.orientation &&
                session.opponent.FeetLocal==pausedFeet && session.RallyStrokes==pausedStrokes &&
                session.opponent.LastAimError==pausedError && session.opponent.LastDecision==pausedDecision,
                "Pause advanced shuttle, AI feet, orientation or hit state");
            session.StartPractice(XRNode.RightHand);
            Require(session.shuttle.Flying && session.shuttle.State.position==paused.position && session.RallyStrokes==1,
                "Resume reset an in-progress rally");

            int initialAttempts=session.opponent.ReturnAttempts;
            float speed=OpponentAbility.For(session.Difficulty).runSpeed;
            float maximumFootDisplacement=0f;
            double maxFrameMilliseconds=0;
            for (int frame=0;frame<900 && session.shuttle.Flying && session.shuttle.Returned;frame++)
            {
                Vector3 feetBefore=session.opponent.FeetLocal;
                var frameTimer=System.Diagnostics.Stopwatch.StartNew();
                AdvanceFrame(session,frameDt);
                frameTimer.Stop();
                maxFrameMilliseconds=Math.Max(maxFrameMilliseconds,frameTimer.Elapsed.TotalMilliseconds);
                float moved=Vector3.Distance(feetBefore,session.opponent.FeetLocal);
                maximumFootDisplacement=Mathf.Max(maximumFootDisplacement,moved);
                Require(moved<=speed*frameDt+.0001f,"AI teleported during live footwork");
            }
            Require(session.shuttle.Flying && !session.shuttle.Returned && session.RallyStrokes==2 &&
                session.opponent.ReturnAttempts==initialAttempts+1,
                "Live AI did not physically return: "+session.opponent.LastDecision+" yaw="+yaw+" fps="+frameRate+" difficulty="+session.Difficulty+
                " aimError="+session.opponent.LastAimError+" position="+session.court.InverseTransformPoint(session.shuttle.State.position));
            Require(maximumFootDisplacement>0f,"Opponent returned without showing required movement");
            float armLength,gripDistance,handGap;
            MeasureAnatomy(session.opponent.Presentation,
                session.opponent.transform.Find("Visible AI player/AI racket contact head"),out armLength,out gripDistance,out handGap);
            maximumContactArmExcess=Mathf.Max(maximumContactArmExcess,gripDistance-armLength);
            maximumContactHandGap=Mathf.Max(maximumContactHandGap,handGap);
            Require(gripDistance<=armLength+.003f && handGap<.025f,
                "Physical AI contact floated beyond real arm: distance="+gripDistance+" arm="+armLength+" gap="+handGap);
            Require(!session.shuttle.OpponentReturn(first),"AI double-hit accepted");

            Vector3 target=session.reachMarker.position;
            bool reachedPlayer=false;
            for (int frame=0;frame<500 && session.shuttle.Flying;frame++)
            {
                if (Vector3.Distance(session.shuttle.State.position,target)<.12f) { reachedPlayer=true; break; }
                AdvanceFrame(session,frameDt);
            }
            Require(reachedPlayer && !session.shuttle.Returned,
                "AI return did not come back to player ring (no tactical corner targeting)");
            RacketContact second=PlayerFixture(session);
            Require(session.shuttle.ReturnShot(second) && session.shuttle.Returned && session.RallyStrokes==3 && session.Hits==2,
                "Player could not hit again after AI return");
            Require(!session.shuttle.ReturnShot(second),"Second player stroke double-hit accepted");

            // Let the real simulation resolve the point; whether the AI misses or
            // returns is allowed, but exactly one side must receive the point.
            for (int frame=0;frame<1800 && session.shuttle.Flying;frame++) AdvanceFrame(session,frameDt);
            Require(!session.shuttle.Flying && session.PlayerPoints+session.OpponentPoints==1,
                "Live rally did not terminate in exactly one practice point");
            Require(session.court.position==courtPosition && Quaternion.Angle(session.court.rotation,courtRotation)<.001f &&
                session.playerCamera.transform.position==headPosition &&
                Quaternion.Angle(session.playerCamera.transform.rotation,headRotation)<.001f,
                "AI play/reaction/pause/point moved player or court");
            Debug.Log("[Opponent Integration] yaw="+yaw+" fps="+frameRate+" difficulty="+session.Difficulty+
                " simulatedHeight="+session.simulatePlayerHeight+" compact="+session.compactSeatedTargets+" rally="+session.RallyStrokes+
                " score="+session.PlayerPoints+":"+session.OpponentPoints+" maxStep="+maximumFootDisplacement.ToString("F5")+
                " maxAIFrameMs="+maxFrameMilliseconds.ToString("F2")+" beginTrackingMs="+beginTimer.Elapsed.TotalMilliseconds.ToString("F2")+
                " contactGripExcess="+Mathf.Max(0f,gripDistance-armLength).ToString("F3")+"m handGap="+handGap.ToString("F3")+"m");
        }

        private static RacketContact PlayerFixture(BadmintonSession session)
        {
            OpponentAbility control=OpponentAbility.For(OpponentDifficulty.Club);
            control.returnDuration=1.75f;
            control.racketSpeed=22f;
            // Test-only inverse control gives the opponent a legal centre clear.
            // The resulting racket pose still passes the actual player contact
            // API; it does not inject a post-hit velocity or fabricate an AI hit.
            Vector3 target=session.court.TransformPoint(new Vector3(.10f,2.20f,6.30f));
            RacketContact contact;
            float error;
            bool found=BadmintonOpponent.TryPlanReturn(session.shuttle.State,target,session.court,
                session.PhysicsSettings,control,out contact,out error);
            Require(found,"Could not construct physical player clear test fixture, error="+error);
            ImpactResult result=RacketImpact.Resolve(session.shuttle.State,contact,session.PhysicsSettings);
            Require(result.hit && session.court.InverseTransformDirection(result.outgoingVelocity).z>0f,
                "Player fixture contact does not send ball toward AI");
            return contact;
        }

        private static void TestDeflection(BadmintonSession session)
        {
            session.shuttle.Stop();
            session.Difficulty=OpponentDifficulty.Easy;
            session.Recenter();
            session.opponent.ResetMatch();
            BadmintonOpponent opponent=session.opponent;
            ShuttleState original=ShuttleState.Launch(session.court.TransformPoint(new Vector3(.1f,2.35f,5.9f)),
                session.court.TransformDirection(new Vector3(.1f,2.5f,3f)));
            opponent.BeginTracking(original);
            Require(opponent.ObservedShots==1,"Initial observed stroke counter incorrect");
            int attempts=opponent.ReturnAttempts;
            ShuttleState tape=ShuttleState.Launch(session.court.TransformPoint(new Vector3(.1f,1.55f,ShuttleMath.NetZ+.025f)),
                session.court.TransformDirection(new Vector3(0f,.3f,2f)));
            opponent.OnIncomingDeflected(tape);
            Require(opponent.ObservedShots==1 && opponent.ReturnAttempts==attempts,
                "Tape deflection counted as a new player/AI strike");
            Require(!opponent.HasIntercept && opponent.EffortActive,
                "Distant AI retained a stale intercept or gave up pursuing a tape deflection");

            // Set up a preceding near-net position for a deterministic unit
            // fixture. Runtime footwork still has no reposition/teleport API.
            FieldInfo feet=typeof(BadmintonOpponent).GetField("feet",BindingFlags.NonPublic|BindingFlags.Instance);
            FieldInfo miss=typeof(BadmintonOpponent).GetField("intentionallyMiss",BindingFlags.NonPublic|BindingFlags.Instance);
            Require(feet!=null && miss!=null,"Deflection test state fields missing");
            feet.SetValue(opponent,new Vector3(.1f,0f,4.3f));
            opponent.OnIncomingDeflected(tape);
            Require(opponent.HasIntercept && opponent.Tracking && opponent.ObservedShots==1,
                "Nearby AI did not replace its stale plan after a legal tape contact");
            for (int frame=0;frame<360 && opponent.Tracking && opponent.LastDecision!="RETURN PLANNED";frame++)
                opponent.AdvancePlanning();
            Require(opponent.LastDecision=="RETURN PLANNED" && opponent.LastAimError<.065f,
                "Tape replan did not produce a controlled physical return: "+opponent.LastDecision);
            Require(opponent.ObservedShots==1 && opponent.ReturnAttempts==attempts,
                "Planning alone produced a hit/counter increment");

            miss.SetValue(opponent,true);
            opponent.OnIncomingDeflected(tape);
            Require((bool)miss.GetValue(opponent) && opponent.ObservedShots==1,
                "Tape replan re-rolled a deliberate mistake as a fresh stroke");
            RacketContact unused;
            for (int step=0;step<60;step++) opponent.TryContact(tape,tape,ShuttlePhysics.Step,out unused);
            Require(opponent.EffortActive && opponent.EffortSwings>0 && opponent.ReturnAttempts==attempts,
                "Deflection changed a missed read into a fake hit, or suppressed its recovery effort");
        }

        private static void TestEfforts(BadmintonSession session)
        {
            session.Difficulty=OpponentDifficulty.Easy;
            foreach (string scenario in new[] { "SHORT","WIDE","LATE","MISREAD","UNSOLVABLE" })
            {
                session.Recenter();
                session.StartPractice(XRNode.RightHand);
                Serve(session); // Establish a resumable in-progress point.
                BadmintonOpponent opponent=session.opponent;
                opponent.ResetMatch();
                Vector3 localPosition,localVelocity;
                if (scenario=="SHORT") { localPosition=new Vector3(.15f,1.25f,3.95f); localVelocity=new Vector3(0f,-1f,.8f); }
                else if (scenario=="WIDE") { localPosition=new Vector3(3.2f,1.5f,6.4f); localVelocity=new Vector3(1.5f,-.5f,1f); }
                else if (scenario=="LATE") { localPosition=new Vector3(-1.6f,.45f,6.2f); localVelocity=new Vector3(0f,-2f,.5f); }
                else { localPosition=new Vector3(.4f,2.35f,5.9f); localVelocity=new Vector3(0f,2.5f,3f); }
                ShuttleState state=ShuttleState.Launch(session.court.TransformPoint(localPosition),session.court.TransformDirection(localVelocity));
                session.shuttle.Launch(state.position,state.velocity);
                // Test fixture: begin on the AI side with the human as last
                // striker. This sets lifecycle only, never forges an impact.
                MethodInfo setReturned=typeof(ShuttleFlight).GetProperty("Returned").GetSetMethod(true);
                Require(setReturned!=null,"Effort fixture cannot assign receiving side");
                setReturned.Invoke(session.shuttle,new object[] { true });
                if (scenario=="MISREAD")
                    typeof(BadmintonOpponent).GetField("shotNumber",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(opponent,6);
                Transform head=session.playerCamera.transform;
                Vector3 savedHead=head.position;
                if (scenario=="UNSOLVABLE") head.position+=session.court.right*12f;
                opponent.BeginTracking(state);
                head.position=savedHead;
                if (scenario=="UNSOLVABLE")
                {
                    // Force a genuinely impossible safe-court destination, then
                    // finish the bounded incremental search without advancing
                    // simulation time. This unit fixture tests its failure branch,
                    // not a runtime synchronous search or a looser aim tolerance.
                    for (int step=0;step<600 && !opponent.EffortActive;step++) opponent.AdvancePlanning();
                    Require(opponent.LastDecision=="CHASE / NO CONTROLLED RETURN","Unsolvable contact plan did not enter effort mode");
                }
                Require(opponent.EffortActive,"AI gave up instead of chasing: "+scenario);
                Vector3 initialFeet=opponent.FeetLocal;
                Vector3 initialHead=head.position;
                Vector3 initialCourt=session.court.position;
                int attempts=opponent.ReturnAttempts;
                int hits=session.Hits,strokes=session.RallyStrokes;
                session.TogglePause();
                Vector3 pausedRacket=opponent.RacketHeadWorld;
                for (int frame=0;frame<30;frame++) AdvanceFrame(session,1f/90f);
                Require(opponent.FeetLocal==initialFeet && opponent.RacketHeadWorld==pausedRacket && opponent.EffortSwings==0,
                    "Paused pursuit or recovery swing advanced: "+scenario);
                session.StartPractice(XRNode.RightHand);
                bool sawSwingBeforePoint=false;
                bool pausedDuringSwing=false;
                float travelled=0f;
                double maxMilliseconds=0;
                for (int frame=0;frame<900 && session.shuttle.Flying;frame++)
                {
                    Vector3 previous=opponent.FeetLocal;
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    AdvanceFrame(session,1f/90f);
                    timer.Stop();
                    maxMilliseconds=Math.Max(maxMilliseconds,timer.Elapsed.TotalMilliseconds);
                    float armLength,gripDistance,handGap;
                    MeasureAnatomy(opponent.Presentation,opponent.transform.Find("Visible AI player/AI racket contact head"),
                        out armLength,out gripDistance,out handGap);
                    Require(gripDistance<=armLength+.003f && handGap<.025f,"Miss swing racket floated outside anatomical reach: "+scenario);
                    float moved=Vector3.Distance(previous,opponent.FeetLocal);
                    travelled+=moved;
                    Require(moved<=OpponentAbility.For(session.Difficulty).runSpeed/90f+.0001f,"Recovery footwork teleported: "+scenario);
                    Require(Mathf.Abs(opponent.FeetLocal.x)<=2.4501f && opponent.FeetLocal.z>=ShuttleMath.NetZ+.2999f &&
                        opponent.FeetLocal.z<=10.1501f,"Recovery feet left permitted court: "+scenario);
                    sawSwingBeforePoint|=session.shuttle.Flying && opponent.EffortSwings>0;
                    Require(opponent.ReturnAttempts==attempts && session.Hits==hits && session.RallyStrokes==strokes,
                        "A visual attempt fabricated a physical hit: "+scenario);
                    if (!pausedDuringSwing && session.shuttle.Flying && opponent.EffortSwingActive)
                    {
                        Vector3 swingFeet=opponent.FeetLocal,swingRacket=opponent.RacketHeadWorld;
                        ShuttleState swingBall=session.shuttle.State;
                        session.TogglePause();
                        for (int pausedFrame=0;pausedFrame<20;pausedFrame++) AdvanceFrame(session,1f/90f);
                        Require(opponent.FeetLocal==swingFeet && opponent.RacketHeadWorld==swingRacket &&
                            session.shuttle.State.position==swingBall.position && opponent.EffortSwingActive,
                            "Pause did not freeze an active recovery swing: "+scenario);
                        session.StartPractice(XRNode.RightHand);
                        pausedDuringSwing=true;
                    }
                }
                Require(!session.shuttle.Flying && travelled>.015f && sawSwingBeforePoint && pausedDuringSwing,
                    "Losing AI did not visibly run and swing before landing: "+scenario+" travelled="+travelled+" swing="+opponent.EffortSwings);
                Require(session.PlayerPoints+session.OpponentPoints==1,"Effort generated extra/missing points: "+scenario);
                Require(!opponent.Tracking && !opponent.EffortActive,"Ended point retained active pursuit: "+scenario);
                Require(head.position==initialHead && session.court.position==initialCourt,"Recovery moved human/court: "+scenario);
                Vector3 finishedFeet=opponent.FeetLocal;
                for (int frame=0;frame<60;frame++) opponent.Idle(1f/90f);
                Require(opponent.FeetLocal==finishedFeet && !opponent.EffortSwingActive,"Post-point recovery moved feet or never completed");
                opponent.ResetRally();
                Require(!opponent.EffortActive && !opponent.EffortSwingActive && !opponent.Tracking,"Reset retained an effort/swing");
                Debug.Log("[Opponent Effort] "+scenario+" travel="+travelled.ToString("F3")+"m swingBeforePoint="+sawSwingBeforePoint+
                    " maxFrameMs="+maxMilliseconds.ToString("F3"));
            }
        }

        private static void TestPointRules()
        {
            Vector3 far=new Vector3(0f,.025f,7f),near=new Vector3(0f,.025f,.5f),wide=new Vector3(4f,.025f,6f);
            Require(BadmintonSession.PlayerWinsAiPoint("LAND",far,true),"Good player shot landing not player point");
            Require(!BadmintonSession.PlayerWinsAiPoint("LAND",near,true),"Player shot falling short wrongly scores");
            Require(!BadmintonSession.PlayerWinsAiPoint("NET",far,true),"Player net fault wrongly scores");
            Require(!BadmintonSession.PlayerWinsAiPoint("OUT",wide,true),"Player out wrongly scores");
            Require(!BadmintonSession.PlayerWinsAiPoint("MISS",near,false),"Legal AI winner wrongly awards player");
            Require(BadmintonSession.PlayerWinsAiPoint("MISS",wide,false),"AI wide shot not player point");
            Require(BadmintonSession.PlayerWinsAiPoint("NET",near,false),"AI net fault not player point");
        }

        private static void TestMatchCompletion(BadmintonSession session)
        {
            session.Recenter();
            session.StartPractice(XRNode.RightHand);
            // Unit-test seven distinct finished-point events. Full live flight
            // resolution and one-point accounting were exercised above.
            for (int point=1;point<=7;point++)
            {
                session.shuttle.Stop();
                session.OnFinished("LAND",new Vector3(0f,.025f,7f),true);
                Require(session.PlayerPoints==point && session.OpponentPoints==0,"Practice scoreboard increment mismatch");
            }
            Require(session.MatchComplete && !session.Running,"First-to-seven practice match never ends");
            session.StartPractice(XRNode.RightHand);
            Require(session.Running && !session.MatchComplete && session.PlayerPoints==0 && session.OpponentPoints==0,
                "Trigger did not start a fresh practice match");
        }
    }
}
