using System;
using System.Collections.Generic;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Simulation policy that uses accumulated rewards from the game state
/// </summary>
public sealed class RewardShapedSimulation : ISimulationPolicy<GameState, SquadAction>
{
    public double Simulate(in GameState state, IGameModel<GameState, SquadAction> game, Random rng, int maxDepth)
    {
        var s = state;
        for (int d = 0; d < maxDepth; d++)
        {
            if (game.IsTerminal(s, out var value))
                return value;

            if (game.IsChanceNode(s))
            {
                s = game.SampleChanceOutcome(s, rng);
                continue;
            }

            var actions = game.LegalActions(s);
            if (actions is IList<SquadAction> listA)
            {
                if (listA.Count == 0) return s.AccumulatedReward; // Return accumulated reward
                s = game.Step(s, listA[rng.Next(listA.Count)]);
            }
            else
            {
                var list = actions.ToList();
                if (list.Count == 0) return s.AccumulatedReward; // Return accumulated reward
                s = game.Step(s, list[rng.Next(list.Count)]);
            }
        }
        
        // At depth cutoff, return the accumulated reward instead of 0
        return s.AccumulatedReward;
    }
}
