using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Shows detailed MCTS tree structure with attack branches
/// </summary>
public static class DetailedTreeView
{
    public static void Main()
    {
        Console.WriteLine("=== Detailed MCTS Tree: Attack with Hit/Miss Branches ===\n");

        var game = new PhaseBasedGame(gridWidth: 7, gridHeight: 7, numHeroes: 2, maxTurns: 30, seed: 42);
        var state = game.InitialState();
        
        // Setup: Monster adjacent to Warrior
        var monster = new PhaseMonster(Index: 0, X: 2, Y: 5, IsAlive: true);
        state = state with
        {
            Monsters = ImmutableList.Create(monster),
            CurrentPhase = Phase.HeroAction,
            ActiveHeroIndex = 0
        };

        Console.WriteLine("ROOT STATE (Decision Node)");
        Console.WriteLine("├─ Warrior at (1,5), 2 actions, Healthy");
        Console.WriteLine("├─ Monster at (2,5), Alive");
        Console.WriteLine("└─ Accumulated Reward: 0.00");
        Console.WriteLine();

        var legalActions = game.LegalActions(state).ToList();
        Console.WriteLine($"Legal Actions: {string.Join(", ", legalActions)}");
        Console.WriteLine();
        Console.WriteLine("Let's trace the Attack action in detail:\n");

        // Step 1: Take Attack action
        Console.WriteLine("┌─────────────────────────────────────────────────────┐");
        Console.WriteLine("│ STEP 1: Warrior chooses Attack action              │");
        Console.WriteLine("└─────────────────────────────────────────────────────┘");
        var attackState = game.Step(state, SquadAction.Attack);
        
        Console.WriteLine("  State after Step():");
        Console.WriteLine($"    - Warrior actions remaining: {attackState.Heroes[0].ActionsRemaining}");
        Console.WriteLine($"    - Accumulated reward: {attackState.AccumulatedReward:F2} (time penalty)");
        Console.WriteLine($"    - AttackResolution: Hero {attackState.AttackResolution?.HeroIndex} vs Monster {attackState.AttackResolution?.MonsterIndex}");
        Console.WriteLine($"    - IsChanceNode: {game.IsChanceNode(attackState)}");
        Console.WriteLine();

        // Step 2: Enumerate chance outcomes
        Console.WriteLine("┌─────────────────────────────────────────────────────┐");
        Console.WriteLine("│ STEP 2: MCTS enumerates chance outcomes            │");
        Console.WriteLine("└─────────────────────────────────────────────────────┘");
        
        var outcomes = game.ChanceOutcomes(attackState).ToList();
        Console.WriteLine($"  ChanceOutcomes() returns {outcomes.Count} branches:\n");

        // Show the tree structure
        Console.WriteLine("  ROOT (after Attack)");
        Console.WriteLine("  │");
        Console.WriteLine("  ├──[CHANCE NODE: Attack Resolution]");
        Console.WriteLine("  │   AttackScore: 7 (need ≥7 on 2d6)");
        Console.WriteLine("  │");

        for (int i = 0; i < outcomes.Count; i++)
        {
            var (outcome, prob) = outcomes[i];
            bool isHit = !outcome.Monsters[0].IsAlive;
            string branch = i == 0 ? "├──" : "└──";
            
            Console.WriteLine($"  {branch} Branch {i + 1}: {(isHit ? "HIT" : "MISS")} (p={prob:F4})");
            Console.WriteLine($"  │   │");
            Console.WriteLine($"  │   ├─ Probability: {prob:P1}");
            Console.WriteLine($"  │   ├─ Monster alive: {outcome.Monsters[0].IsAlive}");
            Console.WriteLine($"  │   ├─ Accumulated reward: {outcome.AccumulatedReward:F2}");
            Console.WriteLine($"  │   ├─ Reward delta: {(outcome.AccumulatedReward - attackState.AccumulatedReward):+0.00;-0.00}");
            Console.WriteLine($"  │   └─ Hero actions: {outcome.Heroes[0].ActionsRemaining}");
            
            if (i == 0)
                Console.WriteLine("  │");
        }

        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────────┐");
        Console.WriteLine("│ STEP 3: MCTS explores both branches                │");
        Console.WriteLine("└─────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine("  From HIT branch (58.3% probability):");
        Console.WriteLine("    - Monster is dead, no longer a threat");
        Console.WriteLine("    - Hero has 1 action left");
        Console.WriteLine("    - Can move toward exit safely");
        Console.WriteLine("    - Expected future value: HIGH");
        Console.WriteLine();
        Console.WriteLine("  From MISS branch (41.7% probability):");
        Console.WriteLine("    - Monster still alive and adjacent");
        Console.WriteLine("    - Hero has 1 action left");
        Console.WriteLine("    - Can attack again, or retreat");
        Console.WriteLine("    - Expected future value: LOWER (monster still threatens)");
        Console.WriteLine();

        // Show expected value calculation
        var hitOutcome = outcomes[0];
        var missOutcome = outcomes[1];
        double hitProb = hitOutcome.probability;
        double missProb = missOutcome.probability;

        Console.WriteLine("┌─────────────────────────────────────────────────────┐");
        Console.WriteLine("│ STEP 4: Expected value calculation                 │");
        Console.WriteLine("└─────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine("  Immediate rewards:");
        Console.WriteLine($"    HIT branch:  {hitOutcome.outcome.AccumulatedReward:F2} × {hitProb:F4} = {hitOutcome.outcome.AccumulatedReward * hitProb:F4}");
        Console.WriteLine($"    MISS branch: {missOutcome.outcome.AccumulatedReward:F2} × {missProb:F4} = {missOutcome.outcome.AccumulatedReward * missProb:F4}");
        Console.WriteLine($"    Expected immediate reward: {(hitOutcome.outcome.AccumulatedReward * hitProb + missOutcome.outcome.AccumulatedReward * missProb):F4}");
        Console.WriteLine();
        Console.WriteLine("  Plus future rewards from simulations...");
        Console.WriteLine("  = Total expected value for Attack action");
        Console.WriteLine();

        // Compare with other actions
        Console.WriteLine("┌─────────────────────────────────────────────────────┐");
        Console.WriteLine("│ STEP 5: Compare with alternative actions           │");
        Console.WriteLine("└─────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine("  Alternative: MoveEast");
        Console.WriteLine("    - Deterministic (no chance node)");
        Console.WriteLine("    - Move away from monster (safer)");
        Console.WriteLine("    - But monster survives (may chase)");
        Console.WriteLine();
        Console.WriteLine("  Alternative: EndTurn");
        Console.WriteLine("    - Waste remaining action");
        Console.WriteLine("    - Monster still alive and adjacent");
        Console.WriteLine("    - Monster will act in Phase 3");
        Console.WriteLine();
        Console.WriteLine("  MCTS explores all options and picks highest expected value!");

        // Run actual MCTS
        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────────┐");
        Console.WriteLine("│ ACTUAL MCTS RUN (100 iterations)                   │");
        Console.WriteLine("└─────────────────────────────────────────────────────┘");
        Console.WriteLine();

        var simulation = new PhaseGameSimulation();
        var mcts = new Mcts.Mcts<PhaseGameState, SquadAction>(
            game,
            new Ucb1Selection<PhaseGameState, SquadAction>(explorationC: 1.414),
            new UniformSingleExpansion<PhaseGameState, SquadAction>(deterministicRollForward: false),
            simulation,
            new SumBackpropagation<PhaseGameState, SquadAction>(),
            new MctsOptions
            {
                Iterations = 100,
                RolloutDepth = 15
            }
        );

        var (chosenAction, stats) = mcts.Search(state);

        foreach (var (action, visits, value) in stats)
        {
            double avg = visits > 0 ? value / visits : 0.0;
            string marker = action == chosenAction ? " ← CHOSEN" : "";
            Console.WriteLine($"  {action,-12}: V={visits,3}, Q={value,7:F1}, Avg={avg,6:F2}{marker}");
        }

        Console.WriteLine();
        Console.WriteLine("Note: Attack has highest average value due to:");
        Console.WriteLine("  1. 58.3% chance of immediate +5 reward");
        Console.WriteLine("  2. Eliminating threat improves future outcomes");
        Console.WriteLine("  3. Even if miss, can attack again with remaining actions");
    }
}
