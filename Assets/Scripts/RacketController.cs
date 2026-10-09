using UnityEngine;
using UnityEngine.XR;

namespace ViveShuttleVR
{
    [DefaultExecutionOrder(-100)]
    public sealed class RacketController : MonoBehaviour
    {
        public XRNode hand = XRNode.RightHand;
        public Transform head;
        public GameObject visual;
        public BadmintonSession session;

        [Header("Grip-pose alignment (not the controller's aim ray)")]
        [Tooltip("Additional position relative to the tracked grip, in metres.")]
        public Vector3 gripPositionOffset = Vector3.zero;
        [Tooltip("Fine adjustment about the tracked grip axes, in degrees. Zero is the OpenXR grip baseline.")]
        public Vector3 gripRotationOffsetEuler = Vector3.zero;
        [Tooltip("Centre of the hand-held part of the procedural racket, in model coordinates.")]
        public Vector3 modelGripCenter = new Vector3(0f, .035f, 0f);

        public bool IsTracked { get; private set; }
        public bool HasMotionSample { get; private set; }
        public Vector3 PreviousHead { get; private set; }
        public Vector3 CurrentHead { get; private set; }
        public Quaternion PreviousRotation { get; private set; }
        // World-space velocity at the racket-head centre, not at the hand.
        public Vector3 HeadVelocity { get; private set; }
        // World-space radians/second. Contact-point velocity adds omega x (point - head centre).
        public Vector3 AngularVelocity { get; private set; }
        // Duration of the valid pose segment used by both the sweep and its derivatives.
        public float MotionDeltaTime { get; private set; }
        public float RecentPeakSpeed { get; private set; }

        // OpenXR grip -Z follows the handle toward the thumb. Unity converts it to +Z.
        // The mesh's shaft is +Y and its face normal is +Z. Put the face normal on
        // grip +X, perpendicular to the palm, for a neutral handshake/forehand grip.
        public static Quaternion GripToRacketRotation
        {
            get { return Quaternion.LookRotation(Vector3.right, Vector3.forward); }
        }

        private InputDevice device;
        private bool wasTrigger, wasGrip, wasMenu, wasPad, hadPose;
        private Vector3 previousGrip;
        private Quaternion previousGripRotation;
        private readonly float[] speedHistory = new float[24];
        private readonly float[] speedTimes = new float[24];
        private int historyCursor, historyCount;

        private void Start()
        {
            ApplyGripAlignment();
        }

        private void OnValidate()
        {
            ApplyGripAlignment();
        }

        public void ApplyGripAlignment()
        {
            if (visual == null) return;
            Quaternion rotation = Quaternion.Euler(gripRotationOffsetEuler) * GripToRacketRotation;
            Vector3 position = gripPositionOffset - rotation * modelGripCenter;
            Transform racket = visual.transform;
            bool changed = Vector3.SqrMagnitude(racket.localPosition - position) > 0.00000001f ||
                           Mathf.Abs(Quaternion.Dot(racket.localRotation, rotation)) < .999999f;
            racket.localPosition = position;
            racket.localRotation = rotation;
            if (changed) ResetMotion();
        }

        private void Update()
        {
            ApplyGripAlignment();
            if (!device.isValid) device = InputDevices.GetDeviceAtXRNode(hand);

            bool tracked;
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            IsTracked = head != null && device.isValid &&
                        device.TryGetFeatureValue(CommonUsages.isTracked, out tracked) && tracked &&
                        device.TryGetFeatureValue(CommonUsages.devicePosition, out position) && Finite(position) &&
                        device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation) && Finite(rotation);
            if (visual != null) visual.SetActive(IsTracked);
            if (!IsTracked)
            {
                ResetMotion();
                wasTrigger = wasGrip = wasMenu = wasPad = false;
                return;
            }

            // Unity's HTC Vive OpenXR profile maps these device features to /input/grip/pose.
            // Do not substitute pointer/aim rotation or apply a guessed Vive tilt.
            PreviousHead = hadPose ? CurrentHead : head.position;
            PreviousRotation = head.rotation;
            transform.localPosition = position;
            transform.localRotation = rotation;
            CurrentHead = head.position;

