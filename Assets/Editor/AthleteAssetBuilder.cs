using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SpatialTracking;

namespace ViveShuttleVR.Editor
{
    public static class AthleteAssetBuilder
    {
        private const string Source="Assets/ThirdParty/Quaternius/UltimateModularMen/";
        private const string ModelPath=Source+"Casual_2.fbx";
        private const string Destination="Assets/Generated/Athlete";
        public const string PrefabPath=Destination+"/PracticeAthlete.prefab";

        public static AthletePresentation BuildPrefab()
        {
            EnsureFolder("Assets/Generated");
            EnsureFolder(Destination);
            // Use geometry and clips from the SAME original rig. The pack also
            // offers a different Humanoid Rig hierarchy; mixing that model with
            // the original animations silently leaves unmatched bones in T-pose.
            ConfigureImporter(ModelPath,true);
            var characterAsset=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if(characterAsset==null) throw new InvalidOperationException("Quaternius character FBX could not be imported");
            GameObject root=new GameObject("Animated badminton practice athlete");
            try
            {
                var presentation=root.AddComponent<AthletePresentation>();
                GameObject character=UnityEngine.Object.Instantiate(characterAsset,root.transform);
                character.name="Quaternius Casual2 skinned athlete";
                character.transform.localRotation=Quaternion.Euler(0f,180f,0f);
                foreach(Animator animator in character.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
                foreach(Animation animation in character.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(animation);
                foreach(Collider collider in character.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                var skins=character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if(skins.Length<3) throw new InvalidOperationException("Athlete source lacks complete skinned character parts");
                Bounds initial=BoundsOf(skins);
                if(initial.size.y<.05f || initial.size.y>1000f) throw new InvalidOperationException("Invalid imported athlete scale: "+initial.size);
                character.transform.localScale*=1.75f/initial.size.y;
                Bounds scaled=BoundsOf(skins);
                character.transform.position-=Vector3.up*scaled.min.y;
                var materials=new Dictionary<string,Material>();
                foreach(SkinnedMeshRenderer skin in skins)
                {
                    skin.updateWhenOffscreen=true;
                    skin.quality=SkinQuality.Bone4;
                    Material[] palette=skin.sharedMaterials;
                    for(int i=0;i<palette.Length;i++)
                    {
                        Material original=palette[i];
                        if(original==null) continue;
                        Material material;
                        if(!materials.TryGetValue(original.name,out material))
                        {
                            string safe=original.name.Replace('/','_').Replace('\\','_').Replace(':','_');
                            string materialPath=Destination+"/Athlete - "+safe+".mat";
                            material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                            var built=new Material(Shader.Find("Standard"));
                            Color color=original.HasProperty("_Color")?original.color:Color.gray;
                            // An in-project sports palette; never recolor skin,
                            // hair or eyes. Original geometry/skin weights remain.
                            if(original.name=="Red_Dark") color=new Color(.12f,.62f,.76f);
                            else if(original.name=="LightBlue") color=new Color(.055f,.12f,.22f);
                            built.color=color;
                            built.SetFloat("_Glossiness",.12f);
                            if(material==null) { built.name="Athlete - "+safe; AssetDatabase.CreateAsset(built,materialPath); material=built; }
                            else { EditorUtility.CopySerialized(built,material); UnityEngine.Object.DestroyImmediate(built); EditorUtility.SetDirty(material); }
                            materials.Add(original.name,material);
                        }
                        palette[i]=material;
                    }
                    skin.sharedMaterials=palette;
                }
                presentation.characterRoot=character.transform;
                presentation.armatureRoot=Find(character.transform,"CharacterArmature");
                presentation.motionRoot=Find(character.transform,"Root");
                presentation.chest=Find(character.transform,"Chest");
                presentation.headBone=Find(character.transform,"Head");
                presentation.rightUpperArm=Find(character.transform,"UpperArm.R");
                presentation.rightForearm=Find(character.transform,"LowerArm.R");
                presentation.rightHand=Find(character.transform,"Wrist.R");
                presentation.idle=CopyClip("Idle_Neutral","Idle");
                presentation.walk=CopyClip("Walk","Walk");
                presentation.run=CopyClip("Run","Run");
                presentation.runBack=CopyClip("Run_Back","Run Back");
                presentation.runLeft=CopyClip("Run_Left","Run Left");
                presentation.runRight=CopyClip("Run_Right","Run Right");
                presentation.animationRoot=ChooseAnimationRoot(presentation,presentation.run);
                if(presentation.armatureRoot==null || presentation.headBone==null || presentation.rightHand==null ||
                    presentation.rightUpperArm==null || presentation.rightForearm==null)
                    throw new InvalidOperationException("Athlete FBX expected arm/head bones missing");
                GameObject prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
                AssetDatabase.SaveAssets();
                int vertices=0;
                foreach(SkinnedMeshRenderer skin in skins) if(skin.sharedMesh!=null) vertices+=skin.sharedMesh.vertexCount;
                Debug.Log("[Athlete Build] CC0 skinned Casual2 athlete: skins="+skins.Length+" vertices="+vertices+
                    " height="+BoundsOf(skins).size.y.ToString("F3")+"m; clips="+presentation.idle.name+","+presentation.walk.name+","+presentation.run.name+
                    "; bones="+presentation.armatureRoot.name+"/"+presentation.rightUpperArm.name+"/"+presentation.rightHand.name);
                Debug.Log("[Athlete Build] model-local shoulder="+root.transform.InverseTransformPoint(presentation.rightUpperArm.position).ToString("F3")+
                    " elbow="+root.transform.InverseTransformPoint(presentation.rightForearm.position).ToString("F3")+
                    " wrist="+root.transform.InverseTransformPoint(presentation.rightHand.position).ToString("F3")+
                    " armLength="+(Vector3.Distance(presentation.rightUpperArm.position,presentation.rightForearm.position)+
                    Vector3.Distance(presentation.rightForearm.position,presentation.rightHand.position)).ToString("F3"));
                return prefab.GetComponent<AthletePresentation>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void ConfigureImporter(string path,bool animations)
        {
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(importer==null) { AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport); importer=AssetImporter.GetAtPath(path) as ModelImporter; }
            if(importer==null) throw new InvalidOperationException("FBX importer unavailable: "+path);
            bool changed=importer.animationType!=ModelImporterAnimationType.Generic || importer.importAnimation!=animations ||
                importer.optimizeGameObjects || !importer.isReadable;
            importer.animationType=ModelImporterAnimationType.Generic;
            importer.importAnimation=animations;
            importer.optimizeGameObjects=false;
            importer.isReadable=true;
            if(changed) importer.SaveAndReimport();
        }

        private static AnimationClip CopyClip(string sourceName,string targetName)
        {
            AnimationClip source=null;
            foreach(UnityEngine.Object item in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                var clip=item as AnimationClip;
                if(clip!=null && !clip.name.StartsWith("__preview__") &&
                    (clip.name==sourceName || clip.name.EndsWith("|"+sourceName))) { source=clip; break; }
            }
            if(source==null) throw new InvalidOperationException("Imported locomotion clip missing: "+sourceName);
            string path=Destination+"/"+targetName+".anim";
            var result=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            AnimationClip copy=UnityEngine.Object.Instantiate(source);
            copy.name=targetName;
            copy.wrapMode=WrapMode.Loop;
            if(result==null) { AssetDatabase.CreateAsset(copy,path); result=copy; }
            else { EditorUtility.CopySerialized(copy,result); UnityEngine.Object.DestroyImmediate(copy); EditorUtility.SetDirty(result); }
            Debug.Log("[Athlete Build] Clip "+source.name+" -> "+targetName+" length="+result.length.ToString("F3")+
                "s curves="+AnimationUtility.GetCurveBindings(result).Length);
            return result;
        }

        private static Transform Find(Transform root,string name)
        { foreach(Transform child in root.GetComponentsInChildren<Transform>(true)) if(child.name==name) return child; return null; }

        private static Transform ChooseAnimationRoot(AthletePresentation athlete,AnimationClip clip)
        {
            EditorCurveBinding[] bindings=AnimationUtility.GetCurveBindings(clip);
            foreach(Transform candidate in new[]{athlete.characterRoot,athlete.armatureRoot})
            {
                if(candidate==null) continue;
                int resolved=0,total=0;
                foreach(var binding in bindings)
                {
                    if(binding.type!=typeof(Transform)) continue;
                    total++;
                    if(string.IsNullOrEmpty(binding.path) || candidate.Find(binding.path)!=null) resolved++;
                }
                Debug.Log("[Athlete Build] Animation binding root="+candidate.name+" resolved="+resolved+"/"+total);
                if(total>10 && resolved==total) return candidate;
            }
            throw new InvalidOperationException("Athlete animation binding paths do not match the imported character skeleton");
        }

        // First-import diagnostic entrypoint. It renders the real scene/prefab,
        // not an illustration, and never saves the temporary presentation poses.
        public static void VerifyAndRenderPreview()
        {
            AthletePresentation prefab=BuildPrefab();
            EditorSceneManager.OpenScene("Assets/Scenes/ShuttlePractice.unity");
            var session=UnityEngine.Object.FindObjectOfType<BadmintonSession>();
            session.athletePrefab=prefab;
            AthleteRegression.Run(session);
            RenderPreview();
        }

        public static void RenderPreview()
        {
            const string scene="Assets/Scenes/ShuttlePractice.unity";
            EditorSceneManager.OpenScene(scene);
            var session=UnityEngine.Object.FindObjectOfType<BadmintonSession>();
            session.athletePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<AthletePresentation>();
            Camera camera=session.playerCamera;
            TrackedPoseDriver driver=camera.GetComponent<TrackedPoseDriver>();
            if(driver!=null) driver.enabled=false;
            camera.transform.SetPositionAndRotation(new Vector3(0f,1.63f,0f),Quaternion.identity);
            if(!session.Recenter()) throw new InvalidOperationException("Athlete preview court could not initialize");
            AthletePresentation athlete=session.opponent.Presentation;
            if(athlete==null) throw new InvalidOperationException("Athlete preview did not instantiate the actual character prefab");
            Transform racket=session.opponent.transform.Find("Visible AI player/AI racket contact head");
            if(racket==null) throw new InvalidOperationException("Athlete preview actual physical racket transform missing");
            foreach(TextMesh label in UnityEngine.Object.FindObjectsOfType<TextMesh>()) label.GetComponent<Renderer>().enabled=false;
            const int width=700,height=1000;
            RenderTexture render=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            Texture2D image=new Texture2D(width,height,TextureFormat.RGB24,false);
            Texture2D sheet=new Texture2D(width*3,height,TextureFormat.RGB24,false);
            RenderTexture previous=RenderTexture.active;
            SkinnedMeshRenderer[] skins=athlete.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var poseObjects=new GameObject[skins.Length];
            var poseMeshes=new Mesh[skins.Length];
            try
            {
                // Several explicit Camera.Render calls in the same editor frame
                // may reuse Unity's previous GPU skinning result. Skin the real
                // sampled bones into world-space render-only snapshots. This also
                // avoids BakeMesh's scale conventions with this FBX's 100x local
                // renderer transforms. Runtime retains normal SkinnedMeshRenderers.
                for(int part=0;part<skins.Length;part++)
                {
                    poseObjects[part]=new GameObject("Editor capture - actual baked "+skins[part].name);
                    poseMeshes[part]=UnityEngine.Object.Instantiate(skins[part].sharedMesh);
                    poseMeshes[part].name="Actual sampled athlete pose in world coordinates";
                    poseObjects[part].AddComponent<MeshFilter>().sharedMesh=poseMeshes[part];
                    MeshRenderer renderer=poseObjects[part].AddComponent<MeshRenderer>();
                    renderer.sharedMaterials=skins[part].sharedMaterials;
                    renderer.shadowCastingMode=skins[part].shadowCastingMode;
                    renderer.receiveShadows=skins[part].receiveShadows;
                    skins[part].enabled=false;
                }
                Vector3 feet=session.court.TransformPoint(session.opponent.FeetLocal);
                camera.orthographic=false;
                camera.fieldOfView=40f;
                camera.nearClipPlane=.03f;
                Vector3 focus=feet+Vector3.up*.95f;
                Vector3 view=feet+session.court.TransformDirection(new Vector3(1.0f,1.20f,-3.35f));
                camera.transform.SetPositionAndRotation(view,Quaternion.LookRotation(focus-view,Vector3.up));
                camera.targetTexture=render;
                for(int pose=0;pose<3;pose++)
                {
                    session.opponent.ResetRally();
                    athlete.ResetPose();
                    Vector3 velocity=pose==1?new Vector3(2.7f,0f,0f):Vector3.zero;
                    if(pose==2)
                    {
                        Vector3 grip=athlete.rightUpperArm.position+session.court.TransformDirection(new Vector3(-.12f,.20f,-.30f));
                        racket.rotation=session.court.rotation*Quaternion.Euler(-20f,180f,20f);
                        racket.position=grip-racket.rotation*(RacketSpecification.GripCenter-RacketSpecification.HeadCenter);
                    }
                    int frames=pose==0?1:pose==1?26:18;
                    for(int frame=0;frame<frames;frame++)
                        athlete.SamplePose(1f/90f,velocity,session.playerCamera.transform.position,racket,pose==2?1f:0f);
                    for(int part=0;part<skins.Length;part++)
                    {
                        BakeWorldSnapshot(skins[part],poseMeshes[part]);
                    }
                    Bounds poseBounds=poseMeshes[0].bounds;
                    for(int part=1;part<poseMeshes.Length;part++) poseBounds.Encapsulate(poseMeshes[part].bounds);
                    if(poseBounds.size.y<1f || poseBounds.size.y>2.5f || Vector3.Distance(poseBounds.center,feet)>2.5f)
                        throw new InvalidOperationException("Athlete preview world skin bounds invalid: "+poseBounds);
                    camera.Render();
                    RenderTexture.active=render;
                    image.ReadPixels(new Rect(0f,0f,width,height),0,0);
                    image.Apply();
                    sheet.SetPixels(pose*width,0,width,height,image.GetPixels());
                    Debug.Log("[Athlete Preview] "+(pose==0?"IDLE":pose==1?"RUN":"SWING")+
                        " shoulderLocal="+session.court.InverseTransformPoint(athlete.rightUpperArm.position).ToString("F3")+
                        " gripLocal="+session.court.InverseTransformPoint(athlete.GripTarget).ToString("F3")+
                        " wristGap="+Vector3.Distance(athlete.rightHand.position,athlete.GripTarget).ToString("F3")+
                        " skinWorldBounds="+poseBounds.size.ToString("F3"));
                }
                sheet.Apply();
                File.WriteAllBytes(Path.GetFullPath("AthletePreview.png"),sheet.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=null;
                RenderTexture.active=previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(sheet);
                UnityEngine.Object.DestroyImmediate(render);
                for(int part=0;part<skins.Length;part++)
                {
                    if(skins[part]!=null) skins[part].enabled=true;
                    if(poseObjects[part]!=null) UnityEngine.Object.DestroyImmediate(poseObjects[part]);
                    if(poseMeshes[part]!=null) UnityEngine.Object.DestroyImmediate(poseMeshes[part]);
                }
                EditorSceneManager.OpenScene(scene);
            }
            Debug.Log("[Athlete Preview] Saved actual sampled skinned model (editor CPU skin snapshot): idle / run / swing.");
        }

        private static void BakeWorldSnapshot(SkinnedMeshRenderer skin,Mesh result)
        {
            Mesh source=skin.sharedMesh;
            Vector3[] vertices=source.vertices,normals=source.normals;
            BoneWeight[] weights=source.boneWeights;
            Matrix4x4[] bindPoses=source.bindposes;
            Transform[] bones=skin.bones;
            if(weights.Length!=vertices.Length || bones.Length!=bindPoses.Length)
                throw new InvalidOperationException("Athlete preview skin data is incomplete: "+skin.name);
            var matrices=new Matrix4x4[bones.Length];
            for(int bone=0;bone<bones.Length;bone++)
            {
                if(bones[bone]==null) throw new InvalidOperationException("Athlete preview bone is missing: "+skin.name);
                matrices[bone]=bones[bone].localToWorldMatrix*bindPoses[bone];
            }
            // Standard four-weight linear blend skinning with no extra renderer
            // transform: bone matrices already produce world-space geometry.
            for(int vertex=0;vertex<vertices.Length;vertex++)
            {
                Vector3 position=vertices[vertex];
                Vector3 normal=normals.Length==vertices.Length?normals[vertex]:Vector3.up;
                BoneWeight weight=weights[vertex];
                vertices[vertex]=matrices[weight.boneIndex0].MultiplyPoint3x4(position)*weight.weight0+
                    matrices[weight.boneIndex1].MultiplyPoint3x4(position)*weight.weight1+
                    matrices[weight.boneIndex2].MultiplyPoint3x4(position)*weight.weight2+
                    matrices[weight.boneIndex3].MultiplyPoint3x4(position)*weight.weight3;
                if(normals.Length==vertices.Length)
                    normals[vertex]=(matrices[weight.boneIndex0].MultiplyVector(normal)*weight.weight0+
                        matrices[weight.boneIndex1].MultiplyVector(normal)*weight.weight1+
                        matrices[weight.boneIndex2].MultiplyVector(normal)*weight.weight2+
                        matrices[weight.boneIndex3].MultiplyVector(normal)*weight.weight3).normalized;
            }
            result.vertices=vertices;
            if(normals.Length==vertices.Length) result.normals=normals;
            else result.RecalculateNormals();
            result.RecalculateBounds();
        }

        private static Bounds BoundsOf(SkinnedMeshRenderer[] skins)
        { Bounds result=skins[0].bounds; for(int i=1;i<skins.Length;i++) result.Encapsulate(skins[i].bounds); return result; }

        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            int slash=path.LastIndexOf('/');
            string parent=path.Substring(0,slash);
            if(!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent,path.Substring(slash+1));
        }
    }
}
