using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using Object=UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    // Standard-match adapter coverage. Legacy seven-point/training regressions
    // stay unchanged and continue exercising the separate original flight path.
    public static class RegulationFlightRegression
    {
        private static int checks;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static void Require(bool value,string message)
        { checks++; if(!value) throw new InvalidOperationException("[Regulation Flight Test] "+message); }

        public static int Run()
        {
            BadmintonSession session=Object.FindObjectOfType<BadmintonSession>();
            if(session==null) throw new InvalidOperationException("[Regulation Flight Test] No built session");
            return Run(session);
        }

        public static int Run(BadmintonSession session)
        {
            checks=0;
            TestNetGeometry();
            PracticeMode oldMode=session.mode;
            Vector3 oldEye=session.playerCamera.transform.position;
            Quaternion oldRotation=session.playerCamera.transform.rotation;
            TrackedPoseDriver driver=session.playerCamera.GetComponent<TrackedPoseDriver>();
            bool oldDriver=driver!=null && driver.enabled;
            bool[] tracked=new bool[session.rackets.Length],sampled=new bool[session.rackets.Length];
            for(int i=0;i<session.rackets.Length;i++)
            { tracked[i]=session.rackets[i].IsTracked; sampled[i]=session.rackets[i].HasMotionSample; }
            try
            {
                if(driver!=null) driver.enabled=false;
                foreach(RacketController racket in session.rackets)
                { SetProperty(racket,"IsTracked",false); SetProperty(racket,"HasMotionSample",false); }
                foreach(float yaw in new[]{0f,135f})
                {
                    TestHandDropAndContact(session,yaw);
                    TestInvalidHeightAndNoRevival(session,yaw);
                    TestUnstruckDrop(session,yaw);
                    TestDeadRallyId(session,yaw);
                    TestNetDeadBallCutoff(session,yaw);
                    foreach(bool odd in new[]{false,true}) TestPhysicalAiServe(session,yaw,odd);
                    TestServiceGroundDeadline(session,yaw);
                }
            }
            finally
            {
                session.shuttle.Stop();
                session.mode=oldMode;
                session.playerCamera.transform.SetPositionAndRotation(oldEye,oldRotation);
                session.Recenter();
                if(driver!=null) driver.enabled=oldDriver;
                for(int i=0;i<session.rackets.Length;i++)
                { SetProperty(session.rackets[i],"IsTracked",tracked[i]); SetProperty(session.rackets[i],"HasMotionSample",sampled[i]); }
            }
            Debug.Log("[Regulation Flight Test] PASS "+checks+
                " checks: actual service impacts, full-height rejection, unstruck drop ownership, service landing, dead-rally cutoff and measured racket-head/net geometry.");
            return checks;
        }

        private static void Prepare(BadmintonSession session,SinglesSide server,float yaw,bool odd=false)
        {
            session.shuttle.Stop();
            session.mode=PracticeMode.SinglesMatch;
            session.playerCamera.transform.SetPositionAndRotation(new Vector3(1.2f,1.63f,-.8f),Quaternion.Euler(0f,yaw,0f));
            Require(session.Recenter(),"Fixture recenter failed");
            session.ResolveStandardTossChoice(true);
            session.Singles.Reset(server);
            if(odd)
            {
                // Score seeding is pure rules setup. The actual tested rally
                // still goes through StartPractice and its virtual placement.
                Require(session.Singles.BeginRally(),"Odd-score seed could not start");
                Require(session.Singles.AwardRally(server,session.Singles.CurrentRallyId),"Odd-score seed failed");
            }
            session.StartPractice(XRNode.RightHand);
            Require(session.StandardRallyActive && session.StandardServerIsPlayer==(server==SinglesSide.Player),
                "Standard fixture did not enter the selected server's rally");
        }

        private static RacketContact Contact(BadmintonSession session,Vector3 point,bool player)
        {
            Vector3 direction=session.court.TransformDirection(new Vector3(0f,.65f,player?1f:-1f)).normalized;
            return new RacketContact { point=point,center=point,centerVelocity=direction*6f,
                recentPeakSpeed=6f,faceRotation=Quaternion.LookRotation(direction,session.court.up) };
        }

        private static void TestHandDropAndContact(BadmintonSession session,float yaw)
        {
            Prepare(session,SinglesSide.Player,yaw);
            Vector3 courtPosition=session.court.position,eye=session.playerCamera.transform.position;
            Quaternion courtRotation=session.court.rotation;
            Vector3 source=session.court.TransformPoint(new Vector3(1.40f,1.02f,.35f));
            session.shuttle.HoldAt(source);
            Require(!session.shuttle.LaunchStandardAiServe(ShuttleState.Launch(source,Vector3.zero),Contact(session,source,false)),
                "AI stole a held player service");
            Require(session.shuttle.ReleaseHeld(Vector3.zero),"Held shuttle did not release into standard flight");
            Require(session.shuttle.StandardPlayerLastHit && !session.shuttle.Returned,"Unstruck player drop lost its server identity");
            session.shuttle.AdvanceFrame(1f/120f);
            Require(session.shuttle.State.position.y<source.y && session.shuttle.Velocity.y<0f,"Service drop did not fall freely");
            RacketContact contact=Contact(session,session.shuttle.State.position,true);
            ShuttleState incoming=session.shuttle.State;
            ImpactResult expected=RacketImpact.Resolve(incoming,contact,session.PhysicsSettings);
            Require(expected.hit && session.shuttle.ReturnShot(contact),"Legal player service impact failed");
            Require(session.shuttle.Returned && session.shuttle.StandardPlayerLastHit && session.StandardServicePending && session.RallyStrokes==1,
                "Player service failed to commit exactly one stroke");
            Require(Vector3.Distance(expected.outgoingVelocity,session.shuttle.Velocity)<.00001f,"Player service velocity bypassed physical impact");
            Require(!session.shuttle.ReturnShot(contact) && session.RallyStrokes==1,"Repeated service callback created another stroke");
            Require(!session.shuttle.OpponentReturn(contact),"AI received a service before it crossed the net");
            Require(session.court.position==courtPosition && session.court.rotation==courtRotation && session.playerCamera.transform.position==eye,
                "In-rally service moved the real head or calibrated court");
        }

        private static void TestInvalidHeightAndNoRevival(BadmintonSession session,float yaw)
        {
            Prepare(session,SinglesSide.Player,yaw);
            Vector3 source=session.court.TransformPoint(new Vector3(1.40f,1.10f,.35f));
            session.shuttle.HoldAt(source);
            Require(session.shuttle.ReleaseHeld(Vector3.zero),"High-service fixture release failed");
            Require(session.StandardShuttleTop(session.shuttle.State)>1.15f,"Fixture must put feathers above 1.15 m while cork is below it");
            int id=session.Singles.CurrentRallyId;
            Require(!session.shuttle.ReturnShot(Contact(session,source,true)),"High feather tip service accepted");
            Require(!session.shuttle.Flying && session.OpponentPoints==1 && session.PlayerPoints==0,
                "Illegal service failed to stop and award exactly one receiver point");
            Vector3 stopped=session.shuttle.State.position;
            session.shuttle.AdvanceFrame(.08f);
            session.FinishStandardRally("SERVICE_HEIGHT",session.court.InverseTransformPoint(source),true,id);
            Require(!session.shuttle.Flying && session.shuttle.State.position==stopped && session.OpponentPoints==1,
                "Validation fault revived integration or duplicated a point");
        }

        private static void TestUnstruckDrop(BadmintonSession session,float yaw)
        {
            Prepare(session,SinglesSide.Player,yaw);
            // Deliberately outside singles: an unstruck drop is still the
            // server's fault, not an apparent out-shot credited to the player.
            session.shuttle.HoldAt(session.court.TransformPoint(new Vector3(3f,.035f,.3f)));
            Require(session.shuttle.ReleaseHeld(Vector3.zero),"Unstruck drop fixture could not release");
            for(int i=0;i<30 && session.shuttle.Flying;i++) session.shuttle.AdvanceFrame(1f/90f);
            Require(!session.shuttle.Flying && session.OpponentPoints==1 && session.PlayerPoints==0,
                "Unstruck wide drop was scored as an AI out-shot");
        }

        private static void TestDeadRallyId(BadmintonSession session,float yaw)
        {
            Prepare(session,SinglesSide.Player,yaw);
            int oldId=session.Singles.CurrentRallyId;
            session.shuttle.Launch(session.court.TransformPoint(new Vector3(1.4f,1f,.3f)),Vector3.zero);
            Require(session.Singles.Let(oldId),"Could not retire stale-flight fixture rally");
            // Deliberately leave the old Flight alive while the pure rules open
            // a new identity. This probes stale callbacks, not normal gameplay.
            Require(session.Singles.BeginRally() && session.Singles.CurrentRallyId!=oldId,"New rally reused its identity");
            session.shuttle.AdvanceFrame(.01f);
            session.FinishStandardRally("OUT",Vector3.zero,true,oldId);
            Require(!session.shuttle.Flying && session.Singles.Phase==SinglesPhase.Rally &&
                session.PlayerPoints==0 && session.OpponentPoints==0,"Stale flight ended or scored the next rally");
        }

        private static void TestPhysicalAiServe(BadmintonSession session,float yaw,bool odd)
        {
            Prepare(session,SinglesSide.Opponent,yaw,odd);
            ShuttleState incoming;
            RacketContact contact;
            Require(session.opponent.TryPrepareRegulationServe(session.Singles.ServerRight,session.GetOpponentTarget(0),out incoming,out contact),
                "AI could not plan a physical standard serve, odd="+odd+" yaw="+yaw);
            ImpactResult expected=RacketImpact.Resolve(incoming,contact,session.opponent.PhysicsSettings);
            Require(expected.hit && session.shuttle.LaunchStandardAiServe(incoming,contact),"AI physical serve did not commit");
            Require(!session.shuttle.Returned && !session.shuttle.StandardPlayerLastHit && session.StandardServicePending && session.RallyStrokes==1,
                "AI service has wrong ownership or stroke count");
            Require(Vector3.Distance(expected.outgoingVelocity,session.shuttle.Velocity)<.00001f,"AI service overwrote impact velocity");
            Require(!session.shuttle.LaunchStandardAiServe(incoming,contact),"Second AI launch stole an active service");
            int before=session.OpponentPoints;
            for(int i=0;i<1200 && session.shuttle.Flying;i++) session.shuttle.AdvanceFrame(1f/120f);
            Require(!session.shuttle.Flying && session.OpponentPoints==before+1 && session.PlayerPoints==0,
                "Unreceived legal AI service did not score for AI exactly once");
        }

        private static void TestNetDeadBallCutoff(BadmintonSession session,float yaw)
        {
            Prepare(session,SinglesSide.Player,yaw);
            // Isolate an ordinary rally's net-body collision. Actual opening
            // services/impacts are exercised by the separate live tests.
            SetField(session,"standardServiceDelivered",true);
            SetField(session,"standardServiceReceived",true);
            Vector3 local=new Vector3(0f,BadmintonCourt.NetCentreHeight-.20f,BadmintonCourt.NetZ-.01f);
            session.shuttle.Launch(session.court.TransformPoint(local),session.court.TransformDirection(new Vector3(0f,-.5f,3f)));
            SetField(session.shuttle,"standardHasContact",true);
            SetProperty(session.shuttle,"Returned",true);
            int id=session.Singles.CurrentRallyId;
            session.shuttle.AdvanceFrame(.02f);
            Require(!session.shuttle.Flying && session.OpponentPoints==1 && session.PlayerPoints==0,
                "Net-body rebound did not end the point immediately");
            Require(session.court.InverseTransformPoint(session.shuttle.State.position).y>1f,
                "Net-body dead-ball decision was delayed until ground");
            Vector3 stopped=session.shuttle.State.position;
            Require(ShuttleFlight.RacketHeadTouchesNet(session.court.TransformPoint(local+Vector3.forward*.01f),session.court.rotation,session.court),
                "Later-touch fixture does not overlap the physical net");
            // An opponent net-touch would reverse the winner if allowed to
            // overwrite this already-dead rally, even with the same event ID.
            session.shuttle.AdvanceFrame(.08f);
            session.FinishStandardRally("NET_TOUCH",local,false,id);
            Require(!session.shuttle.Flying && session.shuttle.State.position==stopped &&
                session.OpponentPoints==1 && session.PlayerPoints==0 && session.Singles.CompletedRallies==1,
                "Net touch after the dead ball replaced or duplicated its point");
        }

        private static void TestServiceGroundDeadline(BadmintonSession session,float yaw)
        {
            Prepare(session,SinglesSide.Opponent,yaw);
            // Seed a post-service state to isolate ground/region handling; full
            // physical launch is separately covered by TestPhysicalAiServe.
            SetField(session,"standardServiceDelivered",true);
            SetField(session,"standardServiceReceived",false);
            session.shuttle.Launch(session.court.TransformPoint(new Vector3(-1.2f,.04f,.4f)),session.court.up*-2f);
            SetField(session.shuttle,"standardHasContact",true);
            session.shuttle.AdvanceFrame(.08f);
            Require(!session.shuttle.Flying && session.PlayerPoints==1 && session.OpponentPoints==0,
                "AI serve in wrong diagonal court was not a service fault");
            Require(Mathf.Abs(session.court.InverseTransformPoint(session.shuttle.State.position).y-.025f)<.0001f,
                "Flight continued beneath its first-ground deadline");
        }

        private static void TestNetGeometry()
        {
            GameObject root=new GameObject("Regulation flight geometry fixture");
            try
            {
                Transform court=root.transform;
                foreach(float yaw in new[]{0f,45f,135f,180f})
                {
                    court.SetPositionAndRotation(new Vector3(4f,.4f,-3f),Quaternion.Euler(0f,yaw,0f));
                    foreach(float x in new[]{-2.9f,0f,2.9f})
                    foreach(float roll in new[]{0f,35f,90f})
                    {
                        Quaternion rotation=court.rotation*Quaternion.Euler(0f,0f,roll);
                        float top=BadmintonCourt.NetHeightAt(x);
                        Require(ShuttleFlight.RacketHeadTouchesNet(court.TransformPoint(new Vector3(x,top-.35f,BadmintonCourt.NetZ)),rotation,court),
                            "Measured head/net overlap missed");
                        Require(!ShuttleFlight.RacketHeadTouchesNet(court.TransformPoint(new Vector3(x,top+.19f,BadmintonCourt.NetZ)),rotation,court),
                            "Racket clearly above tape falsely touched net");
                        Require(!ShuttleFlight.RacketHeadTouchesNet(court.TransformPoint(new Vector3(x,top-.35f,BadmintonCourt.NetZ-.20f)),rotation,court),
                            "Separated racket falsely touched net");
                        Require(!ShuttleFlight.RacketHeadTouchesNet(court.TransformPoint(new Vector3(x,top-BadmintonCourt.NetDepth-.19f,BadmintonCourt.NetZ)),rotation,court),
                            "Racket below physical panel falsely touched net");
                    }
                    Require(!ShuttleFlight.RacketHeadTouchesNet(court.TransformPoint(new Vector3(3.3f,1.1f,BadmintonCourt.NetZ)),court.rotation,court),
                        "Outboard racket falsely touched net panel");
                }
                Require(!ShuttleFlight.RacketHeadTouchesNet(Vector3.zero,Quaternion.identity,null),"Null court must not count a touch");
                ShuttleFlight unattached=root.AddComponent<ShuttleFlight>();
                Require(!unattached.LaunchStandardAiServe(new ShuttleState(),new RacketContact()),"Unattached flight launched AI service");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void SetField(object target,string name,object value)
        { FieldInfo field=target.GetType().GetField(name,Flags); if(field==null) throw new MissingFieldException(name); field.SetValue(target,value); }
        private static void SetProperty(object target,string name,object value)
        { PropertyInfo property=target.GetType().GetProperty(name,Flags); if(property==null) throw new MissingMemberException(name); property.SetValue(target,value,null); }
    }
}
