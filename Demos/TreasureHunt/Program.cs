using Mcts;
using TreasureHunt;

Console.WriteLine("=== Treasure Hunt MCTS Demo ===\n");

// Create game instance
var game = new TreasureHuntGame(gridWidth: 5, gridHeight: 5, maxTurns: 20, treasureReward: 2.0, exitReward: 1.0);

// Setup initial state
// Character at (0,0), exit at (2,2), treasure at (4,4) - opposite side
var initialState = new GameState(
    CharacterX: 0,
    CharacterY: 0,
    TreasureX: 4,
    TreasureY: 4,
    ExitX: 2,
    ExitY: 2,
    HasTreasure: false,
    HasExited: false,
    TurnCount: 0
);

Console.WriteLine("=== Game Setup ===");
Console.WriteLine($"Grid: {game.GridWidth}x{game.GridHeight}");
Console.WriteLine($"Character: ({initialState.CharacterX}, {initialState.CharacterY})");
Console.WriteLine($"Treasure:  ({initialState.TreasureX}, {initialState.TreasureY})");
Console.WriteLine($"Exit:      ({initialState.ExitX}, {initialState.ExitY})");
Console.WriteLine($"Max Turns: {game.MaxTurns}");
Console.WriteLine($"\nRewards: Exit={game.ExitReward}, Treasure={game.TreasureReward}");
Console.WriteLine($"Optimal score: {game.ExitReward + game.TreasureReward} (exit + treasure)\n");

// Setup MCTS
var selection = new Ucb1Selection<GameState, GameAction>(explorationC: 1.414);
var expansion = new UniformSingleExpansion<GameState, GameAction>(deterministicRollForward: true);
var simulation = new UniformRandomSimulation<GameState, GameAction>();
var backprop = new SumBackpropagation<GameState, GameAction>();

var options = new MctsOptions
{
    Iterations = 50000,
    RolloutDepth = 20,
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Seed = 42
};

var mcts = new Mcts<GameState, GameAction>(game, selection, expansion, simulation, backprop, options);

// Run simulation
Console.WriteLine("=== Running MCTS Simulation ===\n");
var currentState = initialState;
int step = 0;

while (!game.IsTerminal(currentState, out var termValue) && step < 50)
{
    var (action, stats) = mcts.Search(currentState, out var rootNode);
    var beforeState = currentState;
    currentState = game.Step(currentState, action);
    
    step++;
    
    Console.WriteLine($"Turn {currentState.TurnCount}: {action}");
    Console.WriteLine($"  Position: ({currentState.CharacterX}, {currentState.CharacterY})");
    if (currentState.HasTreasure && !beforeState.HasTreasure)
        Console.WriteLine($"  *** Picked up treasure! ***");
    if (currentState.HasExited)
        Console.WriteLine($"  *** Exited! ***");
    
    // Display grid
    DisplayGrid(currentState, game);
    Console.WriteLine();
}

// Final results
Console.WriteLine("=== Game Over ===");
game.IsTerminal(currentState, out var finalValue);
Console.WriteLine($"Final Score: {finalValue:F1}");
Console.WriteLine($"Turns Used: {currentState.TurnCount}/{game.MaxTurns}");
Console.WriteLine($"Collected Treasure: {currentState.HasTreasure}");
Console.WriteLine($"Exited: {currentState.HasExited}");

static void DisplayGrid(GameState state, TreasureHuntGame game)
{
    Console.WriteLine("  +" + new string('-', game.GridWidth * 2 - 1) + "+");
    for (int y = 0; y < game.GridHeight; y++)
    {
        Console.Write("  |");
        for (int x = 0; x < game.GridWidth; x++)
        {
            char symbol = '.';
            
            if (x == state.CharacterX && y == state.CharacterY)
                symbol = '@';  // Character
            else if (x == state.TreasureX && y == state.TreasureY && !state.HasTreasure)
                symbol = '$';  // Treasure (if not collected)
            else if (x == state.ExitX && y == state.ExitY)
                symbol = 'X';  // Exit
            
            Console.Write(symbol);
            if (x < game.GridWidth - 1)
                Console.Write(' ');
        }
        Console.WriteLine("|");
    }
    Console.WriteLine("  +" + new string('-', game.GridWidth * 2 - 1) + "+");
}
