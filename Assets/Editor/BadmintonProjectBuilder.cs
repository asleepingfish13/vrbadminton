using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using Object = UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    public static class BadmintonProjectBuilder
    {
        public const string ScenePath = "Assets/Scenes/ShuttlePractice.unity";
        private static Material navy, floor, courtGreen, courtDark, white, mint, coral, gold, black, net;

        [MenuItem("Tools/Vive Shuttle VR/Build Practice Scene")]
        public static void BuildScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Materials");
            AssetDatabase.Refresh();
            PlayerSettings.companyName = "Local VR Lab";
            PlayerSettings.productName = "Vive Shuttle VR";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 4;
            ValidateXR();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            navy = Material("Midnight", new Color(0.025f, 0.047f, 0.067f));
            floor = Material("Surround", new Color(0.07f, 0.10f, 0.12f));
            courtGreen = Material("Court Teal", new Color(0.035f, 0.30f, 0.27f));
            courtDark = Material("Court Deep", new Color(0.025f, 0.23f, 0.22f));
            white = Material("Chalk", new Color(0.9f, 0.95f, 0.9f), true);
            mint = Material("Mint", new Color(0.25f, 1f, 0.78f), true);
            coral = Material("Coral", new Color(1f, 0.31f, 0.19f), true);
            gold = Material("Gold", new Color(1f, 0.74f, 0.19f), true);
            black = Material("Grip", new Color(0.022f, 0.03f, 0.04f));
            net = Material("Net Threads", new Color(0.23f, 0.35f, 0.37f), true);

            Transform court = new GameObject("Practice Court - Recenterable").transform;
            RegulationHallBuilder.BuildAll(court);
            BuildFeeder(court);

            BadmintonSession session = new GameObject("Badminton Session").AddComponent<BadmintonSession>();
            session.court = court;
            session.audioSource = session.gameObject.AddComponent<AudioSource>();
            session.audioSource.playOnAwake = false;
            session.audioSource.spatialBlend = 0f;
            session.scoreText = Text("Score", "SCORE 000    HIT 0/0    STREAK 0", new Vector3(0f, 4.12f, 12.87f), 0.060f, mint.color, court);
            session.statusText = Text("Status", "AI EASY / FIRST TO 7", new Vector3(0f, 3.53f, 12.86f), 0.045f, Color.white, court);
            session.detailText = Text("Controls", "TRIGGER: START    TAP MENU: PAUSE    HOLD MENU: RESET", new Vector3(0f, 2.88f, 12.85f), 0.029f, new Color(0.68f, 0.8f, 0.83f), court);
            Cube("Scoreboard", new Vector3(0f, 3.55f, 13f), new Vector3(10.4f, 2.8f, 0.08f), navy, court);
            Cube("Scoreboard accent", new Vector3(0f, 4.98f, 12.95f), new Vector3(10.4f, 0.025f, 0.05f), mint, court);
            Transform quickGuide = new GameObject("Nearby controls card").transform;
            quickGuide.SetParent(court,false);
            quickGuide.localPosition = new Vector3(-1.25f,1.35f,2.2f);
            quickGuide.localRotation = Quaternion.Euler(0f,-25f,0f);
            Cube("Card backing",Vector3.zero,new Vector3(.95f,.56f,.025f),navy,quickGuide);
            Text("Quick guide","TRIGGER / START\nTAP MENU / PAUSE\nHOLD MENU / RESET",new Vector3(0f,0f,-.025f),.011f,white.color,quickGuide);

            session.reachMarker = Ring("Reach ring - shuttle arrives here", new Vector3(0.22f, 1.07f, 0.62f), 0.11f, 0.11f, 0.0025f, mint, court, false);
            session.landingMarker = Ring("Last landing", new Vector3(0f, 0.035f, 6f), 0.19f, 0.19f, 0.025f, coral, court, true);
            session.targetMarker = Ring("Gold target", new Vector3(0f, 0.03f, 7f), 0.85f, 0.85f, 0.028f, gold, court, true);
            Ring("Target inner", Vector3.zero, 0.22f, 0.22f, 0.016f, gold, session.targetMarker, true);
            session.landingMarker.gameObject.SetActive(false);
            CreateRig(session);
            CreateShuttle(session);
            Light key = new GameObject("Soft daylight").AddComponent<Light>();
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(50f, -25f, 0f);
            key.intensity = 1.15f;
            key.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.52f, 0.58f);
            RenderSettings.fog = false;
            RenderSettings.fogColor = navy.color;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 16f;
            RenderSettings.fogEndDistance = 36f;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Shuttle Build] Practice scene created: " + ScenePath);
        }

        private static void BuildFeeder(Transform root)
        {
            Cube("Feeder base", new Vector3(0f,.14f,8.9f), new Vector3(.7f,.28f,.55f), black, root);
            Cube("Feeder stem", new Vector3(0f,.74f,8.9f), new Vector3(.15f,1.2f,.15f), navy, root);
            Cube("Feeder body", new Vector3(0f,1.43f,8.9f), new Vector3(.65f,.48f,.5f), courtDark, root);
            Cube("Feeder mouth", new Vector3(0f,1.5f,8.63f), new Vector3(.32f,.15f,.05f), black, root);
            Cube("Feeder indicator", new Vector3(0f,1.68f,8.63f), new Vector3(.42f,.025f,.03f), mint, root);
            Text("Feeder label", "AUTO FEED", new Vector3(0f,1.3f,8.62f), .008f, white.color, root);
        }

        private static void CreateRig(BadmintonSession session)
        {
            Transform rig = new GameObject("XR Origin").transform;
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(rig, false);
            cameraObject.transform.localPosition = new Vector3(0f,session.VirtualEyeHeight,0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = .04f;
            camera.farClipPlane = 50f;
            camera.fieldOfView = 72f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = navy.color;
            cameraObject.AddComponent<AudioListener>();
            TrackedPoseDriver driver = cameraObject.AddComponent<TrackedPoseDriver>();
            driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            driver.UseRelativeTransform = false;
            session.playerCamera = camera;
            cameraObject.AddComponent<DesktopPractice>().session = session;
            session.rackets = new[] { CreateRacket(rig, XRNode.LeftHand, mint, session), CreateRacket(rig, XRNode.RightHand, coral, session) };
        }

        private static RacketController CreateRacket(Transform rig, XRNode hand, Material color, BadmintonSession session)
        {
            Transform grip = new GameObject(hand == XRNode.LeftHand ? "Left Racket" : "Right Racket").transform;
            grip.SetParent(rig, false);
            grip.localPosition = new Vector3(hand == XRNode.LeftHand ? -.3f : .3f, .94f, .2f);
            Transform visual = new GameObject("Racket Visual").transform;
            visual.SetParent(grip, false);
            Transform head = RacketGeometry.Build(visual, black, color, white, black);
            PersistRacketMeshes(visual, hand.ToString());
            RacketController controller = grip.gameObject.AddComponent<RacketController>();
            controller.hand = hand;
            controller.head = head;
            controller.visual = visual.gameObject;
            controller.session = session;
            controller.ApplyGripAlignment();
            return controller;
        }

        private static void PersistRacketMeshes(Transform visual, string prefix)
        {
            Directory.CreateDirectory("Assets/Generated/Rackets");
            AssetDatabase.Refresh();
            int index = 0;
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>())
            {
                Mesh generated = filter.sharedMesh;
                if (generated == null || AssetDatabase.Contains(generated)) continue;
                string path = "Assets/Generated/Rackets/" + prefix + "-" + index++ + ".asset";
                Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved == null) AssetDatabase.CreateAsset(generated, path);
                else
                {
                    EditorUtility.CopySerialized(generated, saved);
                    Object.DestroyImmediate(generated);
                    EditorUtility.SetDirty(saved);
                    filter.sharedMesh = saved;
                }
            }
        }

        private static void CreateShuttle(BadmintonSession session)
        {
            GameObject shuttleObject = new GameObject("Shuttlecock");
            shuttleObject.transform.position = new Vector3(.22f,1.1f,.7f);
            ShuttleFlight shuttle = shuttleObject.AddComponent<ShuttleFlight>();
            shuttle.session = session;
            Transform visual = new GameObject("Feathers follow air flow").transform;
            visual.SetParent(shuttleObject.transform,false);
            shuttle.featherVisual = visual;
            GameObject cork = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cork.name = "Cork";
            cork.transform.SetParent(visual,false);
            cork.transform.localScale = Vector3.one * .037f;
            Object.DestroyImmediate(cork.GetComponent<Collider>());
            cork.GetComponent<Renderer>().sharedMaterial = white;
            Ring("Cork band",new Vector3(0f,.016f,0f),.018f,.018f,.006f,coral,visual,true);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12;
                Vector3 end = new Vector3(Mathf.Cos(angle)*.035f,.08f,Mathf.Sin(angle)*.035f);
                Line("Feather",new[] { new Vector3(end.x*.3f,.018f,end.z*.3f),end },.012f,white,visual);
            }
            Ring("Feather binding",new Vector3(0f,.054f,0f),.026f,.026f,.002f,white,visual,true);
            TrailRenderer trail = shuttleObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = mint;
            trail.startWidth = .012f;
            trail.endWidth = 0f;
            trail.time = .22f;
            trail.minVertexDistance = .015f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            shuttle.trail = trail;
            session.shuttle = shuttle;
        }

        private static Material Material(string name, Color color, bool unlit = false)
        {
            string path = "Assets/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(unlit ? "Unlit/Color" : "Standard"));
                AssetDatabase.CreateAsset(material,path);
            }
            material.color = color;
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .18f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject Cube(string name, Vector3 position, Vector3 size, Material material, Transform parent)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent,false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static LineRenderer Line(string name, Vector3[] points, float width, Material material, Transform parent)
        {
            LineRenderer line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent,false);
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.widthMultiplier = width;
            line.sharedMaterial = material;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static Transform Ring(string name, Vector3 position, float x, float y, float width, Material material, Transform parent, bool horizontal)
        {
            Vector3[] points = new Vector3[65];
            for (int i=0;i<points.Length;i++)
            {
                float a = i * Mathf.PI * 2f / (points.Length-1);
                points[i] = horizontal ? new Vector3(Mathf.Cos(a)*x,0f,Mathf.Sin(a)*y) : new Vector3(Mathf.Cos(a)*x,Mathf.Sin(a)*y,0f);
            }
            Transform ring = Line(name,points,width,material,parent).transform;
            ring.localPosition = position;
            return ring;
        }

        private static TextMesh Text(string name, string text, Vector3 position, float size, Color color, Transform parent)
        {
            TextMesh label = new GameObject(name).AddComponent<TextMesh>();
            label.transform.SetParent(parent,false);
            label.transform.localPosition = position;
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 80;
            label.characterSize = size;
            label.color = color;
            return label;
        }

        public static void ValidateXR()
        {
            XRGeneralSettings settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (settings == null || settings.Manager == null || settings.Manager.activeLoaders.Count != 1 ||
                !(settings.Manager.activeLoaders[0] is OpenXRLoader))
                throw new InvalidOperationException("The saved Standalone OpenXR loader is missing.");
            settings.InitManagerOnStart = true;
            EditorUtility.SetDirty(settings);
            var profile = OpenXRSettings.Instance.GetFeature<HTCViveControllerProfile>();
            if (profile == null || !profile.enabled) throw new InvalidOperationException("HTC Vive input profile is missing.");
            XRPlayModeSetup.EnsureEditorInput();
        }

        [MenuItem("Tools/Vive Shuttle VR/Open Practice _F8")]
        public static void OpenScene() { EditorSceneManager.OpenScene(ScenePath); }

        public static void BuildAndVerify()
        {
            BuildScene();
            BadmintonRegression.Run();
            RenderPreview();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Vive Shuttle VR/Render Preview")]
        public static void RenderPreview()
        {
            OpenScene();
            Camera camera = Camera.main;
            camera.GetComponent<TrackedPoseDriver>().enabled = false;
            camera.transform.position = new Vector3(0f,1.63f,0f);
            camera.transform.rotation = Quaternion.Euler(-3f,0f,0f);
            camera.fieldOfView = 72f;
            // Include runtime-created equipment/readout controls in visual QA.
            if (!Object.FindObjectOfType<BadmintonSession>().Recenter())
                throw new InvalidOperationException("Preview could not initialize practice HUD.");
            RenderTexture target = new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);
            Texture2D texture = new Texture2D(1600,900,TextureFormat.RGB24,false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0,0,1600,900),0,0);
                texture.Apply();
                File.WriteAllBytes(Path.GetFullPath("Preview.png"),texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
                OpenScene();
            }
            Debug.Log("[Shuttle Build] Preview rendered.");
        }

        [MenuItem("Tools/Vive Shuttle VR/Build Windows Player")]
        public static void BuildWindows()
        {
            ValidateXR();
            Directory.CreateDirectory("Build/Windows");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = "Build/Windows/ViveShuttleVR.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result);
            Debug.Log("[Shuttle Build] Windows player built successfully.");
        }

        public static void RebuildVerifyAndBuildWindows()
        {
            BuildScene();
            VerifyAndBuildWindows();
        }

        [MenuItem("Tools/Vive Shuttle VR/Verify and Build Windows")]
        public static void VerifyAndBuildWindows()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            BadmintonRegression.Run();
            RenderPreview();
            BuildWindows();
        }
    }
}
