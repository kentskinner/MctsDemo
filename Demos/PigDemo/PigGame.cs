using System;
using System.Collections.Generic;
using Mcts;

namespace PigDemo
{
    // =============================================================
    // Pig Dice Game (CHANCE nodes example)
    // =============================================================

    // Actions: player can Roll (which creates a CHANCE state) or Hold (bank turn points)
    public enum PigAction { Roll, Hold }

    public readonly record struct PigState(
        int P0, // score of root player
        int P1, // score of opponent
        int TurnTotal,
        int PlayerToMove, // 0 = root, 1 = opponent
        bool AwaitingRoll // if true => CHANCE node
    );

    public sealed class PigGame : IGameModel<PigState, PigAction>
    {
        private readonly int _target;
        public PigGame(int targetScore = 20) { _target = targetScore; }

        public bool IsTerminal(in PigState s, out double terminalValue)
        {
            if (s.P0 >= _target)
            {
                terminalValue = +1.0; // root wins
                return true;
            }
            if (s.P1 >= _target)
            {
                terminalValue = -1.0; // root loses
                return true;
            }
            terminalValue = 0;
            return false;
        }

        public bool IsChanceNode(in PigState s) => s.AwaitingRoll;

        public IEnumerable<(PigState outcome, double probability)> ChanceOutcomes(PigState s)
        {
            if (!s.AwaitingRoll) yield break;

            // Six equally likely die rolls
            for (int die = 1; die <= 6; die++)
            {
                PigState outcome;
                if (die == 1)
                {
                    // Bust: lose turn total, pass turn
                    outcome = s.PlayerToMove == 0
                        ? new PigState(s.P0, s.P1, 0, 1, false)
                        : new PigState(s.P0, s.P1, 0, 0, false);
                }
                else
                {
                    // Add to turn total and continue player's turn
                    outcome = new PigState(s.P0, s.P1, s.TurnTotal + die, s.PlayerToMove, false);
                }
                yield return (outcome, 1.0 / 6.0);
            }
        }

        public PigState SampleChanceOutcome(in PigState s, Random rng)
        {
            int die = rng.Next(1, 7);
            if (die == 1)
            {
                // Bust: lose turn total, pass turn
                return s.PlayerToMove == 0
                    ? new PigState(s.P0, s.P1, 0, 1, false)
                    : new PigState(s.P0, s.P1, 0, 0, false);
            }
            else
            {
                // Add to turn total and continue player's turn
                return new PigState(s.P0, s.P1, s.TurnTotal + die, s.PlayerToMove, false);
            }
        }

        public PigState SampleChance(in PigState s, Random rng, out double logProb)
        {
            // Roll a fair d6
            int die = rng.Next(1, 7);
            logProb = Math.Log(1.0 / 6.0);

            if (die == 1)
            {
                // Bust: lose turn total, pass turn
                return s.PlayerToMove == 0
                    ? new PigState(s.P0, s.P1, 0, 1, false)
                    : new PigState(s.P0, s.P1, 0, 0, false);
            }
            else
            {
                // Add to turn total and continue player's turn
                return new PigState(s.P0, s.P1, s.TurnTotal + die, s.PlayerToMove, false);
            }
        }

        public IEnumerable<PigAction> LegalActions(PigState s)
        {
            if (s.AwaitingRoll) yield break; // CHANCE state has no player actions

            // Always allow Roll; allow Hold if there's something to hold (optional rule: allow Hold anytime)
            yield return PigAction.Roll;
            if (s.TurnTotal > 0) yield return PigAction.Hold;
        }

        public PigState Step(in PigState s, in PigAction a)
        {
            if (a == PigAction.Roll)
            {
                // Move to CHANCE node: awaiting die result
                return new PigState(s.P0, s.P1, s.TurnTotal, s.PlayerToMove, true);
            }
            else // Hold
            {
                if (s.PlayerToMove == 0)
                {
                    return new PigState(s.P0 + s.TurnTotal, s.P1, 0, 1, false);
                }
                else
                {
                    return new PigState(s.P0, s.P1 + s.TurnTotal, 0, 0, false);
                }
            }
        }
    }
}
