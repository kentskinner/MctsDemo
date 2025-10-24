using Mcts;
using TacticalSquad;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static TacticalSquad.ChanceType;
using Spectre.Console;

namespace TacticalSquad;

public class InteractiveDemo
{
    private static TacticalSquadGame? _game;
    private static Mcts<GameState, SquadAction>? _mcts;
    private static GameState _currentState = default!;
    private static volatile bool _continueThinking = true;
    private static int _totalIterations = 0;
    private static DateTime _thinkingStartTime;
    private static Dictionary<SquadAction, (int visits, double totalValue)> _accumulatedStats = new();
    private static int _maxDepth = 0;
    private static int _totalNodes = 0;

    public static void Main(string[] args)
    {
        AnsiConsole.Clear();
        RunInteractiveGame();
    }

    private static void RunInteractiveGame()
    {
        AnsiConsole.Write(new FigletText("MCTS Tactical Squad").Color(Color.Cyan1));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Watch the AI think in real-time![/]");
        AnsiConsole.MarkupLine("[green]Press SPACE to make the AI take its best move[/]");
        AnsiConsole.MarkupLine("[red]Press ESC to quit[/]");
        AnsiConsole.WriteLine();
        
        AnsiConsole.Status()
            .Start("Setting up game...", ctx =>
            {
                Thread.Sleep(500);
                
                _game = new TacticalSquadGame(
                    gridWidth: 7,
                    gridHeight: 7,
                    numHeroes: 2,
                    maxTurns: 30,
                    seed: null
                );

                var selection = new Ucb1Selection<GameState, SquadAction>();
                var expansion = new UniformSingleExpansion<GameState, SquadAction>(deterministicRollForward: false);
                var simulation = new RewardShapedSimulation(); // Use custom simulation that respects accumulated rewards
                var backprop = new SumBackpropagation<GameState, SquadAction>();

                var options = new MctsOptions
                {
                    Iterations = 10000,
                    RolloutDepth = 20,
                    FinalActionSelector = NodeStats.SelectByMaxVisit,
                    Verbose = false
                };

                _mcts = new Mcts<GameState, SquadAction>(_game, selection, expansion, simulation, backprop, options);
                _currentState = _game.InitialState();
            });

        if (_game == null || _mcts == null)
        {
            AnsiConsole.MarkupLine("[red]Error: Failed to initialize game![/]");
            return;
        }

        var actionHistory = new List<(SquadAction action, GameState resultState)>();

        while (!_game.IsTerminal(in _currentState, out var terminalValue))
        {
            // Handle chance nodes automatically
            if (_game.IsChanceNode(in _currentState))
            {
                _currentState = _game.SampleChanceOutcome(_currentState, Random.Shared);
                continue;
            }

            // Interactive MCTS thinking
            _continueThinking = true;
            _totalIterations = 0;
            _thinkingStartTime = DateTime.Now;
            _accumulatedStats.Clear();
            _maxDepth = 0;
            _totalNodes = 0;

            // Start background thinking thread
            var thinkingThread = new Thread(() => ThinkingLoop(_currentState));
            thinkingThread.Start();

            // Use Spectre.Console Live Display
            AnsiConsole.Live(CreateDisplay(_currentState))
                .Start(ctx =>
                {
                    while (_continueThinking)
                    {
                        ctx.UpdateTarget(CreateDisplay(_currentState));

                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.Spacebar)
                            {
                                _continueThinking = false;
                            }
                            else if (key.Key == ConsoleKey.Escape)
                            {
                                _continueThinking = false;
                                thinkingThread.Join();
                                AnsiConsole.Clear();
                                AnsiConsole.MarkupLine("[red]Game terminated by user.[/]");
                                Environment.Exit(0);
                            }
                        }

                        Thread.Sleep(100); // Update display ~10 times per second
                    }
                });

            thinkingThread.Join();

            // Take the best action based on accumulated visits
            var bestAction = _accumulatedStats.OrderByDescending(kvp => kvp.Value.visits).First().Key;
            _currentState = _game.Step(in _currentState, in bestAction);
            actionHistory.Add((bestAction, _currentState));

