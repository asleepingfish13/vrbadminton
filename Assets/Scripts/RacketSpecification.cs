using System;
using UnityEngine;

namespace ViveShuttleVR
{
    public enum RacketWeightClass { FourU, ThreeU }

    /// <summary>
    /// Product facts and a deliberately separate, reduced-order simulation model.
    /// Verified 2026-10-09 from Yonex Japan's official shop, linked by yonex.co.jp.
    /// No measured swingweight, balance point, shaft EI or damping was published.
    /// </summary>
    [Serializable]
    public sealed class RacketSpecification
    {
        public const string ProductName = "ASTROX 100 ZZ";
        public const string SourceUrl = "https://yonexshop.jp/item/detail/1_1_AX100ZZ_1";
        public const string SourceDate = "2026-10-09";
        public const string AppearanceReferenceUrl = "https://yonexshop.jp/photo/BDRC/AX100ZZ/zz-AX100ZZ_554-1.jpg";
        public const string AppearanceName = "Dark Navy (554), photo-referenced reconstruction";
        // Appearance colours chosen by visual inspection of the official photo,
        // not manufacturer paint formulas or a calibrated colour measurement.
        public static readonly Color DarkNavyColor = new Color32(20,39,49,255);
        public static readonly Color BlueAccentColor = new Color32(31,102,181,255);
        public static readonly Color WhiteDecalColor = new Color32(215,223,226,255);
        public static readonly Color OrangeAccentColor = new Color32(240,100,33,255);
        public static readonly Color GripColor = new Color32(16,20,25,255);
        public static readonly Color StringColor = new Color32(211,216,215,255);
        public static readonly Color StencilColor = new Color32(219,32,38,255);
        public const string SpecificationNotice = "Official mass/tension; estimated geometry/inertia, not a measured digital twin";
        public const float PublishedLengthExtension = .010f;
        public const float DefaultTensionLbs = 25f;

        public RacketWeightClass weightClass = RacketWeightClass.FourU;
        public float NominalUnstrungMass { get { return weightClass == RacketWeightClass.ThreeU ? .088f : .083f; } }
        public float RecommendedTensionMin { get { return MinTension(weightClass); } }
        public float RecommendedTensionMax { get { return MaxTension(weightClass); } }
        public static float MinTension(RacketWeightClass weight) { return weight == RacketWeightClass.ThreeU ? 21f : 20f; }
        public static float MaxTension(RacketWeightClass weight) { return weight == RacketWeightClass.ThreeU ? 29f : 28f; }

        // Everything below is an adjustable engineering estimate, NOT a Yonex
        // specification. The official page states only "10 mm LONGER", not an
        // absolute length. 675 mm is the nominal geometry chosen for this demo.
        public const float EstimatedLength = .675f;
        public const float ButtY = -.035f;
        public static readonly Vector3 GripCenter = new Vector3(0f, .035f, 0f);
        public static readonly Vector3 HeadCenter = new Vector3(0f, .511f, 0f);
        // String-bed half extents, shared by rendered strings and contact tests.
        public const float HeadHalfWidth = .099f;
        public const float HeadHalfHeight = .121f;
        public const float FrameHalfWidth = .103f;
        public const float FrameHalfHeight = .125f;
        public const float FrameTubeRadius = .004f;
        public const float IsometricExponent = 2.35f;
        public const float EstimatedShaftDiameter = .0065f;

        [Header("Estimated loaded racket properties - not published Yonex measurements")]
        [Tooltip("Estimated installed strings/overgrip addition, kg.")]
        [Range(0f,.02f)] public float fittedMass = .004f;
        [Tooltip("Estimated 4U transverse moment about the grip centre, kg m^2. Not a measured swingweight.")]
        [Range(.006f,.02f)] public float transverseGripInertia4U = .0118f;
        [Tooltip("Estimated 4U twist moment about the shaft, kg m^2.")]
        [Range(.00008f,.001f)] public float twistGripInertia4U = .00028f;

        public float EstimatedPlayingMass { get { return NominalUnstrungMass + Mathf.Clamp(fittedMass,0f,.02f); } }
        public float InertiaScale { get { return EstimatedPlayingMass / .087f; } }
        public float TransverseGripInertia { get { return Mathf.Max(.001f,transverseGripInertia4U)*InertiaScale; } }
        public float TwistGripInertia { get { return Mathf.Max(.00001f,twistGripInertia4U)*InertiaScale; } }

        /// <summary>
        /// Scalar grip-pivot effective mass along a contact direction. This is
        /// I/r^2 for a central face hit, not the total advertised racket weight.
        /// The hand is a prescribed pivot; its mass and active grip impedance are
        /// not measured. The existing contact solver uses this scalar approximation
        /// for recoil, preserving its energy audit without moving the tracked hand.
        /// </summary>
        public float EffectiveContactMass(Vector3 pointInHeadSpace, Vector3 directionInHeadSpace)
        {
            if (directionInHeadSpace.sqrMagnitude < .000001f) return .09f;
            Vector3 arm = HeadCenter - GripCenter + pointInHeadSpace;
            Vector3 torque = Vector3.Cross(arm,directionInHeadSpace.normalized);
            float inverseMass = (torque.x*torque.x + torque.z*torque.z)/TransverseGripInertia +
                                torque.y*torque.y/TwistGripInertia;
            // Axial/near-pivot motion is constrained by the hand. A bounded scalar
            // fallback avoids an artificial infinite effective mass in that axis.
            return Mathf.Clamp(1f/Mathf.Max(.0001f,inverseMass),.02f,.20f);
        }

        public float NormalizedFaceRadius(Vector2 offset)
        {
            return Mathf.Pow(Mathf.Pow(Mathf.Abs(offset.x)/HeadHalfWidth,IsometricExponent) +
                             Mathf.Pow(Mathf.Abs(offset.y)/HeadHalfHeight,IsometricExponent),
                             1f/IsometricExponent);
        }

        public string ShortLabel { get { return ProductName + (weightClass == RacketWeightClass.ThreeU ? " 3U" : " 4U"); } }
        public string TensionNotice(float tension)
        {
            return tension < RecommendedTensionMin || tension > RecommendedTensionMax ?
                "SANDBOX: outside Yonex recommended " + RecommendedTensionMin.ToString("0") + "-" + RecommendedTensionMax.ToString("0") + " lb" :
                "Within Yonex recommended " + RecommendedTensionMin.ToString("0") + "-" + RecommendedTensionMax.ToString("0") + " lb";
        }
    }
}
