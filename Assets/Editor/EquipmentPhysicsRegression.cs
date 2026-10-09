using System;
using System.Reflection;
using UnityEngine;

namespace ViveShuttleVR.Editor
{
    // Invariants for a reduced-order contact model, not fitted commercial-racket data.
    public static class EquipmentPhysicsRegression
    {
        private static int checks;

        public static int Run(BadmintonSession session)
        {
            checks = 0;
            TestDefaultsAndTension();
            TestSwingSpeed();
            TestSliceContact();
            TestPassiveEnergy();
            TestSessionControls(session);
            Debug.Log("[Equipment Test] PASS " + checks + " checks: defaults, string compliance, signed slice contact, passive energy, speed response and guarded controls.");
            return checks;
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("[Equipment Test] " + message);
        }

        private static ShuttlePhysicsSettings Settings(float mass = .0054f, float tension = 25f)
        {
            return new ShuttlePhysicsSettings { mass = mass, stringTensionLbs = tension, seatedAssist = false };
        }

        private static RacketContact Contact(Vector3 swing, Vector3 point)
        {
            return new RacketContact { center = Vector3.zero, point = point,
                centerVelocity = swing, faceRotation = Quaternion.identity,
                surfaceNormal = Vector3.forward, recentPeakSpeed = swing.magnitude };
        }

