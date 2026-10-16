using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SpatialTracking;
using Object=UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    // Render the saved Unity scene's actual hand, racket and physics shuttle.
    // The marker pair is a diagnostic annotation, not a controller mock-up.
    public static class HandServePreview
    {
        [MenuItem("Tools/Vive Shuttle VR/Render Hand Serve Preview")]
        public static void RenderPreview()
        {
            Texture2D closed=null,opened=null,combined=null;
            Material markerMaterial=null;
            try
            {
                EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
                BadmintonSession session=Object.FindObjectOfType<BadmintonSession>();
                if(session==null || session.shuttleHand==null) throw new InvalidOperationException("Saved scene has no free holding hand");
                ShuttleHand hand=session.shuttleHand;
                RacketController right=Array.Find(session.rackets,item=>item.CanStrike);
                foreach(Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
                    if(renderer.gameObject.scene==session.gameObject.scene) renderer.enabled=false;
                hand.source.transform.SetPositionAndRotation(new Vector3(-.26f,.59f,0f),Quaternion.LookRotation(Vector3.right,Vector3.forward));
                hand.UpdateVisuals(1f);
                Transform leftVisual=hand.source.transform.Find("Left holding hand");
                leftVisual.gameObject.SetActive(true);
                foreach(Renderer renderer in leftVisual.GetComponentsInChildren<Renderer>(true)) renderer.enabled=true;
                right.transform.SetPositionAndRotation(new Vector3(.14f,.64f,.01f),Quaternion.LookRotation(Vector3.down,Vector3.right));
                right.ApplyGripAlignment();
                right.visual.SetActive(true);
                foreach(Renderer renderer in right.visual.GetComponentsInChildren<Renderer>(true)) renderer.enabled=true;
                session.shuttle.Stop();
                session.shuttle.HoldAt(hand.PinchPoint.position-Vector3.up*.055f);
                foreach(Renderer renderer in session.shuttle.GetComponentsInChildren<Renderer>(true)) renderer.enabled=true;
                if(session.shuttle.trail!=null) session.shuttle.trail.enabled=false;
                typeof(BadmintonSession).GetProperty("Running").SetValue(session,true,null);

                Camera camera=session.playerCamera;
                TrackedPoseDriver driver=camera.GetComponent<TrackedPoseDriver>();
                if(driver!=null) driver.enabled=false;
                camera.transform.SetPositionAndRotation(new Vector3(-.05f,.45f,-1.8f),Quaternion.identity);
                camera.orthographic=true;
                camera.orthographicSize=.43f;
                camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.045f,.068f,.087f);
                camera.nearClipPlane=.02f;
                camera.farClipPlane=4f;
                Light key=new GameObject("Hand preview key light").AddComponent<Light>();
                key.type=LightType.Directional;
                key.intensity=1.4f;
                key.transform.rotation=Quaternion.Euler(25f,-25f,0f);
                markerMaterial=new Material(Shader.Find("Unlit/Color"));
                markerMaterial.color=new Color(.3f,1f,.85f);
                Vector3 markerOffset=Vector3.back*.025f;
                Vector3 tracked=right.transform.position+markerOffset;
                Vector3 mounted=right.transform.TransformPoint(right.GripAnchorLocal)+markerOffset;
                Marker("Tracked origin",tracked,markerMaterial);
                Marker("Lower grip mount",mounted,markerMaterial);
                LineRenderer shift=new GameObject("8 cm lower-grip diagnostic").AddComponent<LineRenderer>();
                shift.sharedMaterial=markerMaterial;
                shift.positionCount=2;
                shift.SetPositions(new[]{tracked,mounted});
                shift.startWidth=shift.endWidth=.0015f;
                TextMesh title=Label(camera,"PINCH / HELD",new Vector3(0f,.392f,1f),.0075f);
                Label(camera,"ACTUAL GAME GEOMETRY\nRIGHT GRIP: -8 CM",new Vector3(0f,-.385f,1f),.0032f);
                closed=Capture(camera);

                hand.UpdateVisuals(0f);
                if(!session.shuttle.ReleaseHeld(Vector3.zero)) throw new InvalidOperationException("Preview free release failed");
                session.shuttle.AdvanceFrame(.08f);
                session.shuttle.AdvanceFrame(.08f);
                title.text="OPEN / FREE DROP";
                opened=Capture(camera);
                combined=new Texture2D(2000,1100,TextureFormat.RGB24,false);
                combined.SetPixels(0,0,1000,1100,opened.GetPixels());
                combined.SetPixels(1000,0,1000,1100,closed.GetPixels());
                combined.Apply();
                string path=Path.GetFullPath("HandServePreview.png");
                File.WriteAllBytes(path,combined.EncodeToPNG());
                Debug.Log("[Hand Preview] Rendered saved-scene open/drop and pinch/held geometry: "+path);
            }
            finally
            {
                if(closed!=null) Object.DestroyImmediate(closed);
                if(opened!=null) Object.DestroyImmediate(opened);
                if(combined!=null) Object.DestroyImmediate(combined);
                EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
                if(markerMaterial!=null) Object.DestroyImmediate(markerMaterial);
            }
        }

        private static Texture2D Capture(Camera camera)
        {
            var render=new RenderTexture(1000,1100,24);
            RenderTexture previous=RenderTexture.active;
            try
            {
                camera.targetTexture=render;
                camera.Render();
                RenderTexture.active=render;
                var pixels=new Texture2D(1000,1100,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,1000,1100),0,0);
                pixels.Apply();
                return pixels;
            }
            finally
            {
                camera.targetTexture=null;
                RenderTexture.active=previous;
                render.Release();
                Object.DestroyImmediate(render);
            }
        }
        private static void Marker(string label,Vector3 position,Material material)
        {
            GameObject marker=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name=label;
            marker.transform.position=position;
            marker.transform.localScale=Vector3.one*.007f;
            marker.GetComponent<Renderer>().sharedMaterial=material;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
        }
        private static TextMesh Label(Camera camera,string text,Vector3 offset,float size)
        {
            TextMesh label=new GameObject(text).AddComponent<TextMesh>();
            label.transform.SetPositionAndRotation(camera.transform.TransformPoint(offset),camera.transform.rotation);
            label.font=Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.GetComponent<Renderer>().sharedMaterial=label.font.material;
            label.text=text;
            label.fontSize=72;
            label.characterSize=size;
            label.anchor=TextAnchor.MiddleCenter;
            label.alignment=TextAlignment.Center;
            label.color=new Color(.88f,.94f,.97f);
            return label;
        }
    }
}
