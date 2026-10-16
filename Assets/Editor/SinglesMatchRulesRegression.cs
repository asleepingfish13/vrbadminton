using System;

namespace ViveShuttleVR.Editor
{
    // Pure C# fixtures: no scene, XR hardware, physics clock or Unity mutation.
    public static class SinglesMatchRulesRegression
    {
        private static int checks;
        private static void Require(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException("[Singles Rules Test] " + message); }

        public static int Run()
        {
            checks = 0;
            TestStraightGames(SinglesSide.Player);
            TestStraightGames(SinglesSide.Opponent);
            TestDeuce(SinglesSide.Player);
            TestDeuce(SinglesSide.Opponent);
            TestThirtyPointCap(SinglesSide.Player);
            TestThirtyPointCap(SinglesSide.Opponent);
            TestDecidingGame(SinglesSide.Player);
            TestDecidingGame(SinglesSide.Opponent);
            TestIntervals();
            TestServiceAndLets();
            TestResolutionGatesAndReset();
            return checks;
        }

        private static SinglesSide Other(SinglesSide side)
        { return side == SinglesSide.Player ? SinglesSide.Opponent : SinglesSide.Player; }
        private static int Points(SinglesMatchRules rules, SinglesSide side)
        { return side == SinglesSide.Player ? rules.PlayerPoints : rules.OpponentPoints; }
        private static int Games(SinglesMatchRules rules, SinglesSide side)
        { return side == SinglesSide.Player ? rules.PlayerGames : rules.OpponentGames; }

        private static int Score(SinglesMatchRules rules, SinglesSide winner)
        {
            int previousId = rules.CurrentRallyId;
            int previousCount = rules.CompletedRallies;
            int previousPoints = Points(rules, winner);
            int otherPoints = Points(rules, Other(winner));
            Require(rules.BeginRally(), "Ready must accept a new rally");
            int id = rules.CurrentRallyId;
            Require(id > previousId && id > 0, "Rally IDs must increase");
            Require(rules.Phase == SinglesPhase.Rally, "Begin must enter Rally");
            Require(!rules.BeginRally() && rules.CurrentRallyId == id, "A second begin must not replace the active rally");
            Require(rules.AwardRally(winner, id), "Current rally must award once");
            Require(Points(rules, winner) == previousPoints + 1 && Points(rules, Other(winner)) == otherPoints,
                "Rally winner must receive exactly one point");
            Require(rules.CompletedRallies == previousCount + 1, "Awarded rally count mismatch");
            Require(rules.Server == winner, "Rally winner must be the next server");
            Require(rules.ServerRight == ((Points(rules, winner) & 1) == 0), "Serve court must use server's even/odd score");
            string awarded = Snapshot(rules);
            Require(!rules.AwardRally(Other(winner), id) && !rules.Let(id), "Duplicate point/let must be rejected");
            Require(Snapshot(rules) == awarded, "Duplicate resolution must not mutate the match");
            return id;
        }

        private static void WinLoveGame(SinglesMatchRules rules, SinglesSide winner)
        {
            for (int point = 1; point <= 21; point++)
            {
                Score(rules, winner);
                if (point == 11)
                {
                    Require(rules.Phase == SinglesPhase.MidGameInterval && rules.IntervalRemaining == 60f,
                        "Every game must have one 60 second interval at 11");
                    Require(rules.CompleteInterval(), "Agreed early interval completion must succeed");
                }
                else if (point < 21)
                    Require(rules.Phase == SinglesPhase.Ready, "A game must not end before 21 or pause twice");
            }
            Require(Points(rules, winner) == 21 && Points(rules, Other(winner)) == 0, "Expected 21:0 final game score");
        }

