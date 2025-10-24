using System;
using System.Collections.Immutable;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Test program specifically to see attack resolution with probabilistic combat
/// </summary>
public static class AttackTestProgram
{
    public static void Main()
    {
        Console.WriteLine("=== Attack Test: Probabilistic Combat ===");
        Console.WriteLine("Testing hit/miss enumeration with 2d6 probabilities");
        Console.WriteLine("Warrior (AttackScore=7): 58.3% hit");
        Console.WriteLine("Rogue (AttackScore=8): 41.7% hit");
        Console.WriteLine("Elf (AttackScore=9): 27.8% hit");
        Console.WriteLine();

        var game = new PhaseBasedGame(gridWidth: 7, gridHeight: 7, numHeroes: 2, maxTurns: 30, seed: 42);
        var state = game.InitialState();
        
        // Manually create a state with a monster right next to the Warrior
        // Warrior is at (1,5), so put monster at (2,5) - Manhattan distance 1
        var monster = new PhaseMonster(Index: 0, X: 2, Y: 5, IsAlive: true);
        state = state with
        {
            Monsters = ImmutableList.Create(monster),
            CurrentPhase = Phase.HeroAction,
            ActiveHeroIndex = 0
        };

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

        Console.WriteLine("Initial setup:");
        PrintState(game, state);
        Console.WriteLine("\nMonster is adjacent to Warrior - should trigger attack!");
        Console.WriteLine();

        // Run MCTS
        var (action, stats) = mcts.Search(state);
        
        Console.WriteLine("MCTS Statistics:");
        foreach (var (a, visits, value) in stats.Take(5))
        {
            double avg = visits > 0 ? value / visits : 0.0;
            Console.WriteLine($"  {a,-12}: visits={visits,5}, avg={avg,7:F2}");
        }
        Console.WriteLine($"\nChosen action: {action}");
        Console.WriteLine();

        // Take the action
        state = game.Step(state, action);
        
        Console.WriteLine("After action:");
        PrintState(game, state);
        
        // Check if we're in an attack resolution state
        if (state.AttackResolution != null)
        {
            Console.WriteLine("\n*** ATTACK RESOLUTION CHANCE NODE ***");
            var attack = state.AttackResolution;
            var hero = state.Heroes[attack.HeroIndex];
            var target = state.Monsters[attack.MonsterIndex];
            Console.WriteLine($"Hero {attack.HeroIndex} ({hero.Class}) attacking Monster {attack.MonsterIndex}");
            Console.WriteLine($"AttackScore: {attack.AttackScore}");
            
            // Enumerate outcomes
            var outcomes = game.ChanceOutcomes(state).ToList();
            Console.WriteLine($"\nPossible outcomes ({outcomes.Count}):");
            foreach (var (outcome, prob) in outcomes)
            {
                string result = outcome.Monsters[attack.MonsterIndex].IsAlive ? "MISS" : "HIT";
                double reward = outcome.AccumulatedReward - state.AccumulatedReward;
                Console.WriteLine($"  {result}: probability={prob:F4} (={prob:P1}), reward={reward:F1}");
            }
            
            // Sample one
            Console.WriteLine("\nSampling outcome...");
            var rng = new Random();
            state = game.SampleChanceOutcome(state, rng);
            
            Console.WriteLine("After resolution:");
            PrintState(game, state);
        }
    }

    private static void PrintState(PhaseBasedGame game, PhaseGameState state)
    {
        Console.WriteLine($"Turn {state.TurnCount} | Phase: {state.CurrentPhase} | ActiveHero: {state.ActiveHeroIndex}");
        
        // Print grid
        for (int y = 0; y < 7; y++)
        {
            for (int x = 0; x < 7; x++)
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
                    Console.Write("M");
                else
                    Console.Write(".");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"Monsters: {state.Monsters.Count(m => m.IsAlive)} alive, Accumulated Reward: {state.AccumulatedReward:F2}");
    }
}
