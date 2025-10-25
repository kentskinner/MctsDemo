using System;
using System.Collections.Generic;
using System.Linq;
using Mcts;
using MageGame;

class HexMageGameProgram
{
    static void Main(string[] args)
    {
        // Check if running tests
        if (args.Length > 0 && args[0] == "--test")
        {
            HexMageGame.Tests.ActionEconomyTests.RunAllTests();
            return;
        }

        Console.WriteLine("=== HEX MAGE GAME ===");
        Console.WriteLine("Tactical combat on a hex grid with terrain!");
        Console.WriteLine("Terrain: ~ = Water (impassable)");
        Console.WriteLine("         ^ = Hill (high ground gives attack bonus)");
        Console.WriteLine("         * = Tree (cover, harder to hit)");
        Console.WriteLine("         T = Tree on Hill (high ground + cover)");
        Console.WriteLine("         # = Building (strong cover)");
        Console.WriteLine("         B = Building on Hill (fortified position)");
        Console.WriteLine("         . = Ground");
        Console.WriteLine("All hexes cost 1 AP to move into (except water which is impassable)");
        Console.WriteLine();

        var game = new HexTacticalGame(maxTurns: 15);
        var initialState = game.InitialState();
        Console.WriteLine(HexTacticalGame.DisplayState(initialState));

        var simulation = new HexGameSimulation();
        var mcts = new Mcts.Mcts<HexGameState, HexAction>(
            game,
            new Ucb1Selection<HexGameState, HexAction>(explorationC: 1.41),
            new UniformSingleExpansion<HexGameState, HexAction>(deterministicRollForward: false),
            simulation,
            new SumBackpropagation<HexGameState, HexAction>(),
            new MctsOptions
            {
                Iterations = 1000,
                RolloutDepth = 20
            }
        );

        var currentState = initialState;
        int iterations = 0;
        const int maxIterations = 100;

        while (!game.IsTerminal(currentState, out _) && iterations < maxIterations)
        {
            iterations++;

            if (game.IsChanceNode(currentState))
            {
                // Resolve chance node by sampling
                var outcomes = game.ChanceOutcomes(currentState).ToList();
                if (outcomes.Count == 1)
                {
                    currentState = outcomes[0].outcome;
                    Console.WriteLine($"[Chance node resolved: Phase={currentState.CurrentPhase}]");
                }
                else
                {
                    var rng = new Random();
                    currentState = game.SampleChanceOutcome(currentState, rng);
                    Console.WriteLine($"[Chance node resolved: Phase={currentState.CurrentPhase}]");
                }
            }
            else
            {
                // MCTS decision
                var (chosenAction, statistics) = mcts.Search(currentState);

                // Display statistics
                Console.WriteLine("MCTS Statistics:");
                foreach (var (action, visits, value) in statistics.Take(5))
                {
                    double avg = visits > 0 ? value / visits : 0.0;
                    string actionDesc = DescribeHexAction(action, currentState);
                    Console.WriteLine($"  {actionDesc,-30}: visits={visits,4}, avg={avg,6:F2}");
                }

                Console.WriteLine();
                Console.WriteLine($"Chosen action: {DescribeHexAction(chosenAction, currentState)}");
                Console.WriteLine();

                // Apply action
                currentState = game.Step(currentState, chosenAction);
                Console.WriteLine(HexTacticalGame.DisplayState(currentState));
            }
        }

        Console.WriteLine("Stopping after {0} iterations for safety.", iterations);
        Console.WriteLine();
        Console.WriteLine("=== GAME OVER ===");
        game.IsTerminal(currentState, out double finalValue);
        Console.WriteLine($"Terminal Value: {finalValue:F2}");
        Console.WriteLine($"Accumulated Reward: {currentState.AccumulatedReward:F2}");
        Console.WriteLine($"Turns: {currentState.TurnCount}");
        Console.WriteLine($"Heroes Alive: {currentState.Heroes.Count(h => h.IsAlive && !h.HasExited)}/{currentState.Heroes.Count}");
        Console.WriteLine($"Heroes Exited: {currentState.Heroes.Count(h => h.HasExited)}/{currentState.Heroes.Count}");
        
        foreach (var hero in currentState.Heroes.Where(h => h.HasExited))
        {
            Console.WriteLine($"  {hero.Class} exited {hero.Status}");
        }
        
        Console.WriteLine($"Monsters Alive: {currentState.Monsters.Count(m => m.IsAlive)}/{currentState.Monsters.Count}");
    }

    static string DescribeHexAction(HexAction action, HexGameState state)
    {
        return action.Type switch
        {
            HexActionType.ActivateHero => $"Activate({state.Heroes[action.TargetIndex].Class})",
            HexActionType.MoveNE => "MoveNE",
            HexActionType.MoveE => "MoveE",
            HexActionType.MoveSE => "MoveSE",
            HexActionType.MoveSW => "MoveSW",
            HexActionType.MoveW => "MoveW",
            HexActionType.MoveNW => "MoveNW",
            HexActionType.Attack => $"Attack(M{action.TargetIndex})",
            HexActionType.SneakAttack => $"SneakAttack(M{action.TargetIndex})",
            HexActionType.Cast => "Cast",
            HexActionType.ZapMonster => $"Zap(M{action.TargetIndex})",
            HexActionType.FireballMonster => $"Fireball(M{action.TargetIndex})",
            HexActionType.TeleportHero => $"Teleport(H{action.TargetIndex} to {action.TargetPosition})",
            HexActionType.NimbleHero => $"Nimble(H{action.TargetIndex})",
            HexActionType.EndTurn => "EndTurn",
            _ => action.Type.ToString()
        };
    }
}

class HexGameSimulation : ISimulationPolicy<HexGameState, HexAction>
{
    public double Simulate(in HexGameState state, IGameModel<HexGameState, HexAction> game, Random rng, int maxDepth)
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
            if (actions is IList<HexAction> listA)
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
