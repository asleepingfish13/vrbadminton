using System;
using UnityEngine;

namespace ViveShuttleVR.Editor
{
    // Tests the explicitly reduced-order tilt/roll model, not a full gyroscope.
    public static class SpinFlightRegression
    {
        private static int checks;
        private static readonly ShuttlePhysicsSettings settings =
            new ShuttlePhysicsSettings { seatedAssist = false };

        public static int Run()
        {
            checks = 0;
            TestAxialRollDoesNotInventTumble();
            foreach (float speed in new[] { 10f, 40f, 100f })
            foreach (float spin in new[] { -800f, 800f })
            {
                ShuttleState start = Reversed(speed, spin, 0f);
                ShuttleState normal = Advance(start, .1f, ShuttlePhysics.Step);
                ShuttleState fine = Advance(start, .1f, ShuttlePhysics.Step * .5f);
                string fixture = "speed=" + speed + " spin=" + spin;
                Require(Finite(normal) && Finite(fine), "Nonfinite high-spin flight: " + fixture);
                Require(Vector3.Distance(normal.position, fine.position) < .04f,
                    "High-roll position is sensitive to substep size: " + fixture);
                Require(Vector3.Distance(normal.velocity, fine.velocity) < .08f,
                    "High-roll velocity is sensitive to substep size: " + fixture);
                Require(Vector3.Angle(normal.orientation * Vector3.up, fine.orientation * Vector3.up) < 4f,
                    "High-roll tail response is sensitive to substep size: " + fixture);
                Require(Vector3.Dot(normal.orientation * Vector3.up, -normal.velocity.normalized) > .8f,
                    "Axial roll locked a reversed shuttle tail: " + fixture);
                ShuttleState settled = Advance(start, .25f, ShuttlePhysics.Step);
                Require(Vector3.Dot(settled.orientation * Vector3.up, -settled.velocity.normalized) > .9f,
                    "Shuttle did not settle cork-first: " + fixture);

                // Simultaneous cap-level tumble is a separate stress test. It
                // must stay finite/bounded; we do not claim 4-degree convergence
                // during 350 rad/s end-over-end motion at a 360 Hz base step.
                ShuttleState stress = Reversed(speed, spin, 350f);
                bool bounded = true;
                for (int i = 0; i < 180; i++)
                {
                    ShuttlePhysics.Integrate(ref stress, settings, ShuttlePhysics.Step);
                    Vector3 tail = stress.orientation * Vector3.up;
                    float axial = Vector3.Dot(stress.angularVelocity, tail);
                    Vector3 tumble = stress.angularVelocity - tail * axial;
                    bounded &= Finite(stress) && Mathf.Abs(axial) <= 800.01f &&
                        tumble.magnitude <= 350.01f &&
                        Mathf.Abs(Quaternion.Dot(stress.orientation, stress.orientation) - 1f) < .001f;
                }
                Require(bounded, "Combined high-spin/tumble escaped finite component bounds: " + fixture);
                Require(Vector3.Dot(stress.orientation * Vector3.up, -stress.velocity.normalized) > .8f,
                    "Combined high-spin/tumble did not recover cork-first: " + fixture);
            }
            Debug.Log("[Spin Flight Test] PASS " + checks +
                " checks: split tilt/roll, signed high axial spin, short-time cork-first flip, step convergence and bounded tumble.");
            return checks;
        }

        private static void TestAxialRollDoesNotInventTumble()
        {
            Vector3 tail = new Vector3(.3f, .4f, 1f).normalized;
            foreach (float spin in new[] { -800f, 800f })
            {
                ShuttleState state = ShuttleState.Launch(Vector3.up * 2f, -tail * 20f);
                state.angularVelocity = tail * spin;
                ShuttlePhysics.Integrate(ref state, settings, ShuttlePhysics.Step);
                Vector3 afterTail = state.orientation * Vector3.up;
                float axial = Vector3.Dot(state.angularVelocity, afterTail);
                Require(Vector3.Distance(afterTail, tail) < .00001f,
                    "Pure axial roll unexpectedly moved the tail in one aligned step");
                Require(Vector3.ProjectOnPlane(state.angularVelocity, afterTail).magnitude < .002f,
                    "Old-axis axial velocity leaked into new-axis tumble");
                Require(axial * spin > 0f && Mathf.Abs(axial) < Mathf.Abs(spin),
                    "High axial spin should retain sign while aerodynamic relaxation removes excess spin");
            }
        }

        private static ShuttleState Reversed(float speed, float axial, float tumble)
        {
            ShuttleState state = ShuttleState.Launch(Vector3.up * 2f, Vector3.back * speed);
            state.velocity = Vector3.forward * speed;
            state.angularVelocity = Vector3.forward * axial + Vector3.right * tumble;
            return state;
        }

        private static ShuttleState Advance(ShuttleState state, float duration, float step)
        {
            for (float time = 0f; time < duration - .000001f;)
            {
                float dt = Mathf.Min(step, duration - time);
                ShuttlePhysics.Integrate(ref state, settings, dt);
                time += dt;
            }
            return state;
        }

        private static bool Finite(ShuttleState state)
        {
            float sum = state.position.sqrMagnitude + state.velocity.sqrMagnitude +
                state.angularVelocity.sqrMagnitude + Quaternion.Dot(state.orientation, state.orientation);
            return !float.IsNaN(sum) && !float.IsInfinity(sum);
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("[Spin Flight Test] " + message);
        }
    }
}
