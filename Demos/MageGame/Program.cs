using System;
using System.Collections.Generic;
using System.Linq;
using Mcts;
using MageGame;

class MageGameProgram
{
    static void Main()
    {
        Console.WriteLine("=== Mage Tactical Game ===");
        Console.WriteLine("Rules:");
        Console.WriteLine("  - Enemies die in one hit");
        Console.WriteLine("  - Heroes: Healthy -> Injured -> Dead (2 hits)");
        Console.WriteLine("  - Phase 1: Monster spawns (Random or Chaser)");
        Console.WriteLine("  - Phase 2: Heroes act");
        Console.WriteLine("  - Phase 3: Monsters act");
        Console.WriteLine("  - Random monsters move randomly");
        Console.WriteLine("  - Chaser monsters pursue nearest hero");
        Console.WriteLine("  - Mage: Can't attack, but can Zap (range 3) or Teleport heroes (range 4)");
        Console.WriteLine();

        var game = new MageTacticalGame(gridWidth: 5, gridHeight: 5, maxTurns: 20, seed: 42);
        
        var simulation = new MageGameSimulation();
        var mcts = new Mcts.Mcts<MageGameState, MageAction>(
            game,
            new Ucb1Selection<MageGameState, MageAction>(explorationC: 10.0),
            new UniformSingleExpansion<MageGameState, MageAction>(deterministicRollForward: false),
            simulation,
            new SumBackpropagation<MageGameState, MageAction>(),
            new MctsOptions
            {
                Iterations = 1000,
                RolloutDepth = 20
            }
        );

        var state = game.InitialState();
        int turn = 0;

        while (!game.IsTerminal(state, out _))
        {
            Console.WriteLine($"\n--- Turn {state.TurnCount} | Phase: {state.CurrentPhase} | ActiveHero: {state.ActiveHeroIndex} ---");
            PrintState(state, game);

            if (game.IsChanceNode(state))
            {
                // Resolve chance node
                var outcomes = game.ChanceOutcomes(state).ToList();
                if (outcomes.Count == 1)
                {
                    state = outcomes[0].outcome;
                    Console.WriteLine($"[Chance node resolved: Phase={state.CurrentPhase}]");
                }
                else
                {
                    // Pick highest probability outcome for demo
                    state = outcomes.OrderByDescending(o => o.probability).First().outcome;
                    Console.WriteLine($"[Chance node resolved: Phase={state.CurrentPhase}]");
                }
            }
            else
            {
                // MCTS decision
                var (action, stats) = mcts.Search(state);

                // Print statistics
                Console.WriteLine("\nMCTS Statistics:");
                foreach (var (a, visits, value) in stats.Take(3))
                {
                    double avg = visits > 0 ? value / visits : 0.0;
                    string actionDesc = GetActionDescription(a, state);
                    Console.WriteLine($"  {actionDesc,-12}: visits={visits,4}, avg={avg,6:F2}");
                }

                Console.WriteLine($"\nChosen action: {GetActionDescription(action, state)}");

                state = game.Step(state, action);
            }

            turn++;
            if (turn > 100)
            {
                Console.WriteLine("\nStopping after 100 iterations for safety.");
                break;
            }
        }

        Console.WriteLine("\n\n=== GAME OVER ===");
        game.IsTerminal(state, out var terminalValue);
        Console.WriteLine($"Terminal Value: {terminalValue}");
        Console.WriteLine($"Accumulated Reward: {state.AccumulatedReward:F2}");
        Console.WriteLine($"Turns: {state.TurnCount}");
        Console.WriteLine($"Heroes Alive: {state.Heroes.Count(h => h.Status != HeroStatus.Dead)}/{state.Heroes.Count}");
        Console.WriteLine($"Monsters Alive: {state.Monsters.Count(m => m.IsAlive)}/{state.Monsters.Count}");
    }

    static void PrintState(MageGameState state, MageTacticalGame game)
    {
        // Print 5x5 grid
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                if (state.Walls.Contains((x, y)))
                {
                    Console.Write("#");
                }
                else if (x == state.ExitX && y == state.ExitY)
                {
                    Console.Write("E");
                }
                else if (state.Heroes.Any(h => h.Status != HeroStatus.Dead && h.X == x && h.Y == y))
                {
                    var hero = state.Heroes.First(h => h.Status != HeroStatus.Dead && h.X == x && h.Y == y);
                    char c = hero.Class switch
                    {
                        HeroClass.Warrior => 'W',
                        HeroClass.Rogue => 'R',
                        HeroClass.Elf => 'L',
                        HeroClass.Mage => 'M',
                        _ => 'H'
                    };
                    if (hero.Status == HeroStatus.Injured)
                        c = char.ToLower(c);
                    Console.Write(c);
                }
                else if (state.Monsters.Any(m => m.IsAlive && m.X == x && m.Y == y))
                {
                    var monster = state.Monsters.First(m => m.IsAlive && m.X == x && m.Y == y);
                    Console.Write(monster.Type == MonsterType.Random ? "r" : "c");
                }
                else
                {
                    Console.Write(".");
                }
            }
            Console.WriteLine();
        }

        Console.WriteLine("\nHeroes:");
        foreach (var hero in state.Heroes)
        {
            Console.WriteLine($"  {hero.Index}: {hero.Class} at ({hero.X},{hero.Y}) {hero.Status} Actions:{hero.ActionsRemaining}" +
                (hero.Class == HeroClass.Mage ? $" Zap:{hero.ZapRange} Teleport:{hero.TeleportRange}" : ""));
        }

        Console.WriteLine($"Monsters: {state.Monsters.Count(m => m.IsAlive)} alive / {state.Monsters.Count} total");
        Console.WriteLine($"Accumulated Reward: {state.AccumulatedReward:F2}");
    }

    static string GetActionDescription(MageAction action, MageGameState state)
    {
        return action.Type switch
        {
            ActionType.MoveNorth => "MoveNorth",
            ActionType.MoveSouth => "MoveSouth",
            ActionType.MoveEast => "MoveEast",
            ActionType.MoveWest => "MoveWest",
            ActionType.Attack => "Attack",
            ActionType.ZapMonster => $"Zap(M{action.TargetIndex})",
            ActionType.TeleportHero => $"Teleport(H{action.TargetIndex} to {action.TargetX},{action.TargetY})",
            ActionType.EndTurn => "EndTurn",
            _ => action.Type.ToString()
        };
    }
}

class MageGameSimulation : ISimulationPolicy<MageGameState, MageAction>
{
    public double Simulate(in MageGameState state, IGameModel<MageGameState, MageAction> game, Random rng, int maxDepth)
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
            if (actions is IList<MageAction> listA)
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
