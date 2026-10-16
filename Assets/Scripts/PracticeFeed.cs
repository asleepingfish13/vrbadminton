using UnityEngine;

namespace ViveShuttleVR
{
    public enum FeedPattern { Fixed, Mixed }
    public enum FeedKind { Clear, Smash, Drop, Drive, NetShot }

    public struct FeedProfile
    {
        public FeedKind kind;
        public Vector3 sourceLocal, targetLocal;
        public float duration;
        public string label, reachLabel;
    }

    // Pure, deterministic training-feed descriptions. Only the feeder solves a
    // trajectory; these targets are never used to steer a player's return shot.
    public static class PracticeFeed
    {
        // Contact-point comfort profile, independent of impact speed assistance.
        // These are offsets from the actual eye position in court coordinates;
        // they do not move/scale tracking or ask the player to reach with the hand.
        public const float CompactReach = .44f;
        public const float CompactSide = .14f;
        public const float CompactHeight = .04f;

        public static FeedKind[] BalancedSequence(int count, int seed)
        {
            count = Mathf.Max(0, count);
            var result = new FeedKind[count];
            uint random = unchecked((uint)seed) ^ 0x9e3779b9u;
            if (random == 0u) random = 1u;
            int cursor = 0;
            FeedKind previous = (FeedKind)(-1);
            while (cursor < count)
            {
                var block = new[] { FeedKind.Clear, FeedKind.Smash, FeedKind.Drop, FeedKind.Drive, FeedKind.NetShot };
                for (int i = block.Length - 1; i > 0; i--)
                {
                    random ^= random << 13;
                    random ^= random >> 17;
                    random ^= random << 5;
                    int swap = (int)(random % (uint)(i + 1));
                    FeedKind value = block[i];
                    block[i] = block[swap];
                    block[swap] = value;
                }
                if (block[0] == previous)
                {
                    FeedKind value = block[0];
                    block[0] = block[1];
                    block[1] = value;
                }
                foreach (FeedKind kind in block)
                {
                    if (cursor >= count) break;
                    result[cursor++] = kind;
                    previous = kind;
                }
            }
            return result;
        }

        public static FeedProfile Build(FeedKind kind, int handSide, Vector3 eyeLocal,
            float playerCourtZ, float baseReach, int variant, bool compactSeated = false)
        {
            int phase = ((variant % 3) + 3) % 3 - 1;
            float lateral = (handSide < 0 ? -1f : 1f) * (.22f + phase * .035f);
            float reach = Mathf.Clamp(baseReach, .42f, .75f);
            float height = -.12f;
            Vector3 source;
            float duration;
            switch (kind)
            {
                case FeedKind.Clear:
                    source = new Vector3(phase * .45f, 2.35f, 8.4f);
                    reach -= .12f;
                    height = .20f;
                    duration = 1.70f;
                    break;
                case FeedKind.Smash:
                    source = new Vector3(phase * .5f, eyeLocal.y + 2.35f, 8.35f);
                    reach += .02f;
                    height = -.28f;
                    duration = .62f; // Moderated defence feed, not a professional smash speed.
                    break;
                case FeedKind.Drop:
                    source = new Vector3(phase * .35f, 2.6f, 7.4f);
                    reach += .10f;
                    height = -.08f;
                    duration = 1.20f;
                    break;
                case FeedKind.NetShot:
                    source = new Vector3(phase * .35f, 1.6f, 4.8f);
                    reach += .13f;
                    height = -.25f;
                    duration = .85f;
                    break;
                default:
                    kind = FeedKind.Drive;
                    source = new Vector3(phase * .65f, eyeLocal.y + .10f, 7.7f);
                    reach -= .02f;
                    height = -.08f;
                    duration = .55f;
                    break;
            }
            reach = Mathf.Clamp(reach + phase * .025f, .42f, .75f);
            height = Mathf.Clamp(height + phase * .02f, -.35f, .22f);
            if (compactSeated)
            {
                lateral = (handSide < 0 ? -1f : 1f) * (CompactSide + phase * .03f);
                switch (kind)
                {
                    case FeedKind.Clear: reach = .38f; height = .18f; break;
                    case FeedKind.Smash: reach = CompactReach; height = -.02f; break;
                    case FeedKind.Drop: reach = .48f; height = .08f; break;
                    case FeedKind.NetShot: reach = .50f; height = 0f; break;
                    default: reach = .42f; height = CompactHeight; break;
                }
                reach = Mathf.Clamp(reach + phase * .02f, .36f, .50f);
                height = Mathf.Clamp(height + phase * .02f, -.04f, .20f);
            }
            // Stationary small-space mode works seated or standing. Intercepts
            // stay in front of the actual eye position, with no automatic body
            // movement, required stepping, or behind-the-player overhead feeds.
            Vector3 target = new Vector3(eyeLocal.x + lateral, eyeLocal.y + height, eyeLocal.z + reach);
            source.z = Mathf.Max(source.z, playerCourtZ + 1.5f);
            return new FeedProfile { kind = kind, sourceLocal = source, targetLocal = target,
                duration = duration, label = Label(kind),
                reachLabel = compactSeated ? (reach < .40f ? "COMPACT NEAR" : reach > .46f ? "COMPACT FRONT" : "COMPACT MID") :
                    reach < .57f ? "NEAR REACH" : reach > .68f ? "FAR REACH" : "MID REACH" };
        }

        public static FeedProfile Fixed(PracticeDrill drill, int handSide, Vector3 eyeLocal,
            float playerCourtZ, float baseReach, float feedSide, bool compactSeated = false)
        {
            bool net = drill == PracticeDrill.NetTouch;
            bool overhead = drill == PracticeDrill.Overhead;
            Vector3 target = compactSeated ? eyeLocal + new Vector3(
                (handSide < 0 ? -1f : 1f) * CompactSide,
                overhead ? .20f : net ? .02f : CompactHeight, CompactReach) :
                new Vector3((handSide < 0 ? -1f : 1f) * feedSide,
                    Mathf.Max(.65f, eyeLocal.y + (overhead ? .22f : net ? -.16f : -.18f)), playerCourtZ + baseReach);
            return new FeedProfile {
                kind = net ? FeedKind.NetShot : overhead ? FeedKind.Clear : FeedKind.Drive,
                sourceLocal = new Vector3(0f, 1.5f, net ? 4.8f : 8.6f),
                targetLocal = target,
                duration = net ? .85f : 1.75f,
                label = net ? "FIXED NET TOUCH" : overhead ? "FIXED OVERHEAD" : "FIXED RALLY",
                reachLabel = compactSeated ? "COMPACT FIXED" : "FIXED REACH"
            };
        }

        public static string Label(FeedKind kind)
        {
            switch (kind)
            {
                case FeedKind.Clear: return "HIGH CLEAR";
                case FeedKind.Smash: return "SMASH DEFENCE";
                case FeedKind.Drop: return "DROP";
                case FeedKind.NetShot: return "NET-ORIGIN FEED";
                default: return "FLAT DRIVE";
            }
        }
    }
}
