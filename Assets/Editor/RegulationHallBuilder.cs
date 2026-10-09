using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    // Procedural training hall, not a claim of venue certification. All play markings
    // and net reference dimensions come from BadmintonCourt; the hall is art direction.
    public static class RegulationHallBuilder
    {
        public const float HallClearHeight = 10f;
        public const string RootName = "Regulation court and indoor hall";
        public const string NetMeshName = "Fine net cord mesh - batched";
        public const string TapeMeshName = "Folded white net tape";
        private const string MeshFolder = "Assets/GeneratedHall";
        private static Material wall, upperWall, green, wood, woodLight, white, steel, dark, light, blue;

        public static void BuildAll(Transform court)
        {
            Directory.CreateDirectory("Assets/Materials");
            Directory.CreateDirectory(MeshFolder);
            AssetDatabase.Refresh();
            wall = Material("Acoustic wall", new Color(.11f,.19f,.23f));
            upperWall = Material("Upper wall", new Color(.30f,.35f,.37f));
            green = Material("Sports mat", new Color(.047f,.32f,.235f));
            wood = Material("Maple floor", new Color(.49f,.31f,.16f));
            woodLight = Material("Maple light", new Color(.57f,.38f,.21f));
            white = Material("Court white", new Color(.94f,.94f,.88f));
            steel = Material("Brushed steel", new Color(.21f,.24f,.26f));
            dark = Material("Net fine cord", new Color(.028f,.031f,.034f), true);
            light = Material("Ceiling diffuser", new Color(.83f,.89f,.92f), true);
            blue = Material("Net post blue", new Color(.035f,.19f,.30f));
            Transform previous = court.Find(RootName);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            Transform root = Child(RootName, court);
            BuildCourt(root);
            BuildNet(root);
            BuildHall(root);
            AssetDatabase.SaveAssets();
        }

        private static void BuildCourt(Transform root)
        {
            Cube("Sports hall foundation", new Vector3(0f,-.105f,BadmintonCourt.NetZ), new Vector3(16f,.18f,22f), wood, root);
            // Two batched plank meshes, rather than hundreds of separate renderers.
            var planksA = new MeshDraft(); var planksB = new MeshDraft();
            for (int row = 0; row < 44; row++)
            for (int col = 0; col < 8; col++)
            {
                float x = -8f + col*2f;
                float z = BadmintonCourt.NetZ-11f+row*.5f;
                (row%3 == col%3 ? planksA : planksB).HorizontalRect(x+.005f,z+.005f,x+1.995f,z+.495f,-.012f);
            }
            MeshObject("Maple plank pattern A", planksA, wood, root, "MapleA");
            MeshObject("Maple plank pattern B", planksB, woodLight, root, "MapleB");
            Cube("Green badminton mat", new Vector3(0f,-.006f,BadmintonCourt.NetZ), new Vector3(7.2f,.012f,14.7f), green, root);
            var markings = new MeshDraft();
            foreach (BadmintonCourt.Marking line in BadmintonCourt.Markings())
                markings.HorizontalRect(line.centre.x-line.size.x*.5f,line.centre.y-line.size.y*.5f,
                    line.centre.x+line.size.x*.5f,line.centre.y+line.size.y*.5f,.0015f);
            MeshObject("BWF 40 mm court markings", markings, white, root, "CourtMarkings");
            Text("Court identification", "COURT 01", new Vector3(-4.4f,.006f,2f), .025f, white.color, root,
                Quaternion.Euler(90f,0f,90f));
            // A visual standing reminder, not a substitute for the SteamVR boundary.
            var stance = new MeshDraft();
            const int steps = 64;
            for (int i=0;i<steps;i++)
            {
                float a=i*Mathf.PI*2f/steps, b=(i+1)*Mathf.PI*2f/steps;
                stance.Quad(new Vector3(Mathf.Cos(a)*.495f,.003f,Mathf.Sin(a)*.495f),
                    new Vector3(Mathf.Cos(a)*.5f,.003f,Mathf.Sin(a)*.5f),
                    new Vector3(Mathf.Cos(b)*.5f,.003f,Mathf.Sin(b)*.5f),
                    new Vector3(Mathf.Cos(b)*.495f,.003f,Mathf.Sin(b)*.495f), true);
            }
            MeshObject("One metre stance reminder - not court marking",stance,woodLight,root,"StanceRing");
        }

        private static void BuildNet(Transform root)
        {
            float z=BadmintonCourt.NetZ, half=BadmintonCourt.DoublesHalfWidth;
            foreach (float side in new[] {-1f,1f})
            {
                float x=side*half;
                Cube("Net post "+(side<0f?"left":"right"),new Vector3(x,.775f,z),new Vector3(.04f,1.55f,.04f),blue,root);
                // Support stays outside the inner edge of the doubles sideline.
                Cube("Outboard weighted post base",new Vector3(side*(half+.17f),.045f,z),new Vector3(.38f,.09f,.5f),steel,root);
                Cube("Post cap",new Vector3(x,1.5475f,z),new Vector3(.044f,.005f,.044f),white,root);
            }
            var cords=new MeshDraft();
            float r=BadmintonCourt.NetCordDiameter*.5f;
            for (int column=0;column<=BadmintonCourt.NetMeshColumns;column++)
            {
                float x=Mathf.Lerp(-half,half,column/(float)BadmintonCourt.NetMeshColumns);
                float top=BadmintonCourt.NetHeightAt(x);
                cords.VerticalRibbon(new Vector3(x,top-BadmintonCourt.NetDepth,z),new Vector3(x,top,z),r);
            }
            const int curveSegments=64;
            for (int row=0;row<=BadmintonCourt.NetMeshRows;row++)
            for (int i=0;i<curveSegments;i++)
            {
                float x0=Mathf.Lerp(-half,half,i/(float)curveSegments);
                float x1=Mathf.Lerp(-half,half,(i+1)/(float)curveSegments);
                float depth=BadmintonCourt.NetDepth*row/BadmintonCourt.NetMeshRows;
                cords.HorizontalRibbon(new Vector3(x0,BadmintonCourt.NetHeightAt(x0)-depth,z),
                    new Vector3(x1,BadmintonCourt.NetHeightAt(x1)-depth,z),r);
            }
            // Edge cords and binding are contained within the specified panel,
            // rather than adding their radius below the nominal 760 mm depth.
            cords.ClipToNetPanel();
            MeshObject(NetMeshName,cords,dark,root,"NetCordMesh");
            var tape=new MeshDraft(); var bottom=new MeshDraft();
            for (int i=0;i<curveSegments;i++)
            {
                float x0=Mathf.Lerp(-half,half,i/(float)curveSegments), x1=Mathf.Lerp(-half,half,(i+1)/(float)curveSegments);
                float y0=BadmintonCourt.NetHeightAt(x0), y1=BadmintonCourt.NetHeightAt(x1);
                // 75 mm strip folded over the cable: 37.5 mm on each visible side.
                tape.VerticalBand(x0,x1,y0,y1,BadmintonCourt.NetTapeFaceDepth,z,.002f);
                bottom.VerticalBand(x0,x1,y0-BadmintonCourt.NetDepth+.008f,y1-BadmintonCourt.NetDepth+.008f,.008f,z,.0012f);
            }
            MeshObject(TapeMeshName,tape,white,root,"NetTape");
            MeshObject("Dark bottom net binding",bottom,dark,root,"NetBottomBinding");
        }

        private static void BuildHall(Transform root)
        {
            float zc=BadmintonCourt.NetZ, rear=zc+11f, front=zc-11f;
            Cube("Far acoustic wall",new Vector3(0f,2f,rear),new Vector3(16.2f,4f,.18f),wall,root);
            Cube("Near acoustic wall",new Vector3(0f,2f,front),new Vector3(16.2f,4f,.18f),wall,root);
            Cube("Left acoustic wall",new Vector3(-8f,2f,zc),new Vector3(.18f,4f,22f),wall,root);
            Cube("Right acoustic wall",new Vector3(8f,2f,zc),new Vector3(.18f,4f,22f),wall,root);
            Cube("Far upper wall",new Vector3(0f,7.3f,rear),new Vector3(16.2f,6.6f,.18f),upperWall,root);
            Cube("Near upper wall",new Vector3(0f,7.3f,front),new Vector3(16.2f,6.6f,.18f),upperWall,root);
            foreach(float x in new[] {-8f,8f})
            {
                Cube("Upper side wall",new Vector3(x,7.3f,zc),new Vector3(.18f,6.6f,22f),upperWall,root);
                Cube("Padded perimeter rail",new Vector3(x*.978f,.9f,zc),new Vector3(.10f,.12f,21.7f),blue,root);
                for(int i=0;i<8;i++)
                    Cube("Acoustic panel seam",new Vector3(x*.987f,2f,front+.5f+i*3f),new Vector3(.035f,3.6f,.025f),steel,root);
            }
            Cube("Roof underside",new Vector3(0f,10.65f,zc),new Vector3(16.2f,.10f,22f),upperWall,root);
            for(int i=0;i<6;i++)
            {
                float z=front+1.1f+i*3.95f;
                Cube("Roof transverse steel",new Vector3(0f,10.3f,z),new Vector3(16f,.4f,.12f),steel,root);
                foreach(float x in new[] {-6.8f,6.8f})
                    Cube("Side structural column",new Vector3(x,5.1f,z),new Vector3(.16f,10.2f,.16f),steel,root);
            }
            for(int i=0;i<5;i++)
            foreach(float x in new[] {-4.1f,4.1f})
            {
                float z=front+2.1f+i*4.45f;
                Cube("Suspended light housing",new Vector3(x,10.10f,z),new Vector3(.52f,.16f,1.85f),steel,root);
                Cube("Suspended light diffuser",new Vector3(x,10.015f,z),new Vector3(.45f,.012f,1.74f),light,root);
            }
            // Broad, shadow-free fills are inexpensive; no separate real-time light per fixture.
            foreach(float x in new[] {-4.3f,4.3f})
            {
                Light fill=Child("Hall soft fill",root).gameObject.AddComponent<Light>();
                fill.type=LightType.Point; fill.transform.localPosition=new Vector3(x,6.5f,zc);
                fill.range=17f; fill.intensity=.65f; fill.color=new Color(.83f,.9f,1f);
                fill.shadows=LightShadows.None; fill.renderMode=LightRenderMode.ForceVertex;
            }
            foreach(float side in new[] {-1f,1f})
            {
                float x=side*6f;
                Cube("Timber bench seat",new Vector3(x,.44f,7f),new Vector3(.56f,.08f,3.1f),woodLight,root);
                Cube("Timber bench back",new Vector3(x+side*.26f,.75f,7f),new Vector3(.055f,.42f,3.1f),woodLight,root);
                foreach(float z in new[]{5.8f,8.2f})
                    Cube("Bench steel leg",new Vector3(x,.22f,z),new Vector3(.45f,.40f,.07f),steel,root);
                Cube("Court bag",new Vector3(x,.58f,7.8f),new Vector3(.37f,.22f,.78f),blue,root);
                Cube("Folded towel",new Vector3(x,.51f,6f),new Vector3(.35f,.055f,.46f),white,root);
            }
            Cube("Equipment storage cabinet",new Vector3(6f,1.1f,12.8f),new Vector3(1.8f,2.2f,.6f),steel,root);
            foreach(float x in new[]{5.55f,6.45f})
            {
                Cube("Cabinet door",new Vector3(x,1.1f,12.48f),new Vector3(.87f,2.08f,.025f),upperWall,root);
                Cube("Cabinet pull",new Vector3(x+.3f,1.05f,12.44f),new Vector3(.025f,.17f,.025f),dark,root);
            }
            Cube("Exit doorway",new Vector3(-5.8f,1.1f,rear-.11f),new Vector3(1.1f,2.2f,.06f),steel,root);
            Cube("Door vision panel",new Vector3(-5.8f,1.52f,rear-.15f),new Vector3(.54f,.55f,.025f),upperWall,root);
            Text("Hall title","SHUTTLE TRAINING HALL",new Vector3(0f,5.75f,rear-.13f),.051f,white.color,root,Quaternion.identity);
            Text("Hall subtitle","13.40 x 6.10 m  /  SINGLES 5.18 m",new Vector3(0f,5.15f,rear-.14f),.024f,new Color(.60f,.74f,.76f),root,Quaternion.identity);
            Text("Safety reminder","STAY WITHIN YOUR REAL PLAY SPACE\nVIRTUAL COURT SIZE IS NOT ROOM SIZE",new Vector3(-5.2f,2.55f,10.5f),.0125f,white.color,root,Quaternion.Euler(0f,-15f,0f));
        }

        private static Transform Child(string name,Transform parent)
        {
            Transform child=new GameObject(name).transform; child.SetParent(parent,false); return child;
        }

        private static void Cube(string name,Vector3 position,Vector3 size,Material material,Transform parent)
        {
            GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name;
            go.transform.SetParent(parent,false); go.transform.localPosition=position; go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=material; Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        private static void Text(string name,string text,Vector3 position,float size,Color color,Transform parent,Quaternion rotation)
        {
            TextMesh label=Child(name,parent).gameObject.AddComponent<TextMesh>();
            label.transform.localPosition=position; label.transform.localRotation=rotation;
            label.text=text; label.characterSize=size; label.fontSize=96;
            label.anchor=TextAnchor.MiddleCenter; label.alignment=TextAlignment.Center; label.color=color;
        }

        private static Material Material(string name,Color color,bool unlit=false)
        {
            string path="Assets/Materials/Hall - "+name+".mat";
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) { material=new Material(Shader.Find(unlit?"Unlit/Color":"Standard")); AssetDatabase.CreateAsset(material,path); }
            material.color=color;
            if(material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness",.08f);
            EditorUtility.SetDirty(material); return material;
        }

        private static void MeshObject(string name,MeshDraft draft,Material material,Transform parent,string assetName)
        {
            string path=MeshFolder+"/"+assetName+".asset";
            Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null) { mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
            mesh.Clear(); mesh.name=assetName;
            mesh.SetVertices(draft.vertices); mesh.SetTriangles(draft.indices,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            GameObject go=Child(name,parent).gameObject;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            MeshRenderer renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=true;
            // Court can be recentered at runtime: do not static-batch it into world space.
        }

        private sealed class MeshDraft
        {
            public readonly List<Vector3> vertices=new List<Vector3>();
            public readonly List<int> indices=new List<int>();
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,bool bothSides=false)
            {
                int n=vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                indices.Add(n);indices.Add(n+1);indices.Add(n+2);indices.Add(n);indices.Add(n+2);indices.Add(n+3);
                if(bothSides)
                {
                    // Separate rear vertices keep correct normals with lit tape materials.
                    n=vertices.Count; vertices.Add(d);vertices.Add(c);vertices.Add(b);vertices.Add(a);
                    indices.Add(n);indices.Add(n+1);indices.Add(n+2);indices.Add(n);indices.Add(n+2);indices.Add(n+3);
                }
            }
            public void HorizontalRect(float x0,float z0,float x1,float z1,float y)
            { Quad(new Vector3(x0,y,z0),new Vector3(x0,y,z1),new Vector3(x1,y,z1),new Vector3(x1,y,z0)); }
            public void VerticalRibbon(Vector3 a,Vector3 b,float radius)
            { Quad(a-Vector3.right*radius,a+Vector3.right*radius,b+Vector3.right*radius,b-Vector3.right*radius,true); }
            public void HorizontalRibbon(Vector3 a,Vector3 b,float radius)
            { Quad(a-Vector3.up*radius,b-Vector3.up*radius,b+Vector3.up*radius,a+Vector3.up*radius,true); }
            public void VerticalBand(float x0,float x1,float y0,float y1,float depth,float z,float halfThickness)
            {
                Quad(new Vector3(x0,y0-depth,z-halfThickness),new Vector3(x1,y1-depth,z-halfThickness),new Vector3(x1,y1,z-halfThickness),new Vector3(x0,y0,z-halfThickness),true);
                Quad(new Vector3(x0,y0-depth,z+halfThickness),new Vector3(x1,y1-depth,z+halfThickness),new Vector3(x1,y1,z+halfThickness),new Vector3(x0,y0,z+halfThickness),true);
                Quad(new Vector3(x0,y0,z-halfThickness),new Vector3(x1,y1,z-halfThickness),new Vector3(x1,y1,z+halfThickness),new Vector3(x0,y0,z+halfThickness),true);
            }
            public void ClipToNetPanel()
            {
                for(int i=0;i<vertices.Count;i++)
                {
                    Vector3 point=vertices[i];
                    point.x=Mathf.Clamp(point.x,-BadmintonCourt.DoublesHalfWidth,BadmintonCourt.DoublesHalfWidth);
                    float top=BadmintonCourt.NetHeightAt(point.x);
                    point.y=Mathf.Clamp(point.y,top-BadmintonCourt.NetDepth,top);
                    vertices[i]=point;
                }
            }
        }
    }
}
