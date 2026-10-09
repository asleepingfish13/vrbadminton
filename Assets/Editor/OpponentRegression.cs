using System;
using UnityEngine;

namespace ViveShuttleVR.Editor
{
    public static class OpponentRegression
    {
        private static int checks;

        public static void ProbePlans()
        {
            var court=new GameObject("AI planner diagnostic");
            try
            {
                var settings=BadmintonOpponent.CreateStrictSettings(new ShuttlePhysicsSettings());
                ShuttleState state=ShuttleState.Launch(new Vector3(.12f,1.9f,6.3f),new Vector3(.08f,-3f,3f));
                foreach (float speed in new[] { 15f,18f,22f,27f })
                foreach (float duration in new[] { 1.5f,2f })
                {
                    OpponentAbility ability=OpponentAbility.For(OpponentDifficulty.Easy);
                    ability.racketSpeed=speed;
                    ability.returnDuration=duration;
                    RacketContact contact;
                    float error;
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    bool success=BadmintonOpponent.TryPlanReturn(state,new Vector3(.22f,1.58f,.62f),court.transform,settings,ability,out contact,out error);
                    timer.Stop();
                    Debug.Log("[AI Probe] cap="+speed+" seconds="+duration+" success="+success+" error="+error+
                        " racket="+contact.centerVelocity+" face="+contact.surfaceNormal+" ms="+timer.Elapsed.TotalMilliseconds);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(court); }
        }

        public static int Run()
        {
            checks=0;
            TestDifficulties();
            TestMovement();
            var court=new GameObject("AI regression temporary court");
            try
            {
                TestIntercept(court.transform);
                TestContactPlans(court.transform);
            }
            finally { UnityEngine.Object.DestroyImmediate(court); }
            Debug.Log("[Opponent Test] PASS "+checks+" checks: ordered difficulty, strict contact settings, speed-limited footwork, reachable interceptions, physical targeted returns, yaw covariance and genuine misses.");
            return checks;
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("[Opponent Test] "+message);
        }

        private static void TestDifficulties()
        {
            OpponentAbility easy=OpponentAbility.For(OpponentDifficulty.Easy);
            OpponentAbility club=OpponentAbility.For(OpponentDifficulty.Club);
            OpponentAbility advanced=OpponentAbility.For(OpponentDifficulty.Advanced);
            Require(easy.reaction>club.reaction && club.reaction>advanced.reaction && advanced.reaction>.05f,"Reaction hierarchy");
            Require(easy.runSpeed<club.runSpeed && club.runSpeed<advanced.runSpeed,"Footwork hierarchy");
            Require(easy.reach<club.reach && club.reach<advanced.reach,"Reach hierarchy");
            Require(easy.missChance>club.missChance && club.missChance>advanced.missChance && advanced.missChance>0f,"Mistake hierarchy");
            Require(easy.returnDuration>club.returnDuration && club.returnDuration>advanced.returnDuration,"Easy gives player more reaction time");
            Require(easy.racketSpeed<club.racketSpeed && club.racketSpeed<advanced.racketSpeed,"Physical racket speed limits");
            var source=new ShuttlePhysicsSettings { seatedAssist=true, mass=.0054f, stringTensionLbs=25f, swingGain=1.6f };
            ShuttlePhysicsSettings strict=BadmintonOpponent.CreateStrictSettings(source);
            Require(!strict.seatedAssist && source.seatedAssist,"AI strict settings mutate human assistance or retain speed gain");
            Require(strict.mass==source.mass && strict.stringTensionLbs==source.stringTensionLbs &&
                strict.RacketContactMass(Vector3.zero,Vector3.forward)==source.RacketContactMass(Vector3.zero,Vector3.forward),
                "AI equipment differs from player contact model");
            strict.mass=.006f;
            Require(source.mass==.0054f,"AI settings clone aliases human settings");
        }

        private static void TestMovement()
        {
            foreach (OpponentDifficulty difficulty in Enum.GetValues(typeof(OpponentDifficulty)))
            foreach (float dt in new[] { 0f,1f/360f,1f/90f,.08f })
            {
                OpponentAbility ability=OpponentAbility.For(difficulty);
                Vector3 position=new Vector3(0f,0f,7.1f);
                Vector3 goal=new Vector3(9f,4f,20f);
                Vector3 moved=BadmintonOpponent.StepFeet(position,goal,ability.runSpeed,dt);
                Require(Vector3.Distance(position,moved)<=ability.runSpeed*dt+.00001f,"AI footwork teleports: "+difficulty);
                Require(moved.y==0f && Mathf.Abs(moved.x)<=2.45f && moved.z>=ShuttleMath.NetZ+.3f && moved.z<=10.15f,"AI leaves court");
            }
            Vector3 start=new Vector3(0f,0f,7.1f), target=new Vector3(1f,0f,6.2f);
            Vector3 coarse=start,fine=start;
            for (int i=0;i<90;i++) coarse=BadmintonOpponent.StepFeet(coarse,target,1.7f,1f/90f);
            for (int i=0;i<360;i++) fine=BadmintonOpponent.StepFeet(fine,target,1.7f,1f/360f);
            Require(Vector3.Distance(coarse,fine)<.0001f,"Footwork is frame-rate dependent");
        }

        private static void TestIntercept(Transform court)
        {
            var settings=BadmintonOpponent.CreateStrictSettings(new ShuttlePhysicsSettings());
            OpponentAbility ability=OpponentAbility.For(OpponentDifficulty.Club);
            Vector3 feet=new Vector3(0f,0f,7.1f);
            // This state is already on the opponent's side after a legal player
            // clear: it should give the footwork planner time to meet the cork.
            ShuttleState gentle=ShuttleState.Launch(new Vector3(.1f,2.35f,5.9f),new Vector3(.1f,2.5f,3f));
            OpponentIntercept reachable=BadmintonOpponent.FindIntercept(gentle,feet,court,settings,ability);
            Require(reachable.valid && reachable.time>.15f,"Gentle centre clear has no reachable interception");
            if (reachable.valid)
            {
                Vector3 predicted=ShuttlePhysics.Predict(gentle,reachable.time,settings).position;
                Require(Vector3.Distance(predicted,reachable.state.position)<.001f,"Intercept prediction replaced real flight state");
                Vector3 possible=BadmintonOpponent.StepFeet(feet,reachable.feetLocal,ability.runSpeed,reachable.time);
                Vector3 local=court.InverseTransformPoint(reachable.state.position);
                Require(new Vector2(local.x-possible.x,local.z-possible.z).magnitude<ability.reach,"Intercept beyond reachable arm after legal movement");
            }
            ShuttleState outWide=ShuttleState.Launch(new Vector3(3.8f,1.2f,7f),new Vector3(3f,-4f,1f));
            Require(!BadmintonOpponent.FindIntercept(outWide,feet,court,settings,ability).valid,"Out-wide shuttle magically returned");
            ShuttleState netFault=ShuttleState.Launch(new Vector3(0f,.65f,3.4f),new Vector3(0f,0f,8f));
            Require(!BadmintonOpponent.FindIntercept(netFault,feet,court,settings,ability).valid,"AI returns through regulation net");
            ShuttleState late=ShuttleState.Launch(new Vector3(-2.4f,.38f,4f),new Vector3(-1f,-6f,.5f));
            Require(!BadmintonOpponent.FindIntercept(late,feet,court,settings,ability).valid,"Unreachable low drop never causes a miss");
        }

        private static void TestContactPlans(Transform court)
        {
            var settings=BadmintonOpponent.CreateStrictSettings(new ShuttlePhysicsSettings());
            double totalMilliseconds=0,maxMilliseconds=0;
            int plans=0;
            foreach (float yaw in new[] { 0f,90f,210f })
            {
                court.SetPositionAndRotation(new Vector3(1.2f,.15f,-.7f),Quaternion.Euler(0f,yaw,0f));
                foreach (OpponentDifficulty difficulty in Enum.GetValues(typeof(OpponentDifficulty)))
                {
                    OpponentAbility ability=OpponentAbility.For(difficulty);
                    Vector3 start=court.TransformPoint(new Vector3(.12f,1.9f,6.3f));
                    Vector3 incoming=court.TransformDirection(new Vector3(.08f,-3f,3f));
                    Vector3 target=court.TransformPoint(new Vector3(.22f,1.58f,.62f));
                    ShuttleState state=ShuttleState.Launch(start,incoming);
                    RacketContact contact;
                    float error;
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    bool plan=BadmintonOpponent.TryPlanReturn(state,target,court,settings,ability,out contact,out error);
                    timer.Stop();
                    totalMilliseconds+=timer.Elapsed.TotalMilliseconds;
                    maxMilliseconds=Math.Max(maxMilliseconds,timer.Elapsed.TotalMilliseconds);
                    plans++;
                    Require(plan,"Physical return optimizer failed: "+difficulty+" yaw="+yaw+" error="+error);
                    Require(error<.08f && contact.centerVelocity.magnitude<=ability.racketSpeed+.0001f,
                        "AI exceeds aim envelope or physical swing limit");
                    Require(contact.point==state.position && contact.center==state.position && contact.surfaceNormal.sqrMagnitude>.99f,
                        "AI contact lacks geometric surface normal or teleports contact location");
                    ImpactResult impact=RacketImpact.Resolve(state,contact,settings);
                    Require(impact.hit && court.InverseTransformDirection(impact.outgoingVelocity).z<0f,"AI physical return goes backwards");
                    Require(impact.contactDuration>0f && impact.contactDuration<=.005f,"AI bypassed string-bed solver");
                    bool physicalPath=false;
                    for (int variant=0;variant<3;variant++)
                        physicalPath|=BadmintonOpponent.ClearsNet(state,contact,target,
                            BadmintonOpponent.ReturnDuration(ability,variant),court,settings);
                    Require(physicalPath,"AI planned target not reached by actual contact and free flight");
                    Debug.Log("[Opponent Test] "+difficulty+" yaw="+yaw+" swing="+contact.centerVelocity.magnitude.ToString("F2")+
                        "m/s output="+impact.outgoingVelocity.magnitude.ToString("F2")+"m/s error="+error.ToString("F4")+"m");
                }
            }
            Debug.Log("[Opponent Test] Planner timing avgMs="+(totalMilliseconds/Math.Max(1,plans)).ToString("F2")+
                " maxMs="+maxMilliseconds.ToString("F2")+" samples="+plans+" (editor batch CPU; not a headset frame-rate measurement)");
        }
    }
}
