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
        public bool Held { get; private set; }
        public bool Returned { get; private set; }
        public Vector3 Velocity { get { return state.velocity; } }
        public Vector3 AngularVelocity { get { return state.angularVelocity; } }
        public ShuttleState State { get { return state; } }
        public bool StandardPlayerLastHit { get; private set; }
        private ShuttleState state;
        private float age;
        private bool touchedNet, netFault, crossedNetSinceHit;
        private bool standardHasContact, standardPlayerSeparated;
        private int standardRallyId;

        public void Launch(Vector3 position, Vector3 velocity)
        {
            gameObject.SetActive(true);
            state=ShuttleState.Launch(position,velocity);
            Flying=true;
            Held=false;
            Returned=touchedNet=netFault=crossedNetSinceHit=false;
            standardHasContact=standardPlayerSeparated=false;
            StandardPlayerLastHit=session!=null && session.IsStandardMatch && session.StandardServerIsPlayer;
            standardRallyId=session!=null && session.IsStandardMatch?session.Singles.CurrentRallyId:0;
            age=0f;
            ApplyVisualPose();
            if(trail!=null) { trail.Clear(); trail.emitting=true; }
        }

        public void Stop()
        {
            Flying=false;
            Held=false;
            state.velocity=state.angularVelocity=Vector3.zero;
            if(trail!=null) trail.emitting=false;
            gameObject.SetActive(false);
        }

        // Held shuttle is visible but has no flight integration or racket contact.
        // The pinch is at the feathers; position remains the cork/contact origin.
        public void HoldAt(Vector3 position)
        {
            if (Flying) return;
            gameObject.SetActive(true);
            Held=true;
            state=ShuttleState.Launch(position,Vector3.zero);
            Returned=touchedNet=netFault=crossedNetSinceHit=false;
            age=0f;
            if (trail!=null) { trail.Clear(); trail.emitting=false; }
            ApplyVisualPose();
        }

        public bool ReleaseHeld(Vector3 velocity)
        {
            if (!Held || session==null || !session.Running) return false;
            Vector3 position=state.position;
            Quaternion orientation=state.orientation;
            Launch(position,Vector3.ClampMagnitude(velocity,2f));
            state.orientation=orientation;
            ApplyVisualPose();
            return true;
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
            if(session!=null && session.IsStandardMatch) return TryStandardContact(true,contact);
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
            if(session!=null && session.IsStandardMatch) return TryStandardContact(false,contact);
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
            if(session!=null && session.IsStandardMatch) { AdvanceStandardFrame(deltaTime); return; }
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
                        if(!racket.CanStrike || !racket.IsTracked || !racket.HasMotionSample) continue;
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
                        if(tape && session.IsAiMatch && Returned && session.opponent!=null)
                            session.opponent.OnIncomingDeflected(state);
                    }
                }
                if(session.IsAiMatch && Returned && session.opponent!=null)
                {
                    if(netFault) session.opponent.ContinueEffort(state,dt);
                    else
                    {
                        RacketContact opponentContact;
                        if(session.opponent.TryContact(before,state,dt,out opponentContact)) OpponentReturn(opponentContact);
                    }
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

        // A regulation service enters through the same impact solver as every
        // return. The supplied state is the pre-impact shuttle, never an already
        // solved outgoing velocity. A failed physical contact cannot start a ball.
        public bool LaunchStandardAiServe(ShuttleState serveState,RacketContact contact)
        {
            if(session==null || !session.IsStandardMatch || !session.StandardRallyActive ||
                session.StandardServerIsPlayer || session.opponent==null || Flying || Held) return false;
            Launch(serveState.position,serveState.velocity);
            state=serveState;
            bool accepted=TryStandardContact(false,contact);
            if(!accepted && Flying) Stop();
            if(Flying) ApplyVisualPose();
            return accepted;
        }

        private bool TryStandardContact(bool player,RacketContact contact)
        {
            if(standardRallyId!=session.Singles.CurrentRallyId) { if(Flying) Stop(); return false; }
            if(!Flying || Held || !session.Running || !session.StandardRallyActive || netFault) return false;
            // Returned remains a compatibility view for AI planning/UI. These
            // explicit fields distinguish an unstruck hand drop from an AI hit.
            if(standardHasContact && (StandardPlayerLastHit==player || !crossedNetSinceHit)) return false;
            ShuttleState incoming=state;
            ShuttlePhysicsSettings settings=player?session.PhysicsSettings:
                session.opponent!=null?session.opponent.PhysicsSettings:null;
            if(settings==null) return false;
            ImpactResult impact=RacketImpact.Resolve(incoming,contact,settings);
            if(!impact.hit) return false;
            string fault;
            if(!session.ValidateStandardContact(player,incoming,contact,impact,out fault))
            {
                FinishStandard(string.IsNullOrEmpty(fault)?"CONTACT_FAULT":fault,
                    session.court.InverseTransformPoint(incoming.position),player);
                return false;
            }
            if(RacketHeadTouchesNet(contact.center,contact.faceRotation,session.court))
            {
                FinishStandard("NET_TOUCH",session.court.InverseTransformPoint(contact.center),player);
                return false;
            }
            standardHasContact=true;
            standardPlayerSeparated=false;
            StandardPlayerLastHit=player;
            Returned=player;
            state.velocity=impact.outgoingVelocity;
            state.angularVelocity=impact.angularVelocity;
            age=0f;
            touchedNet=netFault=crossedNetSinceHit=false;
            session.OnStandardContact(player,incoming,contact,impact);
            if(!Flying || !session.StandardRallyActive) return false;
            if(player) session.OnHit(impact,contact); else session.OnOpponentHit(impact);
            return true;
        }

        private void AdvanceStandardFrame(float deltaTime)
        {
            if(!Flying || !session.Running) return;
            if(standardRallyId!=session.Singles.CurrentRallyId) { Stop(); return; }
            if(!session.StandardRallyActive) { Stop(); return; }
            if(standardHasContact && StandardPlayerLastHit && session.opponent!=null)
                session.opponent.AdvancePlanning();
            float frame=Mathf.Clamp(deltaTime,0f,.08f);
            if(frame<=0f) return;
            int steps=Mathf.Max(1,Mathf.CeilToInt(frame/ShuttlePhysics.Step));
            float dt=frame/steps;
            for(int i=0;i<steps && Flying && session.StandardRallyActive;i++)
            {
                ShuttleState before=state,next=state;
                ShuttlePhysics.Integrate(ref next,session.PhysicsSettings,dt);
                Vector3 localFrom=session.court.InverseTransformPoint(before.position);
                Vector3 localUnhit=session.court.InverseTransformPoint(next.position);
                float groundFraction=localUnhit.y<=.025f ? Mathf.Clamp01((localFrom.y-.025f)/
                    Mathf.Max(.00001f,localFrom.y-localUnhit.y)) : float.PositiveInfinity;
                bool playerCanReceive=!standardHasContact || (!StandardPlayerLastHit && crossedNetSinceHit);
                if(session.rackets!=null)
                foreach(RacketController racket in session.rackets)
                {
                    if(racket==null || !racket.CanStrike || !racket.IsTracked || !racket.HasMotionSample || racket.head==null) continue;
                    Vector3 a=Vector3.Lerp(racket.PreviousHead,racket.CurrentHead,(float)i/steps);
                    Vector3 b=Vector3.Lerp(racket.PreviousHead,racket.CurrentHead,(float)(i+1)/steps);
                    Quaternion ra=Quaternion.Slerp(racket.PreviousRotation,racket.head.rotation,(float)i/steps);
                    Quaternion rb=Quaternion.Slerp(racket.PreviousRotation,racket.head.rotation,(float)(i+1)/steps);
                    float liveFraction=Mathf.Min(1f,groundFraction);
                    Vector3 liveHead=Vector3.Lerp(a,b,liveFraction);
                    Quaternion liveRotation=Quaternion.Slerp(ra,rb,liveFraction);
                    // Measured racket-head contact only: this is not body/feet,
                    // shaft/post collision or complete umpire-grade detection.
                    if(RacketHeadTouchesNet(a,ra,session.court) ||
                        RacketHeadTouchesNet(Vector3.Lerp(a,liveHead,.5f),Quaternion.Slerp(ra,liveRotation,.5f),session.court) ||
                        RacketHeadTouchesNet(liveHead,liveRotation,session.court))
                    {
                        FinishStandard("NET_TOUCH",session.court.InverseTransformPoint(b),true);
                        break;
                    }
                    if(standardHasContact && StandardPlayerLastHit && IsSeparatedFromFace(before.position,a,ra))
                        standardPlayerSeparated=true;
                    bool secondStroke=standardHasContact && StandardPlayerLastHit && standardPlayerSeparated && age>=.04f;
                    if((!playerCanReceive && !secondStroke) || netFault) continue;
                    Vector3 point,normal;
                    float fraction;
                    if(!ShuttleMath.SweptFaceHit(before.position,next.position,a,b,ra,rb,
                        session.PhysicsSettings.ContactMargin,out point,out fraction,out normal) || fraction>=groundFraction) continue;
                    Quaternion rotation=Quaternion.Slerp(ra,rb,fraction);
                    state=before;
                    ShuttlePhysics.Integrate(ref state,session.PhysicsSettings,dt*fraction);
                    state.position=point;
                    RacketContact contact=new RacketContact { point=point,center=Vector3.Lerp(a,b,fraction),
                        centerVelocity=racket.HeadVelocity,angularVelocity=racket.AngularVelocity,
                        faceRotation=rotation,surfaceNormal=normal,recentPeakSpeed=racket.RecentPeakSpeed };
                    if(secondStroke)
                    {
                        if(RacketImpact.Resolve(state,contact,session.PhysicsSettings).hit)
                        { FinishStandard("DOUBLE_HIT",session.court.InverseTransformPoint(point),true); break; }
                        state=before;
                        continue;
                    }
                    if(ReturnShot(contact))
                    {
                        racket.Pulse(Mathf.Lerp(.1f,.7f,Mathf.Clamp01(state.velocity.magnitude/35f)));
                        ShuttlePhysics.Integrate(ref state,session.PhysicsSettings,dt*(1f-fraction));
                        next=state;
                        break;
                    }
                    // Validation may already have ended the point. Never revive
                    // its old integrator state after a rejected service contact.
                    if(!Flying || !session.StandardRallyActive) break;
                    state=before;
                }
                if(!Flying || !session.StandardRallyActive) break;
                state=next;
                age+=dt;
                Vector3 localNext=session.court.InverseTransformPoint(state.position),crossing;
                bool reachedGround=localNext.y<=.025f;
                if(reachedGround)
                {
                    float deadline=Mathf.Clamp01((localFrom.y-.025f)/Mathf.Max(.00001f,localFrom.y-localNext.y));
                    localNext=Vector3.Lerp(localFrom,localNext,deadline);
                    state.position=session.court.TransformPoint(localNext);
                }
                if(ShuttleMath.NetCrossing(localFrom,localNext,out crossing))
                {
                    if(!session.OnStandardNetCrossing(crossing,standardRallyId)) { Stop(); break; }
                    if(!Flying || !session.StandardRallyActive) break;
                    float netTop=BadmintonCourt.NetHeightAt(crossing.x);
                    crossedNetSinceHit=true;
                    if(StandardPlayerLastHit) session.OnNetCrossing(crossing.y-netTop);
                    if(!touchedNet && Mathf.Abs(crossing.x)<=BadmintonCourt.DoublesHalfWidth && crossing.y<netTop+.018f && crossing.y>0f)
                    {
                        touchedNet=true;
                        if(crossing.y<netTop-BadmintonCourt.NetDepth-.018f)
                        { FinishStandard("NET",crossing,StandardPlayerLastHit); break; }
                        Vector3 velocity=session.court.InverseTransformDirection(state.velocity);
                        bool tape=crossing.y>netTop-.0375f;
                        netFault=!tape;
                        float direction=Mathf.Sign(velocity.z);
                        velocity=new Vector3(velocity.x*.35f,tape?Mathf.Max(.12f,velocity.y*.2f):Mathf.Min(0f,velocity.y*.15f),velocity.z*(tape?.22f:-.06f));
                        state.velocity=session.court.TransformDirection(velocity);
                        crossing.z=ShuttleMath.NetZ+direction*(tape?.025f:-.025f);
                        state.position=session.court.TransformPoint(crossing);
                        state.angularVelocity+=session.court.right*18f;
                        localNext=crossing;
                        // A net-body rebound has already started falling on
                        // the striker's side: the shuttle is no longer in play.
                        // Do not let a later racket/net touch replace this point.
                        // Tape deflections which pass over remain live.
                        if(!tape) { FinishStandard("NET",crossing,StandardPlayerLastHit); break; }
                        if(tape && StandardPlayerLastHit && session.opponent!=null) session.opponent.OnIncomingDeflected(state);
                    }
                }
                // Ground is a deadline, not a contact opportunity. The player's
                // swept contact above was also clipped to its first ground time.
                if(reachedGround || localNext.y<=.025f)
                {
                    float t=Mathf.Clamp01((localFrom.y-.025f)/Mathf.Max(.00001f,localFrom.y-localNext.y));
                    Vector3 landing=Vector3.Lerp(localFrom,localNext,t);
                    string reason=netFault?"NET":session.StandardServicePending && !session.IsStandardServiceLandingLegal(landing)?
                        "SERVICE_OUT":StandardPlayerLastHit?"LAND":"MISS";
                    FinishStandard(reason,landing,StandardPlayerLastHit);
                    break;
                }
                if(age>10f || Mathf.Abs(localNext.x)>10f || localNext.z<ShuttleMath.NetZ-BadmintonCourt.HalfLength-2f ||
                    localNext.z>ShuttleMath.NetZ+BadmintonCourt.HalfLength+2f)
                { FinishStandard(netFault?"NET":"OUT",localNext,StandardPlayerLastHit); break; }
                if(standardHasContact && session.opponent!=null &&
                    RacketHeadTouchesNet(session.opponent.RacketHeadWorld,session.opponent.RacketHeadRotation,session.court))
                { FinishStandard("NET_TOUCH",session.court.InverseTransformPoint(session.opponent.RacketHeadWorld),false); break; }
                if(standardHasContact && StandardPlayerLastHit && session.opponent!=null)
                {
                    if(netFault) session.opponent.ContinueEffort(state,dt);
                    else
                    {
                        RacketContact contact;
                        if(session.opponent.TryContact(before,state,dt,out contact)) OpponentReturn(contact);
                    }
                }
            }
            if(Flying) ApplyVisualPose();
        }

        private static bool IsSeparatedFromFace(Vector3 point,Vector3 centre,Quaternion rotation)
        {
            Vector3 local=Quaternion.Inverse(rotation)*(point-centre);
            return Mathf.Abs(local.z)>.065f || Mathf.Abs(local.x)>RacketSpecification.HeadHalfWidth+.05f ||
                Mathf.Abs(local.y)>RacketSpecification.HeadHalfHeight+.05f;
        }

        // Finite sampled string-bed/frame surface against the physical net panel,
        // including sag. Narrow-phase only; the broad phase keeps remote rackets
        // cheap. No enlarged court or shuttle-contact assist margin is used here.
        public static bool RacketHeadTouchesNet(Vector3 centre,Quaternion rotation,Transform court)
        {
            if(court==null) return false;
            Vector3 localCentre=court.InverseTransformPoint(centre);
            const float bound=.17f;
            if(Mathf.Abs(localCentre.z-BadmintonCourt.NetZ)>bound ||
                Mathf.Abs(localCentre.x)>BadmintonCourt.DoublesHalfWidth+bound ||
                localCentre.y<BadmintonCourt.NetCentreHeight-BadmintonCourt.NetDepth-bound ||
                localCentre.y>BadmintonCourt.NetPostHeight+bound) return false;
            Vector3 previous=Vector3.zero;
            for(int i=0;i<=32;i++)
            {
                float angle=i*Mathf.PI*2f/32f,c=Mathf.Cos(angle),s=Mathf.Sin(angle),power=2f/RacketSpecification.IsometricExponent;
                Vector3 rim=new Vector3(Mathf.Sign(c)*Mathf.Pow(Mathf.Abs(c),power)*RacketSpecification.FrameHalfWidth,
                    Mathf.Sign(s)*Mathf.Pow(Mathf.Abs(s),power)*RacketSpecification.FrameHalfHeight,0f);
                Vector3 point=court.InverseTransformPoint(centre+rotation*rim);
                if(SegmentTouchesNet(localCentre,point) || (i>0 && SegmentTouchesNet(previous,point))) return true;
                previous=point;
            }
            return false;
        }

        private static bool SegmentTouchesNet(Vector3 a,Vector3 b)
        {
            const float thickness=RacketSpecification.FrameTubeRadius;
            float za=a.z-BadmintonCourt.NetZ,zb=b.z-BadmintonCourt.NetZ;
            if(Mathf.Min(za,zb)>thickness || Mathf.Max(za,zb)<-thickness) return false;
            float t=Mathf.Abs(zb-za)>.000001f?Mathf.Clamp01(-za/(zb-za)):.5f;
            Vector3 p=Vector3.Lerp(a,b,t);
            if(Mathf.Abs(p.x)>BadmintonCourt.DoublesHalfWidth+thickness) return false;
            float top=BadmintonCourt.NetHeightAt(p.x);
            return p.y>=top-BadmintonCourt.NetDepth-thickness && p.y<=top+thickness;
        }

        private void FinishStandard(string reason,Vector3 localPosition,bool playerLastHitOrOffender)
        {
            if(!Flying) return;
            Stop();
            session.FinishStandardRally(reason,localPosition,playerLastHitOrOffender,standardRallyId);
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
