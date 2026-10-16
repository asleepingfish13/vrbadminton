using System;
using UnityEditor;
using UnityEngine;

namespace ViveShuttleVR.Editor
{
    public static class AthleteRegression
    {
        private static int checks;
        public static int Run(BadmintonSession session)
        {
            checks=0;
            Require(session.athletePrefab!=null,"Scene has no real athlete prefab");
            GameObject instance=UnityEngine.Object.Instantiate(session.athletePrefab.gameObject);
            GameObject racket=new GameObject("Athlete regression physical racket");
            try
            {
                var athlete=instance.GetComponent<AthletePresentation>();
                athlete.EnsureReady();
                var skins=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Require(skins.Length>=3,"Athlete remains primitive rather than skinned human");
                int vertices=0;
                foreach(var skin in skins)
                {
                    Require(skin.sharedMesh!=null && skin.bones.Length>5,"Athlete part lacks skin/bones");
                    vertices+=skin.sharedMesh.vertexCount;
                    foreach(var material in skin.sharedMaterials) Require(material!=null,"Athlete material is missing");
                }
                Require(vertices>1000 && vertices<40000,"Unexpected athlete geometry complexity");
                foreach(var clip in new[]{athlete.idle,athlete.walk,athlete.run,athlete.runBack,athlete.runLeft,athlete.runRight})
                    Require(clip!=null && clip.length>.1f && AnimationUtility.GetCurveBindings(clip).Length>10,"Actual imported locomotion curves missing");
                foreach(var animator in instance.GetComponentsInChildren<Animator>(true))
                    Require(!animator.enabled && !animator.applyRootMotion,"Background Animator bypasses pause");
                Require(athlete.rightUpperArm!=null && athlete.rightForearm!=null && athlete.rightHand!=null && athlete.headBone!=null,
                    "Athlete articulation bones are absent");
                Require(athlete.animationRoot!=null,"Animation sample root was not resolved against actual skeleton");
                foreach(var clip in new[]{athlete.idle,athlete.walk,athlete.run,athlete.runBack,athlete.runLeft,athlete.runRight})
                foreach(var binding in AnimationUtility.GetCurveBindings(clip))
                    if(binding.type==typeof(Transform))
                        Require(string.IsNullOrEmpty(binding.path) || athlete.animationRoot.Find(binding.path)!=null,
                            "Imported clip has an unbound bone: "+clip.name+" / "+binding.path);
                Transform lowerLeg=null;
                SkinnedMeshRenderer legsSkin=null;
                foreach(Transform bone in athlete.GetComponentsInChildren<Transform>(true)) if(bone.name=="LowerLeg.R") lowerLeg=bone;
                foreach(var skin in skins) if(skin.name=="Casual2_Legs") legsSkin=skin;
                Require(lowerLeg!=null && legsSkin!=null,"Actual animated lower leg / leg mesh missing");
                athlete.ResetPose();
                Vector3 origin=instance.transform.position;
                Quaternion orientation=instance.transform.rotation;
                Vector3 target=athlete.rightUpperArm.position+new Vector3(.02f,-.25f,-.2f);
                racket.transform.rotation=Quaternion.LookRotation(Vector3.back,Vector3.up);
                racket.transform.position=target-racket.transform.rotation*(RacketSpecification.GripCenter-RacketSpecification.HeadCenter);
                Vector3 racketPosition=racket.transform.position;
                Quaternion racketRotation=racket.transform.rotation;
                Quaternion armBefore=athlete.rightUpperArm.rotation;
                Quaternion legBefore=lowerLeg.localRotation;
                float maximumLegAngle=0f;
                Mesh bakedBefore=new Mesh(),bakedAfter=new Mesh();
                legsSkin.BakeMesh(bakedBefore);
                int before=athlete.PoseSamples;
                for(int frame=0;frame<360;frame++)
                {
                    athlete.SamplePose(1f/360f,new Vector3(1.7f,0f,0f),new Vector3(0f,1.5f,-5f),racket.transform,.5f);
                    float change=Quaternion.Angle(legBefore,lowerLeg.localRotation);
                    if(change>maximumLegAngle) { maximumLegAngle=change; legsSkin.BakeMesh(bakedAfter); }
                }
                Require(maximumLegAngle>8f,"Imported locomotion never moved real lower-leg bones (possible binding mismatch)");
                float meshChange=0f;
                Vector3[] restingVertices=bakedBefore.vertices,animatedVertices=bakedAfter.vertices;
                if(restingVertices.Length==animatedVertices.Length)
                    for(int vertex=0;vertex<restingVertices.Length;vertex++)
                        meshChange=Mathf.Max(meshChange,Vector3.Distance(restingVertices[vertex],animatedVertices[vertex]));
                UnityEngine.Object.DestroyImmediate(bakedBefore);
                UnityEngine.Object.DestroyImmediate(bakedAfter);
                Require(meshChange>.01f,"Actual leg skin did not deform under locomotion");
                Debug.Log("[Athlete Test] Actual lower-leg rotation span="+maximumLegAngle.ToString("F2")+"deg; skinned displacement="+meshChange.ToString("F4")+"m");
                Require(athlete.PoseSamples-before>=80 && athlete.PoseSamples-before<=92,"Imported animation sampled at physics rather than visual rate");
                Require(athlete.AnimationClock>.99f && athlete.AnimationClock<1.01f,"Animation dt differs from simulation dt");
                Require(instance.transform.position==origin && instance.transform.rotation==orientation,"Animation moved AI feet/root");
                Require(racket.transform.position==racketPosition && racket.transform.rotation==racketRotation,"Presentation changed physical racket");
                Require(Quaternion.Angle(armBefore,athlete.rightUpperArm.rotation)>1f,"Imported/IK athlete arm never articulated");
                Require(Vector3.Distance(athlete.rightHand.position,athlete.GripTarget)<.08f,"Reachable physical racket grip does not align with athlete hand");
                float upper=Vector3.Distance(athlete.rightUpperArm.position,athlete.rightForearm.position);
                float lower=Vector3.Distance(athlete.rightForearm.position,athlete.rightHand.position);
                Require(upper>.12f && upper<.5f && lower>.12f && lower<.5f,"IK stretched anatomical arm lengths");
                float clock=athlete.AnimationClock;
                Quaternion pausedArm=athlete.rightUpperArm.rotation;
                for(int frame=0;frame<90;frame++) athlete.SamplePose(0f,Vector3.zero,Vector3.zero,racket.transform,0f);
                Require(athlete.AnimationClock==clock && athlete.rightUpperArm.rotation==pausedArm,"Zero-dt pause animated athlete");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); UnityEngine.Object.DestroyImmediate(racket); }
            Debug.Log("[Athlete Test] PASS "+checks+" checks: actual skinned CC0 human, six imported locomotion clips, bounded visual sample rate, deterministic pause, non-stretch arm IK and presentation-only ownership.");
            return checks;
        }

        private static void Require(bool condition,string message)
        { checks++; if(!condition) throw new InvalidOperationException("[Athlete Test] "+message); }
    }
}
