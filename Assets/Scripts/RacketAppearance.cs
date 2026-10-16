using System;
using UnityEngine;

namespace ViveShuttleVR
{
    public enum RacketMaterialPart { Frame, BlueGraphics, Strings, Grip, WhiteGraphics, OrangeGraphics, Stencil }

    // A readability treatment for the existing Dark Navy reference, not a new
    // manufacturer colour claim. Geometry, dimensions and impact physics stay
    // unchanged. Shared by persisted player materials and the runtime AI racket.
    public static class RacketAppearance
    {
        public static string AssetName(RacketMaterialPart part)
        {
            switch(part)
            {
                case RacketMaterialPart.Frame: return "100ZZ - Dark Navy";
                case RacketMaterialPart.BlueGraphics: return "100ZZ - Blue graphics";
                case RacketMaterialPart.Strings: return "100ZZ - Strings";
                case RacketMaterialPart.Grip: return "100ZZ - Grip";
                case RacketMaterialPart.WhiteGraphics: return "100ZZ - White graphics";
                case RacketMaterialPart.OrangeGraphics: return "100ZZ - Orange graphics";
                case RacketMaterialPart.Stencil: return "100ZZ - String stencil";
                default: throw new ArgumentOutOfRangeException("part");
            }
        }

        public static Color BaseColor(RacketMaterialPart part)
        {
            switch(part)
            {
                case RacketMaterialPart.Frame: return new Color32(41,68,84,255);
                case RacketMaterialPart.BlueGraphics: return new Color32(53,139,227,255);
                case RacketMaterialPart.Strings: return new Color32(242,247,245,255);
                case RacketMaterialPart.Grip: return RacketSpecification.GripColor;
                case RacketMaterialPart.WhiteGraphics: return new Color32(236,243,246,255);
                case RacketMaterialPart.OrangeGraphics: return new Color32(255,133,56,255);
                case RacketMaterialPart.Stencil: return new Color32(236,51,57,255);
                default: throw new ArgumentOutOfRangeException("part");
            }
        }

        public static Color Emission(RacketMaterialPart part)
        {
            float strength=part==RacketMaterialPart.Frame?.12f:part==RacketMaterialPart.BlueGraphics?.08f:
                part==RacketMaterialPart.Strings?.06f:part==RacketMaterialPart.WhiteGraphics?.04f:
                part==RacketMaterialPart.OrangeGraphics?.05f:part==RacketMaterialPart.Stencil?.025f:0f;
            Color color=BaseColor(part)*strength;
            color.a=1f;
            return color;
        }

        public static void Apply(Material material,RacketMaterialPart part)
        {
            if(material==null) throw new ArgumentNullException("material");
            material.color=BaseColor(part);
            material.SetFloat("_Metallic",0f);
            material.SetFloat("_Glossiness",part==RacketMaterialPart.Grip?.10f:part==RacketMaterialPart.Strings?.18f:.28f);
            Color emission=Emission(part);
            material.SetColor("_EmissionColor",emission);
            if(emission.maxColorComponent>0f) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
            // Emission is a small visibility floor, not a glowing light source
            // or HDR bloom. It must not alter the hall's baked illumination.
            material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
        }

        public static Material Create(RacketMaterialPart part)
        {
            var material=new Material(Shader.Find("Standard"));
            material.name=AssetName(part);
            Apply(material,part);
            return material;
        }
    }
}
