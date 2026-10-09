using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ViveShuttleVR
{
    /// <summary>
    /// Photo-referenced Dark Navy (554) ASTROX 100 ZZ reconstruction, not official
    /// CAD. Dimensions and string layout remain the existing physics estimates.
    /// No collider: continuous swept cork/face contact owns the physics.
    /// In editor-built scenes, persist generated Mesh assets before saving the scene.
    /// </summary>
    public static class RacketGeometry
    {
        public static Transform Build(Transform parent, Material frame, Material accent, Material strings, Material grip)
        {
            // Existing AI/preview callers remain supported. The detailed player
            // model supplies independent white/orange/stencil materials below.
            return Build(parent,frame,accent,strings,grip,strings,accent,null);
        }

        public static Transform Build(Transform parent, Material frame, Material accent, Material strings,
            Material grip, Material whiteDecal, Material orange, Material stencil)
        {
            Transform head = new GameObject("Racket Head").transform;
            head.SetParent(parent,false);
            head.localPosition = RacketSpecification.HeadCenter;
            // Combine across the whole racket by material. This preserves actual
            // 3-D grommets/paint/grip detail with only six or seven renderers.
            MeshParts carbon=new MeshParts(), blue=new MeshParts(), pale=new MeshParts();
            MeshParts orangePaint=new MeshParts(), black=new MeshParts(), bed=new MeshParts(), red=new MeshParts();
            MeshParts rim = new MeshParts();
            const int rimSteps = 128;
            Vector3[] outline = new Vector3[rimSteps+1];
            for (int i=0;i<=rimSteps;i++)
                outline[i] = Outline(i*Mathf.PI*2f/rimSteps,RacketSpecification.FrameHalfWidth,RacketSpecification.FrameHalfHeight);
            // A flatter front/back section reads as an aero/box frame, not a
            // round rope. Radial radius (and therefore length) is unchanged.
            rim.Tube(outline,RacketSpecification.FrameTubeRadius,10,.72f);
            carbon.Append(rim,RacketSpecification.HeadCenter);
            MeshParts blueHead=new MeshParts(),whiteHead=new MeshParts(),orangeHead=new MeshParts(),blackHead=new MeshParts();
            // The viewed official Dark Navy photo is deliberately asymmetric:
            // blue/white left side, dark right shoulder, orange lower-right.
            AddPaint(blueHead,101f,253f);
            AddPaint(orangeHead,299f,339f);
            foreach (float angle in new[] { 112f,130f,151f,174f,197f,220f,240f,251f })
                AddFrameSlash(whiteHead,angle,3.9f);
            AddFrameSlash(blueHead,91f,1.4f);
            AddFrameSlash(whiteHead,97f,1.5f);
            Vector3 legend=Outline(319f*Mathf.Deg2Rad,RacketSpecification.FrameHalfWidth,RacketSpecification.FrameHalfHeight);
            Vector3 legendRight=(Outline(320f*Mathf.Deg2Rad,RacketSpecification.FrameHalfWidth,RacketSpecification.FrameHalfHeight)-legend).normalized;
            SurfaceText(blackHead,"100 ZZ",legend+Vector3.forward*.00306f,legendRight,Vector3.Cross(Vector3.forward,legendRight),.00049f,true);
            blue.Append(blueHead,RacketSpecification.HeadCenter);
            pale.Append(whiteHead,RacketSpecification.HeadCenter);
            orangePaint.Append(orangeHead,RacketSpecification.HeadCenter);

            MeshParts bedHead=new MeshParts(),stencilHead=new MeshParts();
            for (int i=-11;i<=11;i++)
            {
                float x=i*.008f;
                float extent=OtherExtent(x,RacketSpecification.HeadHalfWidth,RacketSpecification.HeadHalfHeight);
                AddString(bedHead,stencil!=null ? stencilHead : null,new Vector3(x,-extent,-.00018f),new Vector3(x,extent,-.00018f));
            }
            for (int i=-12;i<=12;i++)
            {
                float y=i*.009f;
                float extent=OtherExtent(y,RacketSpecification.HeadHalfHeight,RacketSpecification.HeadHalfWidth);
                AddString(bedHead,stencil!=null ? stencilHead : null,new Vector3(-extent,y,.00018f),new Vector3(extent,y,.00018f));
            }
            // Small dark grommet barrels project just into the string bed.
            // Count/placement is a visual approximation, not a stringing diagram.
            for (int i=0;i<76;i++)
            {
                float angle=(i+.5f)*Mathf.PI*2f/76f;
                Vector3 outer=Outline(angle,RacketSpecification.FrameHalfWidth-.0028f,RacketSpecification.FrameHalfHeight-.0028f);
                Vector3 inward=Outline(angle,RacketSpecification.HeadHalfWidth-.0017f,RacketSpecification.HeadHalfHeight-.0017f);
                blackHead.Tube(new[] { outer,inward },.00112f,5);
            }
            black.Append(blackHead,RacketSpecification.HeadCenter);
            bed.Append(bedHead,RacketSpecification.HeadCenter);
            red.Append(stencilHead,RacketSpecification.HeadCenter);

            carbon.Frustum(.151f,.394f,RacketSpecification.EstimatedShaftDiameter*.5f,RacketSpecification.EstimatedShaftDiameter*.5f,12);
            // Shaped cone with a flatter thumb-side profile, instead of the old
            // uniformly circular cone; the rounded T joint blends into the hoop.
            carbon.Profile(new[] { .114f,.122f,.139f,.151f,.159f },
                new[] { .0140f,.0132f,.0092f,.0048f,.0035f },
                new[] { .0107f,.0100f,.0072f,.0040f,.0035f },12);
            carbon.Profile(new[] { .365f,.378f,.385f,.389f },
                new[] { .00325f,.0042f,.0082f,.015f },new[] { .00325f,.0033f,.0034f,.0025f },12);
            AddShaftRibbon(blue,.170f,.322f,.00329f,1.12f,2.5f,0f);
            AddShaftRibbon(pale,.171f,.327f,.00336f,1.10f,.60f,.65f);
            AddShaftRibbon(pale,.175f,.317f,.00337f,1.07f,.20f,-1.00f);
            SurfaceText(pale,"ASTROX",new Vector3(0f,.346f,.00340f),Vector3.up,Vector3.left,.00032f,true);
            SurfaceText(pale,"100 ZZ",new Vector3(0f,.363f,.00341f),Vector3.up,Vector3.left,.00028f,true);
            SurfaceText(orangePaint,"YONEX",new Vector3(0f,.135f,.00812f),Vector3.up,Vector3.left,.00045f,true,true);

            black.Frustum(-.026f,.115f,.0135f,.013f,8);
            black.Frustum(RacketSpecification.ButtY,-.026f,.017f,.014f,8);
            // A broad, restrained seam follows the polygon grip rather than
            // oversized coloured tape bands. It does not alter the grip anchor.
            Vector3[] helix = new Vector3[181];
            for (int i=0;i<helix.Length;i++)
            {
                float t=(float)i/(helix.Length-1);
                float angle=t*Mathf.PI*18f;
                // Octagonal grip radius, so the seam follows the grip rather
                // than floating above a circular cylinder.
                float octant=Mathf.Repeat(angle,Mathf.PI/4f)-Mathf.PI/8f;
                float radius=Mathf.Lerp(.0135f,.013f,t)*Mathf.Cos(Mathf.PI/8f)/Mathf.Cos(octant)+.00035f;
                helix[i]=new Vector3(Mathf.Cos(angle)*radius,Mathf.Lerp(-.022f,.114f,t),Mathf.Sin(angle)*radius);
            }
            carbon.Tube(helix,.00038f,4);
            // Fine grip texture is geometry only, avoiding imported image assets.
            // Shallow short grooves stay on the near planar octagonal faces.
            for (int row=0;row<34;row++)
                for (int side=0;side<8;side++)
                {
                    float angle=(side+.5f)*Mathf.PI*.25f;
                    float y=-.020f+row*.0039f;
                    float radius=Mathf.Lerp(.0135f,.013f,Mathf.InverseLerp(-.026f,.115f,y))*Mathf.Cos(Mathf.PI/8f)+.00005f;
                    Vector3 centre=new Vector3(Mathf.Cos(angle)*radius,y,Mathf.Sin(angle)*radius);
                    Vector3 tangent=new Vector3(-Mathf.Sin(angle),0f,Mathf.Cos(angle));
                    carbon.Tube(new[] { centre-tangent*.00042f,centre+tangent*.00042f },.00013f,3);
                }
            AddMesh("Dark Navy aero frame shaft cap and grip texture",parent,carbon,frame);
            AddMesh("Blue swept frame and shaft graphics",parent,blue,accent);
            AddMesh("White asymmetric slashes and shaft lettering",parent,pale,whiteDecal);
            AddMesh("Orange lower-right frame and cone markings",parent,orangePaint,orange);
            AddMesh("Black grip grommets and frame legend",parent,black,grip);
            AddMesh("Fine woven off-white string bed",parent,bed,strings);
            if (stencil!=null) AddMesh("Red stencil printed only on strings",parent,red,stencil);
            return head;
        }

        public static Vector3 Outline(float angle,float halfWidth,float halfHeight)
        {
            float cosine=Mathf.Cos(angle), sine=Mathf.Sin(angle);
            float power=2f/RacketSpecification.IsometricExponent;
            return new Vector3(halfWidth*Mathf.Sign(cosine)*Mathf.Pow(Mathf.Abs(cosine),power),
                halfHeight*Mathf.Sign(sine)*Mathf.Pow(Mathf.Abs(sine),power),0f);
        }

        private static float OtherExtent(float coordinate,float alongHalfExtent,float acrossHalfExtent)
        {
            float remaining=1f-Mathf.Pow(Mathf.Abs(coordinate)/alongHalfExtent,RacketSpecification.IsometricExponent);
            return acrossHalfExtent*Mathf.Pow(Mathf.Max(0f,remaining),1f/RacketSpecification.IsometricExponent);
        }

        private static void AddPaint(MeshParts mesh,float begin,float end)
        {
            int count=Mathf.Max(8,Mathf.CeilToInt((end-begin)/2f));
            Vector3[] points=new Vector3[count+1];
            for (int i=0;i<points.Length;i++)
                points[i]=Outline(Mathf.Lerp(begin,end,(float)i/(points.Length-1))*Mathf.Deg2Rad,
                    RacketSpecification.FrameHalfWidth,RacketSpecification.FrameHalfHeight);
            mesh.Tube(points,RacketSpecification.FrameTubeRadius+.00008f,10,.72f);
        }

        private static void AddFrameSlash(MeshParts mesh,float angle,float spread)
        {
            float r=RacketSpecification.FrameTubeRadius*.79f;
            Vector3 a=Outline((angle-spread)*Mathf.Deg2Rad,RacketSpecification.FrameHalfWidth+r,RacketSpecification.FrameHalfHeight+r);
            Vector3 b=Outline((angle-spread*.08f)*Mathf.Deg2Rad,RacketSpecification.FrameHalfWidth-r,RacketSpecification.FrameHalfHeight-r);
            Vector3 c=Outline((angle+spread*.36f)*Mathf.Deg2Rad,RacketSpecification.FrameHalfWidth-r,RacketSpecification.FrameHalfHeight-r);
            Vector3 d=Outline((angle+spread*.28f)*Mathf.Deg2Rad,RacketSpecification.FrameHalfWidth+r,RacketSpecification.FrameHalfHeight+r);
            float z=RacketSpecification.FrameTubeRadius*.74f;
            mesh.Quad(d+Vector3.forward*z,c+Vector3.forward*z,b+Vector3.forward*z,a+Vector3.forward*z);
            mesh.Quad(a-Vector3.forward*z,b-Vector3.forward*z,c-Vector3.forward*z,d-Vector3.forward*z);
        }

        private static void AddShaftRibbon(MeshParts mesh,float bottom,float top,float radius,float turns,float width,float phase)
        {
            const int steps=72;
            for(int i=0;i<steps;i++)
            {
                float t0=(float)i/steps,t1=(float)(i+1)/steps;
                float angle0=phase+t0*Mathf.PI*2f*turns,angle1=phase+t1*Mathf.PI*2f*turns;
                float taper0=Mathf.Sin(Mathf.PI*t0)*width,taper1=Mathf.Sin(Mathf.PI*t1)*width;
                mesh.Quad(OnShaft(Mathf.Lerp(bottom,top,t0),angle0-taper0*.5f,radius),
                    OnShaft(Mathf.Lerp(bottom,top,t1),angle1-taper1*.5f,radius),
                    OnShaft(Mathf.Lerp(bottom,top,t1),angle1+taper1*.5f,radius),
                    OnShaft(Mathf.Lerp(bottom,top,t0),angle0+taper0*.5f,radius));
            }
        }

        private static Vector3 OnShaft(float y,float angle,float radius)
        {
            return new Vector3(Mathf.Cos(angle)*radius,y,Mathf.Sin(angle)*radius);
        }

        private static void AddString(MeshParts bed,MeshParts stencil,Vector3 from,Vector3 to)
        {
            if(stencil==null) { bed.Tube(new[] { from,to },.00034f,4); return; }
            // Stencil is coloured string, not an opaque floating logo card: every
            // opening remains transparent when the player sees through the bed.
            const int samples=120;
            bool painted=InStencil(Vector3.Lerp(from,to,.5f/samples));
            float start=0f;
            for(int i=1;i<=samples;i++)
            {
                bool next=i<samples&&InStencil(Vector3.Lerp(from,to,(i+.5f)/samples));
                if(i<samples&&next==painted) continue;
                (painted ? stencil : bed).Tube(new[] { Vector3.Lerp(from,to,start),Vector3.Lerp(from,to,(float)i/samples) },painted ? .00037f : .00034f,4);
                start=(float)i/samples; painted=next;
            }
        }

        private static bool InStencil(Vector3 p)
        {
            Vector2 q=new Vector2(p.x,p.y);
            if((q-new Vector2(-.028f,.063f)).sqrMagnitude<.022f*.022f ||
               (q-new Vector2(-.028f,-.011f)).sqrMagnitude<.022f*.022f) return true;
            return InTriangle(q,new Vector2(-.014f,.028f),new Vector2(.043f,.061f),new Vector2(.043f,-.005f)) ||
                   InTriangle(q,new Vector2(-.014f,-.047f),new Vector2(.043f,-.014f),new Vector2(.043f,-.080f));
        }

        private static bool InTriangle(Vector2 p,Vector2 a,Vector2 b,Vector2 c)
        {
            float x=(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);
            float y=(c.x-b.x)*(p.y-b.y)-(c.y-b.y)*(p.x-b.x);
            float z=(a.x-c.x)*(p.y-c.y)-(a.y-c.y)*(p.x-c.x);
            return (x>=0f&&y>=0f&&z>=0f)||(x<=0f&&y<=0f&&z<=0f);
        }

        private static void SurfaceText(MeshParts mesh,string text,Vector3 centre,Vector3 right,Vector3 up,float pixel,bool bothSides,bool followCap=false)
        {
            float halfWidth=(text.Length*4f-1f)*pixel*.5f;
            for(int letter=0;letter<text.Length;letter++)
            {
                string glyph=Glyph(text[letter]);
                for(int row=0;row<5;row++)
                    for(int column=0;column<3;column++)
                    {
                        if(glyph[row*3+column]!='1') continue;
                        float x=(letter*4+column)*pixel-halfWidth,y=(2f-row)*pixel;
                        Vector3 a=centre+right*x+up*y,b=a+right*pixel*.91f,c=b+up*pixel*.91f,d=a+up*pixel*.91f;
                        if(followCap)
                        {
                            a.z=CapFront(a.y);b.z=CapFront(b.y);c.z=CapFront(c.y);d.z=CapFront(d.y);
                        }
                        mesh.Quad(a,b,c,d);
                        if(bothSides)
                        {
                            a.z=-a.z;b.z=-b.z;c.z=-c.z;d.z=-d.z;
                            mesh.Quad(d,c,b,a);
                        }
                    }
            }
        }

        private static float CapFront(float y)
        {
            return (y<=.139f ? Mathf.Lerp(.0100f,.0072f,Mathf.InverseLerp(.122f,.139f,y)) :
                Mathf.Lerp(.0072f,.0040f,Mathf.InverseLerp(.139f,.151f,y)))+.00012f;
        }

        private static string Glyph(char letter)
        {
            switch(letter)
            {
                case 'A': return "010101111101101";
                case 'S': return "111100111001111";
                case 'T': return "111010010010010";
                case 'R': return "110101110101101";
                case 'O': return "111101101101111";
                case 'X': return "101101010101101";
                case 'Y': return "101101010010010";
                case 'N': return "101111111111101";
                case 'E': return "111100110100111";
                case 'Z': return "111001010100111";
                case '1': return "010110010010111";
                case '0': return "111101101101111";
                default: return "000000000000000";
            }
        }

        private static void AddMesh(string name,Transform parent,MeshParts data,Material material)
        {
            GameObject child=new GameObject(name);
            child.transform.SetParent(parent,false);
            Mesh mesh=new Mesh { name="Racket "+name };
            mesh.SetVertices(data.vertices);
            mesh.SetTriangles(data.triangles,0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            child.AddComponent<MeshFilter>().sharedMesh=mesh;
            MeshRenderer renderer=child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On;
            renderer.receiveShadows=true;
        }

        private sealed class MeshParts
        {
            public readonly List<Vector3> vertices=new List<Vector3>();
            public readonly List<int> triangles=new List<int>();

            public void Append(MeshParts source,Vector3 offset)
            {
                int first=vertices.Count;
                foreach(Vector3 point in source.vertices) vertices.Add(point+offset);
                foreach(int index in source.triangles) triangles.Add(first+index);
            }

            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int first=vertices.Count;
                vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
                triangles.Add(first);triangles.Add(first+1);triangles.Add(first+2);
                triangles.Add(first);triangles.Add(first+2);triangles.Add(first+3);
            }

            public void Tube(Vector3[] points,float radius,int sides,float depthScale=1f)
            {
                int first=vertices.Count;
                for (int i=0;i<points.Length;i++)
                {
                    Vector3 tangent=(points[Mathf.Min(i+1,points.Length-1)]-points[Mathf.Max(0,i-1)]).normalized;
                    Vector3 seed=Mathf.Abs(Vector3.Dot(tangent,Vector3.forward))<.9f ? Vector3.forward : Vector3.up;
                    Vector3 outward=Vector3.Cross(tangent,seed).normalized;
                    Vector3 around=Vector3.Cross(tangent,outward).normalized;
                    for (int j=0;j<sides;j++)
                    {
                        float angle=j*Mathf.PI*2f/sides;
                        vertices.Add(points[i]+radius*(outward*Mathf.Cos(angle)+around*Mathf.Sin(angle)*depthScale));
                    }
                    if (i>0) Join(first+(i-1)*sides,first+i*sides,sides);
                }
            }

            public void Frustum(float bottom,float top,float bottomRadius,float topRadius,int sides)
            {
                int first=vertices.Count;
                for (int row=0;row<2;row++)
                    for (int j=0;j<sides;j++)
                    {
                        float angle=j*Mathf.PI*2f/sides;
                        float radius=row==0 ? bottomRadius : topRadius;
                        vertices.Add(new Vector3(Mathf.Cos(angle)*radius,row==0 ? bottom : top,Mathf.Sin(angle)*radius));
                    }
                // Frustum ring order is opposite the path-tube ring order.
                Join(first+sides,first,sides);
                int bottomCenter=vertices.Count;
                vertices.Add(new Vector3(0f,bottom,0f));
                int topCenter=vertices.Count;
                vertices.Add(new Vector3(0f,top,0f));
                for (int j=0;j<sides;j++)
                {
                    int next=(j+1)%sides;
                    triangles.Add(bottomCenter); triangles.Add(first+j); triangles.Add(first+next);
                    triangles.Add(topCenter); triangles.Add(first+sides+next); triangles.Add(first+sides+j);
                }
            }

            public void Profile(float[] height,float[] halfWidth,float[] halfDepth,int sides)
            {
                int first=vertices.Count;
                for(int row=0;row<height.Length;row++)
                {
                    for(int j=0;j<sides;j++)
                    {
                        float angle=j*Mathf.PI*2f/sides;
                        vertices.Add(new Vector3(Mathf.Cos(angle)*halfWidth[row],height[row],Mathf.Sin(angle)*halfDepth[row]));
                    }
                    if(row>0) Join(first+row*sides,first+(row-1)*sides,sides);
                }
            }

            private void Join(int previous,int current,int sides)
            {
                for (int j=0;j<sides;j++)
                {
                    int next=(j+1)%sides;
                    triangles.Add(previous+j); triangles.Add(previous+next); triangles.Add(current+j);
                    triangles.Add(previous+next); triangles.Add(current+next); triangles.Add(current+j);
                }
            }
        }
    }
}
