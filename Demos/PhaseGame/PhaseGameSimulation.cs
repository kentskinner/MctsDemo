using System;
using System.Collections.Generic;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Simulation policy for PhaseGameState that returns accumulated rewards
/// </summary>
public sealed class PhaseGameSimulation : ISimulationPolicy<PhaseGameState, SquadAction>
{
    public double Simulate(in PhaseGameState state, IGameModel<PhaseGameState, SquadAction> game, Random rng, int maxDepth)
    {
        var s = state;
        for (int d = 0; d < maxDepth; d++)
        {
            if (game.IsTerminal(s, out var value))
                return value + s.AccumulatedReward;

            if (game.IsChanceNode(s))
            {
                s = game.SampleChanceOutcome(s, rng);
                continue;
            }

            var actions = game.LegalActions(s);
            if (actions is IList<SquadAction> listA)
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
        
        return s.AccumulatedReward;
    }
}
