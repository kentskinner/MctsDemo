using System;
using GenericMcts;
using TinyQuestDemo;

Console.WriteLine("=== TinyQuest MCTS Demo ===");
Console.WriteLine("A cooperative hex-based adventure\n");

var game = new TinyQuestGame();
var selection = new ProgressiveBiasSelection(exitHex: 4, explorationC: 1.414, biasStrength: 2.0);
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

// Graph generation disabled - using ASCII map instead

// Helper function to display a single 5x6 hex grid (compact version for side-by-side)
static List<string> GetMapLines(QuestState state, (int exitHex, int chest0Hex, int chest1Hex, int chest2Hex) mapData)
{
    const int Width = 5;
    const int Height = 6;
    var lines = new List<string>();

    lines.Add("╔═══════════════════════════╗");
    lines.Add("║      5x6 HEX GRID MAP     ║");
    lines.Add("╠═══════════════════════════╣");

    for (int row = 0; row < Height; row++)
    {
        var line = "║  ";
        for (int col = 0; col < Width; col++)
        {
            int hex = row * Width + col;
            string cell = GetCellDisplay(hex, state, mapData);
            line += $"{cell} ";
        }
        line += "║";
        lines.Add(line);
    }

    lines.Add("╚═══════════════════════════╝");
    return lines;
}

// Display two maps side by side with action description in between
static void DisplaySideBySide(QuestState beforeState, QuestState afterState, QuestAction action,
    (int exitHex, int chest0Hex, int chest1Hex, int chest2Hex) mapData, int turnNum, int stepNum)
{
    var beforeLines = GetMapLines(beforeState, mapData);
    var afterLines = GetMapLines(afterState, mapData);

    // Create a friendly action description
    string actionLine1 = "";
    string actionLine2 = "";
    string whatHappened = "";

    string actionStr = action.ToString();
    if (actionStr.StartsWith("Activate"))
    {
        // Activation actions - show which hero is being activated
        string heroName = actionStr.Replace("Activate", "");
        actionLine1 = "Selecting:";
        actionLine2 = heroName;
        whatHappened = $"The party selects {heroName} to take their turn (2 actions)";
    }
    else if (actionStr.StartsWith("MoveTo"))
    {
        // Movement actions - show which hero is moving
        string heroName = beforeState.ActiveHeroIndex switch
        {
            0 => "Warrior",
            1 => "Elf",
            2 => "Thief",
            3 => "Mage",
            _ => "Unknown"
        };
        int fromHex = GetHeroPosition(beforeState, beforeState.ActiveHeroIndex);
        int toHex = int.Parse(actionStr.Replace("MoveToHex", ""));
        actionLine1 = heroName;
        actionLine2 = actionStr;
        whatHappened = $"{heroName} moves from hex {fromHex} to hex {toHex}";
    }
    else if (actionStr == "EndActivation")
    {
        string heroName = beforeState.ActiveHeroIndex switch
        {
            0 => "Warrior",
            1 => "Elf",
            2 => "Thief",
            3 => "Mage",
            _ => "Unknown"
        };
        actionLine1 = heroName;
        actionLine2 = "Ends Turn";
        whatHappened = $"{heroName} ends their turn (no more actions or chose to end)";
    }
    else if (actionStr == "OpenChest")
    {
        string heroName = beforeState.ActiveHeroIndex switch
        {
            0 => "Warrior",
            1 => "Elf",
            2 => "Thief",
            3 => "Mage",
            _ => "Unknown"
        };
        actionLine1 = heroName;
        actionLine2 = "Opens Chest!";
        whatHappened = $"{heroName} opens a chest! Waiting for random item...";
    }
    else if (actionStr.StartsWith("GiveItem") || actionStr == "GiveNothing")
    {
        actionLine1 = "Chest:";
        actionLine2 = actionStr;

        string heroName = beforeState.ActiveHeroIndex switch
        {
            0 => "Warrior",
            1 => "Elf",
            2 => "Thief",
            3 => "Mage",
            _ => "Unknown"
        };

        if (actionStr == "GiveNothing")
            whatHappened = $"Chest was empty or {heroName} already has all items";
        else
            whatHappened = $"{heroName} receives {actionStr.Replace("GiveItem", "Item")} from the chest!";
    }
    else
    {
        actionLine1 = actionStr;
        actionLine2 = "";
        whatHappened = actionStr;
    }

    Console.WriteLine("\n" + new string('═', 100));
    Console.WriteLine($"Turn {turnNum}, Step {stepNum}: {whatHappened}");
    Console.WriteLine(new string('─', 100));

    // Display maps side by side
    for (int i = 0; i < beforeLines.Count; i++)
    {
        Console.Write(beforeLines[i]);

        // In the middle rows, show the action
        if (i == 3)
            Console.Write("    ╔══════════════╗      ");
        else if (i == 4)
            Console.Write($"    ║ {actionLine1,-12} ║      ");
        else if (i == 5)
            Console.Write($"    ║ {actionLine2,-12} ║      ");
        else if (i == 6)
            Console.Write("    ╚══════════════╝      ");
        else
            Console.Write("                          ");

        Console.WriteLine(afterLines[i]);
    }

    Console.WriteLine("     BEFORE                                              AFTER");
    Console.WriteLine(new string('═', 100));
}

static int GetHeroPosition(QuestState state, int heroIndex)
{
    return heroIndex switch
    {
        0 => state.Warrior.CurrentHex,
        1 => state.Elf.CurrentHex,
        2 => state.Thief.CurrentHex,
        3 => state.Mage.CurrentHex,
        _ => -1
    };
}

