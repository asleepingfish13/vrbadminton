using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace ViveShuttleVR
{
    public enum EquipmentSetting { Drill, Tension, Mass, FeedPattern }

    public sealed class BadmintonSession : MonoBehaviour
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
        public int roundLength = 20;
        public ShuttlePhysicsSettings physics = new ShuttlePhysicsSettings();
        public PracticeDrill drill = PracticeDrill.Rally;
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
            get { return physics.stringTensionLbs.ToString("F0") + " LB  |  " +
                (physics.mass * 1000f).ToString("F1") + " G  |  ASSIST " + (physics.seatedAssist ? "ON" : "OFF"); }
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
            statusText.text = "LOOK STRAIGHT AHEAD";
            detailText.text = "TRIGGER: ALIGN + START    MENU / R: RECENTER";
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetInstances(inputs);
            foreach (XRInputSubsystem input in inputs)
                input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            yield return null;
            if (!IsVR)
            {
                TrackedPoseDriver driver = playerCamera.GetComponent<TrackedPoseDriver>();
                if (driver != null) driver.enabled = false;
                playerCamera.transform.localPosition = new Vector3(0f, 1.25f, 0f);
                playerCamera.transform.localRotation = Quaternion.identity;
            }
            Debug.Log("[Shuttle VR] Startup mode=" + (IsVR ? "VR" : "Desktop") +
                "; HeadPoseDriver=" + playerCamera.GetComponent<TrackedPoseDriver>().enabled + "; ContactModel=PoseSweepStringbedV3" +
                "; Equipment=" + EquipmentSummary + "; Feed=" + feedPattern + "; PlayArea=Stationary");
            // Tracking readiness is not the player's choice of forward direction.
            // In VR, leave calibration to the first deliberate start/menu press.
            float stable = 0f;
            while (stable < 0.3f)
            {
                stable = HeadReady() ? stable + Time.unscaledDeltaTime : 0f;
                yield return null;
            }
            if (!IsVR && !calibrated) Recenter();
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
            }
            if (!Running) return;
            bool controllerReady = !IsVR;
            foreach (RacketController racket in rackets) controllerReady |= racket.IsTracked;
            if (!HeadReady() || !controllerReady)
            {
                Running = false;
                statusText.text = "TRACKING PAUSED";
                detailText.text = "WEAR HEADSET / TURN ON CONTROLLER - TRIGGER TO RESUME";
                return;
            }
            if (!shuttle.Flying)
            {
                feedTimer -= Time.deltaTime;
                if (feedTimer <= 0f) Serve();
            }
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
                statusText.text = "WAIT FOR HEADSET TRACKING";
                return false;
            }
            Vector3 forward = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
            {
                Running = false;
                statusText.text = "LOOK FORWARD, THEN PRESS MENU / R";
                return false;
            }
            forward.Normalize();
            float floor = drill == PracticeDrill.NetTouch ? eye.y - 1.5f :
                drill == PracticeDrill.Overhead ? eye.y - 1.75f : eye.y < .5f ? eye.y - 1.25f : 0f;
            court.SetPositionAndRotation(new Vector3(eye.x, floor, eye.z) - forward * PlayerCourtZ, Quaternion.LookRotation(forward));
            ConfigureDrillLayout();
            Running = false;
            calibrated = true;
            if (alignmentHint != null) alignmentHint.gameObject.SetActive(false);
            Score = Feeds = Hits = Combo = 0;
            feedSequence = PracticeFeed.BalancedSequence(roundLength, feedSeed);
            LastFeedProfile = new FeedProfile();
            shuttle.Stop();
            landingMarker.gameObject.SetActive(false);
            LastShot = new ShotReadout();
            LastNetClearance = float.NaN;
            currentTarget = TargetForFeed(0);
            targetMarker.localPosition = currentTarget;
            PositionReachMarker();
            UpdateHud();
            statusText.text = DrillLabel() + " / " + roundLength + " SHUTTLES";
            ShowSettingsHelp();
            Debug.Log("[Shuttle VR] Recentered from current head pose. CourtYaw=" + court.eulerAngles.y.ToString("F1") +
                "; HeadYaw=" + headRotation.eulerAngles.y.ToString("F1") + "; Eye=" + eye + "; Reach=" + seatedReach.ToString("F2") + "m");
            return true;
        }

        public void StartPractice(XRNode selectedHand)
        {
            if (!HeadReady()) return;
            if (Running) return;
            // Always align a new round at the actual start press, even if a
            // previous menu calibration happened while the player was turned.
            // Resuming an in-progress round must NOT rotate the world.
            bool newRound = !calibrated || Feeds == 0 || Feeds >= roundLength && !shuttle.Flying;
            if (newRound && !Recenter()) return;
            side = selectedHand == XRNode.LeftHand ? -1 : 1;
            if (!shuttle.Flying) PositionReachMarker();
            Running = true;
            feedTimer = 1.2f;
            statusText.text = shuttle.Flying ? "RESUMED" : "GET READY";
            detailText.text = "STAY IN PLACE - SMALL SWINGS - CHECK REAL ARM CLEARANCE";
        }

        public void TogglePause()
        {
            if (!Running) { StartPractice(side < 0 ? XRNode.LeftHand : XRNode.RightHand); return; }
            Running = false;
            statusText.text = "PAUSED";
            ShowSettingsHelp();
        }

        private Vector3 ReachLocal()
        {
            Vector3 worldEye;
            Quaternion rotation;
            TryGetHeadWorldPose(out worldEye, out rotation);
            Vector3 eye = court.InverseTransformPoint(worldEye);
            float height = eye.y + (drill == PracticeDrill.Overhead ? .22f : drill == PracticeDrill.NetTouch ? -.16f : -.18f);
            return new Vector3(side * feedSide, Mathf.Max(.65f, height), PlayerCourtZ + seatedReach);
        }

        private void PositionReachMarker() { reachMarker.localPosition = ReachLocal(); }

        private void CreateAlignmentHint()
        {
            alignmentHint = new GameObject("Look forward to align - startup hint").AddComponent<TextMesh>();
            alignmentHint.transform.SetParent(playerCamera.transform, false);
            alignmentHint.transform.localPosition = new Vector3(0f, -0.10f, 1.4f);
            alignmentHint.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            alignmentHint.GetComponent<Renderer>().sharedMaterial = alignmentHint.font.material;
            alignmentHint.fontSize = 72;
            alignmentHint.characterSize = 0.010f;
            alignmentHint.anchor = TextAnchor.MiddleCenter;
            alignmentHint.alignment = TextAlignment.Center;
            alignmentHint.color = new Color(0.4f, 1f, 0.85f);
            alignmentHint.text = "LOOK STRAIGHT AHEAD\nTRIGGER: ALIGN COURT + START";
        }

        private void Serve()
        {
            if (Feeds >= roundLength)
            {
                Running = false;
                statusText.text = "ROUND COMPLETE / " + Score + " POINTS";
                detailText.text = "TRIGGER: NEW ROUND    MENU: RECENTER";
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
                LastFeedProfile = PracticeFeed.Build(feedSequence[Feeds], side, eyeLocal, PlayerCourtZ, seatedReach, Feeds);
            }
            else LastFeedProfile = PracticeFeed.Fixed(drill, side, eyeLocal, PlayerCourtZ, seatedReach, feedSide);
            reachMarker.localPosition = LastFeedProfile.targetLocal;
            // Move only the visible feed machine to its actual launch position.
            // The court and XR origin remain stationary throughout the round.
            if (feederAssembly != null)
                feederAssembly.localPosition = LastFeedProfile.sourceLocal - new Vector3(0f, 1.5f, 8.6f);
            Vector3 source = court.TransformPoint(LastFeedProfile.sourceLocal);
            Vector3 target = court.TransformPoint(LastFeedProfile.targetLocal);
            shuttle.Launch(source, ShuttleMath.SolveLaunch(source, target, LastFeedProfile.duration, physics));
            Feeds++;
            statusText.text = LastFeedProfile.label + " / " + LastFeedProfile.reachLabel;
            detailText.text = "WATCH THE MINT RING / STAY IN PLACE\n" +
                "ARRIVAL " + LastFeedProfile.duration.ToString("F2") + " S  |  GOLD TARGET: YOUR RETURN";
            Debug.Log("[Shuttle Feed] " + LastFeedProfile.label + "; source=" + LastFeedProfile.sourceLocal.ToString("F2") +
                "; target=" + LastFeedProfile.targetLocal.ToString("F2") + "; seconds=" + LastFeedProfile.duration.ToString("F2"));
            Play(feedSound, 0.12f);
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
                distanceToNet = ShuttleMath.NetZ - local.z, heightOverNet = local.y - ShuttleMath.NetHeight });
            Hits++;
            Score += 5;
            statusText.text = ShotClassifier.Label(LastShot.technique);
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
            detailText.text = ShotMetrics() + "\nNET CLEARANCE " + (clearance * 100f).ToString("F0") + " CM";
        }

        private string ShotMetrics()
        {
            return "RACKET " + LastShot.swingSpeed.ToString("F1") + " M/S  >  SHUTTLE " +
                (LastShot.speed * 3.6f).ToString("F0") + " KM/H\nANGLE " + LastShot.launchAngle.ToString("F0") +
                " DEG  |  SPIN " + LastShot.spinRpm.ToString("F0") + " RPM  |  CENTER " + (LastShot.quality * 100f).ToString("F0") + "%\n" +
                "CONTACT " + LastShot.contactMilliseconds.ToString("F2") + " MS  |  SLIP " + (LastShot.slidingFraction*100f).ToString("F0") + "%";
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
            drill = next;
            if (!Recenter()) drill = previous;
        }

        public void ToggleAssist()
        {
            if (!CanAdjustSettings()) return;
            physics.seatedAssist = !physics.seatedAssist;
            statusText.text = "ASSIST " + (physics.seatedAssist ? "ON / SPEED GAIN + CONTACT MARGIN" : "OFF / 1:1 SWING SPEED");
            UpdateHud();
            UpdateQuickGuide();
            ShowSettingsHelp();
        }

        // Settings are deliberately session-local: Inspector defaults are not
        // overwritten and an in-flight shuttle always keeps its launch model.
        public void AdjustTension(int deltaLbs)
        {
            if (!CanAdjustSettings()) return;
            physics.stringTensionLbs = Mathf.Clamp(physics.stringTensionLbs + deltaLbs, 18f, 32f);
            SelectedSetting = EquipmentSetting.Tension;
            EquipmentChanged("TENSION " + physics.stringTensionLbs.ToString("F0") + " LB");
        }

        public void AdjustMass(float deltaGrams)
        {
            if (!CanAdjustSettings()) return;
            if (float.IsNaN(deltaGrams) || float.IsInfinity(deltaGrams)) return;
            float grams = Mathf.Round((physics.mass * 1000f + deltaGrams) * 10f) / 10f;
            physics.mass = Mathf.Clamp(grams * .001f, .0045f, .0060f);
            SelectedSetting = EquipmentSetting.Mass;
            EquipmentChanged("SHUTTLE MASS " + (physics.mass * 1000f).ToString("F1") + " G");
        }

        public void ResetEquipment()
        {
            if (!CanAdjustSettings()) return;
            physics.stringTensionLbs = 25f;
            physics.mass = .0054f;
            EquipmentChanged("EQUIPMENT RESET / 25 LB / 5.4 G");
        }

        public void CycleEquipmentSelection()
        {
            if (!CanAdjustSettings()) return;
            SelectedSetting = (EquipmentSetting)(((int)SelectedSetting + 1) % 4);
            statusText.text = "SELECTED " + SelectedSettingLabel();
            ShowSettingsHelp();
        }

        public void AdjustSelectedSetting(int direction)
        {
            if (direction == 0 || !CanAdjustSettings()) return;
            int step = direction > 0 ? 1 : -1;
            if (SelectedSetting == EquipmentSetting.Tension) AdjustTension(step);
            else if (SelectedSetting == EquipmentSetting.Mass) AdjustMass(step * .1f);
            else if (SelectedSetting == EquipmentSetting.FeedPattern)
                SetFeedPattern(feedPattern == FeedPattern.Mixed ? FeedPattern.Fixed : FeedPattern.Mixed);
            else CycleDrill(step);
        }

        public void SetFeedPattern(FeedPattern next)
        {
            if (!System.Enum.IsDefined(typeof(FeedPattern), next) || !CanAdjustSettings()) return;
            feedPattern = next;
            SelectedSetting = EquipmentSetting.FeedPattern;
            EquipmentChanged("FEEDS " + FeedPatternLabel());
        }

        private string FeedPatternLabel()
        {
            if (drill != PracticeDrill.Rally) return "FIXED / SPECIALIST DRILL";
            return feedPattern == FeedPattern.Mixed ? "MIXED / STATIONARY REACH" : "FIXED";
        }

        private bool CanAdjustSettings()
        {
            if (!Running && (shuttle == null || !shuttle.Flying)) return true;
            statusText.text = "SETTINGS LOCKED / SHUTTLE IN PLAY";
            detailText.text = Running ? "GRIP / P: PAUSE BETWEEN SHUTTLES TO ADJUST" :
                "TRIGGER: RESUME  |  MENU / R: RESET ROUND TO ADJUST";
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
            if (SelectedSetting == EquipmentSetting.Tension) return "TENSION " + physics.stringTensionLbs.ToString("F0") + " LB";
            if (SelectedSetting == EquipmentSetting.Mass) return "MASS " + (physics.mass * 1000f).ToString("F1") + " G";
            if (SelectedSetting == EquipmentSetting.FeedPattern) return "FEEDS " + FeedPatternLabel();
            return "DRILL / " + DrillLabel();
        }

        private void ShowSettingsHelp()
        {
            if (shuttle != null && shuttle.Flying)
            {
                detailText.text = "SHUTTLE IN FLIGHT / SETTINGS LOCKED\nTRIGGER: RESUME  |  MENU / R: RESET ROUND TO ADJUST";
                return;
            }
            detailText.text = "SELECTED: " + SelectedSettingLabel() + "  |  TRIGGER: START\n" +
                "PAD DOWN: SELECT  |  L/R: CHANGE  |  UP: ASSIST";
        }

        private string DrillLabel()
        {
            return drill == PracticeDrill.NetTouch ? "NET TOUCH / BRUSH" :
                drill == PracticeDrill.Overhead ? "OVERHEAD / SMASH / DROP" :
                feedPattern == FeedPattern.Mixed ? "MIXED STATIONARY PRACTICE" : "FIXED FOREHAND RALLY";
        }

        private Vector3 TargetForFeed(int index)
        {
            if (drill == PracticeDrill.NetTouch)
                return new Vector3(index % 2 == 0 ? .65f : -.65f, .03f, 4.2f);
            return targets[index % targets.Length];
        }

        private void ConfigureDrillLayout()
        {
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
            guide.characterSize = .0065f;
            guide.text = "TRIGGER START / GRIP PAUSE\nMENU RESET + RECENTER\nPAUSED + NO SHUTTLE:\n" +
                "DOWN: DRILL / LB / G / FEED\nL/R: CHANGE  UP: ASSIST\n" + EquipmentSummary;
        }

        public void OnFinished(string reason, Vector3 localPosition, bool returned)
        {
            bool legal = returned && reason == "LAND" && ShuttleMath.IsInOpponentCourt(localPosition);
            landingMarker.localPosition = new Vector3(localPosition.x, 0.035f, localPosition.z);
            landingMarker.gameObject.SetActive(true);
            if (legal)
            {
                Combo++;
                Vector2 offset = new Vector2(localPosition.x - currentTarget.x, localPosition.z - currentTarget.z);
                bool bullseye = offset.magnitude < 0.85f;
                Score += bullseye ? 50 : 10;
                statusText.text = bullseye ? "TARGET HIT!  +50" : "IN!  +10";
                Play(scoreSound, 0.2f);
            }
            else
            {
                Combo = 0;
                statusText.text = reason == "NET" ? "INTO THE NET / OPEN FACE OR ADD LIFT" : returned ? "OUT / CHECK FACE ANGLE + SPEED" : "MISSED - NEXT ONE COMING";
            }
            detailText.text = returned ? ShotClassifier.Label(LastShot.technique) + " | " + ShotMetrics() : "GRIP: PAUSE    MENU / R: RESET + RECENTER";
            feedTimer = 2.3f;
            UpdateHud();
        }

        private void UpdateHud()
        {
            scoreText.text = "SCORE " + Score.ToString("000") + "    HIT " + Hits + "/" + Feeds + "    STREAK " + Combo;
            if (equipmentText == null)
            {
                Transform existing = court.Find("Equipment readout");
                equipmentText = existing != null ? existing.GetComponent<TextMesh>() : null;
                if (equipmentText == null)
                {
                    equipmentText = new GameObject("Equipment readout").AddComponent<TextMesh>();
                    equipmentText.transform.SetParent(court, false);
                    equipmentText.transform.localPosition = new Vector3(0f, 4.27f, 7.77f);
                    equipmentText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    equipmentText.GetComponent<Renderer>().sharedMaterial = equipmentText.font.material;
                    equipmentText.fontSize = 72;
                    equipmentText.characterSize = .028f;
                    equipmentText.anchor = TextAnchor.MiddleCenter;
                    equipmentText.alignment = TextAlignment.Center;
                    equipmentText.color = new Color(.68f, .8f, .83f);
                }
            }
            equipmentText.text = EquipmentSummary;
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
