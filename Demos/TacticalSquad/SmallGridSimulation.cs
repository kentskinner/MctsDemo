using System;
using System.Collections.Generic;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Simulation policy for SmallState that returns accumulated rewards
/// </summary>
public sealed class SmallGridSimulation : ISimulationPolicy<SmallState, SmallAction>
{
    public double Simulate(in SmallState state, IGameModel<SmallState, SmallAction> game, Random rng, int maxDepth)
    {
        var s = state;
        for (int d = 0; d < maxDepth; d++)
        {
            if (game.IsTerminal(s, out var value))
                return value + s.AccumulatedReward; // Add accumulated rewards to terminal value

            var actions = game.LegalActions(s);
            if (actions is IList<SmallAction> listA)
            {
                if (listA.Count == 0) return s.AccumulatedReward;
                s = game.Step(s, listA[rng.Next(listA.Count)]);
            }
            else
            {
                var list = actions.ToList();
                if (list.Count == 0) return s.AccumulatedReward;
                s = game.Step(s, list[rng.Next(list.Count)]);
            }
        }
        
        // At depth cutoff, return accumulated reward
        return s.AccumulatedReward;
    }
}
