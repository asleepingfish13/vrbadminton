using UnityEngine;

namespace ViveShuttleVR
{
    // Passive presentation: no Update, Animator controller, root motion, physics,
    // or clock of its own. The opponent supplies simulation dt, so pause freezes
    // both imported locomotion and the visual racket-hand alignment.
    public sealed class AthletePresentation : MonoBehaviour
    {
        public Transform characterRoot, animationRoot;
        public Transform armatureRoot, motionRoot, chest, headBone;
        public Transform rightUpperArm, rightForearm, rightHand;
        public AnimationClip idle, walk, run, runBack, runLeft, runRight;
        public int PoseSamples { get; private set; }
        public float AnimationClock { get { return animationClock; } }
        public Vector3 GripTarget { get; private set; }
        public const float VisualSampleStep = 1f/90f;
        private Transform[] bones;
        private Vector3[] restPositions, restScales, blendPositions, blendScales;
        private Quaternion[] restRotations, blendRotations;
        private Vector3 characterPosition, characterScale, armaturePosition, rootPosition;
        private Quaternion characterRotation, armatureRotation, rootRotation;
        private float animationClock, pendingTime, filteredSpeed;
        private bool ready;

        private void Awake() { EnsureReady(); }

        public void EnsureReady()
        {
            if(ready || characterRoot==null) return;
            // Imported models may carry an Animator even though no controller is
            // installed. Explicitly disable it in case an asset is later changed.
            foreach(Animator animator in GetComponentsInChildren<Animator>(true))
            { animator.enabled=false; animator.applyRootMotion=false; }
            foreach(Animation animation in GetComponentsInChildren<Animation>(true))
            { animation.Stop(); animation.enabled=false; }
            bones=characterRoot.GetComponentsInChildren<Transform>(true);
            int count=bones.Length;
            restPositions=new Vector3[count]; restScales=new Vector3[count];
            restRotations=new Quaternion[count]; blendPositions=new Vector3[count];
            blendScales=new Vector3[count]; blendRotations=new Quaternion[count];
            for(int i=0;i<count;i++)
            { restPositions[i]=bones[i].localPosition; restRotations[i]=bones[i].localRotation; restScales[i]=bones[i].localScale; }
            characterPosition=characterRoot.localPosition; characterRotation=characterRoot.localRotation;
            characterScale=characterRoot.localScale;
            if(armatureRoot!=null) { armaturePosition=armatureRoot.localPosition; armatureRotation=armatureRoot.localRotation; }
            if(motionRoot!=null) { rootPosition=motionRoot.localPosition; rootRotation=motionRoot.localRotation; }
            ready=true;
        }

        public void ResetPose()
        {
            EnsureReady();
            animationClock=pendingTime=filteredSpeed=0f;
            PoseSamples=0;
            if(ready) { SampleLocomotion(Vector3.zero,0f); PoseSamples=0; }
        }

        public void SamplePose(float deltaTime,Vector3 courtLocalVelocity,Vector3 lookTargetWorld,
            Transform physicalRacketHead,float swingAmount)
        {
            EnsureReady();
            if(!ready || !Finite(courtLocalVelocity) || !Finite(lookTargetWorld)) return;
            float dt=Mathf.Clamp(deltaTime,0f,.08f);
            pendingTime+=dt;
            animationClock+=dt;
            // The calling physics uses 360 Hz. Do not evaluate a full imported
            // skin animation four times per VR frame. No Time.frameCount gate:
            // editor regression tests advance explicit dt in a single editor frame.
            if(PoseSamples==0 || pendingTime+1e-6f>=VisualSampleStep)
            {
                float elapsed=pendingTime;
                pendingTime=Mathf.Repeat(pendingTime,VisualSampleStep);
                float speed=new Vector2(courtLocalVelocity.x,courtLocalVelocity.z).magnitude;
                filteredSpeed=Mathf.Lerp(filteredSpeed,speed,1f-Mathf.Exp(-elapsed*12f));
                SampleLocomotion(courtLocalVelocity,filteredSpeed);
                ApplyLook(lookTargetWorld);
                // IK follows the actual visual racket, never the other way round.
                ApplyRacketArm(physicalRacketHead,Mathf.Clamp01(swingAmount));
            }
        }

        // Optional per-render-frame correction after the owner has finalized a
        // visual racket pose. No clock/root/clip advance and no racket mutation.
        public void AlignRacketArm(Transform physicalRacketHead,float swingAmount)
        {
            EnsureReady();
            if(ready) ApplyRacketArm(physicalRacketHead,Mathf.Clamp01(swingAmount));
        }

        private void RestoreRest()
        {
            for(int i=0;i<bones.Length;i++)
            { bones[i].localPosition=restPositions[i]; bones[i].localRotation=restRotations[i]; bones[i].localScale=restScales[i]; }
        }

