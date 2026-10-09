using System;
using UnityEngine;

namespace ViveShuttleVR.Editor
{
    public static class RegulationCourtRegression
    {
        private static int checks;
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
    }
}
