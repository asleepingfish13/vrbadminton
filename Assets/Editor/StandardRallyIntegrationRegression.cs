using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace ViveShuttleVR.Editor
{
    // Test-only inverse racket control: all launched/returned shuttle velocities
    // still come from the production RacketImpact and ShuttleFlight paths.
    public static class StandardRallyIntegrationRegression
    {
        private static int checks;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static void Require(bool condition,string message)
        { checks++; if(!condition) throw new InvalidOperationException("[Standard Rally Test] "+message); }

        public static int Run()
        {
            BadmintonSession session=UnityEngine.Object.FindObjectOfType<BadmintonSession>();
            if(session==null) throw new InvalidOperationException("[Standard Rally Test] No session in scene");
            return Run(session);
        }

        public static int Run(BadmintonSession session)
        {
            checks=0;
            PracticeMode mode=session.mode;
            OpponentDifficulty difficulty=session.Difficulty;
            ShuttlePhysicsSettings settings=session.physics;
            bool compact=session.compactSeatedTargets,simulated=session.simulatePlayerHeight;
            float stature=session.simulatedStature;
            Vector3 eye=session.playerCamera.transform.position;
            Quaternion heading=session.playerCamera.transform.rotation;
            TrackedPoseDriver driver=session.playerCamera.GetComponent<TrackedPoseDriver>();
            bool driverEnabled=driver!=null && driver.enabled;
            var tracked=new bool[session.rackets.Length];
            var sampled=new bool[session.rackets.Length];
            try
            {
                if(driver!=null) driver.enabled=false;
                session.mode=PracticeMode.SinglesMatch;
                session.Difficulty=OpponentDifficulty.Easy;
                session.physics=new ShuttlePhysicsSettings();
                session.compactSeatedTargets=true;
                session.simulatePlayerHeight=true;
                session.simulatedStature=1.75f;
                for(int i=0;i<session.rackets.Length;i++)
                {
                    tracked[i]=session.rackets[i].IsTracked;
                    sampled[i]=session.rackets[i].HasMotionSample;
                    SetProperty(session.rackets[i],"IsTracked",false);
                    SetProperty(session.rackets[i],"HasMotionSample",false);
                }
                foreach(int frameRate in new[]{72,90,120})
                foreach(float yaw in new[]{0f,135f})
                foreach(bool odd in new[]{false,true}) RunRally(session,frameRate,yaw,odd);
            }
            finally
            {
                session.shuttle.Stop();
                session.mode=mode; session.Difficulty=difficulty; session.physics=settings;
                session.compactSeatedTargets=compact; session.simulatePlayerHeight=simulated; session.simulatedStature=stature;
                session.playerCamera.transform.SetPositionAndRotation(eye,heading);
                session.Recenter();
                if(driver!=null) driver.enabled=driverEnabled;
                for(int i=0;i<session.rackets.Length;i++)
                { SetProperty(session.rackets[i],"IsTracked",tracked[i]); SetProperty(session.rackets[i],"HasMotionSample",sampled[i]); }
            }
            Debug.Log("[Standard Rally Test] PASS "+checks+
                " checks across 12 standard live rallies: actual AI service, player impact, moving AI physical reply, natural ground result, exactly one point and no in-rally camera/court motion.");
            return checks;
        }

        private static void RunRally(BadmintonSession session,int frameRate,float yaw,bool odd)
        {
            string label="fps="+frameRate+" yaw="+yaw+" odd="+odd;
            float dt=1f/frameRate;
            session.shuttle.Stop();
            session.playerCamera.transform.SetPositionAndRotation(new Vector3(1.4f,1.63f,-.8f),Quaternion.Euler(0f,yaw,0f));
            Require(session.Recenter(),"Recenter failed: "+label);
            session.ResolveStandardTossChoice(true);
            session.Singles.Reset(SinglesSide.Opponent);
            if(odd)
            {
                // Only initial parity is seeded. The tested point is resolved
                // exclusively through real service/contacts/flight/ground.
                Require(session.Singles.BeginRally(),"Seed begin failed: "+label);
                Require(session.Singles.AwardRally(SinglesSide.Opponent,session.Singles.CurrentRallyId),"Seed score failed: "+label);
            }
            session.StartPractice(XRNode.RightHand);
            Require(session.StandardRallyActive && !session.StandardServerIsPlayer,"AI-server rally did not start: "+label);
            int startingPoints=session.PlayerPoints+session.OpponentPoints;
            int startingCompleted=session.Singles.CompletedRallies,id=session.Singles.CurrentRallyId;
            Vector3 courtPosition=session.court.position,headPosition=session.playerCamera.transform.position;
            Quaternion courtRotation=session.court.rotation,headRotation=session.playerCamera.transform.rotation;
            Vector3 target=session.GetOpponentTarget(0);
            ShuttleState incoming; RacketContact serveContact;
            RuntimeServe(session,dt,label,out incoming,out serveContact);
            ImpactResult serveImpact=RacketImpact.Resolve(incoming,serveContact,session.opponent.PhysicsSettings);
            Require(serveImpact.hit && session.shuttle.Flying,"AI service contact failed: "+label);
            Require(session.RallyStrokes==1 && session.StandardServicePending,"AI service was not one pending stroke: "+label);
            Require(Vector3.Distance(serveImpact.outgoingVelocity,session.shuttle.Velocity)<.00001f,"AI service velocity overwritten: "+label);
            bool reached=false;
            float closest=float.PositiveInfinity;
            for(int frame=0;frame<frameRate*8 && session.shuttle.Flying;frame++)
            {
                float distance=Vector3.Distance(session.shuttle.State.position,target);
                closest=Mathf.Min(closest,distance);
                if(distance<.10f) { reached=true; break; }
                Advance(session,dt);
            }
            Require(reached && session.shuttle.Flying && !session.shuttle.Returned,
                "AI service did not reach the player's compact ring: "+label+" closest="+closest);
            RacketContact receive=PlayerContact(session,label);
            ImpactResult playerImpact=RacketImpact.Resolve(session.shuttle.State,receive,session.PhysicsSettings);
            Require(session.shuttle.ReturnShot(receive),"Physical player reception failed: "+label);
            Require(session.RallyStrokes==2 && session.shuttle.Returned && !session.StandardServicePending && session.opponent.Tracking,
                "Reception did not enter an ordinary rally: "+label);
            Require(Vector3.Distance(playerImpact.outgoingVelocity,session.shuttle.Velocity)<.00001f,"Reception velocity overwritten: "+label);
            Require(!session.shuttle.ReturnShot(receive) && session.RallyStrokes==2,"Repeated callback added a player stroke: "+label);
            Require(!session.shuttle.OpponentReturn(receive),"AI received before the player's shot crossed the net: "+label);

            int attempts=session.opponent.ReturnAttempts;
            float distanceMoved=0f,maxStep=0f;
            for(int frame=0;frame<frameRate*10 && session.shuttle.Flying && session.shuttle.Returned;frame++)
            {
                Vector3 feet=session.opponent.FeetLocal;
                Advance(session,dt);
                float step=Vector3.Distance(feet,session.opponent.FeetLocal);
                distanceMoved+=step; maxStep=Mathf.Max(maxStep,step);
                Require(step<=OpponentAbility.For(session.Difficulty).runSpeed*dt+.0001f,"AI teleported while receiving: "+label);
            }
            Require(session.shuttle.Flying && !session.shuttle.Returned && session.RallyStrokes==3 && session.opponent.ReturnAttempts==attempts+1,
                "AI did not physically reply: "+label+" decision="+session.opponent.LastDecision+" error="+session.opponent.LastAimError);
            Require(distanceMoved>.01f,"AI physically replied without the expected footwork: "+label);
            Require(!session.shuttle.OpponentReturn(receive) && session.RallyStrokes==3,"Repeated AI callback added a stroke: "+label);
            for(int frame=0;frame<frameRate*12 && session.shuttle.Flying;frame++) Advance(session,dt);
            Require(!session.shuttle.Flying && session.PlayerPoints+session.OpponentPoints==startingPoints+1 &&
                session.Singles.CompletedRallies==startingCompleted+1,"Natural flight failed to award exactly one rally: "+label);
            Require(Mathf.Abs(session.court.InverseTransformPoint(session.shuttle.State.position).y-.025f)<.0001f,
                "Live rally ended before the expected natural ground contact: "+label);
            int finalPoints=session.PlayerPoints+session.OpponentPoints;
            for(int i=0;i<frameRate;i++) Advance(session,dt);
            session.FinishStandardRally("LAND",session.court.InverseTransformPoint(session.shuttle.State.position),false,id);
            Require(session.PlayerPoints+session.OpponentPoints==finalPoints && session.Singles.CompletedRallies==startingCompleted+1,
                "Dead-flight updates or duplicate callback added a second point: "+label);
            Require(session.court.position==courtPosition && Quaternion.Angle(session.court.rotation,courtRotation)<.001f &&
                session.playerCamera.transform.position==headPosition && Quaternion.Angle(session.playerCamera.transform.rotation,headRotation)<.001f,
                "Standard rally moved the real head or court before next-point confirmation: "+label);
            Debug.Log("[Standard Live Rally] "+label+" strokes="+session.RallyStrokes+" feetTravel="+distanceMoved.ToString("F3")+
                " maxStep="+maxStep.ToString("F4")+" points="+session.PlayerPoints+":"+session.OpponentPoints);
        }

        private static RacketContact PlayerContact(BadmintonSession session,string label)
        {
            OpponentAbility control=OpponentAbility.For(OpponentDifficulty.Club);
            control.returnDuration=1.75f;
            control.racketSpeed=22f;
            Vector3 target=session.court.TransformPoint(new Vector3(.10f,2.20f,6.30f));
            RacketContact contact; float error;
            bool found=BadmintonOpponent.TryPlanReturn(session.shuttle.State,target,session.court,session.PhysicsSettings,control,out contact,out error);
            Require(found,"Test-only physical receiving fixture failed: "+label+" error="+error);
            ImpactResult impact=RacketImpact.Resolve(session.shuttle.State,contact,session.PhysicsSettings);
            Require(impact.hit && session.court.InverseTransformDirection(impact.outgoingVelocity).z>0f,"Fixture does not send a real impact toward AI: "+label);
            return contact;
        }

        private static void RuntimeServe(BadmintonSession session,float dt,string label,
            out ShuttleState incoming,out RacketContact contact)
        {
            MethodInfo serve=typeof(BadmintonSession).GetMethod("Serve",Flags);
            Require(serve!=null,"Missing runtime Serve entry");
            serve.Invoke(session,null);
            Require(session.opponent.RegulationServePlanning && !session.shuttle.Flying,
                "Runtime service did not start incremental planning: "+label);
            int steps=(int)typeof(BadmintonOpponent).GetField("serviceSteps",Flags).GetValue(session.opponent);
            Vector3 feet=session.opponent.FeetLocal,head=session.opponent.RacketHeadWorld;
            session.TogglePause();
            Require(!session.Running,"Could not pause service preparation: "+label);
            for(int frame=0;frame<12;frame++)
            {
                serve.Invoke(session,null);
                Require(session.opponent.AdvanceRegulationServePlanning(out incoming,out contact)==OpponentServePlanStatus.Pending,
                    "Paused service planner completed or was cancelled: "+label);
                Advance(session,dt);
            }
            Require((int)typeof(BadmintonOpponent).GetField("serviceSteps",Flags).GetValue(session.opponent)==steps &&
                session.opponent.FeetLocal==feet && session.opponent.RacketHeadWorld==head && !session.shuttle.Flying,
                "Paused service preparation advanced search or model: "+label);
            session.TogglePause();
            Require(session.StandardRallyActive,"Service preparation did not resume: "+label);
            for(int frame=0;frame<1085 && session.StandardRallyActive && !session.shuttle.Flying;frame++)
            {
                serve.Invoke(session,null);
                // Same once-per-render pose sampling used by LateUpdate; no
                // synchronous solver call or simulated post-impact velocity.
                session.opponent.SamplePresentation(dt);
            }
            Require(session.shuttle.Flying && session.opponent.AdvanceRegulationServePlanning(out incoming,out contact)==OpponentServePlanStatus.Success,
                "Runtime incremental AI service failed: "+label+" decision="+session.opponent.LastDecision);
            // Definite assignment remains explicit even if a failed Require is
            // replaced by a nonthrowing test reporter in future.
            session.opponent.AdvanceRegulationServePlanning(out incoming,out contact);
        }

        private static void Advance(BadmintonSession session,float dt)
        { session.shuttle.AdvanceFrame(dt); session.opponent.SamplePresentation(dt); }

        private static void SetProperty(object target,string name,object value)
        {
            PropertyInfo property=target.GetType().GetProperty(name,Flags);
            if(property==null) throw new MissingMemberException(name);
            property.SetValue(target,value,null);
        }
    }
}
