using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using Object=UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    public static class ShuttleHandRegression
    {
        private static int checks;
        private static void Require(bool condition,string message)
        {
            checks++;
            if(!condition) throw new InvalidOperationException("[Shuttle Hand Test] "+message);
        }

        public static int Run(BadmintonSession session)
        {
            checks=0;
            RacketController left=null,right=null;
            foreach(RacketController controller in session.rackets)
            {
                if(controller.hand==XRNode.LeftHand) left=controller;
                if(controller.hand==XRNode.RightHand) right=controller;
            }
            Require(left!=null && right!=null,"Both grip-pose input sources must remain present");
            Require(left.IsFreeHand && !left.CanStrike,"The left hand must not remain a hidden striking racket");
            Require(!right.IsFreeHand && right.CanStrike,"The right hand must retain the striking racket");
            ShuttleHand hand=left.GetComponent<ShuttleHand>();
            Require(hand!=null && hand.source==left && hand.session==session,"Saved scene has no wired left holding hand");
            Require(AssetDatabase.Contains(hand.handMaterial) && AssetDatabase.Contains(hand.cuffMaterial),
                "Saved hand materials must be persistent project assets");
            CheckHandGeometry(hand,left);
            CheckGestureEdges();

            GameObject fixture=new GameObject("Uninitialised hand fixture");
            Material replacementHand=null,replacementCuff=null;
            try
            {
                RacketController controller=fixture.AddComponent<RacketController>();
                controller.freeHand=true;
                ShuttleHand fresh=fixture.AddComponent<ShuttleHand>();
                fresh.BuildVisuals();
                Require(fresh.source==controller && fresh.PinchPoint!=null,"A fresh hand must initialise without a scene-authored visual");
                int children=fixture.GetComponentsInChildren<Transform>(true).Length;
                for(int i=0;i<10;i++) fresh.BuildVisuals();
                Require(fixture.GetComponentsInChildren<Transform>(true).Length==children,"Repeated visual initialisation duplicates hand meshes");
                replacementHand=new Material(Shader.Find("Standard"));
                replacementCuff=new Material(Shader.Find("Standard"));
                fresh.handMaterial=replacementHand;
                fresh.cuffMaterial=replacementCuff;
                fresh.BuildVisuals();
                foreach(Renderer renderer in fixture.transform.Find("Left holding hand").GetComponentsInChildren<Renderer>(true))
                    Require(renderer.sharedMaterial==(renderer.name=="Wrist cuff"?replacementCuff:replacementHand),
                        "Re-initialisation failed to bind replacement persistent materials");
                CheckHandGeometry(fresh,controller);
            }
            finally
            {
                bool hadReplacement=!ReferenceEquals(replacementHand,null) && !ReferenceEquals(replacementCuff,null);
                Object.DestroyImmediate(fixture);
                bool retainedMaterials=replacementHand!=null && replacementCuff!=null;
                if(replacementHand!=null) Object.DestroyImmediate(replacementHand);
                if(replacementCuff!=null) Object.DestroyImmediate(replacementCuff);
                if(hadReplacement) Require(retainedMaterials,"Destroying a hand must not destroy externally supplied materials");
            }
            Debug.Log("[Shuttle Hand Test] PASS "+checks+" checks: left free hand, lower-grip geometry, continuous pinch, fresh-press reconnect and menu safety.");
            return checks;
        }

        private static void CheckHandGeometry(ShuttleHand hand,RacketController controller)
        {
            hand.BuildVisuals();
            Transform root=controller.transform.Find("Left holding hand");
            Require(root!=null && root.GetComponentsInChildren<Renderer>(true).Length==17,
                "Hand must show palm, cuff and five articulated fingers");
            Require(root.GetComponentsInChildren<Collider>(true).Length==0,"Holding hand must not introduce an invisible shuttle collider");
            Vector3 savedPosition=controller.transform.localPosition;
            Quaternion savedRotation=controller.transform.localRotation;
            float savedPinch=hand.PinchAmount;
            try
            {
                foreach(float yaw in new[]{0f,73f,210f})
                foreach(float amount in new[]{-1f,0f,.25f,.5f,1f,2f,float.NaN,float.PositiveInfinity})
                {
                    controller.transform.localPosition=new Vector3(.14f,1.23f,-.17f);
                    controller.transform.localRotation=Quaternion.Euler(24f,yaw,-13f);
                    hand.UpdateVisuals(amount);
                    Require(IsFinite(hand.PinchAmount) && hand.PinchAmount>=0f && hand.PinchAmount<=1f,
                        "Pinch input did not clamp invalid values safely");
                    Require(Vector3.Distance(hand.PinchPoint.position,
                        controller.transform.TransformPoint(controller.GripAnchorLocal+ShuttleHand.PinchLocalPosition))<.00001f,
                        "Pinch anchor moved away from the lower tracked grip or jumped while fingers opened");
                    foreach(Transform part in root.GetComponentsInChildren<Transform>(true))
                        Require(Finite(part.position) && Finite(part.localScale) &&
                            IsFinite(part.rotation.x) && IsFinite(part.rotation.y) && IsFinite(part.rotation.z) && IsFinite(part.rotation.w),
                            "Hand articulation generated a nonfinite transform: "+part.name);
                    for(int finger=0;finger<5;finger++)
                    for(int bone=0;bone<3;bone++)
                    {
                        Vector3 a=ShuttleHand.FingerJoint(finger,bone,amount);
                        Vector3 b=ShuttleHand.FingerJoint(finger,bone+1,amount);
                        Require(Finite(a) && Finite(b) && Vector3.Distance(a,b)>.002f && Vector3.Distance(a,b)<.09f,
                            "Pinch articulation collapsed or overstretched a finger segment");
                    }
                }
                Require(Vector3.Distance(ShuttleHand.FingerJoint(0,3,0f),ShuttleHand.FingerJoint(1,3,0f))>.04f,
                    "Released thumb/index should visibly separate");
                Require(Vector3.Distance(ShuttleHand.FingerJoint(0,3,1f),ShuttleHand.PinchLocalPosition)<.00001f &&
                    Vector3.Distance(ShuttleHand.FingerJoint(1,3,1f),ShuttleHand.PinchLocalPosition)<.00001f,
                    "Closed thumb/index must meet at the held shuttle anchor");
            }
            finally
            {
                controller.transform.localPosition=savedPosition;
                controller.transform.localRotation=savedRotation;
                hand.UpdateVisuals(savedPinch);
            }
        }

        private static void CheckGestureEdges()
        {
            var input=new HandServeGesture();
            Expect(input,false,false,false,HandServeGestureAction.None,"untracked start");
            Expect(input,true,false,true,HandServeGestureAction.None,"reconnect with held trigger");
            Expect(input,true,false,false,HandServeGestureAction.None,"release after reconnect only rearms");
            for(int i=0;i<3;i++)
            {
                Expect(input,true,false,true,HandServeGestureAction.Begin,"fresh press");
                Expect(input,true,false,true,HandServeGestureAction.None,"held trigger");
                Expect(input,true,false,false,HandServeGestureAction.Release,"intentional release");
                Expect(input,true,false,false,HandServeGestureAction.None,"released trigger");
            }
            Expect(input,true,false,true,HandServeGestureAction.Begin,"pre-loss pinch");
            Expect(input,false,false,false,HandServeGestureAction.None,"tracking loss must not throw a shuttle");
            Expect(input,true,false,true,HandServeGestureAction.None,"tracking recovery is not a pinch");
            Expect(input,true,false,false,HandServeGestureAction.None,"post-loss release is not a serve");
            Expect(input,true,false,true,HandServeGestureAction.Begin,"new post-loss pinch");
            Expect(input,true,true,true,HandServeGestureAction.None,"menu disarms held trigger");
            Expect(input,true,true,false,HandServeGestureAction.None,"release during menu is not a serve");
            Expect(input,true,false,true,HandServeGestureAction.None,"held trigger on menu exit cannot start");
            Expect(input,true,false,false,HandServeGestureAction.None,"menu exit requires release to rearm");
            Expect(input,true,false,true,HandServeGestureAction.Begin,"fresh pinch after menu");
            Expect(input,true,false,false,HandServeGestureAction.Release,"fresh release after menu");
        }

        private static void Expect(HandServeGesture input,bool tracked,bool menu,bool pressed,HandServeGestureAction expected,string label)
        {
            Require(input.Step(tracked,menu,pressed)==expected,label);
        }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(Vector3 value) { return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z); }
    }
}
