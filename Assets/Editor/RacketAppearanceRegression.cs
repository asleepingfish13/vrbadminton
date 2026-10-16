using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    public static class RacketAppearanceRegression
    {
        private static int checks;
        private static void Require(bool value,string message)
        { checks++; if(!value) throw new InvalidOperationException("[Racket Appearance Test] "+message); }

        public static int Run(BadmintonSession session)
        {
            checks=0;
            Color frame=RacketAppearance.BaseColor(RacketMaterialPart.Frame);
            Require(Luminance(frame)>Luminance(RacketSpecification.DarkNavyColor)*1.6f,
                "Frame/shaft readability did not improve over the photo-reference palette");
            Require(frame.b>frame.r*1.5f && frame.maxColorComponent<.4f,
                "Brightened frame must remain dark navy rather than a pale/neon toy colour");
            Require(Luminance(RacketAppearance.BaseColor(RacketMaterialPart.Strings))>Luminance(RacketSpecification.StringColor)*1.1f,
                "Fine strings were not brightened");
            Color orange=RacketAppearance.BaseColor(RacketMaterialPart.OrangeGraphics);
            Require(orange.r>.99f && orange.g<.65f && orange.b<.3f,"Orange identity was lost");
            Require(Near(RacketAppearance.BaseColor(RacketMaterialPart.Grip),RacketSpecification.GripColor) &&
                RacketAppearance.Emission(RacketMaterialPart.Grip).maxColorComponent==0f,
                "Black grip must remain non-emissive and separate from the frame lift");
            foreach(RacketController controller in session.rackets)
                VerifyMaterials(controller.visual.transform,true,controller.hand.ToString());

            GameObject fixture=new GameObject("AI racket appearance fixture");
            try
            {
                BadmintonOpponent opponent=fixture.AddComponent<BadmintonOpponent>();
                opponent.Initialize(session);
                Transform model=fixture.transform.Find("Visible AI player/AI racket contact head/AI shared racket model");
                Require(model!=null,"AI shared racket model missing");
                VerifyMaterials(model,false,"AI");
            }
            finally { Object.DestroyImmediate(fixture); }
            foreach(RacketMaterialPart part in Enum.GetValues(typeof(RacketMaterialPart)))
            {
                Material material=RacketAppearance.Create(part);
                try
                {
                    RacketAppearance.Apply(material,part);
                    Verify(material,part,"Repeated application");
                }
                finally { Object.DestroyImmediate(material); }
            }
            Debug.Log("[Racket Appearance Test] PASS "+checks+" checks: brighter navy/strings, bounded emission, retained orange/black grip and matching player/AI materials.");
            return checks;
        }

        private static void VerifyMaterials(Transform model,bool persistent,string label)
        {
            Renderer[] renderers=model.GetComponentsInChildren<Renderer>(true);
            Require(renderers.Length==7,label+" must retain seven distinct racket surface groups");
            var observed=new HashSet<RacketMaterialPart>();
            foreach(Renderer renderer in renderers)
            {
                Material material=renderer.sharedMaterial;
                Require(material!=null,label+" has a missing material");
                bool found=false;
                foreach(RacketMaterialPart part in Enum.GetValues(typeof(RacketMaterialPart)))
                    if(material.name==RacketAppearance.AssetName(part))
                    {
                        found=true;
                        Require(observed.Add(part),label+" reused one surface palette for a different group");
                        Verify(material,part,label);
                        if(persistent) Require(AssetDatabase.Contains(material),label+" material is not a saved project asset");
                        break;
                    }
                Require(found,label+" uses an unconfigured or mismatched racket palette: "+material.name);
            }
            Require(observed.Count==7,label+" is missing a racket colour group");
        }

        private static void Verify(Material material,RacketMaterialPart part,string label)
        {
            Require(material.shader.name=="Standard",label+" should preserve physically shaded surfaces");
            Require(Near(material.color,RacketAppearance.BaseColor(part)),label+" base colour differs for "+part);
            Color emission=material.GetColor("_EmissionColor");
            Require(Near(emission,RacketAppearance.Emission(part)),label+" emission differs for "+part);
            Require(emission.maxColorComponent<=.08f && emission.r>=0f && emission.g>=0f && emission.b>=0f,
                label+" emission exceeds the modest readability floor");
            Require(material.IsKeywordEnabled("_EMISSION")== (part!=RacketMaterialPart.Grip),label+" emission keyword mismatch");
            Require(material.GetFloat("_Metallic")==0f && material.GetFloat("_Glossiness")<=.3f,
                label+" became a glossy metallic or neon surface");
            Require(material.globalIlluminationFlags==MaterialGlobalIlluminationFlags.None,
                label+" racket must not become a baked light source");
        }
        private static float Luminance(Color color) { return color.r*.2126f+color.g*.7152f+color.b*.0722f; }
        private static bool Near(Color a,Color b)
        { return Mathf.Abs(a.r-b.r)<.0001f && Mathf.Abs(a.g-b.g)<.0001f && Mathf.Abs(a.b-b.b)<.0001f; }
    }
}
