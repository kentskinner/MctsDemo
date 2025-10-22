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

// Initial state: 
// - Start hex = 0, Exit hex = 1, Chest hex = 2
// - Both heroes start on hex 0
// - ActiveHeroIndex = -1 means we need to select which hero activates first
var initialState = new QuestState(
    Warrior: new Hero(HeroType.Warrior, CurrentHex: 0, HasExited: false, HasMoved: false),
    Elf: new Hero(HeroType.Elf, CurrentHex: 0, HasExited: false, HasMoved: false),
    ActiveHeroIndex: -1, // Hero selection mode
    ActionsRemaining: 0,
    ChestPresent: true,
    ItemRetrieved: false,
    ExitHex: 1,
    ChestHex: 2,
    TurnCount: 0,
    WarriorActivatedThisTurn: false,
    ElfActivatedThisTurn: false
);

Console.WriteLine("=== Map Setup ===");
Console.WriteLine("Hex 0: START (both heroes begin here)");
Console.WriteLine("Hex 1: EXIT (escape here for reward)");
Console.WriteLine("Hex 2: CHEST (contains valuable item)\n");

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
    s => $"W:{s.Warrior.CurrentHex}{(s.Warrior.HasExited ? "X" : "")} E:{s.Elf.CurrentHex}{(s.Elf.HasExited ? "X" : "")} Ch:{(s.ChestPresent ? "Y" : "N")} T:{s.TurnCount}",
    (state, action) => FormatActionWithHero(state, action),
    maxDepth: 4,
    minVisits: 50
);

Console.WriteLine("\n=== MCTS Tree (top branches, min 50 visits) ===");
Console.WriteLine(treeText);

// Save DOT file for Graphviz
var dotContent = MctsTreeVisualizer.ToDot<QuestState, QuestAction>(
    rootNode,
    s => $"W:{s.Warrior.CurrentHex}{(s.Warrior.HasExited ? "X" : "")} E:{s.Elf.CurrentHex}{(s.Elf.HasExited ? "X" : "")}\\nChest:{(s.ChestPresent ? "Yes" : "No")} Turn:{s.TurnCount}",
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
        var activeHeroName = currentState.ActiveHeroIndex == 0 ? "Warrior" : "Elf";
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
Console.WriteLine($"Warrior Exited: {currentState.Warrior.HasExited}");
Console.WriteLine($"Elf Exited: {currentState.Elf.HasExited}");
Console.WriteLine($"Item Retrieved: {currentState.ItemRetrieved}");

static string FormatActionWithHero(QuestState state, QuestAction? action)
{
    if (action == null) return "null";
    
    var a = action.Value;
    
    // Get turn number for prefix
    var turnPrefix = $"T{state.TurnCount + 1}:";
    
    // Activation actions already specify the hero
    if (a == QuestAction.ActivateWarrior) return turnPrefix + "W:ActivateWarrior";
    if (a == QuestAction.ActivateElf) return turnPrefix + "E:ActivateElf";
    
    // For other actions, determine from the state which hero is active
    if (state.ActiveHeroIndex == -1)
    {
        return turnPrefix + a.ToString(); // No active hero yet
    }
    
    var heroPrefix = state.ActiveHeroIndex == 0 ? "W:" : "E:";
    return turnPrefix + heroPrefix + a.ToString();
}

static void PrintState(QuestState s)
{
    Console.WriteLine($"  Warrior: Hex {s.Warrior.CurrentHex}{(s.Warrior.HasExited ? " (EXITED)" : "")}{(s.Warrior.HasMoved ? " [moved]" : "")}");
    Console.WriteLine($"  Elf:     Hex {s.Elf.CurrentHex}{(s.Elf.HasExited ? " (EXITED)" : "")}{(s.Elf.HasMoved ? " [moved]" : "")}");
    Console.WriteLine($"  Chest:   {(s.ChestPresent ? $"On Hex {s.ChestHex}" : "OPENED (item retrieved)")}");
}
