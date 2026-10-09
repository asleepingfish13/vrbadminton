using System;
using System.IO;
using UnityEngine;
using UnityEngine.XR;

namespace ViveShuttleVR.Editor
{
    public static class PracticeControlRegression
    {
        private static int checks;
        private static void Require(bool value,string message)
        { checks++; if(!value) throw new InvalidOperationException("[Control Test] "+message); }

        public static int Run(BadmintonSession session)
        {
            checks=0;
            var gesture=new PracticeMenuGesture();
            Require(gesture.Step(false,.01f)==MenuGestureAction.None,"Idle menu fires");
            for(int i=0;i<20;i++) Require(gesture.Step(true,.01f)==MenuGestureAction.None,"Press fired pause before release");
            Require(gesture.Step(false,.01f)==MenuGestureAction.Pause,"Short release must pause");
            Require(gesture.Step(false,.01f)==MenuGestureAction.None,"Short release repeated");
            int resets=0,pauses=0;
            for(int i=0;i<250;i++)
            { var action=gesture.Step(true,.01f); if(action==MenuGestureAction.Recenter)resets++; if(action==MenuGestureAction.Pause)pauses++; }
            Require(resets==1 && pauses==0,"Long menu should reset exactly once, never pause first");
            Require(gesture.Step(false,.01f)==MenuGestureAction.None,"Long hold release paused");
            gesture.Step(true,.05f); gesture.Reset();
            Require(gesture.Step(false,.01f)==MenuGestureAction.None,"Tracking loss retained a pending menu tap");
            string controller=File.ReadAllText("Assets/Scripts/RacketController.cs");
            Require(!controller.Contains("CommonUsages.gripButton")&&!controller.Contains("CommonUsages.grip)"),"Grip is still bound to an action");

            PracticeMode savedMode=session.mode;
            PracticeDrill savedDrill=session.drill;
            OpponentDifficulty savedDifficulty=session.Difficulty;
            RacketWeightClass savedWeight=session.physics.racketWeightClass;
            float savedTension=session.physics.stringTensionLbs;
            Vector3 savedEye=session.playerCamera.transform.position;
            Quaternion savedHead=session.playerCamera.transform.rotation;
            try
            {
                session.shuttle.Stop();
                session.playerCamera.transform.SetPositionAndRotation(new Vector3(.3f,1.68f,-.2f),Quaternion.Euler(0f,35f,0f));
                session.mode=PracticeMode.AiRally; session.drill=PracticeDrill.Rally; session.Difficulty=OpponentDifficulty.Easy;
                session.Recenter();
                Vector3 eye=session.playerCamera.transform.position;
                Quaternion heading=session.playerCamera.transform.rotation;
                Vector3 courtPosition=session.court.position;
                Vector3 easy=session.GetOpponentTarget(0);
                for(int i=0;i<12;i++) Require(Vector3.Distance(easy,session.GetOpponentTarget(i))<.0001f,"Easy uses tactical varying targets");
                foreach(OpponentDifficulty difficulty in Enum.GetValues(typeof(OpponentDifficulty)))
                {
                    session.Difficulty=difficulty;
                    for(int i=0;i<12;i++)
                    {
                        Vector3 offset=session.court.InverseTransformDirection(session.GetOpponentTarget(i)-eye);
                        Require(offset.z>=.49f&&offset.z<=.68f&&Mathf.Abs(offset.x)<=.20f&&offset.y>=-.19f&&offset.y<=.03f,
                            "AI aim leaves stationary player envelope");
                    }
                }
                Require(session.playerCamera.transform.position==eye&&session.playerCamera.transform.rotation==heading&&session.court.position==courtPosition,
                    "AI targeting moved the player/world");
                session.SetRacketWeight(RacketWeightClass.ThreeU);
                session.AdjustTension(-100); Require(session.physics.stringTensionLbs==21f,"3U lower tension limit");
                session.AdjustTension(100); Require(session.physics.stringTensionLbs==29f,"3U upper tension limit");
                session.SetRacketWeight(RacketWeightClass.FourU); Require(session.physics.stringTensionLbs==28f,"4U switch must clamp unsafe tension");
                session.AdjustTension(-100); Require(session.physics.stringTensionLbs==20f,"4U lower tension limit");
                session.ResetEquipment();
                session.Difficulty=OpponentDifficulty.Easy;
                session.StartPractice(XRNode.RightHand);
                session.SetMode(PracticeMode.Training); session.SetDifficulty(OpponentDifficulty.Advanced); session.SetRacketWeight(RacketWeightClass.ThreeU);
                Require(session.IsAiMatch&&session.Difficulty==OpponentDifficulty.Easy&&session.physics.racketWeightClass==RacketWeightClass.FourU,"Active play settings were mutable");
                session.shuttle.Launch(session.court.TransformPoint(new Vector3(0f,2f,6f)),session.court.forward*-3f);
                session.TogglePause();
                session.SetMode(PracticeMode.Training); session.SetDifficulty(OpponentDifficulty.Advanced);
                Require(session.IsAiMatch&&session.Difficulty==OpponentDifficulty.Easy,"Paused in-flight AI settings were mutable");
                session.Recenter(); session.StartPractice(XRNode.RightHand);
                for(int i=0;i<BadmintonSession.PointsToWin;i++) session.OnFinished("LAND",new Vector3(0f,.025f,7f),true);
                Require(session.MatchComplete&&!session.Running&&session.PlayerPoints==7&&session.OpponentPoints==0,"First-to-seven match did not terminate");
                session.StartPractice(XRNode.RightHand);
                Require(!session.MatchComplete&&session.Running&&session.PlayerPoints==0&&session.OpponentPoints==0,"New match retained points");
                session.OnFinished("MISS",new Vector3(0f,.025f,0f),false);
                Require(session.OpponentPoints==1,"Missed AI return not scored to AI");
            }
            finally
            {
                session.shuttle.Stop();
                session.mode=savedMode; session.drill=savedDrill; session.Difficulty=savedDifficulty;
                session.physics.racketWeightClass=savedWeight; session.physics.stringTensionLbs=savedTension;
                session.playerCamera.transform.SetPositionAndRotation(savedEye,savedHead);
                session.Recenter();
            }
            Require(BadmintonSession.PlayerWinsAiPoint("LAND",new Vector3(2.59f,0f,10.3f),true),"Player boundary winner lost");
            Require(!BadmintonSession.PlayerWinsAiPoint("LAND",new Vector3(2.60f,0f,7f),true),"Player singles-wide ball counted in");
            Require(!BadmintonSession.PlayerWinsAiPoint("NET",new Vector3(0f,0f,7f),true),"Player net fault won");
            Require(BadmintonSession.PlayerWinsAiPoint("NET",new Vector3(0f,0f,0f),false),"Opponent net fault lost");
            Require(!BadmintonSession.PlayerWinsAiPoint("MISS",new Vector3(0f,0f,0f),false),"Opponent in court ball lost");
            Require(BadmintonSession.PlayerWinsAiPoint("MISS",new Vector3(3f,0f,0f),false),"Opponent out ball won");
            Debug.Log("[Control Test] PASS "+checks+" checks: menu release/hold, no grip binding, official tension limits, guarded mode/difficulty, safe easy targets and first-to-seven scoring.");
            return checks;
        }
    }
}