            float dt = Time.unscaledDeltaTime;
            Vector3 gripWorld = transform.position;
            Quaternion gripWorldRotation = transform.rotation;
            bool jump = !hadPose || dt < .001f || dt > .08f ||
                        (gripWorld - previousGrip).sqrMagnitude > 1f ||
                        Quaternion.Angle(previousGripRotation, gripWorldRotation) > 120f;
            if (jump)
            {
                // Reacquisition, stalled frames and tracking teleports must not sweep-hit a shuttle.
                ResetMotion();
                PreviousHead = CurrentHead;
                PreviousRotation = head.rotation;
            }
            else
            {
                // The collision sweep interpolates these same head poses. Differentiate that
                // pose segment, rather than mixing it with SDK instantaneous/predicted velocity:
                // near a swing reversal those samples can have opposite signs and send a ball
                // backwards even though the swept racket moved forwards into it.
                HeadVelocity = HeadVelocityFromPoses(PreviousHead, CurrentHead, dt);
                AngularVelocity = AngularVelocityFromRotation(PreviousRotation, head.rotation, dt);
                // Head positions already include the grip-to-head lever and wrist rotation.
                // Do not add omega x (head - grip) again. The impact resolver only adds the
                // additional rotation from the head centre to the actual contact point.
                if (!Finite(HeadVelocity) || !Finite(AngularVelocity))
                {
                    ResetMotion();
                    PreviousHead = CurrentHead;
                    PreviousRotation = head.rotation;
                }
                else
                {
                    HasMotionSample = true;
                    MotionDeltaTime = dt;
                    RecordSpeed(HeadVelocity.magnitude, Time.unscaledTime);
                }
            }

            previousGrip = gripWorld;
            previousGripRotation = gripWorldRotation;
            hadPose = true;
            HandleButtons();
        }

        public static Vector3 HeadVelocityFromPoses(Vector3 previousHead, Vector3 currentHead, float dt)
        {
            return dt > 0f ? (currentHead - previousHead) / dt : Vector3.zero;
        }

        public static Vector3 VelocityAtPoint(Vector3 gripVelocity, Vector3 angularVelocity, Vector3 offset)
        {
            return gripVelocity + Vector3.Cross(angularVelocity, offset);
        }

        public static Vector3 AngularVelocityFromRotation(Quaternion previous, Quaternion current, float dt)
        {
            if (dt <= 0f) return Vector3.zero;
            Quaternion delta = current * Quaternion.Inverse(previous);
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            float angle;
            Vector3 axis;
            delta.ToAngleAxis(out angle, out axis);
            if (angle > 180f) angle -= 360f;
            if (Mathf.Abs(angle) < .0001f || !Finite(axis)) return Vector3.zero;
            return axis * (angle * Mathf.Deg2Rad / dt);
        }

        private void RecordSpeed(float speed, float now)
        {
            speedHistory[historyCursor] = speed;
            speedTimes[historyCursor] = now;
            historyCursor = (historyCursor + 1) % speedHistory.Length;
            historyCount = Mathf.Min(historyCount + 1, speedHistory.Length);
            RecentPeakSpeed = 0f;
            for (int i = 0; i < historyCount; i++)
                if (now - speedTimes[i] <= .12f)
                    RecentPeakSpeed = Mathf.Max(RecentPeakSpeed, speedHistory[i]);
        }

        private void ResetMotion()
        {
            hadPose = false;
            HasMotionSample = false;
            HeadVelocity = AngularVelocity = Vector3.zero;
            MotionDeltaTime = 0f;
            RecentPeakSpeed = 0f;
            historyCursor = historyCount = 0;
        }

        private void HandleButtons()
        {
            bool trigger = Read(CommonUsages.triggerButton, CommonUsages.trigger);
            bool grip = Read(CommonUsages.gripButton, CommonUsages.grip);
            bool menu, pad;
            device.TryGetFeatureValue(CommonUsages.menuButton, out menu);
            device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out pad);
            if (session != null)
            {
                if (menu && !wasMenu) session.Recenter();
                else if (grip && !wasGrip) session.TogglePause();
                else if (trigger && !wasTrigger) session.StartPractice(hand);
                else if (!session.Running && pad && !wasPad)
                {
                    Vector2 axis;
                    if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis))
                    {
                        if (Mathf.Abs(axis.x) > .45f && Mathf.Abs(axis.x) > Mathf.Abs(axis.y))
                            session.AdjustSelectedSetting(axis.x > 0f ? 1 : -1);
                        else if (axis.y > .5f) session.ToggleAssist();
                        else if (axis.y < -.5f) session.CycleEquipmentSelection();
                    }
                }
            }
            wasTrigger = trigger;
            wasGrip = grip;
            wasMenu = menu;
            wasPad = pad;
        }

        private bool Read(InputFeatureUsage<bool> button, InputFeatureUsage<float> axis)
        {
            bool pressed;
            float value;
            return (device.TryGetFeatureValue(button, out pressed) && pressed) ||
                   (device.TryGetFeatureValue(axis, out value) && value > .65f);
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }

        private static bool Finite(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
                   !float.IsNaN(value.w) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) &&
                   !float.IsInfinity(value.z) && !float.IsInfinity(value.w) &&
                   value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > .5f;
        }

        public void Pulse(float amount)
        {
            HapticCapabilities capabilities;
            if (device.isValid && device.TryGetHapticCapabilities(out capabilities) && capabilities.supportsImpulse)
                device.SendHapticImpulse(0u, Mathf.Clamp01(amount), .05f);
        }
    }
}
