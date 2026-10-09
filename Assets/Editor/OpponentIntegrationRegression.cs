using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace ViveShuttleVR.Editor
{
    public static class OpponentIntegrationRegression
    {
        private static int checks;

        public static int Run(BadmintonSession session)
        {
            checks=0;
            Transform head=session.playerCamera.transform;
            Vector3 headBefore=head.position;
            Quaternion rotationBefore=head.rotation;
            PracticeMode modeBefore=session.mode;
            PracticeDrill drillBefore=session.drill;
            OpponentDifficulty difficultyBefore=session.Difficulty;
            ShuttlePhysicsSettings physicsBefore=session.physics;
            RacketController[] racketsBefore=session.rackets;
            try
            {
                session.mode=PracticeMode.AiRally;
                session.drill=PracticeDrill.Rally;
                session.Difficulty=OpponentDifficulty.Easy;
                session.physics=BadmintonOpponent.CreateStrictSettings(new ShuttlePhysicsSettings());
                // Live player contacts below are explicit physical test fixtures,
                // not sampled XR devices. Never install a human aim solver in game.
                session.rackets=new RacketController[0];
                TestPointRules();
                foreach (float yaw in new[] { 0f,135f })
                {
                    head.SetPositionAndRotation(new Vector3(1.4f,1.65f,-.8f),Quaternion.Euler(0f,yaw,0f));
                    RunLiveRally(session,yaw);
                }
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
                head.SetPositionAndRotation(headBefore,rotationBefore);
                session.Recenter();
            }
            Debug.Log("[Opponent Integration] PASS "+checks+" checks: live starter, physical player/AI/player alternation, honest reaction/footwork, paused flight and AI, hit guards, rotated courts, stationary camera, rally resolution and first-to-seven practice scoring.");
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

        private static void RunLiveRally(BadmintonSession session, float yaw)
        {
            Require(session.Recenter(),"Recenter failed at yaw="+yaw);
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
                float dt=Mathf.Min(1f/90f,duration-time);
                session.shuttle.AdvanceFrame(dt);
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
            for (int frame=0;frame<15;frame++) session.shuttle.AdvanceFrame(1f/90f);
            Require(session.opponent.FeetLocal==reactionFeet && session.opponent.ReturnAttempts==attemptsBeforeReaction,
                "Easy AI moved/hit before its reaction delay");
            session.TogglePause();
            ShuttleState paused=session.shuttle.State;
            Vector3 pausedFeet=session.opponent.FeetLocal;
            int pausedStrokes=session.RallyStrokes;
            for (int frame=0;frame<90;frame++) session.shuttle.AdvanceFrame(1f/90f);
            Require(session.shuttle.State.position==paused.position && session.shuttle.State.velocity==paused.velocity &&
                session.shuttle.State.orientation==paused.orientation &&
                session.opponent.FeetLocal==pausedFeet && session.RallyStrokes==pausedStrokes,
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
                session.shuttle.AdvanceFrame(1f/90f);
                frameTimer.Stop();
                maxFrameMilliseconds=Math.Max(maxFrameMilliseconds,frameTimer.Elapsed.TotalMilliseconds);
                float moved=Vector3.Distance(feetBefore,session.opponent.FeetLocal);
                maximumFootDisplacement=Mathf.Max(maximumFootDisplacement,moved);
                Require(moved<=speed/90f+.0001f,"AI teleported during live footwork");
            }
            Require(session.shuttle.Flying && !session.shuttle.Returned && session.RallyStrokes==2 &&
                session.opponent.ReturnAttempts==initialAttempts+1,
                "Live AI did not physically return: "+session.opponent.LastDecision+" yaw="+yaw+
                " aimError="+session.opponent.LastAimError+" position="+session.court.InverseTransformPoint(session.shuttle.State.position));
            Require(maximumFootDisplacement>0f,"Opponent returned without showing required movement");
            Require(!session.shuttle.OpponentReturn(first),"AI double-hit accepted");

            Vector3 target=session.reachMarker.position;
            bool reachedPlayer=false;
            for (int frame=0;frame<500 && session.shuttle.Flying;frame++)
            {
                if (Vector3.Distance(session.shuttle.State.position,target)<.12f) { reachedPlayer=true; break; }
                session.shuttle.AdvanceFrame(1f/90f);
            }
            Require(reachedPlayer && !session.shuttle.Returned,
                "AI return did not come back to player ring (no tactical corner targeting)");
            RacketContact second=PlayerFixture(session);
            Require(session.shuttle.ReturnShot(second) && session.shuttle.Returned && session.RallyStrokes==3 && session.Hits==2,
                "Player could not hit again after AI return");
            Require(!session.shuttle.ReturnShot(second),"Second player stroke double-hit accepted");

            // Let the real simulation resolve the point; whether the AI misses or
            // returns is allowed, but exactly one side must receive the point.
            for (int frame=0;frame<1800 && session.shuttle.Flying;frame++) session.shuttle.AdvanceFrame(1f/90f);
            Require(!session.shuttle.Flying && session.PlayerPoints+session.OpponentPoints==1,
                "Live rally did not terminate in exactly one practice point");
            Require(session.court.position==courtPosition && Quaternion.Angle(session.court.rotation,courtRotation)<.001f &&
                session.playerCamera.transform.position==headPosition &&
                Quaternion.Angle(session.playerCamera.transform.rotation,headRotation)<.001f,
                "AI play/reaction/pause/point moved player or court");
            Debug.Log("[Opponent Integration] yaw="+yaw+" rally="+session.RallyStrokes+
                " score="+session.PlayerPoints+":"+session.OpponentPoints+" maxStep="+maximumFootDisplacement.ToString("F5")+
                " maxAIFrameMs="+maxFrameMilliseconds.ToString("F2")+" beginTrackingMs="+beginTimer.Elapsed.TotalMilliseconds.ToString("F2"));
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
