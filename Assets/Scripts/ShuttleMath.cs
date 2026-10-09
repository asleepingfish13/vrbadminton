using UnityEngine;

namespace ViveShuttleVR
{
    public static class ShuttleMath
    {
        public const float Step=ShuttlePhysics.Step;
        public const float NetZ=3.6f;
        public const float NetHeight=1.3f; // Retained training-court geometry.
        private static readonly ShuttlePhysicsSettings defaults=new ShuttlePhysicsSettings();

        public static Vector3 Predict(Vector3 start, Vector3 velocity, float duration, ShuttlePhysicsSettings settings=null)
        {
            return ShuttlePhysics.Predict(ShuttleState.Launch(start,velocity),duration,settings??defaults).position;
        }

        // Only the FEEDER uses a target solver. Player hits never call this.
        public static Vector3 SolveLaunch(Vector3 start, Vector3 target, float duration, ShuttlePhysicsSettings settings=null)
        {
            settings=settings??defaults;
            duration=Mathf.Clamp(duration,.35f,3f);
            Vector3 velocity=(target-start)/duration+Vector3.up*ShuttlePhysics.Gravity*duration*.5f;
            velocity*=Mathf.Exp(Mathf.Min(1.1f,settings.LinearDragFactor*Vector3.Distance(start,target)*.35f));
            for(int i=0;i<16;i++)
            {
                Vector3 predicted=Predict(start,velocity,duration,settings);
                Vector3 error=target-predicted;
                if(error.sqrMagnitude<.000001f) break;
                const float epsilon=.05f;
                Vector3 dx=(Predict(start,velocity+Vector3.right*epsilon,duration,settings)-predicted)/epsilon;
                Vector3 dy=(Predict(start,velocity+Vector3.up*epsilon,duration,settings)-predicted)/epsilon;
                Vector3 dz=(Predict(start,velocity+Vector3.forward*epsilon,duration,settings)-predicted)/epsilon;
                float determinant=Vector3.Dot(dx,Vector3.Cross(dy,dz));
                if(Mathf.Abs(determinant)<.000001f) break;
                Vector3 correction=new Vector3(Vector3.Dot(error,Vector3.Cross(dy,dz)),
                    Vector3.Dot(dx,Vector3.Cross(error,dz)),Vector3.Dot(dx,Vector3.Cross(dy,error)))/determinant;
                velocity+=Vector3.ClampMagnitude(correction,35f);
            }
            return velocity;
        }

        public static bool SweptFaceHit(Vector3 from, Vector3 to, Vector3 racketFrom, Vector3 racketTo, Quaternion rotation, out Vector3 contact)
        {
            float fraction;
            return SweptFaceHit(from,to,racketFrom,racketTo,rotation,.025f,out contact,out fraction);
        }

        public static bool SweptFaceHit(Vector3 from, Vector3 to, Vector3 racketFrom, Vector3 racketTo, Quaternion rotation,
            float margin, out Vector3 contact, out float fraction)
        {
            Vector3 surfaceNormal;
            return SweptFaceHit(from,to,racketFrom,racketTo,rotation,rotation,margin,out contact,out fraction,out surfaceNormal);
        }

        // Sweep the same interpolated centre and face orientations that are used
        // to evaluate impact velocity. The normal's SIGN comes from geometry.
        public static bool SweptFaceHit(Vector3 from, Vector3 to, Vector3 racketFrom, Vector3 racketTo,
            Quaternion rotationFrom, Quaternion rotationTo, float margin,
            out Vector3 contact, out float fraction, out Vector3 surfaceNormal)
        {
            contact=from;
            fraction=0f;
            surfaceNormal=Vector3.zero;
            Vector3 a=Quaternion.Inverse(rotationFrom)*(from-racketFrom);
            Vector3 b=Quaternion.Inverse(rotationTo)*(to-racketTo);
            bool crossesPlane=(a.z>0f&&b.z<=0f)||(a.z<0f&&b.z>=0f);
            if (!crossesPlane && Mathf.Abs(b.z)>=Mathf.Abs(a.z)-.000001f) return false;
            if (Mathf.Abs(a.z)<.000001f) return false; // No known incident side.
            float side=Mathf.Sign(a.z);
            if (crossesPlane)
            {
                float low=0f, high=1f;
                // Rotation makes signed plane distance nonlinear; bisect the
                // actual interpolated pose instead of using the middle normal.
                for (int i=0;i<20;i++)
                {
                    float mid=(low+high)*.5f;
                    Vector3 local=Quaternion.Inverse(Quaternion.Slerp(rotationFrom,rotationTo,mid))*
                        (Vector3.Lerp(from,to,mid)-Vector3.Lerp(racketFrom,racketTo,mid));
                    if (local.z*side>0f) low=mid; else high=mid;
                }
                fraction=(low+high)*.5f;
            }
            else fraction=1f; // Approaching contact within the explicit tolerance.
            Quaternion rotation=Quaternion.Slerp(rotationFrom,rotationTo,fraction);
            contact=Vector3.Lerp(from,to,fraction);
            Vector3 p=Quaternion.Inverse(rotation)*(contact-Vector3.Lerp(racketFrom,racketTo,fraction));
            float width=.14f+.018f+margin, height=.185f+.018f+margin;
            surfaceNormal=rotation*Vector3.forward*side;
            return Mathf.Abs(p.z)<=.018f+margin && p.x*p.x/(width*width)+p.y*p.y/(height*height)<=1f;
        }

        public static bool NetCrossing(Vector3 from, Vector3 to, out Vector3 crossing)
        {
            crossing=to;
            if((from.z-NetZ)*(to.z-NetZ)>0f || Mathf.Abs(to.z-from.z)<.00001f) return false;
            crossing=Vector3.Lerp(from,to,Mathf.Clamp01((NetZ-from.z)/(to.z-from.z)));
            return true;
        }

        public static bool CrossesNet(Vector3 from, Vector3 to)
        {
            Vector3 crossing;
            return NetCrossing(from,to,out crossing)&&Mathf.Abs(crossing.x)<2.76f&&crossing.y<NetHeight+.018f&&crossing.y>0f;
        }

        public static bool IsInOpponentCourt(Vector3 local)
        {
            return Mathf.Abs(local.x)<=2.7f&&local.z>NetZ&&local.z<=9.2f;
        }
    }
}