        private void SampleLocomotion(Vector3 velocity,float speed)
        {
            RestoreRest();
            Sample(idle,animationClock);
            float blend=Mathf.Clamp01(speed/.7f);
            AnimationClip locomotion=ChooseLocomotion(velocity,speed);
            if(locomotion!=null && blend>.001f)
            {
                for(int i=0;i<bones.Length;i++)
                { blendPositions[i]=bones[i].localPosition; blendRotations[i]=bones[i].localRotation; blendScales[i]=bones[i].localScale; }
                RestoreRest();
                float referenceSpeed=speed<1.55f?1.25f:3.0f;
                Sample(locomotion,animationClock*Mathf.Clamp(speed/referenceSpeed,.65f,1.45f));
                for(int i=0;i<bones.Length;i++)
                {
                    bones[i].localPosition=Vector3.Lerp(blendPositions[i],bones[i].localPosition,blend);
                    bones[i].localRotation=Quaternion.Slerp(blendRotations[i],bones[i].localRotation,blend);
                    bones[i].localScale=Vector3.Lerp(blendScales[i],bones[i].localScale,blend);
                }
            }
            // Remove imported root motion, retaining hip/leg cycling and body bob.
            characterRoot.localPosition=characterPosition;
            characterRoot.localRotation=characterRotation;
            characterRoot.localScale=characterScale;
            if(armatureRoot!=null) { armatureRoot.localPosition=armaturePosition; armatureRoot.localRotation=armatureRotation; }
            if(motionRoot!=null) { motionRoot.localPosition=rootPosition; motionRoot.localRotation=rootRotation; }
            PoseSamples++;
        }

        private AnimationClip ChooseLocomotion(Vector3 courtVelocity,float speed)
        {
            if(speed<1.55f) return walk!=null?walk:run;
            Vector3 modelVelocity=characterRoot.InverseTransformDirection(transform.TransformDirection(courtVelocity));
            if(Mathf.Abs(modelVelocity.x)>Mathf.Abs(modelVelocity.z))
                return modelVelocity.x>0f?(runRight!=null?runRight:run):(runLeft!=null?runLeft:run);
            return modelVelocity.z<0f && runBack!=null?runBack:run;
        }

        private void Sample(AnimationClip clip,float seconds)
        {
            if(clip!=null && clip.length>.001f)
                clip.SampleAnimation((animationRoot!=null?animationRoot:characterRoot).gameObject,Mathf.Repeat(seconds,clip.length));
        }

        private void ApplyLook(Vector3 target)
        {
            if(headBone==null) return;
            Vector3 direction=target-headBone.position;
            if(direction.sqrMagnitude<.01f) return;
            Quaternion look=Quaternion.FromToRotation(characterRoot.forward,direction.normalized);
            look=Quaternion.RotateTowards(Quaternion.identity,look,28f);
            headBone.rotation=look*headBone.rotation;
        }

        private void ApplyRacketArm(Transform racketHead,float swing)
        {
            if(racketHead==null || rightUpperArm==null || rightForearm==null || rightHand==null) return;
            GripTarget=racketHead.TransformPoint(RacketSpecification.GripCenter-RacketSpecification.HeadCenter);
            Vector3 shoulder=rightUpperArm.position;
            Vector3 forearm=rightForearm.position;
            Vector3 hand=rightHand.position;
            float upperLength=Vector3.Distance(shoulder,forearm),lowerLength=Vector3.Distance(forearm,hand);
            Vector3 offset=GripTarget-shoulder;
            if(upperLength<.01f || lowerLength<.01f || offset.sqrMagnitude<.0001f) return;
            Vector3 direction=offset.normalized;
            float distance=Mathf.Clamp(offset.magnitude,Mathf.Abs(upperLength-lowerLength)+.001f,upperLength+lowerLength-.002f);
            float along=(upperLength*upperLength-lowerLength*lowerLength+distance*distance)/(2f*distance);
            float across=Mathf.Sqrt(Mathf.Max(0f,upperLength*upperLength-along*along));
            Vector3 outward=chest!=null?shoulder-chest.position:characterRoot.right;
            Vector3 hint=outward.normalized*.35f+Vector3.down*.65f+characterRoot.forward*Mathf.Lerp(.3f,.6f,swing);
            Vector3 bend=Vector3.ProjectOnPlane(hint,direction).normalized;
            if(bend.sqrMagnitude<.01f) bend=Vector3.ProjectOnPlane(characterRoot.up,direction).normalized;
            Vector3 elbowTarget=shoulder+direction*along+bend*across;
            rightUpperArm.rotation=Quaternion.FromToRotation(forearm-shoulder,elbowTarget-shoulder)*rightUpperArm.rotation;
            Vector3 reachableHand=shoulder+direction*distance;
            rightForearm.rotation=Quaternion.FromToRotation(rightHand.position-rightForearm.position,reachableHand-rightForearm.position)*rightForearm.rotation;
            // Align the palm to the handle while keeping the arm's joint lengths.
            rightHand.rotation=racketHead.rotation*Quaternion.Euler(90f,0f,90f);
        }

        private static bool Finite(Vector3 value)
        { return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z); }
    }
}
