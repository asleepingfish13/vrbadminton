using UnityEngine;

namespace ViveShuttleVR
{
    public enum HandServeGestureAction { None, Begin, Release }

    // Reconnecting while a trigger is down is not a new pinch. A complete release
    // and fresh press are required after tracking loss or a menu gesture.
    public sealed class HandServeGesture
    {
        private bool armed, wasPressed;
        public HandServeGestureAction Step(bool tracked,bool menuPressed,bool pressed)
        {
            if(!tracked || menuPressed)
            {
                armed=false;
                wasPressed=false;
                return HandServeGestureAction.None;
            }
            if(!armed)
            {
                if(!pressed) armed=true;
                wasPressed=pressed;
                return HandServeGestureAction.None;
            }
            HandServeGestureAction action=pressed && !wasPressed?HandServeGestureAction.Begin:
                !pressed && wasPressed?HandServeGestureAction.Release:HandServeGestureAction.None;
            wasPressed=pressed;
            return action;
        }
    }

    [DefaultExecutionOrder(-50)]
    public sealed class ShuttleHand : MonoBehaviour
    {
        public RacketController source;
        public BadmintonSession session;
        public Material handMaterial;
        public Material cuffMaterial;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform pinchPoint;
        [SerializeField] private Transform[] fingerSegments;
        private readonly HandServeGesture gesture=new HandServeGesture();
        private Material runtimeHandMaterial,runtimeCuffMaterial,boundHandMaterial,boundCuffMaterial;

        public Transform PinchPoint { get { BuildVisuals(); return pinchPoint; } }
        public bool IsPinching { get; private set; }
        public float PinchAmount { get; private set; }
        public static Vector3 PinchLocalPosition { get { return new Vector3(.026f,.013f,.100f); } }

        private void Awake() { BuildVisuals(); }

        public void BuildVisuals()
        {
            if(source==null) source=GetComponent<RacketController>();
            if(session==null && source!=null) session=source.session;
            if(handMaterial==null)
            {
                handMaterial=new Material(Shader.Find("Standard"));
                handMaterial.name="Practice glove - warm ivory";
                handMaterial.color=new Color(.84f,.75f,.62f);
                runtimeHandMaterial=handMaterial;
            }
            if(cuffMaterial==null)
            {
                cuffMaterial=new Material(Shader.Find("Standard"));
                cuffMaterial.name="Practice glove - teal cuff";
                cuffMaterial.color=new Color(.08f,.34f,.33f);
                runtimeCuffMaterial=cuffMaterial;
            }
            if(visualRoot!=null && pinchPoint!=null && fingerSegments!=null && fingerSegments.Length==15)
            { BindMaterials(); return; }
            visualRoot=new GameObject("Left holding hand").transform;
            visualRoot.SetParent(transform,false);
            Transform palm=Part("Palm",PrimitiveType.Sphere,handMaterial);
            palm.localPosition=new Vector3(0f,0f,.025f);
            palm.localScale=new Vector3(.068f,.035f,.085f);
            Transform cuff=Part("Wrist cuff",PrimitiveType.Cylinder,cuffMaterial);
            cuff.localPosition=new Vector3(0f,0f,-.030f);
            cuff.localRotation=Quaternion.Euler(90f,0f,0f);
            cuff.localScale=new Vector3(.048f,.015f,.034f);
            pinchPoint=new GameObject("Shuttle pinch point").transform;
            pinchPoint.SetParent(visualRoot,false);
            pinchPoint.localPosition=PinchLocalPosition;
            fingerSegments=new Transform[15];
            for(int finger=0;finger<5;finger++)
            for(int bone=0;bone<3;bone++)
                fingerSegments[finger*3+bone]=Part((finger==0?"Thumb":"Finger "+finger)+" segment "+bone,
                    PrimitiveType.Capsule,handMaterial);
            UpdateVisuals(0f);
        }

