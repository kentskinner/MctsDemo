using System;
using System.Linq;
using Mcts;

namespace TacticalSquad;

public static class PhaseBasedProgram
{
    public static void Main()
    {
        Console.WriteLine("=== Phase-Based Tactical Game ===");
        Console.WriteLine("Rules:");
        Console.WriteLine("  - Enemies die in one hit");
        Console.WriteLine("  - Heroes: Healthy -> Injured -> Dead (2 hits)");
        Console.WriteLine("  - Phase 1: Monster spawns (Random or Chaser)");
        Console.WriteLine("  - Phase 2: Heroes act");
        Console.WriteLine("  - Phase 3: Monsters act");
        Console.WriteLine("  - Random monsters move randomly");
        Console.WriteLine("  - Chaser monsters pursue nearest hero");
        Console.WriteLine();

        var game = new PhaseBasedGame(gridWidth: 5, gridHeight: 5, numHeroes: 2, maxTurns: 20, seed: 42);
        var state = game.InitialState();

        var simulation = new PhaseGameSimulation();
        var mcts = new Mcts.Mcts<PhaseGameState, SquadAction>(
            game,
            new Ucb1Selection<PhaseGameState, SquadAction>(explorationC: 10.0),
            new UniformSingleExpansion<PhaseGameState, SquadAction>(deterministicRollForward: false),
            simulation,
            new SumBackpropagation<PhaseGameState, SquadAction>(),
            new MctsOptions
            {
                Iterations = 1000,
                RolloutDepth = 20
            }
        );

        int moveCount = 0;
        while (!game.IsTerminal(state, out var terminalValue))
        {
            PrintState(game, state);

            if (game.IsChanceNode(state))
            {
                // Sample chance outcome
                var rng = new Random();
                state = game.SampleChanceOutcome(state, rng);
                Console.WriteLine($"[Chance node resolved: Phase={state.CurrentPhase}]");
                Console.WriteLine();
                continue;
            }

            // Decision node - run MCTS
            var (action, stats) = mcts.Search(state);
            
            Console.WriteLine("\nMCTS Statistics:");
            foreach (var (a, visits, value) in stats.Take(3))
            {
                double avg = visits > 0 ? value / visits : 0.0;
                Console.WriteLine($"  {a,-12}: visits={visits,4}, avg={avg,7:F2}");
            }
            Console.WriteLine($"\nChosen action: {action}");
            Console.WriteLine();

            state = game.Step(state, action);
            moveCount++;

            if (moveCount > 200)
            {
                Console.WriteLine("Move limit reached!");
                break;
            }
        }

        game.IsTerminal(state, out var finalValue);
        Console.WriteLine("\n=== GAME OVER ===");
        Console.WriteLine($"Terminal Value: {finalValue}");
        Console.WriteLine($"Accumulated Reward: {state.AccumulatedReward:F2}");
        Console.WriteLine($"Turns: {state.TurnCount}");
        Console.WriteLine($"Heroes Alive: {state.Heroes.Count(h => h.Status != HeroStatus.Dead)}/{state.Heroes.Count}");
        Console.WriteLine($"Monsters Alive: {state.Monsters.Count(m => m.IsAlive)}/{state.Monsters.Count}");
    }

    private static void PrintState(PhaseBasedGame game, PhaseGameState state)
    {
        Console.WriteLine($"--- Turn {state.TurnCount} | Phase: {state.CurrentPhase} | ActiveHero: {state.ActiveHeroIndex} ---");

        // Print grid (5x5)
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                if (x == state.ExitX && y == state.ExitY)
                    Console.Write("E");
                else if (state.Walls.Contains((x, y)))
                    Console.Write("#");
                else if (state.Heroes.Any(h => h.Status != HeroStatus.Dead && h.X == x && h.Y == y))
                {
                    var hero = state.Heroes.First(h => h.X == x && h.Y == y);
                    Console.Write(hero.Status == HeroStatus.Healthy ? "H" : "h");
                }
                else if (state.Monsters.Any(m => m.IsAlive && m.X == x && m.Y == y))
                {
                    var monster = state.Monsters.First(m => m.IsAlive && m.X == x && m.Y == y);
                    Console.Write(monster.Type == MonsterType.Random ? "R" : "C");
                }
                else
                    Console.Write(".");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"\nHeroes:");
        foreach (var hero in state.Heroes)
        {
            Console.WriteLine($"  {hero.Index}: {hero.Class} at ({hero.X},{hero.Y}) {hero.Status} Actions:{hero.ActionsRemaining}");
        }
        Console.WriteLine($"Monsters: {state.Monsters.Count(m => m.IsAlive)} alive / {state.Monsters.Count} total");
        Console.WriteLine($"Accumulated Reward: {state.AccumulatedReward:F2}");
    }
}
