using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;
using Object=UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    public static class HandServeRegression
    {
        private static int checks;
        private const float Frame=1f/90f;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static void Require(bool condition,string message)
        { checks++; if(!condition) throw new InvalidOperationException("[Hand Serve Test] "+message); }

        public static int Run(BadmintonSession session)
        {
            checks=0;
            Require(!session.shuttle.Flying && !session.HandServeHeld,"Lifecycle fixture must start without an active shuttle");
            ShuttleHand hand=session.shuttleHand;
            RacketController right=Array.Find(session.rackets,item=>item.CanStrike);
            Require(hand!=null && hand.source!=null && right!=null,"Lifecycle fixture requires free left hand and right racket");
            var snapshots=new List<FieldSnapshot> { new FieldSnapshot(session),new FieldSnapshot(session.shuttle),
                new FieldSnapshot(hand),new FieldSnapshot(hand.source),new FieldSnapshot(right) };
            if(session.opponent!=null) snapshots.Add(new FieldSnapshot(session.opponent));
            string physicsJson=JsonUtility.ToJson(session.physics);
            var originalObjects=new HashSet<GameObject>();
            var poses=new List<PoseSnapshot>();
            var texts=new List<TextSnapshot>();
            foreach(Transform item in Resources.FindObjectsOfTypeAll<Transform>())
                if(item.gameObject.scene==session.gameObject.scene)
                {
                    originalObjects.Add(item.gameObject);
                    poses.Add(new PoseSnapshot(item));
                    TextMesh text=item.GetComponent<TextMesh>();
                    if(text!=null) texts.Add(new TextSnapshot(text));
                }
            try
            {
                foreach(float yaw in new[]{0f,135f})
                foreach(bool virtualHeight in new[]{false,true})
                {
                    Prepare(session,hand,right,yaw,virtualHeight);
                    TestFreeDropAndHit(session,hand,right);
                    Prepare(session,hand,right,yaw,virtualHeight);
                    TestPausedAndLostTracking(session,hand);
                    Prepare(session,hand,right,yaw,virtualHeight);
                    TestReleaseSamplesAndReset(session,hand);
                }
            }
            finally
            {
                session.shuttle.Stop();
                // Restore original parents before removing objects created by
                // calibration (including a temporary feeder assembly and AI).
                foreach(PoseSnapshot pose in poses) pose.Restore();
                foreach(TextSnapshot text in texts) text.Restore();
                foreach(FieldSnapshot snapshot in snapshots) snapshot.Restore();
                JsonUtility.FromJsonOverwrite(physicsJson,session.physics);
                foreach(Transform item in Resources.FindObjectsOfTypeAll<Transform>())
                    if(item!=null && item.gameObject.scene==session.gameObject.scene && !originalObjects.Contains(item.gameObject))
                        Object.DestroyImmediate(item.gameObject);
            }
            Debug.Log("[Hand Serve Test] PASS "+checks+" checks: actual hold/drop/right-racket/AI lifecycle, paused and lost tracking safety, no stolen flight, single feed accounting and bounded release momentum.");
            return checks;
        }

        private static void Prepare(BadmintonSession session,ShuttleHand hand,RacketController right,float yaw,bool virtualHeight)
        {
            session.shuttle.Stop();
            session.mode=PracticeMode.AiRally;
            session.drill=PracticeDrill.Rally;
            session.Difficulty=OpponentDifficulty.Easy;
            session.compactSeatedTargets=true;
            session.simulatePlayerHeight=virtualHeight;
            session.simulatedStature=1.75f;
            session.playerCamera.transform.SetPositionAndRotation(new Vector3(1.4f,1.35f,-.8f),Quaternion.Euler(0f,yaw,0f));
            Require(session.Recenter(),"Fixture calibration failed");
            Vector3 eye=session.court.InverseTransformPoint(session.playerCamera.transform.position);
            hand.source.transform.SetPositionAndRotation(session.court.TransformPoint(new Vector3(-.22f,eye.y-.30f,.25f)),session.court.rotation);
            SetProperty(hand.source,"IsTracked",true);
            SetProperty(hand.source,"MenuPressed",false);
            SetProperty(hand.source,"HasMotionSample",false);
            SetProperty(right,"IsTracked",false);
            SetProperty(right,"HasMotionSample",false);
            SetProperty(hand,"IsPinching",false);
            hand.UpdateVisuals(0f);
        }

        private static void TestFreeDropAndHit(BadmintonSession session,ShuttleHand hand,RacketController right)
        {
            SetProperty(hand.source,"IsTracked",false);
            Require(!session.BeginHandServe(hand) && !session.HandServeHeld,"Untracked hand created a held shuttle");
            SetProperty(hand.source,"IsTracked",true);
            SetProperty(hand.source,"MenuPressed",true);
            Require(!session.BeginHandServe(hand),"Menu gesture also grabbed a shuttle");
            SetProperty(hand.source,"MenuPressed",false);
            bool savedFree=hand.source.freeHand;
            hand.source.freeHand=false;
            Require(!session.BeginHandServe(hand),"Striking controller was accepted as a holding hand");
            hand.source.freeHand=savedFree;
            Require(!session.BeginHandServe(null),"Null holding hand accepted");

            var gesture=new HandServeGesture();
            Input(session,hand,gesture,true,false,false);
            Require(Input(session,hand,gesture,true,false,true),"Fresh left pinch failed to start an idle session");
            Require(session.Running && session.HandServeHeld && session.shuttle.Held && !session.shuttle.Flying,
                "Pinch must start play with a visible, non-flying held shuttle");
            Require(session.shuttle.gameObject.activeSelf && session.Feeds==0 && session.Hits==0,
                "Picking up the shuttle must not count a feed/hit or hide it");
            Vector3 expected=hand.PinchPoint.position-Vector3.up*.055f;
            Require(Vector3.Distance(session.shuttle.State.position,expected)<.00001f,
                "Held cork must hang 5.5 cm below the finger pinch");
            Require(Vector3.Dot(session.shuttle.State.orientation*Vector3.up,Vector3.up)>.999f,
                "Held feathers must point up above the cork");
            Vector3 before=session.shuttle.State.position;
            session.shuttle.AdvanceFrame(.08f);
            Require(session.shuttle.State.position==before,"Held shuttle was integrated by gravity");
            Require(!session.shuttle.ReturnShot(session.court.forward*12f,session.court.forward) && session.Hits==0,
                "Racket contact hit a held shuttle before release");
            hand.source.transform.position+=session.court.right*.025f+Vector3.up*.01f;
            Input(session,hand,gesture,true,false,true);
            Require(Vector3.Distance(session.shuttle.State.position,hand.PinchPoint.position-Vector3.up*.055f)<.00001f,
                "Held shuttle failed to follow the tracked left hand");
            SetField(session,"feedTimer",-1f);
            for(int i=0;i<8;i++) Invoke(session,"Update");
            Require(session.HandServeHeld && !session.shuttle.Flying && session.Feeds==0 && session.opponent.ObservedShots==0,
                "Automatic AI service replaced or counted the held shuttle");

            // One stationary sample removes hand momentum; dropping must not
            // contain hidden aim, a launch impulse, or an automatic racket hit.
            Input(session,hand,gesture,true,false,true);
            Require(Input(session,hand,gesture,true,false,false),"Intentional pinch release failed");
            Require(!session.HandServeHeld && !session.shuttle.Held && session.shuttle.Flying && !session.shuttle.Returned,
                "Release did not transition held -> freely flying");
            Require(session.Feeds==1 && session.Hits==0 && session.shuttle.Velocity==Vector3.zero,
                "A stationary release must count exactly one feed and have zero launch speed");
            Vector3 released=session.shuttle.State.position;
            session.shuttle.AdvanceFrame(.08f);
            Require(session.shuttle.State.position.y<released.y-.005f && session.shuttle.Velocity.y<-.1f,
                "Released shuttle did not fall under the flight physics");
            Require(Mathf.Abs(session.court.InverseTransformDirection(session.shuttle.State.position-released).x)<.00001f &&
                Mathf.Abs(session.court.InverseTransformDirection(session.shuttle.State.position-released).z)<.00001f,
                "Stationary drop acquired an artificial sideways/forward launch");
            Require(!session.ReleaseHandServe(hand) && session.Feeds==1,"Duplicate release counted a second feed");
            Vector3 flyingPosition=session.shuttle.State.position;
            Require(!session.BeginHandServe(hand) && session.shuttle.Flying && session.shuttle.State.position==flyingPosition,
                "A pinch stole or teleported an already flying shuttle");

            // Drive the real right-racket swept collision -> ReturnShot ->
            // Session.OnHit -> AI.BeginTracking path, not a scripted ball launch.
            Vector3 normal=(session.court.forward+Vector3.up*.6f).normalized;
            Quaternion face=Quaternion.LookRotation(normal,Vector3.up);
            Vector3 cork=session.shuttle.State.position;
            const float swingFrame=.011f;
            SetProperty(right,"IsTracked",true);
            SetProperty(right,"HasMotionSample",true);
            SetProperty(right,"PreviousHead",cork-normal*.03f);
            SetProperty(right,"CurrentHead",cork+normal*.03f);
            SetProperty(right,"PreviousRotation",face);
            right.head.rotation=face;
            SetProperty(right,"HeadVelocity",normal*(.06f/swingFrame));
            SetProperty(right,"AngularVelocity",Vector3.zero);
            SetProperty(right,"MotionDeltaTime",swingFrame);
            SetProperty(right,"RecentPeakSpeed",.06f/swingFrame);
            session.shuttle.AdvanceFrame(swingFrame);
            Require(session.shuttle.Returned && session.Hits==1 && session.RallyStrokes==1 && session.Feeds==1,
                "Physical right-racket service contact failed or counted twice");
            Require(Vector3.Dot(session.shuttle.Velocity,session.court.forward)>0f &&
                session.opponent.ObservedShots==1 && session.opponent.Tracking,
                "Physical service did not travel forward and activate AI tracking");
            SetProperty(right,"IsTracked",false);
            SetProperty(right,"HasMotionSample",false);
        }

        private static void TestPausedAndLostTracking(BadmintonSession session,ShuttleHand hand)
        {
            var gesture=new HandServeGesture();
            Input(session,hand,gesture,true,false,false);
            Require(Input(session,hand,gesture,true,false,true),"Pause fixture could not pinch a shuttle");
            session.TogglePause();
            Vector3 held=session.shuttle.State.position;
            hand.source.transform.position+=session.court.right*.15f;
            Input(session,hand,gesture,true,false,true);
            Require(!session.Running && session.HandServeHeld && session.shuttle.State.position==held,
                "Paused held shuttle followed hand movement");
            Require(!Input(session,hand,gesture,true,false,false) && session.HandServeHeld && !session.shuttle.Flying,
                "A trigger release while paused served the shuttle");
            string settings=JsonUtility.ToJson(session.physics);
            EquipmentSetting selection=session.SelectedSetting;
            FeedPattern pattern=session.feedPattern;
            session.SetMode(PracticeMode.Training);
            session.SetDifficulty(OpponentDifficulty.Advanced);
            session.SetDrill(PracticeDrill.NetTouch);
            session.AdjustTension(1);
            session.AdjustMass(.1f);
            session.SetRacketWeight(RacketWeightClass.ThreeU);
            session.SetFeedPattern(pattern==FeedPattern.Mixed?FeedPattern.Fixed:FeedPattern.Mixed);
            Require(session.IsAiMatch && session.Difficulty==OpponentDifficulty.Easy && session.drill==PracticeDrill.Rally &&
                session.feedPattern==pattern && session.SelectedSetting==selection && JsonUtility.ToJson(session.physics)==settings,
                "Paused holding state allowed equipment/mode changes");
            session.StartPractice(XRNode.RightHand);
            Require(session.Running && session.HandServeHeld && session.Feeds==0,"Resume discarded or counted the held shuttle");
            Require(!session.ReleaseHandServe(hand),"Resume rearmed release without a fresh left pinch");
            Require(Input(session,hand,gesture,true,false,true),"Fresh pinch after pause failed to rearm");
            Require(Input(session,hand,gesture,true,false,false) && session.Feeds==1,
                "Fresh post-pause pinch/release did not serve exactly once");

            session.Recenter();
            gesture=new HandServeGesture();
            Input(session,hand,gesture,true,false,false);
            Require(Input(session,hand,gesture,true,false,true),"Tracking-loss fixture could not pinch");
            held=session.shuttle.State.position;
            Input(session,hand,gesture,false,false,false);
            Require(!session.Running && session.HandServeHeld && session.shuttle.State.position==held,
                "Tracking loss released or moved the held shuttle instead of pausing");
            Require(!session.ReleaseHandServe(hand),"Untracked release was accepted");
            hand.source.transform.position+=session.court.right*.20f;
            Input(session,hand,gesture,true,false,true);
            Require(!session.Running && session.shuttle.State.position==held,"Tracking recovery automatically resumed the held shuttle");
            session.StartPractice(XRNode.RightHand);
            Require(!session.ReleaseHandServe(hand),"Tracking resume armed an old trigger hold");
            Require(!Input(session,hand,gesture,true,false,false) && session.HandServeHeld,
                "First post-reconnect release was incorrectly treated as a serve");
            Require(Input(session,hand,gesture,true,false,true),"Fresh post-reconnect pinch was not accepted");
            Input(session,hand,gesture,true,false,true);
            Require(Input(session,hand,gesture,true,false,false) && session.Feeds==1,
                "Fresh post-reconnect release did not serve exactly once");
            Require(session.shuttle.Velocity==Vector3.zero,"Tracking jump leaked into the next release speed");
        }

        private static void TestReleaseSamplesAndReset(BadmintonSession session,ShuttleHand hand)
        {
            var gesture=new HandServeGesture();
            Input(session,hand,gesture,true,false,false);
            Require(Input(session,hand,gesture,true,false,true),"Final-pose release fixture could not pinch");
            Vector3 lastHeld=session.shuttle.State.position;
            hand.source.transform.position+=session.court.forward*.006f+Vector3.up*.004f;
            Vector3 releasePoint=hand.PinchPoint.position-Vector3.up*.055f;
            Require(Input(session,hand,gesture,true,false,false),"Moving release was rejected");
            Require(Vector3.Distance(session.shuttle.State.position,releasePoint)<.00001f,
                "Release used the previous frame's held position instead of the latest tracked hand pose");
            Require(Vector3.Distance(session.shuttle.Velocity,(releasePoint-lastHeld)/Frame)<.0002f,
                "Release used stale hand momentum instead of the final pose segment");
            session.Recenter();
            gesture=new HandServeGesture();
            Input(session,hand,gesture,true,false,false);
            Require(Input(session,hand,gesture,true,false,true),"Head-loss release fixture could not pinch");
            bool savedVr=(bool)typeof(BadmintonSession).GetField("xrSession",Flags).GetValue(session);
            try
            {
                // Batch-mode editor has no live HMD. Mark this as an XR session
                // to exercise the real HeadReady input gate, not a test override.
                SetField(session,"xrSession",true);
                bool headReady=(bool)typeof(BadmintonSession).GetMethod("HeadReady",Flags).Invoke(session,null);
                if(!headReady)
                {
                    SetProperty(hand,"IsPinching",false);
                    Require(!session.ReleaseHandServe(hand) && session.Feeds==0 && session.HandServeHeld && !session.shuttle.Flying,
                        "HMD loss allowed a release before Session.Update could pause");
                    Vector3 held=session.shuttle.State.position;
                    hand.source.transform.position+=session.court.right*.03f;
                    session.AdvanceHeldServe(hand,Frame);
                    Require(!session.Running && session.HandServeHeld && session.shuttle.State.position==held,
                        "HMD loss did not freeze and pause the held shuttle in the early hand update");
                }
                else Debug.Log("[Hand Serve Test] HMD-loss fixture skipped: this editor has a live tracked HMD; no tracking outage was fabricated.");
            }
            finally { SetField(session,"xrSession",savedVr); }
            session.Recenter();
            gesture=new HandServeGesture();
            Input(session,hand,gesture,true,false,false);
            Require(Input(session,hand,gesture,true,false,true),"Momentum fixture could not pinch");
            hand.source.transform.position+=session.court.right*.04f;
            session.AdvanceHeldServe(hand,.001f);
            SetProperty(hand,"IsPinching",false);
            Require(session.ReleaseHandServe(hand) && Mathf.Abs(session.shuttle.Velocity.magnitude-2f)<.0001f,
                "Manual release momentum must be capped at 2 m/s");
            session.Recenter();
            gesture=new HandServeGesture();
            Input(session,hand,gesture,true,false,false);
            Require(Input(session,hand,gesture,true,false,true),"Reset fixture could not pinch");
            hand.source.transform.position+=session.court.right*.40f;
            session.AdvanceHeldServe(hand,Frame);
            SetProperty(hand,"IsPinching",false);
            Require(session.ReleaseHandServe(hand) && session.shuttle.Velocity==Vector3.zero,
                "Tracking-like large position jump produced a throw impulse");
            session.Recenter();
            gesture=new HandServeGesture();
            Input(session,hand,gesture,true,false,false);
            Require(Input(session,hand,gesture,true,false,true),"Cancel fixture could not pinch");
            Require(session.Recenter() && !session.HandServeHeld && !session.shuttle.Held && !session.shuttle.Flying &&
                !session.shuttle.gameObject.activeSelf && !session.Running && session.Feeds==0,
                "Recenter did not cancel and hide the held shuttle");
            Require(!session.ReleaseHandServe(hand) && session.Feeds==0,"Release after reset revived the cancelled shuttle");
        }

        // Mirrors ShuttleHand.Update without requiring an XR hardware device.
        // The same gesture object and the real Session/Flight APIs are exercised.
        private static bool Input(BadmintonSession session,ShuttleHand hand,HandServeGesture gesture,bool tracked,bool menu,bool pressed)
        {
            SetProperty(hand.source,"IsTracked",tracked);
            SetProperty(hand.source,"MenuPressed",menu);
            SetProperty(hand.source,"TriggerPressed",tracked && pressed);
            SetProperty(hand.source,"TriggerValue",tracked && pressed?1f:0f);
            SetProperty(hand,"IsPinching",tracked && pressed);
            hand.UpdateVisuals(tracked && pressed?1f:0f);
            HandServeGestureAction action=gesture.Step(tracked,menu,tracked && pressed);
            bool accepted=false;
            if(action==HandServeGestureAction.Begin) accepted=session.BeginHandServe(hand);
            session.AdvanceHeldServe(hand,Frame);
            if(action==HandServeGestureAction.Release) accepted=session.ReleaseHandServe(hand);
            return accepted;
        }

        private static void SetProperty(object target,string name,object value)
        { target.GetType().GetProperty(name,Flags).SetValue(target,value,null); }
        private static void SetField(object target,string name,object value)
        { target.GetType().GetField(name,Flags).SetValue(target,value); }
        private static void Invoke(object target,string method)
        { target.GetType().GetMethod(method,Flags).Invoke(target,null); }

        private sealed class FieldSnapshot
        {
            private readonly object target;
            private readonly Dictionary<FieldInfo,object> values=new Dictionary<FieldInfo,object>();
            public FieldSnapshot(object value)
            {
                target=value;
                foreach(FieldInfo field in value.GetType().GetFields(Flags|BindingFlags.DeclaredOnly))
                    if(!field.IsInitOnly) values[field]=field.GetValue(value);
            }
            public void Restore() { foreach(var pair in values) pair.Key.SetValue(target,pair.Value); }
        }
        private sealed class PoseSnapshot
        {
            private readonly Transform target,parent;
            private readonly Vector3 position,scale;
            private readonly Quaternion rotation;
            private readonly bool active;
            private readonly int sibling;
            public PoseSnapshot(Transform item)
            { target=item; parent=item.parent; position=item.localPosition; rotation=item.localRotation; scale=item.localScale;
                active=item.gameObject.activeSelf; sibling=item.GetSiblingIndex(); }
            public void Restore()
            {
                if(target==null) return;
                target.SetParent(parent,false); target.SetSiblingIndex(sibling);
                target.localPosition=position; target.localRotation=rotation; target.localScale=scale;
                target.gameObject.SetActive(active);
            }
        }
        private sealed class TextSnapshot
        {
            private readonly TextMesh target;
            private readonly string text;
            private readonly float size;
            public TextSnapshot(TextMesh value) { target=value; text=value.text; size=value.characterSize; }
            public void Restore() { if(target!=null) { target.text=text; target.characterSize=size; } }
        }
    }
}
