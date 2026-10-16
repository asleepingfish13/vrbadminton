using System;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ViveShuttleVR.Editor
{
    public static class RegulationCourtRegression
    {
        private static int checks;
        public static void VerifyStandalone()
        {
            EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
            BadmintonSession session=UnityEngine.Object.FindObjectOfType<BadmintonSession>();
            if(session==null) throw new InvalidOperationException("Court-only regression scene has no session");
            LogBoundaryPrecision();
            Run(session.court);
        }

        private static void LogBoundaryPrecision()
        {
            GameObject fixture=new GameObject("Boundary precision diagnostic");
            float maximumOvershoot=0f,minimumOutwardGap=float.PositiveInfinity;
            string worst="none";
            try
            {
                foreach(Vector3 origin in new[]{Vector3.zero,new Vector3(1.4f,-.38f,-.6f),new Vector3(-3.2f,.22f,2.1f)})
                foreach(float yaw in new[]{-135f,-45f,0f,40f,90f,170f})
                foreach(float width in new[]{2.59f,3.05f})
                foreach(float side in new[]{-1f,1f})
                foreach(float z in new[]{-3.1f,10.3f})
                {
                    fixture.transform.SetPositionAndRotation(origin,Quaternion.Euler(0f,yaw,0f));
                    Vector3 original=new Vector3(width*side,0f,z);
                    Vector3 roundTrip=fixture.transform.InverseTransformPoint(fixture.transform.TransformPoint(original));
                    float ox=Mathf.Max(0f,Mathf.Abs(roundTrip.x)-width);
                    float oz=Mathf.Max(0f,BadmintonCourt.NearBaseline-roundTrip.z,roundTrip.z-BadmintonCourt.FarBaseline);
                    float overshoot=Mathf.Sqrt(ox*ox+oz*oz);
                    if(overshoot>maximumOvershoot)
                    {
                        maximumOvershoot=overshoot;
                        worst="origin="+origin+" yaw="+yaw+" local="+PrecisionPoint(original)+" roundTrip="+PrecisionPoint(roundTrip);
                    }
                    foreach(Vector3 outward in new[]{Vector3.right*(side*.00001f),Vector3.forward*((z>3.6f?1f:-1f)*.00001f)})
                    {
                        Vector3 outside=fixture.transform.InverseTransformPoint(fixture.transform.TransformPoint(original+outward));
                        float dx=Mathf.Max(0f,Mathf.Abs(outside.x)-width);
                        float dz=Mathf.Max(0f,BadmintonCourt.NearBaseline-outside.z,outside.z-BadmintonCourt.FarBaseline);
                        minimumOutwardGap=Mathf.Min(minimumOutwardGap,Mathf.Sqrt(dx*dx+dz*dz));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
            Debug.Log("[Court Precision] max boundary overshoot="+maximumOvershoot.ToString("R")+"m; min10um-outside gap="+minimumOutwardGap.ToString("R")+"m; worst="+worst);
        }
        public static int Run(Transform court)
        {
            checks=0;
            Near(BadmintonCourt.Length,13.4f,"length");
            Near(BadmintonCourt.DoublesWidth,6.1f,"doubles width");
            Near(BadmintonCourt.SinglesWidth,5.18f,"singles width");
            Near(BadmintonCourt.LineWidth,.04f,"line width");
            Near(BadmintonCourt.FarBaseline-BadmintonCourt.NearBaseline,13.4f,"baseline separation");
            Near(BadmintonCourt.NetHeightAt(0f),1.524f,"net centre height");
            Near(BadmintonCourt.NetHeightAt(-3.05f),1.55f,"left net edge");
            Near(BadmintonCourt.NetHeightAt(3.05f),1.55f,"right net edge");
            Near(BadmintonCourt.NetDepth,.760f,"net depth");
            Near(BadmintonCourt.NetTapeUnfoldedWidth,.075f,"unfolded tape width");
            Near(BadmintonCourt.NetTapeFaceDepth*2f,.075f,"folded tape coverage");
            for(int i=-100;i<=100;i++)
            {
                float x=i*.0305f;
                Check(BadmintonCourt.NetHeightAt(x)>=1.524f && BadmintonCourt.NetHeightAt(x)<=1.55001f,"net sag bounds");
                Near(BadmintonCourt.NetHeightAt(x),BadmintonCourt.NetHeightAt(-x),"net symmetry");
            }
            float openingX=BadmintonCourt.DoublesWidth/BadmintonCourt.NetMeshColumns-BadmintonCourt.NetCordDiameter;
            float openingY=BadmintonCourt.NetDepth/BadmintonCourt.NetMeshRows-BadmintonCourt.NetCordDiameter;
            Check(openingX>=.015f && openingX<=.020f,"15-20 mm mesh horizontal opening");
            Check(openingY>=.015f && openingY<=.020f,"15-20 mm mesh vertical opening");
            BadmintonCourt.Marking[] markings=BadmintonCourt.Markings();
            Check(markings.Length==12,"complete singles and doubles markings");
            foreach(BadmintonCourt.Marking marking in markings)
                Near(Mathf.Min(marking.size.x,marking.size.y),.04f,"40 mm "+marking.name);
            Near(markings[0].centre.y-markings[0].size.y*.5f,BadmintonCourt.NearBaseline,"near baseline outer edge");
            Near(markings[1].centre.y+markings[1].size.y*.5f,BadmintonCourt.FarBaseline,"far baseline outer edge");
            Near(markings[2].centre.x-markings[2].size.x*.5f,-3.05f,"doubles left outer edge");
            Near(markings[3].centre.x+markings[3].size.x*.5f,3.05f,"doubles right outer edge");
            Near(markings[4].centre.x-markings[4].size.x*.5f,-2.59f,"singles left outer edge");
            Near(markings[5].centre.x+markings[5].size.x*.5f,2.59f,"singles right outer edge");
            Near(BadmintonCourt.NetZ-(markings[6].centre.y+.02f),1.98f,"near short service clear gap");
            Near(markings[7].centre.y-.02f-BadmintonCourt.NetZ,1.98f,"far short service clear gap");
            Near(markings[8].centre.y-.02f-BadmintonCourt.NearBaseline,.76f,"near doubles back service distance");
            Near(BadmintonCourt.FarBaseline-(markings[9].centre.y+.02f),.76f,"far doubles back service distance");
            Near(markings[10].centre.y-markings[10].size.y*.5f,BadmintonCourt.NearBaseline,"near centre line reaches baseline");
            Near(markings[10].centre.y+markings[10].size.y*.5f,BadmintonCourt.NetZ-1.98f,"near centre line stops at service line");
            Near(markings[11].centre.y+markings[11].size.y*.5f,BadmintonCourt.FarBaseline,"far centre line reaches baseline");
            Check(BadmintonCourt.IsInSingles(new Vector3(2.59f,0f,BadmintonCourt.FarBaseline)),"outer line corner is IN");
            Check(!BadmintonCourt.IsInSingles(new Vector3(2.591f,0f,BadmintonCourt.NetZ)),"outside singles is OUT");
            Check(BadmintonCourt.IsInDoubles(new Vector3(2.9f,0f,BadmintonCourt.NetZ)),"doubles alley is IN for doubles");
            Check(!BadmintonCourt.IsInSingles(new Vector3(2.9f,0f,BadmintonCourt.NetZ)),"doubles alley is OUT for singles");
            Check(BadmintonCourt.IsInSingles(new Vector3(2.60f,0f,BadmintonCourt.NetZ),.013f),"cork footprint touching line is IN");
            Check(!BadmintonCourt.IsInSingles(new Vector3(2.601f,0f,BadmintonCourt.FarBaseline+.011f),.013f),"diagonal footprint outside corner is OUT");
            Check(BadmintonCourt.IsInServiceCourt(new Vector3(1f,0f,BadmintonCourt.NetZ+2f),true,true,false),"far right singles service");
            Check(!BadmintonCourt.IsInServiceCourt(new Vector3(-1f,0f,BadmintonCourt.NetZ+2f),true,true,false),"wrong service side");
            Check(!BadmintonCourt.IsInServiceCourt(new Vector3(1f,0f,BadmintonCourt.NetZ+1.97f),true,true,false),"short service OUT");
            Check(BadmintonCourt.IsInServiceCourt(new Vector3(1f,0f,BadmintonCourt.NetZ+6.4f),true,true,false),"singles deep service IN");
            Check(!BadmintonCourt.IsInServiceCourt(new Vector3(1f,0f,BadmintonCourt.NetZ+6.4f),true,true,true),"doubles deep service OUT");
            Check(BadmintonCourt.IsInServiceCourt(new Vector3(0f,0f,BadmintonCourt.NetZ-2.2f),false,false,false),"centre line shared by service courts");
            Check(BadmintonCourt.IsInServiceCourt(new Vector3(0f,0f,BadmintonCourt.NetZ-2.2f),false,true,false),"centre line shared other side");
            TestBoundaryRoundoff(court);

            Transform hall=court.Find(RegulationHallBuilder.RootName);
            Check(hall!=null,"regulation hall exists in built scene");
            Transform net=hall.Find(RegulationHallBuilder.NetMeshName);
            Check(net!=null,"batched net exists");
            Mesh netMesh=net.GetComponent<MeshFilter>().sharedMesh;
            Check(netMesh!=null && netMesh.vertexCount>20000 && netMesh.vertexCount<65535,"net batched within 16-bit vertex budget");
            Near(netMesh.bounds.min.x,-3.05f,"net visual left extent");
            Near(netMesh.bounds.max.x,3.05f,"net visual right extent");
            Near(netMesh.bounds.min.y,1.524f-.760f,"net centre bottom extent");
            Near(netMesh.bounds.max.y,1.55f,"net upper extent");
            bool cordsInsidePanel=true;
            foreach(Vector3 point in netMesh.vertices)
            {
                float localTop=BadmintonCourt.NetHeightAt(point.x);
                cordsInsidePanel &= point.y>=localTop-BadmintonCourt.NetDepth-.00001f && point.y<=localTop+.00001f;
            }
            Check(cordsInsidePanel,"cord radius stays inside regulation panel");
            Check(net.GetComponentsInChildren<Renderer>().Length==1,"one net-cord draw renderer");
            Transform tape=hall.Find(RegulationHallBuilder.TapeMeshName);
            Check(tape!=null,"folded tape exists");
            Mesh tapeMesh=tape.GetComponent<MeshFilter>().sharedMesh;
            Near(tapeMesh.bounds.min.y,1.524f-.0375f,"visible tape depth");
            Near(tapeMesh.bounds.max.y,1.55f,"tape top at post height");
            Mesh bottomBinding=hall.Find("Dark bottom net binding").GetComponent<MeshFilter>().sharedMesh;
            Near(bottomBinding.bounds.min.y,1.524f-.760f,"bottom binding does not deepen net");
            foreach(string side in new[]{"left","right"})
            {
                Transform post=hall.Find("Net post "+side);
                Check(post!=null,"post exists "+side);
                Near(post.localPosition.y+post.localScale.y*.5f,1.55f,"post height "+side);
                Near(Mathf.Abs(post.localPosition.x),3.05f,"post on doubles sideline "+side);
                Check(Mathf.Abs(post.localPosition.x)-post.localScale.x*.5f>=3.01f,"post not inside inner sideline "+side);
            }
            Transform visualLines=hall.Find("BWF 40 mm court markings");
            Check(visualLines!=null,"batched court lines exist");
            Mesh lineMesh=visualLines.GetComponent<MeshFilter>().sharedMesh;
            Near(lineMesh.bounds.size.x,6.1f,"rendered court width");
            Near(lineMesh.bounds.size.z,13.4f,"rendered court length");
            Check(lineMesh.vertexCount==markings.Length*4,"every marking rendered");
            foreach(Transform part in hall)
            {
                Check(!part.gameObject.isStatic,"recenterable hall must not use static batching");
                if(part.name.Contains("Suspended light") || part.name=="Roof transverse steel" || part.name=="Roof underside")
                    Check(part.localPosition.y-part.localScale.y*.5f>=RegulationHallBuilder.HallClearHeight,"10 m clear ceiling design");
            }
            checks += VerifyWorldDimensions(court);
            Debug.Log("[Regulation Court Regression] Passed "+checks+" checks.");
            return checks;
        }

        private static void TestBoundaryRoundoff(Transform court)
        {
            Check(BadmintonCourt.BoundaryRoundoffTolerance>0f && BadmintonCourt.BoundaryRoundoffTolerance<=.000005f,
                "boundary comparison allowance must stay micrometre-scale");
            // Independent decimal literals reproduce the reported winner bug;
            // deriving these coordinates from the same constants masks it.
            Check(BadmintonCourt.IsInSingles(new Vector3(2.59f,0f,10.3f)),"literal far singles corner must be IN");
            Check(BadmintonCourt.IsOnFarSinglesSide(new Vector3(2.59f,0f,10.3f)),"literal far boundary retains winning side");
            Check(BadmintonCourt.IsOnNearSinglesSide(new Vector3(-2.59f,0f,-3.1f)),"literal near boundary retains winning side");
            Check(!BadmintonCourt.IsInSingles(new Vector3(float.NaN,0f,10.3f)),"NaN landing is not a boundary hit");
            Check(!BadmintonCourt.IsInSingles(new Vector3(0f,0f,float.PositiveInfinity)),"infinite landing is OUT");
            Check(!BadmintonCourt.IsInSingles(Vector3.zero,float.NaN),"invalid cork footprint is rejected");

            CheckBoundaryAtTransform(court,"current scene");
            GameObject fixture=new GameObject("Boundary round-trip regression fixture");
            try
            {
                foreach(Vector3 origin in new[]{Vector3.zero,new Vector3(1.4f,-.38f,-.6f),new Vector3(-3.2f,.22f,2.1f)})
                foreach(float yaw in new[]{-135f,-45f,0f,40f,90f,170f})
                {
                    fixture.transform.SetPositionAndRotation(origin,Quaternion.Euler(0f,yaw,0f));
                    CheckBoundaryAtTransform(fixture.transform,"origin="+origin+" yaw="+yaw);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        private static void CheckBoundaryAtTransform(Transform frame,string label)
        {
            // Ten micrometres (0.01 mm) is already beyond the round-off budget;
            // centimetre-scale racket assistance must never leak into IN/OUT.
            const float justOutside=.00001f;
            foreach(bool doubles in new[]{false,true})
            foreach(float sign in new[]{-1f,1f})
            foreach(float baseline in new[]{-3.1f,10.3f})
            {
                float width=doubles?3.05f:2.59f;
                float endSign=baseline>3.6f?1f:-1f;
                Vector3 corner=new Vector3(sign*width,0f,baseline);
                Vector3 roundTrip=frame.InverseTransformPoint(frame.TransformPoint(corner));
                Check(doubles?BadmintonCourt.IsInDoubles(roundTrip):BadmintonCourt.IsInSingles(roundTrip),
                    "rotated literal corner remains IN / doubles="+doubles+" "+label+" local="+PrecisionPoint(corner)+" roundTrip="+PrecisionPoint(roundTrip));
                Vector3 outsideSide=corner+Vector3.right*(sign*justOutside);
                outsideSide=frame.InverseTransformPoint(frame.TransformPoint(outsideSide));
                Check(!(doubles?BadmintonCourt.IsInDoubles(outsideSide):BadmintonCourt.IsInSingles(outsideSide)),
                    "10 micrometres beyond sideline remains OUT / "+label);
                Vector3 outsideEnd=corner+Vector3.forward*(endSign*justOutside);
                outsideEnd=frame.InverseTransformPoint(frame.TransformPoint(outsideEnd));
                Check(!(doubles?BadmintonCourt.IsInDoubles(outsideEnd):BadmintonCourt.IsInSingles(outsideEnd)),
                    "10 micrometres beyond baseline remains OUT / "+label);
                Vector3 inside=corner-new Vector3(sign*justOutside,0f,endSign*justOutside);
                inside=frame.InverseTransformPoint(frame.TransformPoint(inside));
                Check(doubles?BadmintonCourt.IsInDoubles(inside):BadmintonCourt.IsInSingles(inside),
                    "point just inside corner remains IN / "+label);
            }
            foreach(float sign in new[]{-1f,1f})
            {
                const float cork=.013f;
                Vector3 tangent=new Vector3(sign*(2.59f+cork),0f,6f);
                Vector3 roundTrip=frame.InverseTransformPoint(frame.TransformPoint(tangent));
                Check(BadmintonCourt.IsInSingles(roundTrip,cork),"touching cork footprint survives round-trip / "+label);
                Vector3 outside=tangent+Vector3.right*(sign*justOutside);
                outside=frame.InverseTransformPoint(frame.TransformPoint(outside));
                Check(!BadmintonCourt.IsInSingles(outside,cork),"cork footprint with a 10 micrometre gap is OUT / "+label);
            }
            foreach(bool farSide in new[]{false,true})
            foreach(bool doubles in new[]{false,true})
            {
                float side=farSide?1f:-1f;
                float width=doubles?3.05f:2.59f;
                float shortZ=farSide?5.58f:1.62f;
                float longZ=doubles?(farSide?9.54f:-2.34f):(farSide?10.3f:-3.1f);
                foreach(float edgeZ in new[]{shortZ,longZ})
                {
                    Vector3 edge=new Vector3(width,0f,edgeZ);
                    edge=frame.InverseTransformPoint(frame.TransformPoint(edge));
                    Check(BadmintonCourt.IsInServiceCourt(edge,farSide,true,doubles),
                        "literal service boundary remains IN / "+label);
                }
                Vector3 tooShort=new Vector3(1f,0f,shortZ-side*justOutside);
                tooShort=frame.InverseTransformPoint(frame.TransformPoint(tooShort));
                Check(!BadmintonCourt.IsInServiceCourt(tooShort,farSide,true,doubles),
                    "10 micrometres short of service line remains OUT / "+label);
                Vector3 tooLong=new Vector3(1f,0f,longZ+side*justOutside);
                tooLong=frame.InverseTransformPoint(frame.TransformPoint(tooLong));
                Check(!BadmintonCourt.IsInServiceCourt(tooLong,farSide,true,doubles),
                    "10 micrometres past service line remains OUT / "+label);
            }
        }

        // Height simulation may translate the whole hall, but must never scale
        // BWF dimensions or decouple rendered posts/lines from physics geometry.
        public static int VerifyWorldDimensions(Transform court)
        {
            int verified=0;
            Action<bool,string> verify=(value,label)=> {
                verified++;
                if(!value) throw new InvalidOperationException("[Regulation Court Regression] "+label);
            };
            verify(Vector3.Distance(court.lossyScale,Vector3.one)<.00001f,"court metric scale changed by player-height simulation");
            Vector3 near=court.TransformPoint(new Vector3(0f,0f,BadmintonCourt.NearBaseline));
            Vector3 far=court.TransformPoint(new Vector3(0f,0f,BadmintonCourt.FarBaseline));
            verify(Mathf.Abs(Vector3.Distance(near,far)-13.4f)<.0001f,"world court is not 13.40 m long");
            Vector3 left=court.TransformPoint(new Vector3(-3.05f,0f,BadmintonCourt.NetZ));
            Vector3 right=court.TransformPoint(new Vector3(3.05f,0f,BadmintonCourt.NetZ));
            verify(Mathf.Abs(Vector3.Distance(left,right)-6.1f)<.0001f,"world court is not 6.10 m wide");
            Vector3 groundCentre=court.TransformPoint(new Vector3(0f,0f,BadmintonCourt.NetZ));
            Vector3 topCentre=court.TransformPoint(new Vector3(0f,BadmintonCourt.NetCentreHeight,BadmintonCourt.NetZ));
            verify(Mathf.Abs(Vector3.Distance(groundCentre,topCentre)-1.524f)<.0001f,"virtual floor offset changed net clearance height");
            Transform hall=court.Find(RegulationHallBuilder.RootName);
            verify(hall!=null,"height fixture has no regulation hall");
            foreach(string side in new[]{"left","right"})
            {
                Transform post=hall.Find("Net post "+side);
                verify(post!=null,"height fixture missing net post "+side);
                Vector3 localFoot=court.InverseTransformPoint(post.TransformPoint(Vector3.down*.5f));
                Vector3 localTop=court.InverseTransformPoint(post.TransformPoint(Vector3.up*.5f));
                verify(Mathf.Abs(localFoot.y)<.0001f && Mathf.Abs(localTop.y-1.55f)<.0001f,
                    "rendered post moved relative to virtual floor "+side);
            }
            return verified;
        }

        private static void Check(bool value,string label)
        {
            checks++; if(!value) throw new InvalidOperationException("[Regulation Court Regression] "+label);
        }
        private static void Near(float actual,float expected,string label)
        { Check(Mathf.Abs(actual-expected)<.0001f,label+" expected "+expected+" got "+actual); }
        private static string PrecisionPoint(Vector3 point)
        { return "("+point.x.ToString("R")+", "+point.y.ToString("R")+", "+point.z.ToString("R")+")"; }
    }
}
