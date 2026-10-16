using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using Object = UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    public static class StandardSinglesSessionRegression
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static int checks;
        private static void Require(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException("[Standard Singles Session] " + message); }

        public static int Run()
        {
            checks = 0;
            UnityEngine.Random.State randomBefore = UnityEngine.Random.state;
            try
            {
                EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
                BadmintonSession session = Object.FindObjectOfType<BadmintonSession>();
                Require(session != null && session.shuttleHand != null && session.rackets.Length == 2,
                    "Saved scene needs the session, two tracked controls and the free hand");
                TrackedPoseDriver driver = session.playerCamera.GetComponent<TrackedPoseDriver>();
                Require(driver != null, "Saved scene needs its tracked camera driver");
                driver.enabled = false;
                session.enabled = false;
                session.shuttleHand.enabled = false;
                foreach (RacketController racket in session.rackets) racket.enabled = false;
                foreach (float yaw in new[] { 0f, 137f })
                foreach (bool simulatedHeight in new[] { false, true })
                foreach (SinglesSide server in new[] { SinglesSide.Player, SinglesSide.Opponent })
                {
                    Prepare(session, server, yaw, simulatedHeight);
                    TestServicePlacement(session, server);
                }
                Prepare(session, SinglesSide.Player, 37f, false);
                TestPauseAndServiceLifecycle(session);
                Prepare(session, SinglesSide.Player, 0f, false);
                TestMenuAndRequiredTracking(session);
                Prepare(session, SinglesSide.Player, 137f, true);
                TestIntervalsAndEndConfirmation(session);
            }
            finally
            {
                // Reopening discards all scene-only fixtures, spawned opponent,
                // disabled drivers, hand tracking overrides and changed poses.
                // No scene/asset is saved. Callers must reacquire their session.
                EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
                UnityEngine.Random.state = randomBefore;
            }
            Debug.Log("[Standard Singles Session] PASS " + checks +
                " checks: tracked-pose invariance, dead-ball service placement, diagonal singles boundaries, full-shuttle service height/orientation, upward serve, server ownership, pause and stale event guards, explicit interval/end confirmation. Long-score fixtures inject point events; they are not full physical matches.");
            return checks;
        }

        private static void Prepare(BadmintonSession session, SinglesSide first, float yaw, bool simulatedHeight)
        {
            session.shuttle.Stop();
            session.mode = PracticeMode.SinglesMatch;
            session.drill = PracticeDrill.Rally;
            session.Difficulty = OpponentDifficulty.Easy;
            session.simulatePlayerHeight = simulatedHeight;
            session.compactSeatedTargets = true;
            session.simulatedStature = 1.75f;
            Transform eye = session.playerCamera.transform;
            eye.SetPositionAndRotation(new Vector3(1.4f, simulatedHeight ? 1.25f : 1.63f, -.8f), Quaternion.Euler(0f, yaw, 0f));
            for (int i = 0; i < session.rackets.Length; i++)
            {
                RacketController racket = session.rackets[i];
                racket.transform.SetPositionAndRotation(eye.position + eye.right * (racket.IsFreeHand ? -.23f : .27f) +
                    Vector3.down * .6f + eye.forward * .32f, eye.rotation * Quaternion.Euler(12f, 0f, -8f));
                SetProperty(racket, "IsTracked", true);
                SetProperty(racket, "MenuPressed", false);
            }
            session.shuttleHand.BuildVisuals();
            session.shuttleHand.UpdateVisuals(0f);
            TrackedPoses tracked = new TrackedPoses(session);
            Require(session.Recenter(), "Desktop pose fixture must calibrate");
            tracked.AssertUnchanged("Recenter must not alter tracked head/hands/origin");
            session.ResolveStandardTossChoice(true);
            session.Singles.Reset(first); // Keep the resolved toss/end choice; only force who opens the test match.
            Require(!session.Running && session.Singles.Phase == SinglesPhase.Ready && session.Singles.Server == first,
                "Resolved toss must leave a ready match awaiting explicit player confirmation");
        }

        private static void Confirm(BadmintonSession session)
        {
            TrackedPoses tracked = new TrackedPoses(session);
            session.StartPractice(XRNode.RightHand);
            Require(session.Running && session.StandardRallyActive && session.Singles.Phase == SinglesPhase.Rally,
                "Right trigger must prepare a service court and begin a real rules rally");
            tracked.AssertUnchanged("Service preparation must not write XR poses or origin");
            Vector3 eye = session.court.InverseTransformPoint(session.playerCamera.transform.position);
            float expectedX = session.Singles.ServerRight ? 1.295f : -1.295f;
            Require(Mathf.Abs(eye.x - expectedX) < .00002f && Mathf.Abs(eye.z) < .00002f,
                "Virtual player must stand in the current right/left service box");
            Require(Mathf.Abs(session.opponent.FeetLocal.x + expectedX) < .00002f &&
                session.opponent.FeetLocal.z > BadmintonCourt.NetZ,
                "AI must stand diagonally opposite the stationary player");
        }

        private static void TestServicePlacement(BadmintonSession session, SinglesSide server)
        {
            Confirm(session);
            Require(session.StandardServerIsPlayer == (server == SinglesSide.Player) && session.Singles.ServerRight,
                "Forced first server must open at 0:0 from the right court");
            TestContactAndLandingRules(session);
            PoseState courtDuringRally = new PoseState(session.court);
            int id = session.Singles.CurrentRallyId;
            // Simulate ordinary head movement, then press start again. The
            // tracked movement is input, not a pose that the session may reset.
            session.playerCamera.transform.position += session.court.right * .03f;
            session.playerCamera.transform.rotation *= Quaternion.Euler(0f, 12f, 0f);
            TrackedPoses movedTracked = new TrackedPoses(session);
            session.StartPractice(XRNode.RightHand);
            courtDuringRally.AssertUnchanged("Repeated start during a rally must not reposition the court");
            movedTracked.AssertUnchanged("Repeated start must preserve the latest head/controller input poses");
            Require(session.Singles.CurrentRallyId == id, "Repeated start must retain the current rally ID");

            // A free left hand is allowed only when the player owns service.
            bool accepted = session.BeginHandServe(session.shuttleHand);
            Require(accepted == (server == SinglesSide.Player), "Hand service must respect the rules server");
            Require(session.HandServeHeld == accepted && session.shuttle.Held == accepted && !session.shuttle.Flying,
                "Wrong-server hand press must not create or launch a held shuttle");
            courtDuringRally.AssertUnchanged("Holding/wrong-server input must not move the active service court");

            int previousId = FinishPointFixture(session, server);
            Require(session.Singles.Phase == SinglesPhase.Ready && !session.Running && !session.Singles.ServerRight,
                "First point winner must next serve from the left after confirmation");
            PoseState courtAfterPoint = new PoseState(session.court);
            Confirm(session);
            Require(Vector3.Distance(session.court.position, courtAfterPoint.WorldPosition) > 2.5f,
                "Next-point confirmation must move only the virtual court from right to left service box");
            int nextId = session.Singles.CurrentRallyId;
            int completed = session.Singles.CompletedRallies;
            session.FinishStandardRally("LAND", new Vector3(0f, 0f, BadmintonCourt.NetZ + 2f), true, previousId);
            Require(session.Singles.CurrentRallyId == nextId && session.Singles.CompletedRallies == completed &&
                session.Singles.Phase == SinglesPhase.Rally && session.Running,
                "A delayed previous-rally callback must not award or stop the new rally");
            TestContactAndLandingRules(session);
        }

        private static void TestContactAndLandingRules(BadmintonSession session)
        {
            bool player = session.StandardServerIsPlayer;
            float[] tilts = { 0f, 90f, 180f, 45f };
            float[] extents = { .086f, .041f, .0185f, (.08f + .035f) * .70710678f + .006f };
            for (int i = 0; i < tilts.Length; i++)
            {
                ShuttleState state = ContactState(session, player, 1f, tilts[i]);
                Require(Mathf.Abs(session.StandardShuttleTop(state) - (1f + extents[i])) < .00001f,
                    "Full-shuttle height must include the rotated feather skirt and cork, tilt=" + tilts[i]);
                state.position = session.court.TransformPoint(new Vector3(player ? 0f : session.opponent.FeetLocal.x,
                    1.14f - extents[i], player ? .4f : 6.7f));
                string fault;
                ImpactResult upward = Outgoing(session, player, 2f);
                Require(session.ValidateStandardContact(player, state, Contact(state), upward, out fault) && fault == "",
                    "A 1.14 m whole-shuttle upward serve should be accepted, tilt=" + tilts[i] + ", fault=" + fault);
                state.position += Vector3.up * .02f;
                Require(!session.ValidateStandardContact(player, state, Contact(state), upward, out fault) && fault == "SERVICE_HEIGHT",
                    "A feather/cork top above 1.15 m must fault even when the cork origin is lower");
            }
            // Exact upright boundary is tested with local-space height. A
            // separate 1.16 m test above avoids hiding orientation/floor errors.
            ShuttleState boundary = ContactState(session, player, 1.15f - .086f, 0f);
            string reason;
            Require(Mathf.Abs(session.StandardShuttleTop(boundary) - 1.15f) < .000001f,
                "Boundary fixture must put the whole shuttle top at 1.15 m");
            Require(!session.ValidateStandardContact(player, boundary, Contact(boundary), Outgoing(session, player, 2f), out reason) &&
                reason == "SERVICE_HEIGHT", "Exactly 1.15 m is not below the service-height limit");
            ShuttleState low = ContactState(session, player, 1f, 0f);
            foreach (float vertical in new[] { -1f, 0f })
                Require(!session.ValidateStandardContact(player, low, Contact(low), Outgoing(session, player, vertical), out reason) &&
                    reason == "SERVICE_UPWARD", "A downward/flat initial serve must be rejected");
            ImpactResult backward = Outgoing(session, !player, 2f);
            Require(!session.ValidateStandardContact(player, low, Contact(low), backward, out reason) && reason == "SERVICE_UPWARD",
                "An upward serve toward the server's own baseline must be rejected");
            ShuttleState wrongServer = ContactState(session, !player, 1f, 0f);
            Require(!session.ValidateStandardContact(!player, wrongServer, Contact(wrongServer), Outgoing(session, !player, 2f), out reason) &&
                reason == "WRONG_SERVER", "The receiver cannot hit the initial serve");
            ShuttleState overNet = low;
            overNet.position = session.court.TransformPoint(new Vector3(0f, 1f, player ? 4f : 3f));
            Require(!session.ValidateStandardContact(player, overNet, Contact(overNet), Outgoing(session, player, 2f), out reason) &&
                reason == "OVER_NET", "First racket contact must remain on the striker's side of the net");
            TestServiceLandingZones(session);
        }

        private static void TestServiceLandingZones(BadmintonSession session)
        {
            bool far = session.StandardServerIsPlayer;
            float forward = far ? 1f : -1f;
            float side = (far ? !session.Singles.ServerRight : session.Singles.ServerRight) ? 1f : -1f;
            Func<float, float, Vector3> point = (x, distance) => new Vector3(side * x, 0f, BadmintonCourt.NetZ + forward * distance);
            Require(session.IsStandardServiceLandingLegal(point(1.295f, 3.5f)), "Service must land diagonally opposite");
            Require(!session.IsStandardServiceLandingLegal(point(-1.295f, 3.5f)), "Same-side service landing must be out");
            Require(session.IsStandardServiceLandingLegal(point(2.59f, 3.5f)) &&
                !session.IsStandardServiceLandingLegal(point(2.5901f, 3.5f)), "Singles sideline is in; beyond its outer edge is out");
            Require(!session.IsStandardServiceLandingLegal(point(2.8f, 3.5f)), "Doubles side alley must not count as singles service court");
            Require(session.IsStandardServiceLandingLegal(point(0f, 3.5f)), "Centre service line belongs to the service court");
            Require(session.IsStandardServiceLandingLegal(point(1f, 1.98f)) &&
                !session.IsStandardServiceLandingLegal(point(1f, 1.9799f)), "Short service line is in; immediately short is out");
            Require(session.IsStandardServiceLandingLegal(point(1f, 5.94f)) &&
                session.IsStandardServiceLandingLegal(point(1f, 6.1f)), "Singles service continues beyond the doubles long service line");
            Require(session.IsStandardServiceLandingLegal(point(1f, 6.7f)) &&
                !session.IsStandardServiceLandingLegal(point(1f, 6.7001f)), "Singles back boundary is in; beyond baseline is out");
            Require(!session.IsStandardServiceLandingLegal(point(1f, -3f)), "Landing back on the server's half cannot be a legal service");
        }

        private static void TestPauseAndServiceLifecycle(BadmintonSession session)
        {
            Confirm(session);
            ShuttleState state = ContactState(session, true, 1f, 0f);
            RacketContact contact = Contact(state);
            ImpactResult impact = Outgoing(session, true, 2f);
            string fault;
            Require(session.ValidateStandardContact(true, state, contact, impact, out fault), "Service lifecycle fixture must be legal");
            session.OnStandardContact(true, state, contact, impact);
            Require(session.StandardServicePending, "First legal serve contact must await reception or diagonal landing");
            int id = session.Singles.CurrentRallyId;
            PoseState court = new PoseState(session.court);
            TrackedPoses tracked = new TrackedPoses(session);
            session.TogglePause();
            Require(!session.Running && !session.StandardRallyActive && session.StandardServicePending &&
                session.Singles.CurrentRallyId == id && session.Singles.Phase == SinglesPhase.Rally,
                "Pause must preserve the active rally ID and pending service");
            Require(!session.ValidateStandardContact(true, state, contact, impact, out fault) && fault == "NOT_READY",
                "Paused match must reject contact validation");
            Invoke(session, "Update");
            session.StartPractice(XRNode.RightHand);
            Require(session.Running && session.StandardServicePending && session.Singles.CurrentRallyId == id &&
                session.Singles.Server == SinglesSide.Player, "Resume must retain service ownership and not create another rally");
            court.AssertUnchanged("Resuming an active rally must not prepare or move the court again");
            tracked.AssertUnchanged("Pause/resume must not modify tracked transforms");
            ShuttleState received = ContactState(session, false, 1.5f, 0f);
            Require(session.ValidateStandardContact(false, received, Contact(received), Outgoing(session, false, -1f), out fault),
                "Receiver's rally shot is not subject to initial service height/upward restrictions");
            session.OnStandardContact(false, received, Contact(received), Outgoing(session, false, -1f));
            Require(!session.StandardServicePending, "Opponent contact must complete the service reception stage");
            FinishPointFixture(session, SinglesSide.Opponent);
            Confirm(session);
            Require(!session.BeginHandServe(session.shuttleHand) && !session.HandServeHeld && !session.shuttle.Held,
                "Player cannot take AI's service after losing a rally");
        }

        private static void TestIntervalsAndEndConfirmation(BadmintonSession session)
        {
            Confirm(session);
            // These deliberately synthesize legal end-of-rally events. They test
            // session transitions, not 53 complete physics-driven exchanges.
            for (int game = 0; game < 2; game++)
            {
                SinglesSide winner = game == 0 ? SinglesSide.Player : SinglesSide.Opponent;
                for (int point = 1; point <= 21; point++)
                {
                    if (!session.StandardRallyActive) Confirm(session);
                    FinishPointFixture(session, winner);
                    if (point == 11) ExpireThenConfirm(session, SinglesPhase.MidGameInterval, 60f, false);
                }
                Require(session.Singles.Phase == SinglesPhase.GameInterval && session.Running,
                    "Game victory must start a session interval without auto-serving");
                ExpireThenConfirm(session, SinglesPhase.GameInterval, 120f, true);
            }
            Require(session.Singles.PlayerGames == 1 && session.Singles.OpponentGames == 1 && !session.Singles.EndsSwapped,
                "Third-game fixture must enter at 1:1 after two end changes");
            for (int point = 1; point <= 11; point++)
            {
                if (!session.StandardRallyActive) Confirm(session);
                FinishPointFixture(session, SinglesSide.Player);
            }
            Require(session.Singles.Phase == SinglesPhase.MidGameInterval && session.Singles.EndsSwapped,
                "Deciding-game 11th point must register rules end change and interval together");
            ExpireThenConfirm(session, SinglesPhase.MidGameInterval, 60f, true);
            Require(session.Singles.PlayerPoints == 11 && session.Singles.OpponentPoints == 0 && session.Singles.CompletedRallies == 53,
                "Interval/start confirmation must not alter score or count another point");
        }

        private static void TestMenuAndRequiredTracking(BadmintonSession session)
        {
            PoseState readyCourt = new PoseState(session.court);
            session.TogglePause();
            Require(!session.Running && session.Singles.Phase == SinglesPhase.Ready && session.Singles.CurrentRallyId == 0,
                "Menu at Ready must not implicitly confirm the next service court or begin a rally");
            readyCourt.AssertUnchanged("Menu at Ready must leave the virtual court in place");
            Require(session.Recenter(), "Menu/toss fixture recenter");
            SinglesSide tossServer = session.Singles.Server;
            session.TogglePause();
            Require((bool)typeof(BadmintonSession).GetField("standardTossPending", Flags).GetValue(session) &&
                session.Singles.Server == tossServer && session.Singles.CurrentRallyId == 0 && !session.Running,
                "Menu during toss must not choose first reception or an end for the player");
            for (int i = 0; i < Enum.GetValues(typeof(EquipmentSetting)).Length; i++)
            {
                session.CycleEquipmentSelection();
                string selected = (string)Invoke(session, "SelectedSettingLabel");
                Require(session.detailText.text.Contains("设置：" + selected) && session.detailText.text.Contains("掷币"),
                    "Toss instructions must retain the visible current settings selection");
                Require((bool)typeof(BadmintonSession).GetField("standardTossPending", Flags).GetValue(session),
                    "Browsing settings must not resolve the toss");
            }
            session.SetDifficulty(OpponentDifficulty.Club);
            Require(session.detailText.text.Contains(GameLocalization.Difficulty(OpponentDifficulty.Club)) &&
                session.Singles.CurrentRallyId == 0 && session.PlayerPoints == 0 && session.OpponentPoints == 0,
                "Toss-time difficulty change must display its value without starting or scoring a match");
            session.ResolveStandardTossChoice(true);
            session.Singles.Reset(SinglesSide.Player);
            Confirm(session);
            RacketController right = Array.Find(session.rackets, item => item != null && item.CanStrike);
            RacketController left = session.shuttleHand.source;
            Require(right != null && left != null, "Tracking fixture needs a striking right control and a free left control");
            SetProperty(right, "IsTracked", false);
            SetProperty(left, "IsTracked", true);
            Require(!(bool)Invoke(session, "RequiredControllersTracked"),
                "Left-hand tracking alone must not keep a formal match playable when the racket is lost");
            SetProperty(right, "IsTracked", true);
            SetProperty(left, "IsTracked", false);
            Require((bool)Invoke(session, "RequiredControllersTracked"),
                "An unneeded free hand may be untracked when the right racket is tracked");
            SetProperty(left, "IsTracked", true);
            Require(session.BeginHandServe(session.shuttleHand), "Tracking fixture must hold a service shuttle");
            SetProperty(left, "IsTracked", false);
            Require(!(bool)Invoke(session, "RequiredControllersTracked"), "Holding a shuttle also requires the holding hand's tracking");
            SetProperty(left, "IsTracked", true);
            SetProperty(right, "IsTracked", false);
            Require(!(bool)Invoke(session, "RequiredControllersTracked"), "A tracked holding hand must not replace the missing racket");
            SetProperty(right, "IsTracked", true);
            Require((bool)Invoke(session, "RequiredControllersTracked"), "Both tracked controls must satisfy held-service readiness");

            // Finish two synthetic scored games to exercise the real session's
            // match-over menu path; these are not physical shot simulations.
            for (int game = 0; game < 2; game++)
            for (int point = 1; point <= 21; point++)
            {
                if (!session.StandardRallyActive) Confirm(session);
                FinishPointFixture(session, SinglesSide.Player);
            }
            Require(session.MatchComplete && !session.Running && session.Singles.PlayerGames == 2 &&
                session.Singles.CompletedRallies == 42, "Match-over menu fixture must contain a completed match");
            PoseState finalCourt = new PoseState(session.court);
            int finalId = session.Singles.CurrentRallyId;
            session.TogglePause();
            Require(session.MatchComplete && !session.Running && session.Singles.PlayerGames == 2 &&
                session.PlayerPoints == 21 && session.Singles.CurrentRallyId == finalId && session.Singles.CompletedRallies == 42,
                "Menu at match-over must retain the result instead of starting a new toss or resetting scores");
            finalCourt.AssertUnchanged("Menu at match-over must not recenter the court");
        }

        private static void ExpireThenConfirm(BadmintonSession session, SinglesPhase phase, float duration, bool flipVenue)
        {
            Require(session.Singles.Phase == phase && session.Singles.IntervalRemaining == duration,
                "Expected correct interval phase and full allowance");
            PoseState court = new PoseState(session.court);
            Transform venue = session.court.Find("Regulation court and indoor hall");
            Require(venue != null, "Saved scene needs the regulation venue hierarchy");
            PoseState hall = new PoseState(venue);
            Quaternion hallBefore = venue.localRotation;
            TrackedPoses tracked = new TrackedPoses(session);
            int id = session.Singles.CurrentRallyId;
            session.TogglePause();
            for (int i = 0; i < 3; i++) Invoke(session, "Update");
            Require(!session.Running && session.Singles.Phase == phase && session.Singles.IntervalRemaining == duration &&
                session.Singles.CurrentRallyId == id, "Pause must not consume interval time or service/rally state");
            court.AssertUnchanged("Paused interval must not move the court");
            hall.AssertUnchanged("Paused interval must not apply pending end change");
            session.TogglePause();
            Require(session.Running && session.Singles.Phase == phase && session.Singles.IntervalRemaining == duration &&
                session.Singles.CurrentRallyId == id,
                "Menu resume must continue the same interval, not consent to end it early or start another rally");
            court.AssertUnchanged("Resuming an interval with menu must not prepare a new service court");
            hall.AssertUnchanged("Resuming an interval with menu must not apply a pending end change");
            // Advance the resumed timer deterministically without wall time.
            bool handled = (bool)Invoke(session, "AdvanceStandardInterval", duration);
            Require(handled && session.Singles.Phase == SinglesPhase.Ready && !session.Running,
                "Expired interval must wait for a new confirmation, not auto-start play");
            court.AssertUnchanged("Interval expiration alone must not move the virtual court");
            hall.AssertUnchanged("Interval expiration alone must not apply the pending venue end");
            Confirm(session);
            float hallAngle = Quaternion.Angle(hallBefore, venue.localRotation);
            Require(flipVenue ? Mathf.Abs(hallAngle - 180f) < .001f : hallAngle < .001f,
                "Venue end change must occur only on the next explicit preparation, and only when due");
            tracked.AssertUnchanged("Interval/end exchange must leave real head, hands and XR origin unchanged");
        }

        private static int FinishPointFixture(BadmintonSession session, SinglesSide winner)
        {
            Require(session.StandardRallyActive, "Point event fixture requires a currently active session rally");
            // Score-event fixture only: emulate a service already exchanged.
            // Do not spoof points, game count, the server, phase or rally IDs.
            SetField(session, "standardServiceDelivered", true);
            SetField(session, "standardServiceReceived", true);
            int completed = session.Singles.CompletedRallies;
            int winnerPoints = winner == SinglesSide.Player ? session.PlayerPoints : session.OpponentPoints;
            int id = session.Singles.CurrentRallyId;
            PoseState court = new PoseState(session.court);
            PoseState hall = new PoseState(session.court.Find("Regulation court and indoor hall"));
            TrackedPoses tracked = new TrackedPoses(session);
            Vector3 landing = new Vector3(0f, 0f, BadmintonCourt.NetZ + (winner == SinglesSide.Player ? 2f : -2f));
            session.FinishStandardRally("LAND", landing, winner == SinglesSide.Player, id);
            Require(session.Singles.CompletedRallies == completed + 1 && session.Singles.Server == winner &&
                (winner == SinglesSide.Player ? session.PlayerPoints : session.OpponentPoints) == winnerPoints + 1,
                "One legal end event must award exactly one point and transfer service to its winner");
            court.AssertUnchanged("Point end must not reposition the court before the user confirms");
            hall.AssertUnchanged("Point/game end must not immediately rotate venue art");
            tracked.AssertUnchanged("Point/game end must leave tracked head/hands/origin untouched");
            int playerPoints = session.PlayerPoints, opponentPoints = session.OpponentPoints;
            SinglesPhase phase = session.Singles.Phase;
            session.FinishStandardRally("LAND", landing, winner == SinglesSide.Player, id);
            Require(session.Singles.CompletedRallies == completed + 1 && session.PlayerPoints == playerPoints &&
                session.OpponentPoints == opponentPoints && session.Singles.Phase == phase,
                "Immediately duplicated end callback must not score twice or skip an interval");
            return id;
        }

        private static ShuttleState ContactState(BadmintonSession session, bool player, float localHeight, float tilt)
        {
            return new ShuttleState {
                position = session.court.TransformPoint(new Vector3(player ? 0f : session.opponent.FeetLocal.x, localHeight, player ? .4f : 6.7f)),
                orientation = session.court.rotation * Quaternion.Euler(tilt, 0f, 0f), velocity = Vector3.down * .3f
            };
        }
        private static RacketContact Contact(ShuttleState state)
        { return new RacketContact { point = state.position, center = state.position, faceRotation = Quaternion.identity }; }
        private static ImpactResult Outgoing(BadmintonSession session, bool player, float vertical)
        { return new ImpactResult { hit = true, outgoingVelocity = session.court.TransformDirection(new Vector3(0f, vertical, player ? 4f : -4f)) }; }
        private static object Invoke(object target, string method, params object[] args)
        { return target.GetType().GetMethod(method, Flags).Invoke(target, args); }
        private static void SetProperty(object target, string property, object value)
        { target.GetType().GetProperty(property, Flags).SetValue(target, value, null); }
        private static void SetField(object target, string field, object value)
        { target.GetType().GetField(field, Flags).SetValue(target, value); }

        private sealed class TrackedPoses
        {
            private readonly List<PoseState> poses = new List<PoseState>();
            public TrackedPoses(BadmintonSession session)
            {
                var tracked = new HashSet<Transform>();
                AddAncestors(tracked, session.playerCamera.transform);
                foreach (RacketController racket in session.rackets) AddAncestors(tracked, racket.transform);
                foreach (Transform transform in tracked) poses.Add(new PoseState(transform));
            }
            private static void AddAncestors(HashSet<Transform> targets, Transform target)
            { for (Transform current = target; current != null; current = current.parent) targets.Add(current); }
            public void AssertUnchanged(string label)
            { foreach (PoseState pose in poses) pose.AssertUnchanged(label); }
        }
        private sealed class PoseState
        {
            private readonly Transform target, parent;
            private readonly Vector3 position, localPosition, localScale, worldScale;
            private readonly Quaternion rotation, localRotation;
            public Vector3 WorldPosition { get { return position; } }
            public PoseState(Transform transform)
            {
                if (transform == null) throw new InvalidOperationException("Pose fixture requires an existing transform");
                target = transform; parent = transform.parent;
                position = transform.position; rotation = transform.rotation;
                localPosition = transform.localPosition; localRotation = transform.localRotation;
                localScale = transform.localScale; worldScale = transform.lossyScale;
            }
            public void AssertUnchanged(string label)
            {
                Require(target != null && target.parent == parent && Vector3.Distance(target.position, position) < .00001f &&
                    Vector3.Distance(target.localPosition, localPosition) < .00001f && Quaternion.Angle(target.rotation, rotation) < .001f &&
                    Quaternion.Angle(target.localRotation, localRotation) < .001f && Vector3.Distance(target.localScale, localScale) < .00001f &&
                    Vector3.Distance(target.lossyScale, worldScale) < .00001f, label + " / " + (target != null ? target.name : "destroyed"));
            }
        }
    }
}
