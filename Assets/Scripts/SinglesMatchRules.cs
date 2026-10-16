using System;

namespace ViveShuttleVR
{
    public enum SinglesSide { Player, Opponent }
    public enum SinglesPhase { Ready, Rally, MidGameInterval, GameInterval, MatchOver }

    // BWF 3 x 21 rally scoring, current in October 2026: Laws 7, 8, 10, 14 and 16.2.
    // The approved 3 x 15 replacement takes effect on 2027-01-04, not before.
    // This explicit ruleset intentionally does not change with the computer clock.
    // No Unity objects, timers or input are owned here; the caller decides when play
    // starts and advances intervals only while the match is not externally paused.
    public sealed class SinglesMatchRules
    {
        public const int PointsToWinGame = 21;
        public const int MaximumGamePoints = 30;
        public const int GamesToWinMatch = 2;
        public const float MidGameIntervalSeconds = 60f;
        public const float GameIntervalSeconds = 120f;

        public int PlayerPoints { get; private set; }
        public int OpponentPoints { get; private set; }
        public int PlayerGames { get; private set; }
        public int OpponentGames { get; private set; }
        public SinglesSide Server { get; private set; }
        public SinglesPhase Phase { get; private set; }
        public bool EndsSwapped { get; private set; }
        public bool ServerRight
        {
            get { return ((Server == SinglesSide.Player ? PlayerPoints : OpponentPoints) & 1) == 0; }
        }
        // Awarded rallies only: a let is replayed and does not add a point/rally.
        public int CompletedRallies { get; private set; }
        public int CurrentRallyId { get; private set; }
        public float IntervalRemaining { get; private set; }

        private int nextRallyId;
        private bool midGameIntervalTaken;

        public SinglesMatchRules() { Reset(SinglesSide.Player); }

        public void Reset(SinglesSide firstServer)
        {
            if (!ValidSide(firstServer)) throw new ArgumentOutOfRangeException("firstServer");
            PlayerPoints = OpponentPoints = PlayerGames = OpponentGames = 0;
            Server = firstServer;
            Phase = SinglesPhase.Ready;
            EndsSwapped = false;
            CompletedRallies = CurrentRallyId = 0;
            IntervalRemaining = 0f;
            midGameIntervalTaken = false;
            // Keep the private sequence across reset, so a delayed callback from
            // the previous match cannot award a newly started rally with its ID.
        }

        public bool BeginRally()
        {
            if (Phase != SinglesPhase.Ready || nextRallyId == int.MaxValue) return false;
            CurrentRallyId = ++nextRallyId;
            Phase = SinglesPhase.Rally;
            return true;
        }

        public bool AwardRally(SinglesSide winner, int rallyId)
        {
            if (!ValidSide(winner) || !CanResolve(rallyId)) return false;
            if (winner == SinglesSide.Player) PlayerPoints++; else OpponentPoints++;
            CompletedRallies++;
            Server = winner;

            int leadingPoints = Math.Max(PlayerPoints, OpponentPoints);
            int margin = Math.Abs(PlayerPoints - OpponentPoints);
            bool gameWon = leadingPoints == MaximumGamePoints ||
                (leadingPoints >= PointsToWinGame && margin >= 2);
            if (gameWon)
            {
                if (winner == SinglesSide.Player) PlayerGames++; else OpponentGames++;
                if (PlayerGames == GamesToWinMatch || OpponentGames == GamesToWinMatch)
                {
                    Phase = SinglesPhase.MatchOver;
                    IntervalRemaining = 0f;
                }
                else
                {
                    EndsSwapped = !EndsSwapped;
                    Phase = SinglesPhase.GameInterval;
                    IntervalRemaining = GameIntervalSeconds;
                }
                // Retain the finished game's score throughout the interval and
                // after match-over. Its winner serves first in the next game.
            }
            else if (!midGameIntervalTaken && leadingPoints == 11)
            {
                midGameIntervalTaken = true;
                if (PlayerGames == 1 && OpponentGames == 1) EndsSwapped = !EndsSwapped;
                Phase = SinglesPhase.MidGameInterval;
                IntervalRemaining = MidGameIntervalSeconds;
            }
            else
            {
                Phase = SinglesPhase.Ready;
                IntervalRemaining = 0f;
            }
            return true;
        }

        public bool Let(int rallyId)
        {
            if (!CanResolve(rallyId)) return false;
            Phase = SinglesPhase.Ready;
            // Scores, the server, ends and the one-per-game interval are retained.
            // A fresh BeginRally supplies a new ID for the replay.
            return true;
        }

        // False means no interval ended (including invalid deltas/non-intervals).
        // Overrun time is discarded: it must not run the next rally or next rest.
        public bool AdvanceInterval(float dt)
        {
            if (!InInterval() || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return false;
            if (dt >= IntervalRemaining) return CompleteInterval();
            IntervalRemaining -= dt;
            return false;
        }

        // Caller may offer this when both local participants agree to resume early.
        public bool CompleteInterval()
        {
            if (!InInterval()) return false;
            if (Phase == SinglesPhase.GameInterval)
            {
                PlayerPoints = OpponentPoints = 0;
                midGameIntervalTaken = false;
            }
            IntervalRemaining = 0f;
            Phase = SinglesPhase.Ready;
            return true;
        }

        private bool CanResolve(int rallyId)
        {
            return Phase == SinglesPhase.Rally && rallyId > 0 && rallyId == CurrentRallyId;
        }
        private bool InInterval()
        {
            return Phase == SinglesPhase.MidGameInterval || Phase == SinglesPhase.GameInterval;
        }
        private static bool ValidSide(SinglesSide side)
        {
            return side == SinglesSide.Player || side == SinglesSide.Opponent;
        }
    }
}