        private static void TestStraightGames(SinglesSide winner)
        {
            var rules = new SinglesMatchRules();
            rules.Reset(Other(winner));
            Require(rules.ServerRight && rules.Server == Other(winner), "At 0:0 the first server uses the right court");
            WinLoveGame(rules, winner);
            Require(Games(rules, winner) == 1 && Games(rules, Other(winner)) == 0, "First game must be credited");
            Require(rules.Phase == SinglesPhase.GameInterval && rules.IntervalRemaining == 120f && rules.EndsSwapped,
                "First game must swap ends and start 120 second rest");
            Require(rules.Server == winner && !rules.BeginRally(), "Winning server retained while next game is blocked by rest");
            Require(rules.CompleteInterval(), "Game interval must support agreed early completion");
            Require(rules.PlayerPoints == 0 && rules.OpponentPoints == 0 && rules.ServerRight && rules.Server == winner,
                "Next game starts at 0:0 with previous game winner serving from the right");
            WinLoveGame(rules, winner);
            Require(Games(rules, winner) == 2 && rules.Phase == SinglesPhase.MatchOver,
                "Two won games must end the match");
            Require(rules.CompletedRallies == 42 && rules.EndsSwapped && rules.IntervalRemaining == 0f,
                "Match-over must not add an unnecessary end change or interval");
            string final = Snapshot(rules);
            Require(!rules.BeginRally() && !rules.AwardRally(Other(winner), rules.CurrentRallyId) &&
                !rules.Let(rules.CurrentRallyId) && !rules.CompleteInterval() && !rules.AdvanceInterval(120f),
                "Match-over must reject all play/interval operations");
            Require(Snapshot(rules) == final, "Match-over state must remain stable");
            int finishedMatchId = rules.CurrentRallyId;
            rules.Reset(Other(winner));
            Require(rules.PlayerPoints == 0 && rules.OpponentPoints == 0 && rules.PlayerGames == 0 && rules.OpponentGames == 0 &&
                rules.CompletedRallies == 0 && rules.CurrentRallyId == 0 && !rules.EndsSwapped && rules.IntervalRemaining == 0f &&
                rules.Phase == SinglesPhase.Ready && rules.Server == Other(winner) && rules.ServerRight,
                "Reset after match-over must clear points, games, ends, rest and service state");
            for (int point = 1; point <= 11; point++) Score(rules, Other(winner));
            Require(rules.CurrentRallyId > finishedMatchId && rules.Phase == SinglesPhase.MidGameInterval &&
                rules.IntervalRemaining == 60f && !rules.EndsSwapped,
                "Reset must restore first-game 11-point interval without reusing old IDs or treating it as a decider");
            rules.AdvanceInterval(12f);
            rules.Reset(winner);
            Require(rules.Phase == SinglesPhase.Ready && rules.IntervalRemaining == 0f && rules.CompletedRallies == 0 &&
                rules.PlayerPoints == 0 && rules.OpponentPoints == 0 && rules.Server == winner,
                "Reset during a partially elapsed interval must cancel its countdown and retained scores");
        }

        private static void ReachTie(SinglesMatchRules rules, int target)
        {
            for (int point = 1; point <= target; point++)
            {
                Score(rules, SinglesSide.Player);
                if (point == 11)
                {
                    Require(rules.Phase == SinglesPhase.MidGameInterval, "First 11 must pause");
                    rules.CompleteInterval();
                }
                else Require(rules.Phase == SinglesPhase.Ready, "One-point lead cannot end a deuce game");
                Score(rules, SinglesSide.Opponent);
                Require(rules.Phase == SinglesPhase.Ready, "Equal score must not end game or repeat its interval");
            }
            Require(rules.PlayerPoints == target && rules.OpponentPoints == target, "Expected tied score");
        }

        private static void TestDeuce(SinglesSide winner)
        {
            var rules = new SinglesMatchRules();
            ReachTie(rules, 20);
            Score(rules, winner);
            Require(rules.Phase == SinglesPhase.Ready && Games(rules, winner) == 0, "21:20 must continue");
            Score(rules, winner);
            Require(rules.Phase == SinglesPhase.GameInterval && Points(rules, winner) == 22 &&
                Points(rules, Other(winner)) == 20 && Games(rules, winner) == 1,
                "22:20 must win by two");
        }

        private static void TestThirtyPointCap(SinglesSide winner)
        {
            var rules = new SinglesMatchRules();
            ReachTie(rules, 29);
            Score(rules, winner);
            Require(Points(rules, winner) == 30 && Points(rules, Other(winner)) == 29 &&
                rules.Phase == SinglesPhase.GameInterval && Games(rules, winner) == 1,
                "30:29 must win without a two-point lead");
        }