static string GetCellDisplay(int hex, QuestState state, (int exitHex, int chest0Hex, int chest1Hex, int chest2Hex) mapData)
{
    var heroes = new List<string>();

    // Check each hero's position
    if (!state.Warrior.HasExited && !state.Warrior.IsDead && state.Warrior.CurrentHex == hex)
        heroes.Add("W");
    if (!state.Elf.HasExited && !state.Elf.IsDead && state.Elf.CurrentHex == hex)
        heroes.Add("E");
    if (!state.Thief.HasExited && !state.Thief.IsDead && state.Thief.CurrentHex == hex)
        heroes.Add("T");
    if (!state.Mage.HasExited && !state.Mage.IsDead && state.Mage.CurrentHex == hex)
        heroes.Add("M");

    // If heroes present, show them
    if (heroes.Count > 0)
        return string.Join("", heroes).PadRight(4);

    // Show special locations
    if (hex == mapData.exitHex)
        return " X  ";
    if (hex == mapData.chest0Hex)
        return state.Chest0Present ? "C0  " : "[C0]";
    if (hex == mapData.chest1Hex)
        return state.Chest1Present ? "C1  " : "[C1]";
    if (hex == mapData.chest2Hex)
        return state.Chest2Present ? "C2  " : "[C2]";

    // Empty hex - show hex number for reference
    return $"{hex,2}  ";
}

// Simulate a complete game
Console.WriteLine("\n=== Simulating Complete Game ===\n");
var currentState = initialState;
int step = 1;
const int MaxSteps = 500; // Safety limit to prevent infinite loops

// Store map data for display
var mapData = (exitHex, chest0Hex, chest1Hex, chest2Hex);

// Use much fewer iterations for the simulation (it's called on every step!)
var simOptions = new MctsOptions
{
    Iterations = 100,  // Much less than the initial 10,000
    RolloutDepth = 100,
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Seed = 42
};
var simMcts = new Mcts<QuestState, QuestAction>(game, selection, expansion, simulation, backprop, simOptions);

while (!game.IsTerminal(currentState, out var termValue) && step <= MaxSteps)
{
    var beforeState = currentState;

    // Check if we're at a chance node - if so, sample it directly instead of searching
    if (game.IsChanceNode(currentState))
    {
        var rng = new Random();
        currentState = game.SampleChance(currentState, rng, out var logProb);

        // Display the chance resolution
        DisplaySideBySide(beforeState, currentState, QuestAction.GiveNothing /* placeholder */, mapData, beforeState.TurnCount + 1, step);
        PrintState(currentState);
        step++;
        continue;
    }

    var (action, _) = simMcts.Search(currentState);
    currentState = game.Step(currentState, action);

    DisplaySideBySide(beforeState, currentState, action, mapData, beforeState.TurnCount + 1, step);
    PrintState(currentState);
    step++;
}

if (step > MaxSteps)
{
    Console.WriteLine($"\n=== Simulation stopped after {MaxSteps} steps (safety limit) ===");
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

// Custom selection policy with progressive bias toward the exit
class ProgressiveBiasSelection : ISelectionPolicy<QuestState, QuestAction>
{
    private readonly double _c;
    private readonly int _exitHex;
    private readonly double _biasStrength;

    public ProgressiveBiasSelection(int exitHex, double explorationC = 1.414, double biasStrength = 2.0)
    {
        _c = explorationC;
        _exitHex = exitHex;
        _biasStrength = biasStrength;
    }

    public Node<QuestState, QuestAction> SelectChild(Node<QuestState, QuestAction> node, Random rng)
    {
        if (node.Children.Count == 0) throw new InvalidOperationException("No children to select.");

        double lnN = Math.Log(Math.Max(1, node.Visits));
        Node<QuestState, QuestAction>? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (var child in node.Children)
        {
            // Standard UCB1
            if (child.Visits == 0)
            {
                return child; // Prioritize unvisited nodes
            }

            double q = child.TotalValue / child.Visits;
            double u = _c * Math.Sqrt(lnN / child.Visits);
            double ucb = q + u;

            // Add progressive bias: decreases as visits increase
            double bias = GetHeuristicBias(node.State, child.IncomingAction!) / (1.0 + child.Visits);
            double score = ucb + bias;

            if (score > bestScore)
            {
                bestScore = score;
                best = child;
            }
        }

        return best!;
    }

    private double GetHeuristicBias(QuestState state, QuestAction action)
    {
        // Only bias movement actions toward the exit
        string actionStr = action.ToString();
        if (!actionStr.StartsWith("MoveTo")) return 0;

        // Get which hero is moving
        int heroIndex = state.ActiveHeroIndex;
        if (heroIndex < 0) return 0;

        int currentHex = heroIndex switch
        {
            0 => state.Warrior.CurrentHex,
            1 => state.Elf.CurrentHex,
            2 => state.Thief.CurrentHex,
            3 => state.Mage.CurrentHex,
            _ => -1
        };

        // Parse target hex from action
        int targetHex = int.Parse(actionStr.Replace("MoveToHex", ""));

        // Calculate Manhattan distances on 5x6 grid
        int currentDist = ManhattanDistance(currentHex, _exitHex);
        int targetDist = ManhattanDistance(targetHex, _exitHex);

        // Reward moves that get closer to the exit
        if (targetDist < currentDist)
            return _biasStrength;  // Moving closer
        else if (targetDist == currentDist)
            return 0.0;  // Sideways
        else
            return -_biasStrength / 2.0;  // Moving away (slight penalty)
    }

    private static int ManhattanDistance(int hex1, int hex2)
    {
        const int Width = 5;
        int row1 = hex1 / Width, col1 = hex1 % Width;
        int row2 = hex2 / Width, col2 = hex2 % Width;
        return Math.Abs(row1 - row2) + Math.Abs(col1 - col2);
    }
}
