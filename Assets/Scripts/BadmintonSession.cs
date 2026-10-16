using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace ViveShuttleVR
{
    public enum PracticeMode { AiRally, Training, SinglesMatch }
    public enum EquipmentSetting { Mode, Difficulty, Drill, Tension, RacketWeight, Mass, FeedPattern }

    public sealed partial class BadmintonSession : MonoBehaviour
    {
        public Transform court;
        public Camera playerCamera;
        public ShuttleFlight shuttle;
        public RacketController[] rackets;
        public Transform reachMarker, landingMarker, targetMarker;
        public TextMesh scoreText, statusText, detailText;
        public AudioSource audioSource;
        [Range(0.4f, 0.9f)] public float seatedReach = 0.62f;
        [Range(0.1f, 0.4f)] public float feedSide = 0.22f;
        [Tooltip("Closer, higher contact targets for seated play; independent of swing-speed assistance. Disable to use the original wider training reach.")]
        public bool compactSeatedTargets = true;
        public ShuttleHand shuttleHand;
        public bool HandServeHeld { get { return heldServeHand != null && shuttle != null && shuttle.Held; } }
        public int roundLength = 20;
        public ShuttlePhysicsSettings physics = new ShuttlePhysicsSettings();
        public PracticeDrill drill = PracticeDrill.Rally;
        public PracticeMode mode = PracticeMode.SinglesMatch;
        public OpponentDifficulty Difficulty = OpponentDifficulty.Easy;
        public BadmintonOpponent opponent;
        public AthletePresentation athletePrefab;
        [Tooltip("Calibrate the court to a virtual standing stature without scaling or overriding tracked head/hand motion.")]
        public bool simulatePlayerHeight = true;
        [Range(1.4f, 2.1f)] public float simulatedStature = 1.75f;
        // Eye-to-crown offset is an avatar convention, not an anthropometric measurement.
        public const float SimulatedEyeBelowCrown = .12f;
        public float VirtualEyeHeight { get { return Mathf.Clamp(simulatedStature - SimulatedEyeBelowCrown, 1.1f, 2.0f); } }
        public bool IsAiMatch { get { return mode != PracticeMode.Training; } }
        public bool IsStandardMatch { get { return mode == PracticeMode.SinglesMatch; } }
        public const int PointsToWin = 7;
        private int practicePlayerPoints, practiceOpponentPoints;
        public int PlayerPoints { get { return IsStandardMatch ? Singles.PlayerPoints : practicePlayerPoints; } private set { practicePlayerPoints = value; } }
        public int OpponentPoints { get { return IsStandardMatch ? Singles.OpponentPoints : practiceOpponentPoints; } private set { practiceOpponentPoints = value; } }
        public int RallyStrokes { get; private set; }
        public bool MatchComplete { get { return IsStandardMatch ? Singles.Phase == SinglesPhase.MatchOver : PlayerPoints >= PointsToWin || OpponentPoints >= PointsToWin; } }
        public FeedPattern feedPattern = FeedPattern.Mixed;
        [Tooltip("Mixed rounds repeat this balanced, non-repeating order for comparison tests.")]
        public int feedSeed = 25054;
        public ShuttlePhysicsSettings PhysicsSettings { get { return physics; } }
        public ShotReadout LastShot { get; private set; }
        public FeedProfile LastFeedProfile { get; private set; }
        public float LastNetClearance { get; private set; }
        public bool Running { get; private set; }
        public int Score { get; private set; }
        public int Feeds { get; private set; }
        public int Hits { get; private set; }
        public int Combo { get; private set; }
        public EquipmentSetting SelectedSetting { get; private set; }
        public string EquipmentSummary
        {
            get { return (physics.racketWeightClass == RacketWeightClass.FourU ? "100 ZZ 4U  |  " : "100 ZZ 3U  |  ") + physics.stringTensionLbs.ToString("F0") + " 磅  |  " +
                (physics.mass * 1000f).ToString("F1") + " 克  |  辅助" + (physics.seatedAssist ? "开启" : "关闭"); }
        }
        private bool xrSession;
        public bool IsVR
        {
            get
            {
                // OpenXR can have an initialized loader several frames before
                // isDeviceActive becomes true. Never disable head tracking in
                // that gap, nor silently switch to desktop after tracking loss.
                XRGeneralSettings settings = XRGeneralSettings.Instance;
                bool loaderReady = settings != null && settings.Manager != null && settings.Manager.activeLoader != null;
                xrSession |= XRSettings.isDeviceActive || loaderReady;
                return xrSession;
            }
        }
        private bool calibrated;
        private TextMesh alignmentHint;
        private TextMesh equipmentText;
        private int side = 1;
        private float feedTimer;
        private ShuttleHand heldServeHand;
        private bool heldReleaseArmed, hasHeldSample;
        private Vector3 previousHeldPosition, heldReleaseVelocity;
        private FeedKind[] feedSequence;
        private AudioClip hitSound, scoreSound, feedSound;
        private readonly Vector3[] targets = { new Vector3(0f, 0.03f, 7f), new Vector3(-1.6f, 0.03f, 6.2f), new Vector3(1.6f, 0.03f, 8f) };
        private Vector3 currentTarget;
        private Transform feederAssembly;
        private float PlayerCourtZ { get { return drill == PracticeDrill.NetTouch ? 2.4f : drill == PracticeDrill.Overhead ? 1.2f : 0f; } }

        private IEnumerator Start()
        {
            hitSound = MakeTone("Racket tap", 900f, 0.075f);
            scoreSound = MakeTone("Point", 1300f, 0.15f);
            feedSound = MakeTone("Feed cue", 520f, 0.1f);
            CreateAlignmentHint();
            shuttle.Stop();
            UpdateHud();
            statusText.text = "请面向正前方";
            detailText.text = IsStandardMatch ? "正式单打 / 先完成掷币选择\n长按菜单键：重新校准" :
                "左扳机：持球  |  右扳机：自动开始\n长按菜单键：重新校准";
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetInstances(inputs);
            foreach (XRInputSubsystem input in inputs)
                input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            yield return null;
            if (!IsVR)
            {
                TrackedPoseDriver driver = playerCamera.GetComponent<TrackedPoseDriver>();
                if (driver != null) driver.enabled = false;
                playerCamera.transform.localPosition = new Vector3(0f, VirtualEyeHeight, 0f);
                playerCamera.transform.localRotation = Quaternion.identity;
            }
            Debug.Log("[Shuttle VR] Startup mode=" + (IsVR ? "VR" : "Desktop") +
                "; HeadPoseDriver=" + playerCamera.GetComponent<TrackedPoseDriver>().enabled + "; ContactModel=LowerGripActiveAiV5" +
                "; Equipment=" + EquipmentSummary + "; Mode=" + mode + "; AI=" + Difficulty + "; VirtualStature=" + simulatedStature.ToString("F2") + "; CompactSeated=" + compactSeatedTargets + "; LeftHandServe=" + (shuttleHand != null) + "; Court=13.40x6.10; PlayArea=Stationary; Rules=BWF21SmallSpaceV6");
            // Show the idle opponent ahead once tracking is stable. The first
            // deliberate start press still calibrates again to the chosen forward.
            float stable = 0f;
            while (stable < 0.3f)
            {
                stable = HeadReady() ? stable + Time.unscaledDeltaTime : 0f;
                yield return null;
            }
            if (!calibrated) Recenter();
        }

        private bool HeadReady()
        {
            if (!IsVR) return true;
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool tracked, present;
            return head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out tracked) && tracked &&
                (!head.TryGetFeatureValue(CommonUsages.userPresence, out present) || present);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R)) Recenter();
            if (Input.GetKeyDown(KeyCode.Space)) StartPractice(side < 0 ? XRNode.LeftHand : XRNode.RightHand);
            if (Input.GetKeyDown(KeyCode.P)) TogglePause();
            if (!Running)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) SetDrill(PracticeDrill.Rally);
                if (Input.GetKeyDown(KeyCode.Alpha2)) SetDrill(PracticeDrill.Overhead);
                if (Input.GetKeyDown(KeyCode.Alpha3)) SetDrill(PracticeDrill.NetTouch);
                if (Input.GetKeyDown(KeyCode.A)) ToggleAssist();
                if (Input.GetKeyDown(KeyCode.LeftBracket)) AdjustTension(-1);
                if (Input.GetKeyDown(KeyCode.RightBracket)) AdjustTension(1);
                if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) AdjustMass(-.1f);
                if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) AdjustMass(.1f);
                if (Input.GetKeyDown(KeyCode.Backspace)) ResetEquipment();
                if (Input.GetKeyDown(KeyCode.M)) SetFeedPattern(feedPattern == FeedPattern.Mixed ? FeedPattern.Fixed : FeedPattern.Mixed);
                if (Input.GetKeyDown(KeyCode.F4)) SetMode(NextMode());
                if (Input.GetKeyDown(KeyCode.F5)) SetDifficulty(OpponentDifficulty.Easy);
                if (Input.GetKeyDown(KeyCode.F6)) SetDifficulty(OpponentDifficulty.Club);
                if (Input.GetKeyDown(KeyCode.F7)) SetDifficulty(OpponentDifficulty.Advanced);
                if (Input.GetKeyDown(KeyCode.B)) SetRacketWeight(physics.racketWeightClass == RacketWeightClass.FourU ? RacketWeightClass.ThreeU : RacketWeightClass.FourU);
                if (Input.GetKeyDown(KeyCode.H)) SetCompactTargets(!compactSeatedTargets);
            }
            if (!Running) return;
            bool controllerReady = !IsVR || RequiredControllersTracked();
            if (!HeadReady() || !controllerReady)
            {
                Running = false;
                statusText.text = "追踪中断，已暂停";
                detailText.text = "请戴好头显并开启控制器\n恢复追踪后按右扳机继续";
                return;
            }
            if (IsStandardMatch && AdvanceStandardInterval(Time.deltaTime)) return;
            if (!shuttle.Flying && !HandServeHeld)
            {
                feedTimer -= Time.deltaTime;
                if (feedTimer <= 0f) Serve();
            }
            if (IsAiMatch && opponent != null && (!IsStandardMatch || standardServiceDelivered) && (!shuttle.Flying || !shuttle.Returned)) opponent.Idle(Time.deltaTime);
        }

        private bool TryGetHeadWorldPose(out Vector3 eye, out Quaternion rotation)
        {
            eye = playerCamera.transform.position;
            rotation = playerCamera.transform.rotation;
            if (!IsVR) return true;
            if (!HeadReady()) return false;
            // Controller Update executes before TrackedPoseDriver.Update. Read
            // the same pose source directly instead of last frame's camera pose.
            TrackedPoseDriver driver = playerCamera.GetComponent<TrackedPoseDriver>();
            Pose pose;
            if (!PoseDataSource.TryGetDataFromSource(driver != null ? driver.poseSource : TrackedPoseDriver.TrackedPose.Center, out pose))
                return false;
            Transform origin = playerCamera.transform.parent;
            eye = origin != null ? origin.TransformPoint(pose.position) : pose.position;
            rotation = origin != null ? origin.rotation * pose.rotation : pose.rotation;
            return true;
        }

        public bool Recenter()
        {
            Vector3 eye;
            Quaternion headRotation;
            if (!TryGetHeadWorldPose(out eye, out headRotation))
            {
                Running = false;
                statusText.text = "等待头显恢复追踪";
                return false;
            }
            Vector3 forward = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
            {
                Running = false;
                statusText.text = "面向正前方，再长按菜单键或按 R";
                return false;
            }
            forward.Normalize();
            // Height simulation changes the court's reference floor only at an
            // explicit calibration. Never scale or write the tracked head/hands,
            // and never follow crouching by moving the floor every frame.
            float floor = simulatePlayerHeight ? eye.y - VirtualEyeHeight : (eye.y < .5f ? eye.y - 1.25f : 0f);
            court.SetPositionAndRotation(new Vector3(eye.x, floor, eye.z) - forward * PlayerCourtZ, Quaternion.LookRotation(forward));
            ConfigureDrillLayout();
            Running = false;
            calibrated = true;
            if (alignmentHint != null) alignmentHint.gameObject.SetActive(false);
            Score = Feeds = Hits = Combo = 0;
            PlayerPoints = OpponentPoints = RallyStrokes = 0;
            ResetStandardMatch();
            if (opponent != null) opponent.ResetMatch();
            feedSequence = PracticeFeed.BalancedSequence(roundLength, feedSeed);
            LastFeedProfile = new FeedProfile();
            heldServeHand = null;
            heldReleaseArmed = hasHeldSample = false;
            heldReleaseVelocity = Vector3.zero;
            shuttle.Stop();
            landingMarker.gameObject.SetActive(false);
            LastShot = new ShotReadout();
            LastNetClearance = float.NaN;
            currentTarget = TargetForFeed(0);
            targetMarker.localPosition = currentTarget;
            PositionReachMarker();
            UpdateHud();
            statusText.text = IsAiMatch ? "AI " + GameLocalization.Difficulty(Difficulty) + " / 先得 7 分获胜" : DrillLabel() + " / " + roundLength + " 球";
            ShowSettingsHelp();
            if (IsStandardMatch) ShowStandardToss();
            Debug.Log("[Shuttle VR] Recentered from current head pose. CourtYaw=" + court.eulerAngles.y.ToString("F1") +
                "; HeadYaw=" + headRotation.eulerAngles.y.ToString("F1") + "; Eye=" + eye + "; VirtualEye=" + court.InverseTransformPoint(eye).y.ToString("F2") +
                "; CompactSeated=" + compactSeatedTargets + "; ContactTarget=" + reachMarker.localPosition.ToString("F3"));
            return true;
        }

        public void StartPractice(XRNode selectedHand)
        {
            if (IsStandardMatch) { StartStandardPractice(); return; }
            if (!HeadReady()) return;
            if (Running) return;
            // Always align a new round at the actual start press, even if a
            // previous menu calibration happened while the player was turned.
            // Resuming an in-progress round must NOT rotate the world.
            bool newRound = !calibrated || (Feeds == 0 && !HandServeHeld) || (IsAiMatch ? MatchComplete : Feeds >= roundLength && !shuttle.Flying);
            if (newRound && !Recenter()) return;
            side = selectedHand == XRNode.LeftHand ? -1 : 1;
            if (!shuttle.Flying) PositionReachMarker();
            Running = true;
            feedTimer = 1.2f;
            statusText.text = shuttle.Flying ? "已继续" : "请准备";
            detailText.text = "保持原位，小幅挥拍\n确认现实中的手臂活动范围内没有障碍物";
        }

        public void TogglePause()
        {
            if (IsStandardMatch && !Running)
            {
                if (standardTossPending) { ShowStandardToss(); return; }
                if (Singles.Phase == SinglesPhase.MidGameInterval || Singles.Phase == SinglesPhase.GameInterval)
                {
                    // Resuming a paused rest is not agreement to skip it or to
                    // reposition the court. Only the explicit ready trigger is.
                    if (!HeadReady() || (IsVR && !RequiredControllersTracked())) return;
                    Running = true;
                    statusText.text = "继续间歇";
                    return;
                }
                if (Singles.Phase != SinglesPhase.Rally)
                {
                    detailText.text = MatchComplete ? "右扳机：开始新比赛" : "右扳机：确认站位，准备下一球";
                    return;
                }
            }
            if (!Running) { StartPractice(side < 0 ? XRNode.LeftHand : XRNode.RightHand); return; }
            Running = false;
            heldReleaseArmed = hasHeldSample = false;
            heldReleaseVelocity = Vector3.zero;
            statusText.text = "已暂停";
            ShowSettingsHelp();
        }

        private bool RequiredControllersTracked()
        {
            bool ready = false;
            foreach (RacketController racket in rackets)
                if (racket != null && racket.IsTracked && (!IsStandardMatch || racket.CanStrike)) ready = true;
            return ready && (!IsStandardMatch || !HandServeHeld ||
                (heldServeHand.source != null && heldServeHand.source.IsTracked));
        }

        private Vector3 ReachLocal()
        {
            Vector3 worldEye;
            Quaternion rotation;
            TryGetHeadWorldPose(out worldEye, out rotation);
            Vector3 eye = court.InverseTransformPoint(worldEye);
            if (IsAiMatch) return court.InverseTransformPoint(GetOpponentTarget(0));
            if (compactSeatedTargets)
                return PracticeFeed.Fixed(drill, side, eye, PlayerCourtZ, seatedReach, feedSide, true).targetLocal;
            float height = eye.y + (drill == PracticeDrill.Overhead ? .22f : drill == PracticeDrill.NetTouch ? -.16f : -.18f);
            return new Vector3(side * feedSide, Mathf.Max(.65f, height), PlayerCourtZ + seatedReach);
        }

        private void PositionReachMarker() { reachMarker.localPosition = ReachLocal(); }

        private void CreateAlignmentHint()
        {
            alignmentHint = new GameObject("Look forward to align - startup hint").AddComponent<TextMesh>();
            alignmentHint.transform.SetParent(playerCamera.transform, false);
            alignmentHint.transform.localPosition = new Vector3(0f, -0.10f, 1.4f);
            alignmentHint.fontSize = 72;
            alignmentHint.characterSize = 0.010f;
            alignmentHint.anchor = TextAnchor.MiddleCenter;
            alignmentHint.alignment = TextAlignment.Center;
            alignmentHint.color = new Color(0.4f, 1f, 0.85f);
            GameLocalization.ApplyFont(alignmentHint);
            alignmentHint.text = IsStandardMatch ? "请面向正前方\n正式单打 / 等待追踪就绪" :
                "请面向正前方\n左扳机：持球发球\n右扳机：自动开始";
        }

        public bool BeginHandServe(ShuttleHand hand)
        {
            if (hand == null || hand.source == null || !hand.source.IsFreeHand ||
                !hand.source.IsTracked || hand.source.MenuPressed || shuttle.Flying || !HeadReady()) return false;
            if (IsStandardMatch)
            {
                if (standardTossPending) { ResolveStandardTossChoice(true); return false; }
                if (!StandardServerIsPlayer || Singles.Phase == SinglesPhase.MidGameInterval ||
                    Singles.Phase == SinglesPhase.GameInterval || MatchComplete) return false;
            }
            if (HandServeHeld)
            {
                if (heldServeHand != hand || !Running) return false;
                heldReleaseArmed = true;
                hasHeldSample = false;
                return true;
            }
            // The free hand starts a right-handed service, not left-handed
            // auto feeding. It never steals or replaces an active shuttle.
            if (!Running) StartPractice(XRNode.RightHand);
            if (!Running || MatchComplete || (!IsAiMatch && Feeds >= roundLength)) return false;
            side = 1;
            heldServeHand = hand;
            heldReleaseArmed = true;
            hasHeldSample = false;
            heldReleaseVelocity = Vector3.zero;
            if (opponent != null)
            {
                if (IsStandardMatch) opponent.PositionForService(Singles.ServerRight);
                else opponent.ResetRally();
            }
            RallyStrokes = 0;
            landingMarker.gameObject.SetActive(false);
            shuttle.HoldAt(hand.PinchPoint.position - Vector3.up * .055f);
            statusText.text = "左手持球中";
            detailText.text = "松开左扳机，再用右手球拍击球\n短按菜单键：暂停  |  长按：取消并重新校准";
            if (IsStandardMatch) detailText.text = "松开左扳机后低手发球\n击球时整个球低于 1.15 米，向上飞向对角区";
            Debug.Log("[Shuttle Serve] Left-hand pinch started; flight and collisions disabled until release.");
            return true;
        }

        public void UpdateHeldServe(ShuttleHand hand) { AdvanceHeldServe(hand, Time.unscaledDeltaTime); }

        public void AdvanceHeldServe(ShuttleHand hand, float dt)
        {
            if (!HandServeHeld || heldServeHand != hand) return;
            if (hand.source == null || !hand.source.IsTracked || !HeadReady())
            {
                Running = false;
                heldReleaseArmed = hasHeldSample = false;
                heldReleaseVelocity = Vector3.zero;
                statusText.text = "手柄或头显追踪中断，已暂停";
                detailText.text = "恢复追踪并继续游戏\n重新按住左扳机，再松开发球";
                return;
            }
            if (!Running)
            {
                heldReleaseArmed = hasHeldSample = false;
                heldReleaseVelocity = Vector3.zero;
                return;
            }
            // The hand updates its pinch state before invoking this callback.
            // Capture the final tracked release pose too, not last frame's hold.
            if (!hand.IsPinching && !heldReleaseArmed) return;
            Vector3 position = hand.PinchPoint.position - Vector3.up * .055f;
            Vector3 delta = position - previousHeldPosition;
            heldReleaseVelocity = hasHeldSample && dt >= .001f && dt <= .08f && delta.sqrMagnitude < .0625f ?
                Vector3.ClampMagnitude(delta / dt, 2f) : Vector3.zero;
            previousHeldPosition = position;
            hasHeldSample = true;
            shuttle.HoldAt(position);
        }

        public bool ReleaseHandServe(ShuttleHand hand)
        {
            if (!HandServeHeld || heldServeHand != hand || !Running || !heldReleaseArmed ||
                hand.source == null || !hand.source.IsTracked || hand.source.MenuPressed || !HeadReady()) return false;
            if (!shuttle.ReleaseHeld(heldReleaseVelocity)) return false;
            heldServeHand = null;
            heldReleaseArmed = hasHeldSample = false;
            heldReleaseVelocity = Vector3.zero;
            Feeds++;
            LastFeedProfile = new FeedProfile { sourceLocal = court.InverseTransformPoint(shuttle.State.position),
                targetLocal = court.InverseTransformPoint(shuttle.State.position), label = "LEFT HAND SERVE", reachLabel = "FREE DROP" };
            statusText.text = "轮到你发球，用右手球拍击球";
            detailText.text = "左手已松开，羽毛球自由下落\n无自动瞄准，也无自动击球";
            Debug.Log("[Shuttle Serve] Released freely; velocity=" + shuttle.Velocity.ToString("F3"));
            UpdateHud();
            return true;
        }

        private void Serve()
        {
            if (HandServeHeld) return;
            if (IsStandardMatch) { ServeStandardOpponent(); return; }
            if (IsAiMatch) { ServeOpponent(); return; }
            if (Feeds >= roundLength)
            {
                Running = false;
                statusText.text = "本轮完成 / 得分 " + Score;
                detailText.text = "右扳机：新一轮\n长按菜单键：重新校准";
                return;
            }
            currentTarget = TargetForFeed(Feeds);
            LastNetClearance = float.NaN;
            targetMarker.localPosition = currentTarget;
            landingMarker.gameObject.SetActive(false);
            Vector3 worldEye;
            Quaternion headRotation;
            TryGetHeadWorldPose(out worldEye, out headRotation);
            Vector3 eyeLocal = court.InverseTransformPoint(worldEye);
            if (feedPattern == FeedPattern.Mixed && drill == PracticeDrill.Rally)
            {
                if (feedSequence == null || feedSequence.Length != roundLength)
                    feedSequence = PracticeFeed.BalancedSequence(roundLength, feedSeed);
                LastFeedProfile = PracticeFeed.Build(feedSequence[Feeds], side, eyeLocal, PlayerCourtZ, seatedReach, Feeds, compactSeatedTargets);
            }
            else LastFeedProfile = PracticeFeed.Fixed(drill, side, eyeLocal, PlayerCourtZ, seatedReach, feedSide, compactSeatedTargets);
            reachMarker.localPosition = LastFeedProfile.targetLocal;
            // Move only the visible feed machine to its actual launch position.
            // The court and XR origin remain stationary throughout the round.
            if (feederAssembly != null)
                feederAssembly.localPosition = LastFeedProfile.sourceLocal - new Vector3(0f, 1.5f, 8.6f);
            Vector3 source = court.TransformPoint(LastFeedProfile.sourceLocal);
            Vector3 target = court.TransformPoint(LastFeedProfile.targetLocal);
            shuttle.Launch(source, ShuttleMath.SolveLaunch(source, target, LastFeedProfile.duration, physics));
            Feeds++;
            statusText.text = GameLocalization.Feed(LastFeedProfile.kind) + " / " + GameLocalization.Reach(LastFeedProfile.reachLabel);
            detailText.text = "观察浅绿色圆环，保持原位\n" +
                "来球约 " + LastFeedProfile.duration.ToString("F2") + " 秒  |  回球瞄准金色目标";
            Debug.Log("[Shuttle Feed] " + LastFeedProfile.label + "; source=" + LastFeedProfile.sourceLocal.ToString("F2") +
                "; target=" + LastFeedProfile.targetLocal.ToString("F2") + "; seconds=" + LastFeedProfile.duration.ToString("F2"));
            Play(feedSound, 0.12f);
            UpdateHud();
        }

        public Vector3 GetOpponentTarget(int variant)
        {
            Vector3 eye;
            Quaternion rotation;
            TryGetHeadWorldPose(out eye, out rotation);
            Vector3 localEye = court.InverseTransformPoint(eye);
            int phase = Mathf.Abs(variant) % 3 - 1;
            float forward = Difficulty == OpponentDifficulty.Easy ? .62f : .60f + phase * .06f;
            float lateral = Difficulty == OpponentDifficulty.Easy ? .17f : .16f + phase * .025f;
            float vertical = Difficulty == OpponentDifficulty.Easy ? -.10f : -.08f + phase * .09f;
            if (compactSeatedTargets)
            {
                forward = Difficulty == OpponentDifficulty.Easy ? PracticeFeed.CompactReach : .44f + phase * .04f;
                lateral = Difficulty == OpponentDifficulty.Easy ? PracticeFeed.CompactSide : .14f + phase * .02f;
                vertical = Difficulty == OpponentDifficulty.Easy ? PracticeFeed.CompactHeight : .05f + phase * .05f;
            }
            Vector3 target = localEye + new Vector3(side * lateral, vertical, forward);
            reachMarker.localPosition = target;
            return court.TransformPoint(target);
        }

        private void ServeOpponent()
        {
            if (MatchComplete) { Running = false; return; }
            if (opponent == null) ConfigureDrillLayout();
            opponent.ResetRally();
            opponent.PrepareServe();
            RallyStrokes = 0;
            LastNetClearance = float.NaN;
            landingMarker.gameObject.SetActive(false);
            targetMarker.gameObject.SetActive(false);
            Vector3 target = GetOpponentTarget(0);
            Vector3 source = opponent.ServePosition;
            // A gentle practice rally starter, not a regulation service action.
            const float duration = 1.35f;
            Vector3 velocity = ShuttleMath.SolveLaunch(source, target, duration, physics);
            shuttle.Launch(source, velocity);
            Feeds++;
            LastFeedProfile = new FeedProfile { sourceLocal = court.InverseTransformPoint(source),
                targetLocal = court.InverseTransformPoint(target), duration = duration, label = "AI PRACTICE STARTER" };
            statusText.text = "AI " + GameLocalization.Difficulty(Difficulty) + " / 向你附近发球";
            detailText.text = "练习规则：先得 7 分获胜\n保持原位，短按菜单键暂停";
            Debug.Log("[Shuttle AI] Practice starter; difficulty=" + Difficulty + "; target=" + target);
            Play(feedSound, .12f);
            UpdateHud();
        }

        public void OnOpponentHit(ImpactResult impact)
        {
            RallyStrokes++;
            LastNetClearance = float.NaN;
            statusText.text = "AI 回球 / " + GameLocalization.Difficulty(Difficulty);
            detailText.text = "观察浅绿色圆环，保持原位\n本回合 " + RallyStrokes + " 拍  |  短按菜单键：暂停";
            Play(hitSound, .15f);
            Debug.Log("[Shuttle AI] Physical return; difficulty=" + Difficulty + "; speed=" + impact.outgoingVelocity.magnitude.ToString("F2") +
                "; dwellMs=" + (impact.contactDuration*1000f).ToString("F3") + "; rally=" + RallyStrokes);
            UpdateHud();
        }

        public static bool PlayerWinsAiPoint(string reason, Vector3 landing, bool playerLastHit)
        {
            bool fault = reason == "NET" || reason == "OUT";
            return playerLastHit ? !fault && BadmintonCourt.IsOnFarSinglesSide(landing) :
                fault || !BadmintonCourt.IsOnNearSinglesSide(landing);
        }

        private void FinishAiPoint(string reason, Vector3 landing, bool playerLastHit)
        {
            if (IsStandardMatch) { FinishStandardRally(reason, landing, playerLastHit); return; }
            if (opponent != null) opponent.OnPointEnded();
            bool won = PlayerWinsAiPoint(reason, landing, playerLastHit);
            if (won) PlayerPoints++; else OpponentPoints++;
            Score = PlayerPoints;
            landingMarker.localPosition = new Vector3(landing.x, .035f, landing.z);
            landingMarker.gameObject.SetActive(true);
            statusText.text = MatchComplete ? (PlayerPoints > OpponentPoints ? "你获胜了" : "AI 获胜") : (won ? "你得分" : "AI 得分");
            detailText.text = MatchComplete ? "7 分制对局结束\n右扳机：开始新对局" :
                "本回合 " + RallyStrokes + " 拍\n短按菜单键：暂停  |  长按：重置";
            feedTimer = 2.3f;
            if (MatchComplete) Running = false;
            UpdateHud();
        }

        public void OnHit(ImpactResult impact, RacketContact contact)
        {
            Vector3 eye;
            Quaternion rotation;
            TryGetHeadWorldPose(out eye, out rotation);
            Vector3 local = court.InverseTransformPoint(contact.point);
            LastShot = ShotClassifier.Classify(impact, new ShotContext {
                position = contact.point, eye = eye, courtForward = court.forward,
                distanceToNet = ShuttleMath.NetZ - local.z, heightOverNet = local.y - BadmintonCourt.NetHeightAt(local.x) });
            Hits++;
            if (IsAiMatch) { RallyStrokes++; opponent.BeginTracking(shuttle.State); }
            else Score += 5;
            statusText.text = GameLocalization.Technique(LastShot.technique);
            detailText.text = ShotMetrics();
            Debug.Log("[Shuttle Contact] " + ShotClassifier.Label(LastShot.technique) +
                "; grip-based head speed=" + impact.swingSpeed.ToString("F2") +
                "m/s; normal=" + impact.normalSwing.ToString("F2") +
                "m/s; outgoing=" + LastShot.speed.ToString("F2") +
                "m/s; angle=" + LastShot.launchAngle.ToString("F1") +
                "; sweet spot=" + impact.quality.ToString("F2") +
                "; tensionLb=" + physics.stringTensionLbs.ToString("F0") + "; massG=" + (physics.mass*1000f).ToString("F1") +
                "; contactMs=" + LastShot.contactMilliseconds.ToString("F3") + "; indentationMm=" + LastShot.indentationMillimeters.ToString("F2") +
                "; slip=" + impact.slidingFraction.ToString("F2") + "; axialRpm=" + LastShot.axialSpinRpm.ToString("F0") +
                "; materialSweepDeg=" + impact.wrapAngle.ToString("F1") +
                "; courtIn=" + court.InverseTransformDirection(impact.incomingVelocity).ToString("F3") +
                "; courtRacket=" + court.InverseTransformDirection(contact.PointVelocity).ToString("F3") +
                "; courtNormal=" + court.InverseTransformDirection(impact.resolvedNormal).ToString("F3") +
                "; courtOut=" + court.InverseTransformDirection(impact.outgoingVelocity).ToString("F3") +
                "; geometricSide=" + (contact.surfaceNormal.sqrMagnitude > .5f));
            Play(hitSound, Mathf.Lerp(.12f,.45f,Mathf.Clamp01(LastShot.speed/35f)));
            UpdateHud();
        }

        public void OnNetCrossing(float clearance)
        {
            LastNetClearance = clearance;
            detailText.text = ShotMetrics() + "\n过网余量 " + (clearance * 100f).ToString("F0") + " 厘米";
        }

        private string ShotMetrics()
        {
            return "挥速 " + LastShot.swingSpeed.ToString("F1") + " 米/秒  >  球速 " +
                (LastShot.speed * 3.6f).ToString("F0") + " 千米/小时\n仰角 " + LastShot.launchAngle.ToString("F0") +
                " 度  |  旋转 " + LastShot.spinRpm.ToString("F0") + " 转/分  |  甜区 " + (LastShot.quality * 100f).ToString("F0") + "%\n" +
                "接触 " + LastShot.contactMilliseconds.ToString("F2") + " 毫秒  |  滑移 " + (LastShot.slidingFraction*100f).ToString("F0") + "%";
        }

        public void CycleDrill(int direction)
        {
            if (direction == 0 || !CanAdjustSettings()) return;
            int count = System.Enum.GetValues(typeof(PracticeDrill)).Length;
            SetDrill((PracticeDrill)(((int)drill + (direction > 0 ? 1 : -1) + count) % count));
        }

        public void SetDrill(PracticeDrill next)
        {
            if (!System.Enum.IsDefined(typeof(PracticeDrill), next) || !CanAdjustSettings()) return;
            PracticeDrill previous = drill;
            PracticeMode previousMode = mode;
            mode = PracticeMode.Training;
            drill = next;
            if (!Recenter()) { drill = previous; mode = previousMode; }
        }

        public void ToggleAssist()
        {
            if (!CanAdjustSettings()) return;
            physics.seatedAssist = !physics.seatedAssist;
            statusText.text = "辅助" + (physics.seatedAssist ? "开启 / 挥速增益与击球容错" : "关闭 / 原始挥拍速度");
            UpdateHud();
            UpdateQuickGuide();
            ShowSettingsHelp();
        }

        public void SetCompactTargets(bool enabled)
        {
            if (!CanAdjustSettings()) return;
            compactSeatedTargets = enabled;
            PositionReachMarker();
            statusText.text = enabled ? "坐姿近点 / 来球更近、更高" : "原始原地击球范围";
            // This changes only the next target, never the court or XR pose.
            detailText.text = "暂停时按 H：切换击球范围\n右扳机：开始";
        }

        // Settings are deliberately session-local: Inspector defaults are not
        // overwritten and an in-flight shuttle always keeps its launch model.
        public void AdjustTension(int deltaLbs)
        {
            if (!CanAdjustSettings()) return;
            physics.stringTensionLbs = Mathf.Clamp(physics.stringTensionLbs + deltaLbs,
                RacketSpecification.MinTension(physics.racketWeightClass), RacketSpecification.MaxTension(physics.racketWeightClass));
            SelectedSetting = EquipmentSetting.Tension;
            EquipmentChanged("拍线磅数 " + physics.stringTensionLbs.ToString("F0") + " 磅");
        }

        public void AdjustMass(float deltaGrams)
        {
            if (!CanAdjustSettings()) return;
            if (float.IsNaN(deltaGrams) || float.IsInfinity(deltaGrams)) return;
            float grams = Mathf.Round((physics.mass * 1000f + deltaGrams) * 10f) / 10f;
            physics.mass = Mathf.Clamp(grams * .001f, .0045f, .0060f);
            SelectedSetting = EquipmentSetting.Mass;
            EquipmentChanged("羽毛球重量 " + (physics.mass * 1000f).ToString("F1") + " 克");
        }

        public void ResetEquipment()
        {
            if (!CanAdjustSettings()) return;
            physics.stringTensionLbs = 25f;
            physics.mass = .0054f;
            EquipmentChanged("装备已重置 / 25 磅 / 5.4 克");
        }

        public void CycleEquipmentSelection()
        {
            if (!CanAdjustSettings()) return;
            SelectedSetting = (EquipmentSetting)(((int)SelectedSetting + 1) % System.Enum.GetValues(typeof(EquipmentSetting)).Length);
            statusText.text = "当前选择：" + SelectedSettingLabel();
            ShowSettingsHelp();
        }

        public void AdjustSelectedSetting(int direction)
        {
            if (direction == 0 || !CanAdjustSettings()) return;
            int step = direction > 0 ? 1 : -1;
            if (SelectedSetting == EquipmentSetting.Mode) SetMode(NextMode());
            else if (SelectedSetting == EquipmentSetting.Difficulty) SetDifficulty((OpponentDifficulty)(((int)Difficulty + step + 3) % 3));
            else if (SelectedSetting == EquipmentSetting.RacketWeight) SetRacketWeight(physics.racketWeightClass == RacketWeightClass.FourU ? RacketWeightClass.ThreeU : RacketWeightClass.FourU);
            else if (SelectedSetting == EquipmentSetting.Tension) AdjustTension(step);
            else if (SelectedSetting == EquipmentSetting.Mass) AdjustMass(step * .1f);
            else if (SelectedSetting == EquipmentSetting.FeedPattern)
                SetFeedPattern(feedPattern == FeedPattern.Mixed ? FeedPattern.Fixed : FeedPattern.Mixed);
            else CycleDrill(step);
        }

        public void SetMode(PracticeMode next)
        {
            if (!System.Enum.IsDefined(typeof(PracticeMode), next) || !CanAdjustSettings()) return;
            PracticeMode previousMode = mode;
            PracticeDrill previousDrill = drill;
            EquipmentSetting previousSelection = SelectedSetting;
            mode = next;
            if (IsAiMatch) drill = PracticeDrill.Rally;
            SelectedSetting = EquipmentSetting.Mode;
            if (!Recenter())
            {
                mode = previousMode;
                drill = previousDrill;
                SelectedSetting = previousSelection;
            }
        }

        public void SetDifficulty(OpponentDifficulty next)
        {
            if (!System.Enum.IsDefined(typeof(OpponentDifficulty), next) || !CanAdjustSettings()) return;
            Difficulty = next;
            SelectedSetting = EquipmentSetting.Difficulty;
            if (opponent != null) opponent.ResetRally();
            EquipmentChanged("AI 难度：" + GameLocalization.Difficulty(Difficulty));
        }

        public void SetRacketWeight(RacketWeightClass next)
        {
            if (!System.Enum.IsDefined(typeof(RacketWeightClass), next) || !CanAdjustSettings()) return;
            physics.racketWeightClass = next;
            physics.stringTensionLbs = Mathf.Clamp(physics.stringTensionLbs, RacketSpecification.MinTension(next), RacketSpecification.MaxTension(next));
            SelectedSetting = EquipmentSetting.RacketWeight;
            EquipmentChanged(physics.RacketModel.ShortLabel + " / 标称 " + (physics.RacketModel.NominalUnstrungMass*1000f).ToString("F0") + " 克");
        }

        public void SetFeedPattern(FeedPattern next)
        {
            if (!System.Enum.IsDefined(typeof(FeedPattern), next) || !CanAdjustSettings()) return;
            feedPattern = next;
            SelectedSetting = EquipmentSetting.FeedPattern;
            EquipmentChanged("喂球方式：" + FeedPatternLabel());
        }

        private string FeedPatternLabel()
        {
            if (drill != PracticeDrill.Rally) return "固定来球 / 专项练习";
            return feedPattern == FeedPattern.Mixed ? "混合来球 / 原地可及" : "固定来球";
        }

        private bool CanAdjustSettings()
        {
            if (!Running && !HandServeHeld && (shuttle == null || !shuttle.Flying)) return true;
            statusText.text = "球仍在进行中，暂不能调整设置";
            detailText.text = Running ? "等本球结束，短按菜单键或按 P\n暂停后即可调整设置" :
                "右扳机：继续\n长按菜单键或按 R：重置后调整";
            return false;
        }

        private void EquipmentChanged(string message)
        {
            statusText.text = message;
            UpdateHud();
            UpdateQuickGuide();
            ShowSettingsHelp();
        }

        private string SelectedSettingLabel()
        {
            if (SelectedSetting == EquipmentSetting.Mode) return "模式：" + ModeLabel();
            if (SelectedSetting == EquipmentSetting.Difficulty) return "AI 难度：" + GameLocalization.Difficulty(Difficulty);
            if (SelectedSetting == EquipmentSetting.RacketWeight) return "球拍：" + physics.RacketModel.ShortLabel;
            if (SelectedSetting == EquipmentSetting.Tension) return "拍线 " + physics.stringTensionLbs.ToString("F0") + " 磅";
            if (SelectedSetting == EquipmentSetting.Mass) return "球重 " + (physics.mass * 1000f).ToString("F1") + " 克";
            if (SelectedSetting == EquipmentSetting.FeedPattern) return "喂球：" + FeedPatternLabel();
            return "练习：" + DrillLabel();
        }

        private void ShowSettingsHelp()
        {
            if (IsStandardMatch && standardTossPending)
            {
                ShowStandardToss();
                detailText.text += "\n设置：" + SelectedSettingLabel();
                return;
            }
            if (HandServeHeld)
            {
                detailText.text = "持球中，暂不能调整设置\n先继续游戏，再重新按住并松开左扳机发球";
                return;
            }
            if (shuttle != null && shuttle.Flying)
            {
                detailText.text = "球在飞行中，暂不能调整设置\n右扳机：继续  |  长按菜单键或按 R：重置";
                return;
            }
            detailText.text = "当前：" + SelectedSettingLabel() + "\n" +
                "触控板下：选项  |  左右：调整  |  上：辅助\n右扳机：开始";
        }

        private string DrillLabel()
        {
            return drill == PracticeDrill.NetTouch ? "网前放网与搓球" :
                drill == PracticeDrill.Overhead ? "头顶高球、杀球与吊球" :
                feedPattern == FeedPattern.Mixed ? "原地混合球路练习" : "固定正手对拉";
        }

        private Vector3 TargetForFeed(int index)
        {
            if (drill == PracticeDrill.NetTouch)
                return new Vector3(index % 2 == 0 ? .65f : -.65f, .03f, 4.2f);
            return targets[index % targets.Length];
        }

        private void ConfigureDrillLayout()
        {
            if (opponent == null)
            {
                opponent = new GameObject("AI practice opponent").AddComponent<BadmintonOpponent>();
                opponent.Initialize(this);
            }
            opponent.SetVisible(IsAiMatch);
            Transform subheading = court.Find("Subheading");
            if (subheading != null) subheading.gameObject.SetActive(false);
            Transform safety = court.Find("Safety notice");
            if (safety != null) safety.localPosition = new Vector3(-4.3f, 2.65f, 7.4f);
            if (feederAssembly == null)
            {
                feederAssembly = new GameObject("Feeder Assembly").transform;
                feederAssembly.SetParent(court, false);
                var pieces = new List<Transform>();
                foreach (Transform child in court)
                    if (child.name.StartsWith("Feeder ") && child != feederAssembly) pieces.Add(child);
                foreach (Transform piece in pieces) piece.SetParent(feederAssembly, true);
            }
            feederAssembly.localPosition = new Vector3(0f,0f,drill == PracticeDrill.NetTouch ? -3.8f : 0f);
            feederAssembly.gameObject.SetActive(!IsAiMatch);
            targetMarker.gameObject.SetActive(!IsAiMatch);
            Transform card = court.Find("Nearby controls card");
            if (card != null)
            {
                card.localPosition = new Vector3(-1.25f,1.35f,PlayerCourtZ + 2.2f);
            }
            UpdateQuickGuide();
        }

        private void UpdateQuickGuide()
        {
            Transform card = court.Find("Nearby controls card");
            TextMesh guide = card != null ? card.GetComponentInChildren<TextMesh>() : null;
            if (guide == null) return;
            GameLocalization.ApplyFont(guide);
            guide.characterSize = .006f;
            if (IsStandardMatch)
            {
                guide.text = "正式单打：21 分，三局两胜\n" + (standardTossPending ?
                    (standardTossPlayerWon ? "你赢掷币：左先发 / 右先接\n" : "AI 先发：左选 A / 右选 B\n") :
                    "右扳机：确认准备 / 继续\n左扳机：持球 / 松开发球\n") +
                    "发球时整球低于 1.15 米\n短按菜单：暂停 / 长按：重置\n暂停后触控板调整设置";
                return;
            }
            guide.text = "左扳机：持球 / 松手发球\n右扳机：自动开始\n短按菜单键：暂停 / 继续\n长按菜单键：重置\n" +
                "暂停时用触控板调整设置\n" +
                (IsStandardMatch ? "正式单打：21 分，三局两胜" : IsAiMatch ? "AI " + GameLocalization.Difficulty(Difficulty) + " / 先得 7 分获胜" : "喂球训练");
        }

        public void OnFinished(string reason, Vector3 localPosition, bool returned)
        {
            if (IsAiMatch) { FinishAiPoint(reason, localPosition, returned); return; }
            bool legal = returned && reason == "LAND" && ShuttleMath.IsInOpponentCourt(localPosition);
            landingMarker.localPosition = new Vector3(localPosition.x, 0.035f, localPosition.z);
            landingMarker.gameObject.SetActive(true);
            if (legal)
            {
                Combo++;
                Vector2 offset = new Vector2(localPosition.x - currentTarget.x, localPosition.z - currentTarget.z);
                bool bullseye = offset.magnitude < 0.85f;
                Score += bullseye ? 50 : 10;
                statusText.text = bullseye ? "命中目标！加 50 分" : "界内好球！加 10 分";
                Play(scoreSound, 0.2f);
            }
            else
            {
                Combo = 0;
                statusText.text = reason == "NET" ? "下网 / 稍微打开拍面或增加上挑" : returned ? "出界 / 留意拍面角度和挥速" : "未接到球，准备下一球";
            }
            detailText.text = returned ? GameLocalization.Technique(LastShot.technique) + "\n" + ShotMetrics() : "短按菜单键：暂停\n长按菜单键或按 R：重置";
            feedTimer = 2.3f;
            UpdateHud();
        }

        private void UpdateHud()
        {
            GameLocalization.ApplyFont(scoreText);
            GameLocalization.ApplyFont(statusText);
            GameLocalization.ApplyFont(detailText);
            scoreText.text = IsAiMatch ? "你 " + PlayerPoints + "  :  " + OpponentPoints + " AI    本回合 " + RallyStrokes + " 拍" :
                "得分 " + Score.ToString("000") + "    击中 " + Hits + "/" + Feeds + "    连续得分 " + Combo;
            if (IsStandardMatch) scoreText.text = "局数 " + Singles.PlayerGames + " : " + Singles.OpponentGames +
                "    比分 " + PlayerPoints + " : " + OpponentPoints;
            if (equipmentText == null)
            {
                Transform existing = court.Find("Equipment readout");
                equipmentText = existing != null ? existing.GetComponent<TextMesh>() : null;
                if (equipmentText == null)
                {
                    equipmentText = new GameObject("Equipment readout").AddComponent<TextMesh>();
                    equipmentText.transform.SetParent(court, false);
                    equipmentText.transform.localPosition = new Vector3(0f, 4.55f, 12.84f);
                    equipmentText.fontSize = 72;
                    equipmentText.characterSize = .034f;
                    equipmentText.anchor = TextAnchor.MiddleCenter;
                    equipmentText.alignment = TextAlignment.Center;
                    equipmentText.color = new Color(.68f, .8f, .83f);
                }
            }
            GameLocalization.ApplyFont(equipmentText);
            equipmentText.text = EquipmentSummary + (simulatePlayerHeight ? "  |  身高 " + (simulatedStature * 100f).ToString("F0") + " 厘米" : "  |  实际追踪身高");
        }

        public void DesktopSwing(float horizontal)
        {
            if (IsVR || !Running || !shuttle.Flying || shuttle.Returned) return;
            Vector3 relative = court.InverseTransformPoint(shuttle.transform.position) - reachMarker.localPosition;
            if (relative.magnitude > 0.8f) return;
            Vector3 normal = court.TransformDirection(new Vector3(horizontal * .25f,.38f,1f)).normalized;
            shuttle.ReturnShot(normal * 5f, normal);
        }

        private void Play(AudioClip clip, float volume) { if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume); }
        private static AudioClip MakeTone(string name, float frequency, float duration)
        {
            const int rate = 22050;
            float[] samples = new float[Mathf.CeilToInt(rate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / rate;
                samples[i] = Mathf.Sin(t * frequency * Mathf.PI * 2f) * Mathf.Exp(-t * 35f) * Mathf.Min(1f, i / 60f);
            }
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
        private void OnDestroy()
        {
            if (hitSound != null) Destroy(hitSound);
            if (scoreSound != null) Destroy(scoreSound);
            if (feedSound != null) Destroy(feedSound);
        }
    }
}