        private static void TestDecidingGame(SinglesSide firstToEleven)
        {
            var rules = new SinglesMatchRules();
            WinLoveGame(rules, SinglesSide.Player);
            rules.CompleteInterval();
            WinLoveGame(rules, SinglesSide.Opponent);
            Require(rules.PlayerGames == 1 && rules.OpponentGames == 1 && !rules.EndsSwapped,
                "After the second game ends must be restored for a third game");
            Require(rules.Phase == SinglesPhase.GameInterval && rules.IntervalRemaining == 120f,
                "A deciding game still needs the full game interval");
            rules.CompleteInterval();
            Require(rules.Server == SinglesSide.Opponent && rules.ServerRight, "Second game winner opens the third game");
            for (int point = 1; point <= 10; point++)
            {
                Score(rules, firstToEleven);
                Require(!rules.EndsSwapped && rules.Phase == SinglesPhase.Ready, "Deciding-game ends must not swap before 11");
            }
            int elevenId = Score(rules, firstToEleven);
            Require(rules.EndsSwapped && rules.Phase == SinglesPhase.MidGameInterval && rules.IntervalRemaining == 60f,
                "First 11 must atomically swap ends and begin the deciding-game interval");
            Require(!rules.AwardRally(firstToEleven, elevenId) && rules.EndsSwapped,
                "Duplicate 11th point must not swap ends back");
            rules.CompleteInterval();
            SinglesSide eventualWinner = Other(firstToEleven);
            for (int point = 1; point <= 21; point++)
            {
                Score(rules, eventualWinner);
                Require(rules.EndsSwapped, "No second end change when the other side reaches 11 or match ends");
                if (point < 21) Require(rules.Phase == SinglesPhase.Ready, "Other side reaching 11 must not repeat the interval");
            }
            Require(rules.Phase == SinglesPhase.MatchOver && Games(rules, eventualWinner) == 2 &&
                Games(rules, firstToEleven) == 1 && rules.CompletedRallies == 74,
                "Deciding game must finish 2:1 with correct rally count");
        }

        private static void TestIntervals()
        {
            var rules = new SinglesMatchRules();
            Require(!rules.AdvanceInterval(10f) && !rules.CompleteInterval(), "Ready has no interval to advance");
            for (int i = 0; i < 11; i++) Score(rules, SinglesSide.Player);
            string rest = Snapshot(rules);
            Require(!rules.BeginRally() && !rules.AwardRally(SinglesSide.Player, rules.CurrentRallyId) &&
                !rules.Let(rules.CurrentRallyId), "An interval must gate rallies, points and lets");
            foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Require(!rules.AdvanceInterval(invalid) && Snapshot(rules) == rest, "Invalid interval delta must be a no-op");
            Require(!rules.AdvanceInterval(59.5f) && rules.IntervalRemaining == .5f && rules.Phase == SinglesPhase.MidGameInterval,
                "Partial rest must preserve the remainder");
            // Pausing is intentionally external: no advance call means no elapsed time.
            float frozen = rules.IntervalRemaining;
            for (int i = 0; i < 10; i++) Require(rules.IntervalRemaining == frozen, "Clock-free state must not consume paused time");
            Require(rules.AdvanceInterval(.5f) && rules.IntervalRemaining == 0f && rules.Phase == SinglesPhase.Ready &&
                rules.PlayerPoints == 11 && rules.Server == SinglesSide.Player,
                "Exactly 60 seconds must resume with same mid-game score/server");
            Require(!rules.AdvanceInterval(1f) && !rules.CompleteInterval(), "A finished rest must not complete twice");
            for (int i = 11; i < 21; i++) Score(rules, SinglesSide.Player);
            Require(!rules.AdvanceInterval(119.75f) && rules.PlayerPoints == 21 && rules.IntervalRemaining == .25f,
                "Game score must remain visible until game interval ends");
            Require(rules.AdvanceInterval(1000f) && rules.IntervalRemaining == 0f &&
                rules.PlayerPoints == 0 && rules.OpponentPoints == 0 && rules.Phase == SinglesPhase.Ready,
                "Overrunning rest clears the previous game once without advancing play");
        }