        private void BindMaterials()
        {
            if(boundHandMaterial!=handMaterial || boundCuffMaterial!=cuffMaterial)
            {
                foreach(Renderer renderer in visualRoot.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterial=renderer.name=="Wrist cuff"?cuffMaterial:handMaterial;
                boundHandMaterial=handMaterial;
                boundCuffMaterial=cuffMaterial;
            }
            // Track actual allocations rather than booleans: an editor builder
            // can replace fallback fields with persistent materials after Awake.
            if(runtimeHandMaterial!=null && runtimeHandMaterial!=handMaterial)
            { DestroyOwnedMaterial(runtimeHandMaterial); runtimeHandMaterial=null; }
            if(runtimeCuffMaterial!=null && runtimeCuffMaterial!=cuffMaterial)
            { DestroyOwnedMaterial(runtimeCuffMaterial); runtimeCuffMaterial=null; }
        }

        private Transform Part(string label,PrimitiveType primitive,Material material)
        {
            GameObject part=GameObject.CreatePrimitive(primitive);
            part.name=label;
            part.transform.SetParent(visualRoot,false);
            part.GetComponent<Renderer>().sharedMaterial=material;
            Collider collider=part.GetComponent<Collider>();
            if(collider!=null)
            {
                collider.enabled=false;
                if(Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            }
            return part.transform;
        }

        public void UpdateVisuals(float triggerValue)
        {
            BuildVisuals();
            PinchAmount=float.IsNaN(triggerValue) || float.IsInfinity(triggerValue)?0f:Mathf.Clamp01(triggerValue);
            visualRoot.localPosition=source!=null?source.GripAnchorLocal:RacketController.ViveLowerHandleMountOffset;
            visualRoot.localRotation=Quaternion.identity;
            for(int finger=0;finger<5;finger++)
            for(int bone=0;bone<3;bone++)
            {
                Vector3 from=FingerJoint(finger,bone,PinchAmount);
                Vector3 to=FingerJoint(finger,bone+1,PinchAmount);
                Transform segment=fingerSegments[finger*3+bone];
                segment.localPosition=(from+to)*.5f;
                segment.localRotation=Quaternion.FromToRotation(Vector3.up,(to-from).normalized);
                float thickness=finger==0?.017f:finger==4?.012f:.014f;
                segment.localScale=new Vector3(thickness,Mathf.Max((to-from).magnitude*.5f,.008f),thickness);
            }
        }

        public static Vector3 FingerJoint(int finger,int joint,float amount)
        {
            amount=float.IsNaN(amount) || float.IsInfinity(amount)?0f:Mathf.Clamp01(amount);
            joint=Mathf.Clamp(joint,0,3);
            finger=Mathf.Clamp(finger,0,4);
            if(finger==0)
            {
                Vector3 open=joint==0?new Vector3(.037f,0f,.016f):joint==1?new Vector3(.055f,.003f,.040f):
                    joint==2?new Vector3(.067f,.004f,.047f):new Vector3(.075f,.004f,.055f);
                Vector3 closed=joint==0?new Vector3(.037f,0f,.016f):joint==1?new Vector3(.060f,.002f,.052f):
                    joint==2?new Vector3(.048f,.007f,.084f):PinchLocalPosition;
                return Vector3.Lerp(open,closed,amount);
            }
            float x=.024f-(finger-1)*.016f;
            float length=finger==1?.071f:finger==2?.078f:finger==3?.072f:.057f;
            float along=joint==0?0f:joint==1?.46f:joint==2?.76f:1f;
            Vector3 straight=new Vector3(x,0f,.055f+along*length);
            Vector3 bent;
            if(finger==1)
                bent=joint==0?new Vector3(x,0f,.055f):joint==1?new Vector3(.027f,.020f,.084f):
                    joint==2?new Vector3(.028f,.023f,.103f):PinchLocalPosition;
            else
                bent=joint==0?straight:joint==1?new Vector3(x,.014f,.055f+length*.40f):
                    joint==2?new Vector3(x,.030f,.055f+length*.46f):new Vector3(x,.034f,.055f+length*.22f);
            return Vector3.Lerp(straight,bent,amount);
        }

        private void Update()
        {
            BuildVisuals();
            bool tracked=source!=null && source.IsFreeHand && source.IsTracked;
            bool menu=source!=null && source.MenuPressed;
            bool pressed=tracked && source.TriggerPressed;
            IsPinching=pressed;
            UpdateVisuals(tracked?source.TriggerValue:0f);
            visualRoot.gameObject.SetActive(tracked);
            HandServeGestureAction action=gesture.Step(tracked,menu,pressed);
            if(session==null) return;
            if(action==HandServeGestureAction.Begin) session.BeginHandServe(this);
            // Session owns pause/tracking-loss policy; never release a held shuttle
            // merely because this component has lost tracking or become hidden.
            session.UpdateHeldServe(this);
            if(action==HandServeGestureAction.Release) session.ReleaseHandServe(this);
        }

        private void OnDestroy()
        {
            DestroyOwnedMaterial(runtimeHandMaterial);
            DestroyOwnedMaterial(runtimeCuffMaterial);
        }
        private static void DestroyOwnedMaterial(Material material)
        {
            if(material==null) return;
            if(Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }
}