            // Show action taken briefly
            AnsiConsole.MarkupLine($"[bold green]✓ Action taken: {bestAction}[/]");
            Thread.Sleep(300);
        }

        // Game over
        AnsiConsole.Clear();
        _game.IsTerminal(in _currentState, out var finalValue);

        var panel = new Panel(
            Align.Center(
                new Markup($"[bold cyan]Final Score:[/] [yellow]{finalValue}[/]\n" +
                          $"[bold cyan]Turns Used:[/] [yellow]{_currentState.TurnCount}/30[/]\n" +
                          $"[bold cyan]Heroes Alive:[/] [yellow]{_currentState.Heroes.Count(h => h.Health > 0)}/{_currentState.Heroes.Length}[/]\n" +
                          $"[bold cyan]Hero at Exit:[/] [yellow]{_currentState.Heroes.Any(h => h.Health > 0 && h.X == _currentState.ExitX && h.Y == _currentState.ExitY)}[/]"),
                VerticalAlignment.Middle))
        {
            Header = new PanelHeader("[bold yellow]GAME OVER[/]", Justify.Center),
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Yellow)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to exit...[/]");
        Console.ReadKey(true);
    }

    private static void ThinkingLoop(GameState state)
    {
        while (_continueThinking)
        {
            var (action, stats) = _mcts!.Search(state);
            _totalIterations += 10000;
            
            // Debug: Write first search stats
            if (_totalIterations == 10000)
            {
                System.IO.File.WriteAllText("mcts_debug.txt", 
                    "=== Stats from first Search() (non-deterministic rollout) ===\n" +
                    string.Join("\n", stats.OrderByDescending(s => s.visits).Select(s => 
                        $"{s.action}: visits={s.visits}, value={s.value:F2}, avg={(s.visits > 0 ? s.value/s.visits : 0):F4}")) +
                    "\n=============================================\n");
            }
            
            lock (_accumulatedStats)
            {
                foreach (var (a, visits, value) in stats)
                {
                    if (_accumulatedStats.ContainsKey(a))
                    {
                        var existing = _accumulatedStats[a];
                        _accumulatedStats[a] = (existing.visits + visits, existing.totalValue + value);
                    }
                    else
                    {
                        _accumulatedStats[a] = (visits, value);
                    }
                }
                
                _totalNodes = _accumulatedStats.Values.Sum(s => s.visits) + 1;
                _maxDepth = (int)Math.Log(_totalNodes, 2) + 1;
            }
        }
    }

    private static Layout CreateDisplay(GameState state)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Body"),
                new Layout("Footer").Size(3));

        // Header - Title and controls
        layout["Header"].Update(
            new Panel(
                Align.Center(new Markup("[bold yellow]MCTS Interactive Thinking[/]\n" +
                                       "[grey]SPACE: Execute Best Move  |  ESC: Quit[/]")))
            {
                Border = BoxBorder.Double,
                BorderStyle = new Style(Color.Cyan1)
            });

        // Body - split into game view and stats
        layout["Body"].SplitColumns(
            new Layout("GameView"),
            new Layout("Stats").Size(50));

        // Game grid and hero info
        var gameTable = CreateGameGrid(state);
        var heroPanel = CreateHeroPanel(state);
        
        layout["GameView"].Update(
            new Rows(gameTable, heroPanel));

        // MCTS Statistics
        layout["Stats"].Update(CreateStatsPanel());

        // Footer - thinking status
        var thinkingTime = DateTime.Now - _thinkingStartTime;
        var iterPerSec = thinkingTime.TotalSeconds > 0 ? (int)(_totalIterations / thinkingTime.TotalSeconds) : 0;
        
        layout["Footer"].Update(
            new Panel(
                Align.Center(new Markup(
                    $"[bold green]● THINKING[/]  " +
                    $"[cyan]Iterations:[/] [yellow]{_totalIterations:N0}[/]  " +
                    $"[cyan]Speed:[/] [yellow]{iterPerSec:N0}/sec[/]  " +
                    $"[cyan]Time:[/] [yellow]{thinkingTime.TotalSeconds:F1}s[/]")))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Green)
            });

        return layout;
    }

    private static Table CreateGameGrid(GameState state)
    {
        var table = new Table()
        {
            Border = TableBorder.Square,
            BorderStyle = new Style(Color.Grey)
        };

        // Add columns for grid
        table.AddColumn(new TableColumn("").Width(2));
        for (int x = 0; x < state.GridWidth; x++)
        {
            table.AddColumn(new TableColumn($"[grey]{x}[/]").Width(3).Centered());
        }

        // Add rows
        for (int y = 0; y < state.GridHeight; y++)
        {
            var rowCells = new List<string> { $"[grey]{y}[/]" };
            
            for (int x = 0; x < state.GridWidth; x++)
            {
                var cell = GetCellDisplay(state, x, y);
                rowCells.Add(cell);
            }
            
            table.AddRow(rowCells.ToArray());
        }

        return table;
    }

    private static string GetCellDisplay(GameState state, int x, int y)
    {
        // Check for exit
        if (x == state.ExitX && y == state.ExitY)
            return "[bold green]E[/]";

        // Check for heroes
        var hero = state.Heroes.FirstOrDefault(h => h.X == x && h.Y == y && h.Health > 0);
        if (hero != null)
        {
            var symbol = hero.Class switch
            {
                HeroClass.Warrior => "W",
                HeroClass.Rogue => "R",
                HeroClass.Mage => "M",
                _ => "H"
            };
            return $"[bold cyan]{symbol}[/]";
        }

        // Check for monsters
        var monster = state.Monsters.FirstOrDefault(m => m.X == x && m.Y == y && m.Health > 0);
        if (monster != null)
        {
            var symbol = monster.Behavior switch
            {
                MonsterBehavior.Hunter => "◆",
                MonsterBehavior.Random => "○",
                _ => "M"
            };
            return $"[bold red]{symbol}[/]";
        }

        // Check for walls
        if (state.Walls.Any(w => w.X == x && w.Y == y))
            return "[grey]█[/]";

        return " ";
    }

    private static Panel CreateHeroPanel(GameState state)
    {
        var heroInfo = new Table()
        {
            Border = TableBorder.None,
            ShowHeaders = true
        };

        heroInfo.AddColumn("Hero");
        heroInfo.AddColumn("HP");
        heroInfo.AddColumn("Dmg");
        heroInfo.AddColumn("Rng");
        heroInfo.AddColumn("Acts");

        foreach (var hero in state.Heroes.Where(h => h.Health > 0))
        {
            var className = hero.Class.ToString()[0].ToString();
            var hpBar = new string('█', hero.Health) + new string('░', hero.MaxHealth - hero.Health);
            var hpColor = hero.Health > hero.MaxHealth / 2 ? "green" : hero.Health > hero.MaxHealth / 4 ? "yellow" : "red";
            
            heroInfo.AddRow(
                $"[cyan]{className}{hero.Id}[/]",
                $"[{hpColor}]{hpBar}[/] {hero.Health}/{hero.MaxHealth}",
                $"[yellow]{hero.Damage}[/]",
                $"[yellow]{hero.AttackRange}[/]",
                $"[yellow]{hero.ActionsRemaining}[/]");
        }

        return new Panel(heroInfo)
        {
            Header = new PanelHeader("[bold]Heroes[/]"),
            Border = BoxBorder.Rounded
        };
    }

    private static Panel CreateStatsPanel()
    {
        List<(SquadAction action, int visits, double totalValue)> statsCopy;
        lock (_accumulatedStats)
        {
            statsCopy = _accumulatedStats.Select(kvp => (kvp.Key, kvp.Value.visits, kvp.Value.totalValue)).ToList();
        }

        var statsTable = new Table()
        {
            Border = TableBorder.None,
            ShowHeaders = false
        };

        statsTable.AddColumn(new TableColumn("").Width(40));

        // Overall stats
        statsTable.AddRow($"[cyan]Total Nodes:[/] [yellow]{_totalNodes:N0}[/]");
        statsTable.AddRow($"[cyan]Max Depth:[/] [yellow]{_maxDepth}[/]");
        statsTable.AddEmptyRow();
        statsTable.AddRow($"[bold underline]Top Moves:[/]");
        statsTable.AddEmptyRow();

        var topMoves = statsCopy.OrderByDescending(s => s.visits).Take(3).ToList();
        var totalVisits = statsCopy.Sum(s => s.visits);

        for (int i = 0; i < topMoves.Count; i++)
        {
            var (action, visits, totalValue) = topMoves[i];
            var percentage = totalVisits > 0 ? (visits * 100.0 / totalVisits) : 0;
            var avgValue = visits > 0 ? totalValue / visits : 0;

            var actionStr = FormatAction(action);
            var barLength = (int)(percentage / 100.0 * 20);
            var bar = new string('█', barLength) + new string('░', 20 - barLength);

            statsTable.AddRow($"[bold yellow]{i + 1}.[/] [white]{actionStr}[/]");
            statsTable.AddRow($"   [{GetValueColor(avgValue)}]{bar}[/] {percentage:F1}%");
            statsTable.AddRow($"   [grey]Visits: {visits:N0} | Avg: {avgValue:F2}[/]");
            
            if (i < topMoves.Count - 1)
                statsTable.AddEmptyRow();
        }

        return new Panel(statsTable)
        {
            Header = new PanelHeader("[bold]MCTS Statistics[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };
    }

    private static string FormatAction(SquadAction action)
    {
        return action switch
        {
            SquadAction.MoveNorth => "Move North ↑",
            SquadAction.MoveSouth => "Move South ↓",
            SquadAction.MoveEast => "Move East →",
            SquadAction.MoveWest => "Move West ←",
            SquadAction.Attack => "Attack Monster",
            SquadAction.EndTurn => "End Turn",
            _ => action.ToString()
        };
    }

    private static string GetValueColor(double value)
    {
        return value switch
        {
            > 0.7 => "green",
            > 0.4 => "yellow",
            > 0.2 => "orange1",
            _ => "red"
        };
    }
}
