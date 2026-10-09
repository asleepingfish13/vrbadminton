using UnityEngine;

namespace ViveShuttleVR
{
    public enum ShotTechnique { Unknown, Smash, Drop, SliceDrop, NetShot, SpinningNet, Clear, Lift, Drive, Block, Mishit }
    public enum PracticeDrill { Overhead, NetTouch, Rally }

    public struct ShotContext
    {
        public Vector3 position, eye, courtForward;
        public float distanceToNet, heightOverNet;
    }

    public struct ShotReadout
    {
        public ShotTechnique technique;
        public float confidence, launchAngle, speed, spinRpm, swingSpeed, brushSpeed, quality;
        public float contactMilliseconds, indentationMillimeters, slidingFraction, axialSpinRpm;
    }

    // Feature-based coaching hints, not a trained biomechanical classifier. The
    // label never feeds back into impact physics or chooses a landing point.
    public static class ShotClassifier
    {
        public static ShotReadout Classify(ImpactResult impact, ShotContext context)
        {
            Vector3 velocity=impact.outgoingVelocity;
            float horizontal=Vector3.ProjectOnPlane(velocity,Vector3.up).magnitude;
            float angle=Mathf.Atan2(velocity.y,horizontal)*Mathf.Rad2Deg;
            bool overhead=context.position.y>=context.eye.y+.06f;
            bool nearNet=context.distanceToNet>=-.1f && context.distanceToNet<1.45f;
            bool forward=Vector3.Dot(velocity,context.courtForward)>.15f;
            ShotReadout result=new ShotReadout { technique=ShotTechnique.Unknown, confidence=.3f, launchAngle=angle,
                speed=velocity.magnitude, spinRpm=impact.angularVelocity.magnitude*60f/(2f*Mathf.PI),
                swingSpeed=impact.swingSpeed,brushSpeed=impact.brushSpeed,quality=impact.quality,
                contactMilliseconds=impact.contactDuration*1000f, indentationMillimeters=impact.maxIndentation*1000f,
                slidingFraction=impact.slidingFraction, axialSpinRpm=impact.axialSpin*60f/(2f*Mathf.PI) };
            if (!impact.hit) return result;
            if (impact.normalizedOffset>1.02f)
                return Label(result,ShotTechnique.Mishit,.85f);
            if (!forward) return Label(result,ShotTechnique.Unknown,.35f);
            if (nearNet && result.speed<9f && context.heightOverNet<.35f && angle>-25f)
            {
                if (impact.brushSpeed>.65f && impact.sliceRatio>.85f && impact.angularVelocity.magnitude>10f)
                    return Label(result,ShotTechnique.SpinningNet,.8f);
                return Label(result,ShotTechnique.NetShot,.82f);
            }
            if (overhead && angle<-6f && result.speed>13f && impact.normalSwing>2.5f)
                return Label(result,ShotTechnique.Smash,.88f);
            if (overhead && angle<18f && impact.brushSpeed>1f && impact.sliceRatio>.7f)
                return Label(result,ShotTechnique.SliceDrop,.76f);
            if (overhead && result.speed<15f && angle<18f)
                return Label(result,ShotTechnique.Drop,.78f);
            if (angle>25f) return Label(result,overhead?ShotTechnique.Clear:ShotTechnique.Lift,.75f);
            if (Mathf.Abs(angle)<25f && result.speed>8f) return Label(result,ShotTechnique.Drive,.7f);
            return Label(result,ShotTechnique.Block,.55f);
        }

        private static ShotReadout Label(ShotReadout result, ShotTechnique technique, float confidence)
        {
            result.technique=technique;
            result.confidence=confidence*Mathf.Lerp(.65f,1f,result.quality);
            return result;
        }

        public static string Label(ShotTechnique technique)
        {
            switch(technique)
            {
                case ShotTechnique.Smash: return "SMASH";
                case ShotTechnique.Drop: return "DROP SHOT";
                case ShotTechnique.SliceDrop: return "SLICED DROP";
                case ShotTechnique.NetShot: return "NET SHOT";
                case ShotTechnique.SpinningNet: return "SPINNING NET";
                case ShotTechnique.Clear: return "CLEAR";
                case ShotTechnique.Lift: return "LIFT";
                case ShotTechnique.Drive: return "DRIVE";
                case ShotTechnique.Block: return "BLOCK";
                case ShotTechnique.Mishit: return "OFF-CENTER CONTACT";
                default: return "UNCLASSIFIED CONTACT";
            }
        }
    }
}