        private static void TestServiceAndLets()
        {
            var rules = new SinglesMatchRules();
            rules.Reset(SinglesSide.Opponent);
            Require(rules.Server == SinglesSide.Opponent && rules.ServerRight, "Chosen opening server must be honored");
            foreach (SinglesSide winner in new[] { SinglesSide.Player, SinglesSide.Player, SinglesSide.Opponent,
                SinglesSide.Opponent, SinglesSide.Player }) Score(rules, winner);
            Require(rules.PlayerPoints == 3 && rules.OpponentPoints == 2 && rules.Server == SinglesSide.Player && !rules.ServerRight,
                "Service position must use the server's score, not total points or receiver score");
            for (int replay = 0; replay < 3; replay++)
            {
                int previousId = rules.CurrentRallyId;
                Require(rules.BeginRally(), "Let fixture begin");
                int id = rules.CurrentRallyId;
                Require(id > previousId && rules.Let(id), "Let must accept the current rally once");
                Require(rules.Phase == SinglesPhase.Ready && rules.Server == SinglesSide.Player && !rules.ServerRight &&
                    rules.PlayerPoints == 3 && rules.OpponentPoints == 2 && rules.CompletedRallies == 5 && !rules.EndsSwapped,
                    "Let must not alter score, rally count, service court, server or ends");
                Require(!rules.Let(id) && !rules.AwardRally(SinglesSide.Opponent, id), "Let consumes the rally ID");
            }
            Score(rules, SinglesSide.Opponent);
            Require(rules.OpponentPoints == 3 && rules.CompletedRallies == 6 && !rules.ServerRight,
                "A replayed rally must award normally and transfer service");
        }

        private static void TestResolutionGatesAndReset()
        {
            var rules = new SinglesMatchRules();
            string ready = Snapshot(rules);
            Require(!rules.AwardRally(SinglesSide.Player, 0) && !rules.Let(0) && Snapshot(rules) == ready,
                "No rally exists immediately after reset");
            Require(rules.BeginRally(), "Gate fixture begin");
            int oldId = rules.CurrentRallyId;
            string active = Snapshot(rules);
            foreach (int wrongId in new[] { -1, 0, oldId + 1, int.MaxValue })
                Require(!rules.AwardRally(SinglesSide.Player, wrongId) && !rules.Let(wrongId) && Snapshot(rules) == active,
                    "Mismatched/stale/future IDs must leave active rally untouched");
            Require(!rules.AwardRally((SinglesSide)99, oldId) && Snapshot(rules) == active, "Invalid winner must not consume a rally");
            Require(!rules.AdvanceInterval(60f) && !rules.CompleteInterval() && Snapshot(rules) == active,
                "Rally must reject interval completion");
            Require(rules.Let(oldId) && rules.BeginRally(), "Replay must have a new active rally");
            active = Snapshot(rules);
            Require(!rules.AwardRally(SinglesSide.Player, oldId) && !rules.Let(oldId) && Snapshot(rules) == active,
                "Old rally events must not award the replay");
            int beforeResetId = rules.CurrentRallyId;
            rules.Reset(SinglesSide.Opponent);
            Require(rules.PlayerPoints == 0 && rules.OpponentPoints == 0 && rules.PlayerGames == 0 && rules.OpponentGames == 0 &&
                rules.CurrentRallyId == 0 && rules.CompletedRallies == 0 && !rules.EndsSwapped && rules.IntervalRemaining == 0f &&
                rules.Phase == SinglesPhase.Ready && rules.Server == SinglesSide.Opponent && rules.ServerRight,
                "Reset must restore all visible match state");
            Require(rules.BeginRally() && rules.CurrentRallyId > beforeResetId, "Reset must not reuse IDs from the previous match");
            active = Snapshot(rules);
            Require(!rules.AwardRally(SinglesSide.Player, beforeResetId) && !rules.Let(beforeResetId) && Snapshot(rules) == active,
                "Delayed old-match callback must not resolve a new-match rally");
            bool rejected = false;
            try { rules.Reset((SinglesSide)(-1)); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected && Snapshot(rules) == active, "Invalid reset server must be rejected without state mutation");
            Require(rules.AwardRally(SinglesSide.Opponent, rules.CurrentRallyId), "Valid event must still succeed after rejected events");
        }

        private static string Snapshot(SinglesMatchRules rules)
        {
            return rules.PlayerPoints + ":" + rules.OpponentPoints + "/" + rules.PlayerGames + ":" + rules.OpponentGames +
                "/" + rules.Server + "/" + rules.Phase + "/" + rules.EndsSwapped + "/" + rules.ServerRight +
                "/" + rules.CompletedRallies + "/" + rules.CurrentRallyId + "/" + rules.IntervalRemaining;
        }
    }
}