        private static ShuttleState Incoming(float speed)
        {
            return ShuttleState.Launch(Vector3.zero, Vector3.back * speed);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void CheckContact(ImpactResult impact, string name)
        {
            Require(impact.hit, name + " should make contact");
            Require(Finite(impact.outgoingVelocity.sqrMagnitude) && Finite(impact.angularVelocity.sqrMagnitude), name + " has nonfinite output");
            Require(impact.contactDuration > 0f && impact.contactDuration <= .0051f, name + " has invalid contact duration");
            Require(Finite(impact.maxIndentation) && impact.maxIndentation >= 0f && impact.maxIndentation <= .0601f, name + " has invalid indentation");
            Require(Finite(impact.slidingFraction) && impact.slidingFraction >= 0f && impact.slidingFraction <= 1f, name + " has invalid slip fraction");
            Require(Finite(impact.wrapAngle) && impact.wrapAngle >= 0f && impact.wrapAngle <= 180f && Finite(impact.axialSpin), name + " has invalid cork telemetry");
        }

        private static void TestDefaultsAndTension()
        {
            ShuttlePhysicsSettings defaults = new ShuttlePhysicsSettings();
            Require(Mathf.Abs(defaults.mass - .0054f) < .0000001f, "Default mass must be 5.4 grams");
            Require(Mathf.Abs(defaults.stringTensionLbs - 25f) < .0001f, "Default tension must be 25 pounds");
            ShuttlePhysicsSettings soft = Settings(.0054f, 18f), hard = Settings(.0054f, 32f);
            Require(hard.StringbedStiffness > soft.StringbedStiffness, "Higher tension must increase normal stiffness");
            Require(hard.TangentialStiffness > soft.TangentialStiffness, "Higher tension must increase shear stiffness");
            Require(hard.SweetSpotFalloff > soft.SweetSpotFalloff, "Higher tension should have a narrower sweet spot in this tuning model");
            ImpactResult low = RacketImpact.Resolve(Incoming(3f), Contact(Vector3.forward * 6f, Vector3.zero), soft);
            ImpactResult high = RacketImpact.Resolve(Incoming(3f), Contact(Vector3.forward * 6f, Vector3.zero), hard);
            CheckContact(low, "18 lb centered hit");
            CheckContact(high, "32 lb centered hit");
            Require(high.contactDuration < low.contactDuration, "Higher tension should shorten centered contact");
            Require(high.maxIndentation < low.maxIndentation, "Higher tension should indent less at matched impact speed");
            ImpactResult lowOff = RacketImpact.Resolve(Incoming(3f), Contact(Vector3.forward * 6f, Vector3.right * .09f), soft);
            ImpactResult highOff = RacketImpact.Resolve(Incoming(3f), Contact(Vector3.forward * 6f, Vector3.right * .09f), hard);
            Require(lowOff.hit && highOff.hit && highOff.quality < lowOff.quality, "Off-center tolerance must depend on tension");
            Debug.Log(string.Format("[Equipment Test] 18/32 lb: dwell {0:F3}/{1:F3} ms; indentation {2:F3}/{3:F3} mm; outgoing {4:F3}/{5:F3} m/s", low.contactDuration * 1000f, high.contactDuration * 1000f, low.maxIndentation * 1000f, high.maxIndentation * 1000f, low.outgoingVelocity.magnitude, high.outgoingVelocity.magnitude));
        }

        private static void TestSwingSpeed()
        {
            foreach (float tension in new[] { 18f, 25f, 32f })
            {
                float previous = -1f;
                foreach (float speed in new[] { 0f, 1f, 2f, 4f, 8f, 12f, 20f })
                {
                    ImpactResult result = RacketImpact.Resolve(Incoming(3f), Contact(Vector3.forward * speed, Vector3.zero), Settings(.0054f, tension));
                    CheckContact(result, tension + " lb swing " + speed);
                    Require(result.outgoingVelocity.z > 0f, "Centered forward contact returned backwards");
                    Require(result.outgoingVelocity.z > previous + .0001f, "More forward racket speed must increase outgoing speed at " + tension + " lb");
                    previous = result.outgoingVelocity.z;
                }
            }
        }

        private static void TestSliceContact()
        {
            ShuttlePhysicsSettings settings = Settings();
            ShuttleState incoming = Incoming(3f);
            // A tipped cork lets transverse string travel spin about the feather axis.
            incoming.orientation = Quaternion.AngleAxis(-25f, Vector3.right) * incoming.orientation;
            const float speed = 8f, tangent = 6f;
            float normal = Mathf.Sqrt(speed * speed - tangent * tangent);
            ImpactResult flat = RacketImpact.Resolve(incoming, Contact(Vector3.forward * speed, Vector3.zero), settings);
            ImpactResult left = RacketImpact.Resolve(incoming, Contact(new Vector3(-tangent, 0f, normal), Vector3.zero), settings);
            ImpactResult right = RacketImpact.Resolve(incoming, Contact(new Vector3(tangent, 0f, normal), Vector3.zero), settings);
            CheckContact(flat, "flat contact");
            CheckContact(left, "left slice");
            CheckContact(right, "right slice");
            Require(Mathf.Abs(left.swingSpeed - flat.swingSpeed) < .0001f && Mathf.Abs(right.swingSpeed - flat.swingSpeed) < .0001f, "Slice comparison must keep total swing speed equal");
            Require(right.normalSwing < flat.normalSwing && right.outgoingVelocity.z < flat.outgoingVelocity.z, "Tangential swing must not receive flat-hit normal power");
            Require(left.axialSpin * right.axialSpin < 0f && Mathf.Abs(right.axialSpin) > .01f, "Opposite tangential contact must create opposite signed axial spin");
            Require(Mathf.Abs(left.axialSpin + right.axialSpin) < .05f, "Mirrored slice spin should be symmetric before flight chirality");
            Require(Mathf.Abs(left.outgoingVelocity.x + right.outgoingVelocity.x) < .01f && Mathf.Abs(left.outgoingVelocity.z - right.outgoingVelocity.z) < .01f, "Mirrored cut must not be forced toward a court heading");
            Require((right.angularVelocity - flat.angularVelocity).magnitude > .05f, "Cut and flat contact must produce different rotation");
            ShuttleState flatFlight = incoming, sliceFlight = incoming;
            flatFlight.position = sliceFlight.position = new Vector3(0f, 2f, 0f);
            flatFlight.velocity = flat.outgoingVelocity; flatFlight.angularVelocity = flat.angularVelocity;
            sliceFlight.velocity = right.outgoingVelocity; sliceFlight.angularVelocity = right.angularVelocity;
            flatFlight = ShuttlePhysics.Predict(flatFlight, .35f, settings);
            sliceFlight = ShuttlePhysics.Predict(sliceFlight, .35f, settings);
            Require(Vector3.Distance(flatFlight.position, sliceFlight.position) > .02f, "Slice must alter real flight, not only its label");
            Require(Finite(sliceFlight.velocity.sqrMagnitude) && Finite(sliceFlight.angularVelocity.sqrMagnitude), "Slice flight must remain finite");
            Debug.Log(string.Format("[Equipment Test] Equal 8 m/s swings flat/slice: launch {0:F3}/{1:F3} m/s; axial slice +/- {2:F2}/{3:F2} rad/s; slice dwell {4:F3} ms, slip {5:F2}, wrap {6:F2} deg; trajectory separation at 0.35 s {7:F3} m", flat.outgoingVelocity.magnitude, right.outgoingVelocity.magnitude, left.axialSpin, right.axialSpin, right.contactDuration * 1000f, right.slidingFraction, right.wrapAngle, Vector3.Distance(flatFlight.position, sliceFlight.position)));
        }

        private static float Energy(ShuttleState state, float mass)
        {
            Vector3 tail = state.orientation * Vector3.up;
            float axial = Vector3.Dot(state.angularVelocity, tail);
            Vector3 transverse = state.angularVelocity - tail * axial;
            return .5f * mass * state.velocity.sqrMagnitude + .5f * (mass / .005f) *
                (ShuttlePhysics.AxialInertia * axial * axial + ShuttlePhysics.TransverseInertia * transverse.sqrMagnitude);
        }

        private static void TestPassiveEnergy()
        {
            int contacts = 0;
            foreach (float mass in new[] { .0045f, .0054f, .006f })
            foreach (float tension in new[] { 18f, 25f, 32f })
            foreach (float tilt in new[] { -35f, 0f, 35f })
            foreach (float spin in new[] { -80f, 0f, 80f })
            {
                ShuttleState incoming = Incoming(12f);
                incoming.velocity += new Vector3(.7f, -.3f, 0f);
                incoming.orientation = Quaternion.AngleAxis(tilt, Vector3.up) * incoming.orientation;
                incoming.angularVelocity = new Vector3(spin, spin * -.5f, spin * .7f);
                ImpactResult result = RacketImpact.Resolve(incoming, Contact(Vector3.zero, Vector3.zero), Settings(mass, tension));
                if (!result.hit) continue;
                contacts++;
                CheckContact(result, "passive contact");
                ShuttleState outgoing = incoming;
                outgoing.velocity = result.outgoingVelocity;
                outgoing.angularVelocity = result.angularVelocity;
                Require(Energy(outgoing, mass) <= Energy(incoming, mass) + .000005f, "Passive stationary racket created energy at mass/tension/tilt/spin " + mass + "/" + tension + "/" + tilt + "/" + spin);
            }
            Require(contacts >= 60, "Energy sweep did not exercise sufficient actual contacts");
            Debug.Log("[Equipment Test] Passive translational + rotational energy checked for " + contacts + " contact combinations.");
        }

        private static void SetProperty(BadmintonSession session, string name, object value)
        {
            PropertyInfo property = typeof(BadmintonSession).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || property.GetSetMethod(true) == null) throw new InvalidOperationException("Missing test fixture property " + name);
            property.GetSetMethod(true).Invoke(session, new[] { value });
        }

