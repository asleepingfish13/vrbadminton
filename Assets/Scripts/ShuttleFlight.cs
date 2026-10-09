using UnityEngine;

namespace ViveShuttleVR
{
    [DefaultExecutionOrder(100)]
    public sealed class ShuttleFlight : MonoBehaviour
    {
        public BadmintonSession session;
        public Transform featherVisual;
        public TrailRenderer trail;
        public bool Flying { get; private set; }
        public bool Returned { get; private set; }
        public Vector3 Velocity { get { return state.velocity; } }
        public Vector3 AngularVelocity { get { return state.angularVelocity; } }
        public ShuttleState State { get { return state; } }
        private ShuttleState state;
        private float age;
        private bool touchedNet, netFault, crossedNetSinceHit;

        public void Launch(Vector3 position, Vector3 velocity)
        {
            gameObject.SetActive(true);
            state=ShuttleState.Launch(position,velocity);
            Flying=true;
            Returned=touchedNet=netFault=crossedNetSinceHit=false;
            age=0f;
            ApplyVisualPose();
            if(trail!=null) { trail.Clear(); trail.emitting=true; }
        }

        public void Stop()
        {
            Flying=false;
            state.velocity=state.angularVelocity=Vector3.zero;
            if(trail!=null) trail.emitting=false;
            gameObject.SetActive(false);
        }

        public bool ReturnShot(Vector3 racketVelocity, Vector3 normal)
        {
            if(normal.sqrMagnitude<.01f) return false;
            return ReturnShot(new RacketContact { point=state.position, center=state.position, centerVelocity=racketVelocity,
                recentPeakSpeed=racketVelocity.magnitude,
                faceRotation=Quaternion.LookRotation(normal,Mathf.Abs(Vector3.Dot(normal.normalized,Vector3.up))>.95f?Vector3.forward:Vector3.up) });
        }

        public bool ReturnShot(RacketContact contact)
        {
            if(!Flying||Returned||!session.Running) return false;
            if(session.IsAiMatch && session.court.InverseTransformPoint(state.position).z>=ShuttleMath.NetZ) return false;
            ImpactResult impact=RacketImpact.Resolve(state,contact,session.PhysicsSettings);
            if(!impact.hit) return false;
            Returned=true;
            state.velocity=impact.outgoingVelocity;
            state.angularVelocity=impact.angularVelocity;
            age=0f;
            touchedNet=netFault=crossedNetSinceHit=false;
            // Preserve pre-impact feather orientation: it must turn over under air load.
            session.OnHit(impact,contact);
            return true;
        }

        public bool OpponentReturn(RacketContact contact)
        {
            if(!Flying||!Returned||!session.Running||!session.IsAiMatch||session.opponent==null||
                !crossedNetSinceHit||netFault||session.court.InverseTransformPoint(state.position).z<=ShuttleMath.NetZ) return false;
            ImpactResult impact=RacketImpact.Resolve(state,contact,session.opponent.PhysicsSettings);
            if(!impact.hit) return false;
            Returned=false;
            state.velocity=impact.outgoingVelocity;
            state.angularVelocity=impact.angularVelocity;
            age=0f;
            touchedNet=netFault=crossedNetSinceHit=false;
            session.OnOpponentHit(impact);
            return true;
        }

        private void LateUpdate() { AdvanceFrame(Time.deltaTime); }

