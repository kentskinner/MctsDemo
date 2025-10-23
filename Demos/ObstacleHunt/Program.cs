using Mcts;
using ObstacleHunt;

Console.WriteLine("=== Obstacle Hunt MCTS Demo ===\n");

// Create obstacles - a wall separating the grid
var obstacles = new HashSet<(int X, int Y)>
{
    (2, 0), (2, 1), (2, 3), (2, 4)  // Vertical wall with gap at (2, 2)
};

// Create game instance
var game = new ObstacleHuntGame(
    gridWidth: 5, 
    gridHeight: 5, 
    maxTurns: 30, 
    treasureReward: 2.0, 
    exitReward: 1.0,
    obstacles: obstacles
);

// Setup initial state
// Character at (0,0), exit at (1,2), treasure at (4,4)
// Character must navigate around the wall
var initialState = new GameState(
    CharacterX: 0,
    CharacterY: 0,
    TreasureX: 4,
    TreasureY: 4,
    ExitX: 1,
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
Console.WriteLine($"Obstacles: {obstacles.Count} (shown as #)");
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
    RolloutDepth = 30,
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Seed = 42
};

var mcts = new Mcts<GameState, GameAction>(game, selection, expansion, simulation, backprop, options);

// Run simulation
Console.WriteLine("=== Running MCTS Simulation ===\n");
var currentState = initialState;
int step = 0;

DisplayGrid(currentState, game);

while (!game.IsTerminal(currentState, out var termValue) && step < 50)
{
    var (action, stats) = mcts.Search(currentState, out var rootNode);
    var beforeState = currentState;
    currentState = game.Step(currentState, action);
    step++;

    Console.WriteLine($"Turn {currentState.TurnCount}: {action}");
    Console.WriteLine($"  Position: ({currentState.CharacterX}, {currentState.CharacterY})");
    
    if (action == GameAction.PickupTreasure)
        Console.WriteLine("  *** Picked up treasure! ***");
    if (action == GameAction.Exit)
        Console.WriteLine("  *** Exited! ***");
    
    DisplayGrid(currentState, game);
}

// Display final results
Console.WriteLine("\n=== Game Over ===");
game.IsTerminal(currentState, out var finalValue);
Console.WriteLine($"Final Score: {finalValue}");
Console.WriteLine($"Turns Used: {currentState.TurnCount}/{game.MaxTurns}");
Console.WriteLine($"Collected Treasure: {currentState.HasTreasure}");
Console.WriteLine($"Exited: {currentState.HasExited}");

void DisplayGrid(GameState state, ObstacleHuntGame g)
{
    Console.WriteLine("  +---------+");
    for (int y = 0; y < g.GridHeight; y++)
    {
        Console.Write("  |");
        for (int x = 0; x < g.GridWidth; x++)
        {
            if (x == state.CharacterX && y == state.CharacterY)
                Console.Write("@");
            else if (g.Obstacles.Contains((x, y)))
                Console.Write("#");
            else if (!state.HasTreasure && x == state.TreasureX && y == state.TreasureY)
                Console.Write("$");
            else if (x == state.ExitX && y == state.ExitY)
                Console.Write("X");
            else
                Console.Write(".");
            
            Console.Write(" ");
        }
        Console.WriteLine("|");
    }
    Console.WriteLine("  +---------+\n");
}