        private static void TestSessionControls(BadmintonSession session)
        {
            Require(session != null && session.shuttle != null, "Session fixture required");
            ShuttlePhysicsSettings saved = session.physics;
            PracticeDrill savedDrill = session.drill;
            object savedSelection = session.SelectedSetting;
            bool savedRunning = session.Running;
            session.shuttle.Stop();
            SetProperty(session, "Running", false);
            session.physics = new ShuttlePhysicsSettings();
            try
            {
                session.AdjustTension(-100);
                Require(Mathf.Abs(session.physics.stringTensionLbs - 18f) < .0001f, "Tension minimum clamp");
                session.AdjustTension(100);
                Require(Mathf.Abs(session.physics.stringTensionLbs - 32f) < .0001f, "Tension maximum clamp");
                session.AdjustMass(-100f);
                Require(Mathf.Abs(session.physics.mass - .0045f) < .0000001f, "Mass minimum clamp");
                session.AdjustMass(100f);
                Require(Mathf.Abs(session.physics.mass - .006f) < .0000001f, "Mass maximum clamp");
                session.ResetEquipment();
                Require(Mathf.Abs(session.physics.mass - .0054f) < .0000001f && session.physics.stringTensionLbs == 25f, "Equipment reset defaults");
                session.AdjustMass(float.NaN);
                session.AdjustMass(float.PositiveInfinity);
                Require(Mathf.Abs(session.physics.mass - .0054f) < .0000001f, "Invalid mass input rejected");
                session.AdjustTension(1);
                session.AdjustSelectedSetting(-1);
                Require(session.physics.stringTensionLbs == 25f, "Selected tension dispatch");
                session.AdjustMass(.1f);
                session.AdjustSelectedSetting(-1);
                Require(Mathf.Abs(session.physics.mass - .0054f) < .0000001f, "Selected mass dispatch");
                object beforeCycle = session.SelectedSetting;
                session.CycleEquipmentSelection();
                Require(!Equals(beforeCycle, session.SelectedSetting), "Equipment selection should cycle");
                Require(session.EquipmentSummary.Contains("25") && session.EquipmentSummary.Contains("5.4"), "Equipment HUD must show current pounds and grams");
                // Non-default values prove ResetEquipment itself is guarded.
                session.AdjustTension(1);
                session.AdjustMass(.1f);
                foreach (bool running in new[] { true, false })
                {
                    SetProperty(session, "Running", running);
                    if (!running) session.shuttle.Launch(new Vector3(0f, 2f, 0f), Vector3.forward);
                    object selection = session.SelectedSetting;
                    float mass = session.physics.mass, tension = session.physics.stringTensionLbs;
                    bool assist = session.physics.seatedAssist;
                    PracticeDrill drill = session.drill;
                    session.AdjustTension(1); session.AdjustMass(.1f);
                    session.ResetEquipment(); session.CycleEquipmentSelection(); session.AdjustSelectedSetting(1);
                    session.ToggleAssist(); session.CycleDrill(1);
                    Require(session.physics.mass == mass && session.physics.stringTensionLbs == tension, "Equipment changed during live or paused in-flight shuttle");
                    Require(Equals(selection, session.SelectedSetting) && drill == session.drill && assist == session.physics.seatedAssist, "Control selection changed during guarded phase");
                    session.shuttle.Stop();
                }
            }
            finally
            {
                session.shuttle.Stop();
                session.physics = saved;
                session.drill = savedDrill;
                SetProperty(session, "SelectedSetting", savedSelection);
                SetProperty(session, "Running", savedRunning);
            }
        }
    }
}