        public void AdvanceFrame(float deltaTime)
        {
            if(!Flying||!session.Running) return;
            if(session.IsAiMatch && Returned && session.opponent!=null) session.opponent.AdvancePlanning();
            float frame=Mathf.Clamp(deltaTime,0f,.08f);
            int steps=Mathf.Max(1,Mathf.CeilToInt(frame/ShuttlePhysics.Step));
            float dt=frame/steps;
            for(int i=0;i<steps&&Flying;i++)
            {
                ShuttleState before=state;
                ShuttleState next=state;
                ShuttlePhysics.Integrate(ref next,session.PhysicsSettings,dt);
                if(!Returned)
                {
                    foreach(RacketController racket in session.rackets)
                    {
                        // A stationary angled racket can block or play a soft net shot.
                        if(!racket.IsTracked || !racket.HasMotionSample) continue;
                        Vector3 a=Vector3.Lerp(racket.PreviousHead,racket.CurrentHead,(float)i/steps);
                        Vector3 b=Vector3.Lerp(racket.PreviousHead,racket.CurrentHead,(float)(i+1)/steps);
                        Quaternion rotationFrom=Quaternion.Slerp(racket.PreviousRotation,racket.head.rotation,(float)i/steps);
                        Quaternion rotationTo=Quaternion.Slerp(racket.PreviousRotation,racket.head.rotation,(float)(i+1)/steps);
                        Vector3 point;
                        Vector3 surfaceNormal;
                        float fraction;
                        if(!ShuttleMath.SweptFaceHit(before.position,next.position,a,b,rotationFrom,rotationTo,session.PhysicsSettings.ContactMargin,out point,out fraction,out surfaceNormal)) continue;
                        Quaternion rotation=Quaternion.Slerp(rotationFrom,rotationTo,fraction);
                        ShuttlePhysics.Integrate(ref state,session.PhysicsSettings,dt*fraction);
                        state.position=point;
                        RacketContact contact=new RacketContact { point=point, center=Vector3.Lerp(a,b,fraction),
                            centerVelocity=racket.HeadVelocity, angularVelocity=racket.AngularVelocity,
                            faceRotation=rotation,surfaceNormal=surfaceNormal,recentPeakSpeed=racket.RecentPeakSpeed };
                        if(ReturnShot(contact))
                        {
                            racket.Pulse(Mathf.Lerp(.1f,.7f,Mathf.Clamp01(state.velocity.magnitude/35f)));
                            ShuttlePhysics.Integrate(ref state,session.PhysicsSettings,dt*(1f-fraction));
                            next=state;
                            break;
                        }
                        state=before;
                    }
                }
                state=next;
                age+=dt;
                Vector3 localFrom=session.court.InverseTransformPoint(before.position);
                Vector3 localNext=session.court.InverseTransformPoint(state.position);
                Vector3 crossing;
                if(ShuttleMath.NetCrossing(localFrom,localNext,out crossing))
                {
                    float netTop=BadmintonCourt.NetHeightAt(crossing.x);
                    crossedNetSinceHit=true;
                    if(Returned) session.OnNetCrossing(crossing.y-netTop);
                    if(!touchedNet&&Mathf.Abs(crossing.x)<=BadmintonCourt.DoublesHalfWidth&&crossing.y<netTop+.018f&&crossing.y>0f)
                    {
                        touchedNet=true;
                        if(crossing.y<netTop-BadmintonCourt.NetDepth-.018f)
                        {
                            // Passing below the physical net is a fault, not a
                            // bounce off an invisible floor-to-tape solid wall.
                            Finish("NET",crossing);
                            break;
                        }
                        Vector3 localVelocity=session.court.InverseTransformDirection(state.velocity);
                        bool tape=crossing.y>netTop-.0375f;
                        netFault=!tape;
                        float travelSign=Mathf.Sign(localVelocity.z);
                        localVelocity=new Vector3(localVelocity.x*.35f,tape?Mathf.Max(.12f,localVelocity.y*.2f):Mathf.Min(0f,localVelocity.y*.15f),localVelocity.z*(tape?.22f:-.06f));
                        state.velocity=session.court.TransformDirection(localVelocity);
                        crossing.z=ShuttleMath.NetZ+travelSign*(tape?.025f:-.025f);
                        state.position=session.court.TransformPoint(crossing);
                        state.angularVelocity+=session.court.right*18f;
                        localNext=crossing;
                    }
                }
                if(session.IsAiMatch && Returned && !touchedNet && session.opponent!=null)
                {
                    RacketContact opponentContact;
                    if(session.opponent.TryContact(before,state,dt,out opponentContact)) OpponentReturn(opponentContact);
                }
                if(localNext.y<=.025f)
                {
                    float t=Mathf.Clamp01((localFrom.y-.025f)/Mathf.Max(.00001f,localFrom.y-localNext.y));
                    Finish(netFault?"NET":Returned?"LAND":"MISS",Vector3.Lerp(localFrom,localNext,t));
                }
                else if(age>10f||Mathf.Abs(localNext.x)>10f||localNext.z<ShuttleMath.NetZ-BadmintonCourt.HalfLength-2f||localNext.z>ShuttleMath.NetZ+BadmintonCourt.HalfLength+2f)
                    Finish(netFault?"NET":Returned?"OUT":"MISS",localNext);
            }
            ApplyVisualPose();
        }

        private void ApplyVisualPose()
        {
            transform.position=state.position;
            if(featherVisual!=null) featherVisual.rotation=state.orientation;
        }

        private void Finish(string reason, Vector3 localPosition)
        {
            bool returned=Returned;
            Stop();
            session.OnFinished(reason,localPosition,returned);
        }
    }
}
