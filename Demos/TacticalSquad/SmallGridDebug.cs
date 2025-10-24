using System;
using System.Collections.Generic;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Interactive debugging tool for SmallGridGame
/// Press ENTER to run one MCTS search iteration
/// See detailed output of what MCTS is doing
/// </summary>
public static class SmallGridDebug
{
    public static void Main()
    {
        var game = new SmallGridGame();
        var state = game.InitialState();
        var simulation = new SmallGridSimulation();

        Console.WriteLine("=== Small Grid MCTS Debugger ===");
        Console.WriteLine("4x4 grid, Hero at (0,3), Exit at (3,0)");
        Console.WriteLine("Optimal path: East, East, East, North, North, North = 6 moves");
        Console.WriteLine();
        
        game.PrintState(state);

        Console.WriteLine("Press ENTER to run MCTS searches (100 iterations each)");
        Console.WriteLine("Press 'q' to quit");
        Console.WriteLine();

        var mcts = new Mcts.Mcts<SmallState, SmallAction>(
            game,
            new Ucb1Selection<SmallState, SmallAction>(explorationC: 50.0),
            new UniformSingleExpansion<SmallState, SmallAction>(deterministicRollForward: false),
            simulation,
            new SumBackpropagation<SmallState, SmallAction>(),
            new MctsOptions
            {
                Iterations = 1000,
                RolloutDepth = 20
            }
        );

        int searchCount = 0;

        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.KeyChar == 'q' || key.Key == ConsoleKey.Escape)
                break;

            if (key.Key == ConsoleKey.Enter)
            {
                searchCount++;
                Console.WriteLine($"\n>>> Running Search #{searchCount} (100 iterations)...\n");

                var (bestAction, stats) = mcts.Search(state);

                // Show results
                var sorted = stats.OrderByDescending(s => s.visits).ToList();
                
                Console.WriteLine($"{"Action",-15} {"Visits",8} {"Value",12} {"Average",10}");
                Console.WriteLine(new string('-', 50));
                
                foreach (var stat in sorted)
                {
                    double avg = stat.visits > 0 ? stat.value / stat.visits : 0.0;
                    Console.WriteLine($"{stat.action,-15} {stat.visits,8} {stat.value,12:F2} {avg,10:F4}");
                }

                // Show best move
                Console.WriteLine();
                var best = sorted.First();
                Console.WriteLine($"Best move: {best.action} (visits={best.visits}, avg={best.value / best.visits:F4})");
                
                // Ask if user wants to take the move
                Console.WriteLine();
                Console.Write("Take this move? (y/n): ");
                var response = Console.ReadKey();
                Console.WriteLine();
                
                if (response.KeyChar == 'y' || response.KeyChar == 'Y')
                {
                    state = game.Step(state, best.action);
                    game.PrintState(state);
                    
                    if (game.IsTerminal(state, out var termValue))
                    {
                        Console.WriteLine($"GAME OVER! Terminal value: {termValue:F2}");
                        Console.WriteLine();
                        Console.Write("Start new game? (y/n): ");
                        var restart = Console.ReadKey();
                        Console.WriteLine();
                        
                        if (restart.KeyChar == 'y' || restart.KeyChar == 'Y')
                        {
                            state = game.InitialState();
                            game.PrintState(state);
                            searchCount = 0;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                
                Console.WriteLine("\nPress ENTER for another search, 'q' to quit");
            }
        }

        Console.WriteLine("\nGoodbye!");
    }
}
