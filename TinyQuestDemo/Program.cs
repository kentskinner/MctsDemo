using System;
using GenericMcts;
using TinyQuestDemo;

Console.WriteLine("=== TinyQuest MCTS Demo ===");
Console.WriteLine("A cooperative hex-based adventure\n");

var game = new TinyQuestGame();
var selection = new Ucb1Selection<QuestState, QuestAction>(explorationC: 1.414);
var expansion = new UniformSingleExpansion<QuestState, QuestAction>(deterministicRollForward: true);
var simulation = new UniformRandomSimulation<QuestState, QuestAction>();
var backprop = new SumBackpropagation<QuestState, QuestAction>();

var options = new MctsOptions
{
    Iterations = 10_000,
    RolloutDepth = 100,
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Seed = 42
};

var mcts = new Mcts<QuestState, QuestAction>(game, selection, expansion, simulation, backprop, options);

// Generate random map layout
var random = new Random(42); // Use seed for reproducibility
var (startHex, exitHex, chest0Hex, chest1Hex, chest2Hex) = GenerateRandomMap(random);

// Initial state: 
// - All heroes start on randomly chosen start hex
// - Exit and 3 chests placed randomly (chests never on exit hex)
var initialState = new QuestState(
    Warrior: new Hero(HeroType.Warrior, CurrentHex: startHex, HasExited: false, IsDead: false, IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
    Elf: new Hero(HeroType.Elf, CurrentHex: startHex, HasExited: false, IsDead: false, IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
    Thief: new Hero(HeroType.Thief, CurrentHex: startHex, HasExited: false, IsDead: false, IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
    Mage: new Hero(HeroType.Mage, CurrentHex: startHex, HasExited: false, IsDead: false, IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
    ActiveHeroIndex: -1, // Hero selection mode
    ActionsRemaining: 0,
    ExitHex: exitHex,
    Chest0Hex: chest0Hex,
    Chest1Hex: chest1Hex,
    Chest2Hex: chest2Hex,
    TurnCount: 0,
    WarriorActivatedThisTurn: false,
    ElfActivatedThisTurn: false,
    ThiefActivatedThisTurn: false,
    MageActivatedThisTurn: false,
    // Chests (contents revealed when opened by a hero)
    Chest0Present: true,
    Chest1Present: true,
    Chest2Present: true,
    PendingChestItem: false
);

Console.WriteLine("=== Random Map Setup ===");
Console.WriteLine($"Hex {startHex}: START (all heroes begin here)");
Console.WriteLine($"Hex {exitHex}: EXIT (escape here for reward)");
Console.WriteLine($"Hex {chest0Hex}: Chest 0");
Console.WriteLine($"Hex {chest1Hex}: Chest 1");
Console.WriteLine($"Hex {chest2Hex}: Chest 2");
Console.WriteLine("\nChests give hero-specific items when opened (or nothing if hero has all 3 items)\n");

Console.WriteLine("=== Initial State ===");
PrintState(initialState);

Console.WriteLine("\n=== Running MCTS to find best action ===\n");

var (bestAction, stats) = mcts.Search(initialState, out var rootNode);

Console.WriteLine($"Best action: {bestAction}\n");
Console.WriteLine("Action statistics:");
foreach (var (action, visits, value) in stats)
{
    var mean = visits > 0 ? value / visits : 0.0;
    Console.WriteLine($"  {action,-20}  visits={visits,6}  total={value,8:F2}  mean={mean,7:F3}");
}

// Display tree statistics
var treeStats = MctsTreeVisualizer.GetStats(rootNode);
Console.WriteLine($"\nTree Statistics: {treeStats}");

// Export tree visualization
// Note: We pass a state+action formatter to show which hero performs each action
var treeText = MctsTreeVisualizer.ToText<QuestState, QuestAction>(
    rootNode,
    s => $"W:{s.Warrior.CurrentHex}{(s.Warrior.HasExited ? "X" : (s.Warrior.IsDead ? "D" : ""))} E:{s.Elf.CurrentHex}{(s.Elf.HasExited ? "X" : (s.Elf.IsDead ? "D" : ""))} T:{s.Thief.CurrentHex}{(s.Thief.HasExited ? "X" : (s.Thief.IsDead ? "D" : ""))} M:{s.Mage.CurrentHex}{(s.Mage.HasExited ? "X" : (s.Mage.IsDead ? "D" : ""))} Ch:{(s.Chest0Present ? "0" : "")}{(s.Chest1Present ? "1" : "")}{(s.Chest2Present ? "2" : "")} T:{s.TurnCount}",
    (state, action) => FormatActionWithHero(state, action),
    maxDepth: 4,
    minVisits: 50
);

Console.WriteLine("\n=== MCTS Tree (top branches, min 50 visits) ===");
Console.WriteLine(treeText);

// Save DOT file for Graphviz
var dotContent = MctsTreeVisualizer.ToDot<QuestState, QuestAction>(
    rootNode,
    s => $"W:{s.Warrior.CurrentHex}{(s.Warrior.HasExited ? "X" : (s.Warrior.IsDead ? "D" : ""))} E:{s.Elf.CurrentHex}{(s.Elf.HasExited ? "X" : (s.Elf.IsDead ? "D" : ""))} T:{s.Thief.CurrentHex}{(s.Thief.HasExited ? "X" : (s.Thief.IsDead ? "D" : ""))} M:{s.Mage.CurrentHex}{(s.Mage.HasExited ? "X" : (s.Mage.IsDead ? "D" : ""))}\\nChests:{(s.Chest0Present ? "0" : "")}{(s.Chest1Present ? "1" : "")}{(s.Chest2Present ? "2" : "")} Turn:{s.TurnCount}",
    a => a.ToString(),
    maxDepth: 5,
    minVisits: 50
);

File.WriteAllText("mcts_tree.dot", dotContent);
Console.WriteLine("\nTree exported to mcts_tree.dot");

// Try to generate PNG using Graphviz
try
{
    var dotProcess = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dot",
            Arguments = "-Tpng mcts_tree.dot -o mcts_tree.png",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };
    
    dotProcess.Start();
    dotProcess.WaitForExit();
    
    if (dotProcess.ExitCode == 0 && File.Exists("mcts_tree.png"))
    {
        Console.WriteLine("PNG generated: mcts_tree.png");
        
        // Open the PNG
        var openProcess = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mcts_tree.png",
                UseShellExecute = true
            }
        };
        openProcess.Start();
        Console.WriteLine("Opening mcts_tree.png...");
    }
    else
    {
        var error = dotProcess.StandardError.ReadToEnd();
        Console.WriteLine($"Failed to generate PNG: {error}");
        Console.WriteLine("(Install Graphviz and add it to PATH to enable PNG generation)");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Could not generate PNG: {ex.Message}");
    Console.WriteLine("(Install Graphviz from https://graphviz.org/download/ and add to PATH)");
}

// Simulate a complete game
Console.WriteLine("\n=== Simulating Complete Game ===\n");
var currentState = initialState;
int step = 1;

while (!game.IsTerminal(currentState, out var termValue))
{
    string statusMsg;
    if (currentState.ActiveHeroIndex == -1)
    {
        statusMsg = "Hero Selection";
    }
    else
    {
        var activeHeroName = currentState.ActiveHeroIndex switch
        {
            0 => "Warrior",
            1 => "Elf",
            2 => "Thief",
            3 => "Mage",
            _ => "Unknown"
        };
        statusMsg = $"Active: {activeHeroName} (Actions: {currentState.ActionsRemaining})";
    }
    
    Console.WriteLine($"--- Turn {currentState.TurnCount + 1}, Step {step++} ---");
    Console.WriteLine($"{statusMsg}");
    
    var (action, _) = mcts.Search(currentState);
    Console.WriteLine($"Action: {action}");
    
    currentState = game.Step(currentState, action);
    PrintState(currentState);
    Console.WriteLine();
}

Console.WriteLine("=== Game Over ===");
game.IsTerminal(currentState, out var finalValue);
Console.WriteLine($"Final Score: {finalValue:F1}");
Console.WriteLine($"Warrior: Exited={currentState.Warrior.HasExited}, Dead={currentState.Warrior.IsDead}, Items={CountItems(currentState.Warrior)}");
Console.WriteLine($"Elf: Exited={currentState.Elf.HasExited}, Dead={currentState.Elf.IsDead}, Items={CountItems(currentState.Elf)}");
Console.WriteLine($"Thief: Exited={currentState.Thief.HasExited}, Dead={currentState.Thief.IsDead}, Items={CountItems(currentState.Thief)}");
Console.WriteLine($"Mage: Exited={currentState.Mage.HasExited}, Dead={currentState.Mage.IsDead}, Items={CountItems(currentState.Mage)}");

static int CountItems(Hero hero) => (hero.HasItem1 ? 1 : 0) + (hero.HasItem2 ? 1 : 0) + (hero.HasItem3 ? 1 : 0);

static string FormatActionWithHero(QuestState state, QuestAction? action)
{
    if (action == null) return "null";
    
    var a = action.Value;
    
    // Get turn number for prefix
    var turnPrefix = $"T{state.TurnCount + 1}:";
    
    // Activation actions already specify the hero
    if (a == QuestAction.ActivateWarrior) return turnPrefix + "W:ActivateWarrior";
    if (a == QuestAction.ActivateElf) return turnPrefix + "E:ActivateElf";
    if (a == QuestAction.ActivateThief) return turnPrefix + "T:ActivateThief";
    if (a == QuestAction.ActivateMage) return turnPrefix + "M:ActivateMage";

    // For other actions, determine from the state which hero is active
    if (state.ActiveHeroIndex == -1)
    {
        return turnPrefix + a.ToString(); // No active hero yet
    }

    var heroPrefix = state.ActiveHeroIndex switch
    {
        0 => "W:",
        1 => "E:",
        2 => "T:",
        3 => "M:",
        _ => "?"
    };
    return turnPrefix + heroPrefix + a.ToString();
}

static void PrintState(QuestState s)
{
    Console.WriteLine($"  Warrior: Hex {s.Warrior.CurrentHex}{(s.Warrior.HasExited ? " (EXITED)" : (s.Warrior.IsDead ? " (DEAD)" : ""))}{(s.Warrior.IsInjured ? " [injured]" : "")}{(s.Warrior.HasMoved ? " [moved]" : "")} Items:{CountItems(s.Warrior)}");
    Console.WriteLine($"  Elf:     Hex {s.Elf.CurrentHex}{(s.Elf.HasExited ? " (EXITED)" : (s.Elf.IsDead ? " (DEAD)" : ""))}{(s.Elf.IsInjured ? " [injured]" : "")}{(s.Elf.HasMoved ? " [moved]" : "")} Items:{CountItems(s.Elf)}");
    Console.WriteLine($"  Thief:   Hex {s.Thief.CurrentHex}{(s.Thief.HasExited ? " (EXITED)" : (s.Thief.IsDead ? " (DEAD)" : ""))}{(s.Thief.IsInjured ? " [injured]" : "")}{(s.Thief.HasMoved ? " [moved]" : "")} Items:{CountItems(s.Thief)}");
    Console.WriteLine($"  Mage:    Hex {s.Mage.CurrentHex}{(s.Mage.HasExited ? " (EXITED)" : (s.Mage.IsDead ? " (DEAD)" : ""))}{(s.Mage.IsInjured ? " [injured]" : "")}{(s.Mage.HasMoved ? " [moved]" : "")} Items:{CountItems(s.Mage)}");
    Console.WriteLine($"  Chests:  {(s.Chest0Present ? $"Hex{s.Chest0Hex} " : "")}{(s.Chest1Present ? $"Hex{s.Chest1Hex} " : "")}{(s.Chest2Present ? $"Hex{s.Chest2Hex}" : "")}");
    Console.WriteLine($"  Exit:    Hex {s.ExitHex}");
}

static (int startHex, int exitHex, int chest0Hex, int chest1Hex, int chest2Hex) GenerateRandomMap(Random random)
{
    // Pick 5 unique random hexes: 1 start, 1 exit, 3 chests
    // Constraint: Chests can't be on the exit hex
    var hexes = new HashSet<int>();

    // Pick start hex
    int startHex = random.Next(0, 30);
    hexes.Add(startHex);

    // Pick exit hex
    int exitHex;
    do
    {
        exitHex = random.Next(0, 30);
    } while (hexes.Contains(exitHex));
    hexes.Add(exitHex);

    // Pick 3 chest hexes (can't be on exit, but can be on start)
    var chestHexes = new List<int>();
    while (chestHexes.Count < 3)
    {
        int chestHex = random.Next(0, 30);
        if (chestHex != exitHex && !chestHexes.Contains(chestHex))
        {
            chestHexes.Add(chestHex);
        }
    }

    return (startHex, exitHex, chestHexes[0], chestHexes[1], chestHexes[2]);
}
