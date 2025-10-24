using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Visualize MCTS tree to show attack resolution with hit/miss branches
/// </summary>
public static class TreeVisualization
{
    public static void Main()
    {
        Console.WriteLine("=== MCTS Tree Visualization: Attack Resolution ===");
        Console.WriteLine("This shows how MCTS enumerates hit/miss outcomes\n");

        var game = new PhaseBasedGame(gridWidth: 7, gridHeight: 7, numHeroes: 2, maxTurns: 30, seed: 42);
        var state = game.InitialState();
        
        // Create a scenario with monster adjacent to Warrior
        var monster = new PhaseMonster(Index: 0, X: 2, Y: 5, IsAlive: true);
        state = state with
        {
            Monsters = ImmutableList.Create(monster),
            CurrentPhase = Phase.HeroAction,
            ActiveHeroIndex = 0
        };

        Console.WriteLine("Initial State:");
        Console.WriteLine("  Warrior (AttackScore=7) at (1,5)");
        Console.WriteLine("  Monster at (2,5)");
        Console.WriteLine("  Hit Chance: 58.3% (21/36 on 2d6)");
        Console.WriteLine();

        // Run MCTS with very few iterations so we can see the tree
        var simulation = new PhaseGameSimulation();
        var mcts = new Mcts.Mcts<PhaseGameState, SquadAction>(
            game,
            new Ucb1Selection<PhaseGameState, SquadAction>(explorationC: 1.414),
            new UniformSingleExpansion<PhaseGameState, SquadAction>(deterministicRollForward: false),
            simulation,
            new SumBackpropagation<PhaseGameState, SquadAction>(),
            new MctsOptions
            {
                Iterations = 50,  // Very few iterations to keep tree small
                RolloutDepth = 10
            }
        );

        Console.WriteLine("Running MCTS with 50 iterations...\n");
        var (action, stats) = mcts.Search(state);

        Console.WriteLine("=== MCTS Statistics ===");
        foreach (var (a, visits, value) in stats)
        {
            double avg = visits > 0 ? value / visits : 0.0;
            Console.WriteLine($"  {a,-12}: visits={visits,3}, totalValue={value,7:F1}, avgValue={avg,6:F2}");
        }

        Console.WriteLine($"\n=== Tree Structure Explanation ===");
        Console.WriteLine("When Warrior chooses Attack:");
        Console.WriteLine("  1. Action: Attack (decision node)");
        Console.WriteLine("  2. Transition to AttackResolution chance node");
        Console.WriteLine("  3. MCTS enumerates TWO branches:");
        Console.WriteLine();
        Console.WriteLine("     Branch A: HIT (probability 0.5833)");
        Console.WriteLine("       - Monster dies");
        Console.WriteLine("       - Reward: +5.0 (killing bonus) -0.05 (time)");
        Console.WriteLine("       - Hero continues with 1 action remaining");
        Console.WriteLine();
        Console.WriteLine("     Branch B: MISS (probability 0.4167)");
        Console.WriteLine("       - Monster survives");
        Console.WriteLine("       - Reward: -0.05 (time penalty only)");
        Console.WriteLine("       - Hero continues with 1 action remaining");
        Console.WriteLine();
        Console.WriteLine("  4. Both branches explored during tree search");
        Console.WriteLine("  5. Expected value = 0.5833×(HIT value) + 0.4167×(MISS value)");
        Console.WriteLine();

        // Now demonstrate by manually exploring the attack
        Console.WriteLine("=== Manual Branch Exploration ===");
        var attackState = game.Step(state, SquadAction.Attack);
        
        if (attackState.AttackResolution != null)
        {
            Console.WriteLine($"After Attack action, state is a chance node");
            Console.WriteLine($"  AttackResolution: Hero {attackState.AttackResolution.HeroIndex} attacking Monster {attackState.AttackResolution.MonsterIndex}");
            Console.WriteLine($"  AttackScore: {attackState.AttackResolution.AttackScore}");
            Console.WriteLine();

            var outcomes = game.ChanceOutcomes(attackState).ToList();
            Console.WriteLine($"Enumerated outcomes: {outcomes.Count}");
            
            for (int i = 0; i < outcomes.Count; i++)
            {
                var (outcome, prob) = outcomes[i];
                string result = outcome.Monsters[0].IsAlive ? "MISS" : "HIT ";
                double reward = outcome.AccumulatedReward - attackState.AccumulatedReward;
                int monstersAlive = outcome.Monsters.Count(m => m.IsAlive);
                
                Console.WriteLine($"\n  Outcome {i + 1}: {result}");
                Console.WriteLine($"    Probability: {prob:F4} ({prob:P1})");
                Console.WriteLine($"    Reward Gain: {reward:+0.00;-0.00}");
                Console.WriteLine($"    Monsters Alive: {monstersAlive}");
                Console.WriteLine($"    Hero Actions Remaining: {outcome.Heroes[0].ActionsRemaining}");
                
                // What actions are available after this outcome?
                var legalActions = game.LegalActions(outcome).ToList();
                Console.WriteLine($"    Legal actions after: {string.Join(", ", legalActions.Take(5))}");
            }
        }

        Console.WriteLine("\n=== Key Insight ===");
        Console.WriteLine("MCTS explores BOTH hit and miss branches during tree search.");
        Console.WriteLine("This allows it to reason about risk vs reward:");
        Console.WriteLine("  - High hit chance (58.3%) makes attack attractive");
        Console.WriteLine("  - But 41.7% chance of wasting an action");
        Console.WriteLine("  - MCTS weighs expected value against alternatives");
        Console.WriteLine();
        Console.WriteLine($"In this scenario, MCTS chose: {action}");
    }
}
