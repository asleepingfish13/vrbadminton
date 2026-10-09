using UnityEngine;

namespace ViveShuttleVR
{
    // BWF Laws 26 April 2025, section 1 and Diagram A. Metres throughout.
    // World origin is arbitrary: NetZ is retained for the stationary VR calibration.
    public static class BadmintonCourt
    {
        public const float Length = 13.4f;
        public const float HalfLength = 6.7f;
        public const float DoublesWidth = 6.1f;
        public const float DoublesHalfWidth = 3.05f;
        public const float SinglesWidth = 5.18f;
        public const float SinglesHalfWidth = 2.59f;
        public const float LineWidth = .04f;
        public const float NetZ = 3.6f;
        public const float NearBaseline = NetZ - HalfLength;
        public const float FarBaseline = NetZ + HalfLength;
        // Distances reference the edge which belongs to the service court, not line centres.
        public const float ShortServiceDistance = 1.98f;
        public const float DoublesLongServiceDistance = HalfLength - .76f;
        public const float NetPostHeight = 1.55f;
        public const float NetCentreHeight = 1.524f;
        public const float NetCenterHeight = NetCentreHeight;
        public const float NetDepth = .760f;
        public const float NetTapeUnfoldedWidth = .075f;
        public const float NetTapeFaceDepth = NetTapeUnfoldedWidth * .5f;
        public const float NetCordDiameter = .0012f;
        // 340 spaces across 6.1 m = 17.94 mm; 42 spaces deep = 18.10 mm.
        public const int NetMeshColumns = 340;
        public const int NetMeshRows = 42;

        public struct Marking
        {
            public string name;
            public Vector2 centre; // x and court z
            public Vector2 size;
            public Marking(string name, float x, float z, float width, float depth)
            {
                this.name = name; centre = new Vector2(x, z); size = new Vector2(width, depth);
            }
        }

        public static Marking[] Markings()
        {
            float h = LineWidth * .5f;
            return new[] {
                new Marking("Near baseline", 0f, NearBaseline+h, DoublesWidth, LineWidth),
                new Marking("Far baseline", 0f, FarBaseline-h, DoublesWidth, LineWidth),
                new Marking("Doubles left", -DoublesHalfWidth+h, NetZ, LineWidth, Length),
                new Marking("Doubles right", DoublesHalfWidth-h, NetZ, LineWidth, Length),
                new Marking("Singles left", -SinglesHalfWidth+h, NetZ, LineWidth, Length),
                new Marking("Singles right", SinglesHalfWidth-h, NetZ, LineWidth, Length),
                new Marking("Near short service", 0f, NetZ-ShortServiceDistance-h, DoublesWidth, LineWidth),
                new Marking("Far short service", 0f, NetZ+ShortServiceDistance+h, DoublesWidth, LineWidth),
                new Marking("Near doubles long service", 0f, NetZ-DoublesLongServiceDistance+h, DoublesWidth, LineWidth),
                new Marking("Far doubles long service", 0f, NetZ+DoublesLongServiceDistance-h, DoublesWidth, LineWidth),
                new Marking("Near centre service", 0f, (NearBaseline+NetZ-ShortServiceDistance)*.5f, LineWidth, HalfLength-ShortServiceDistance),
                new Marking("Far centre service", 0f, (FarBaseline+NetZ+ShortServiceDistance)*.5f, LineWidth, HalfLength-ShortServiceDistance)
            };
        }

        // Smooth sag is a geometric approximation; the three mandated heights are exact.
        public static float NetHeightAt(float courtX)
        {
            float t = Mathf.Clamp01(Mathf.Abs(courtX) / DoublesHalfWidth);
            return NetCentreHeight + (NetPostHeight-NetCentreHeight) * t * t;
        }

        // A touching boundary counts IN. Optional footprint is the first-contact cork radius.
        public static bool IsInSingles(Vector3 localPoint, float contactRadius = 0f)
        {
            return IsInCourt(localPoint, SinglesHalfWidth, contactRadius);
        }

        public static bool IsInDoubles(Vector3 localPoint, float contactRadius = 0f)
        {
            return IsInCourt(localPoint, DoublesHalfWidth, contactRadius);
        }

        private static bool IsInCourt(Vector3 p, float halfWidth, float contactRadius)
        {
            if (!Finite(p.x) || !Finite(p.z) || !Finite(contactRadius)) return false;
            float radius = Mathf.Max(0f, contactRadius);
            float outsideX = Mathf.Max(0f, Mathf.Abs(p.x)-halfWidth);
            float outsideZ = Mathf.Max(0f, NearBaseline-p.z, p.z-FarBaseline);
            return outsideX*outsideX + outsideZ*outsideZ <= radius*radius;
        }

        public static bool IsOnFarSinglesSide(Vector3 p, float contactRadius = 0f)
        {
            return p.z >= NetZ && IsInSingles(p, contactRadius);
        }

        public static bool IsOnNearSinglesSide(Vector3 p, float contactRadius = 0f)
        {
            return p.z <= NetZ && IsInSingles(p, contactRadius);
        }

        public static bool IsInServiceCourt(Vector3 p, bool farSide, bool rightSide, bool doubles)
        {
            if (!Finite(p.x) || !Finite(p.z)) return false;
            float relativeZ = (p.z-NetZ) * (farSide ? 1f : -1f);
            float limitX = doubles ? DoublesHalfWidth : SinglesHalfWidth;
            float limitZ = doubles ? DoublesLongServiceDistance : HalfLength;
            // Centre line belongs to both adjoining service courts.
            bool correctHalf = rightSide ? p.x >= -LineWidth*.5f : p.x <= LineWidth*.5f;
            return correctHalf && Mathf.Abs(p.x) <= limitX &&
                relativeZ >= ShortServiceDistance && relativeZ <= limitZ;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
