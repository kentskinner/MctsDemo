using Mcts;
using MonsterMaze;

Console.WriteLine("=== Monster Maze MCTS Demo ===\n");

// Create obstacles - walls forming a maze
var obstacles = new HashSet<(int X, int Y)>
{
    // Vertical walls
    (2, 1), (2, 2), (2, 3),
    (4, 3), (4, 4), (4, 5),
};

// Create mines - dangerous spots
var mines = new HashSet<(int X, int Y)>
{
    (3, 2), (5, 3), (1, 5)
};

// Create game instance
var game = new MonsterMazeGame(
    gridWidth: 7, 
    gridHeight: 7, 
    maxTurns: 40, 
    treasureReward: 3.0,  // High reward to encourage collecting
    exitReward: 1.0,
    deathPenalty: -5.0,   // Strong penalty for dying
    obstacles: obstacles,
    mines: mines
);

// Setup initial state
// Player at (0,0), monster at (6,6), treasure at (6,0), exit at (3,6)
var initialState = new GameState(
    PlayerX: 0,
    PlayerY: 0,
    MonsterX: 6,
    MonsterY: 6,
    TreasureX: 6,
    TreasureY: 0,
    ExitX: 3,
    ExitY: 6,
    HasTreasure: false,
    HasExited: false,
    IsDead: false,
    MonsterDead: false,
    TurnCount: 0
);

Console.WriteLine("=== Game Setup ===");
Console.WriteLine($"Grid: {game.GridWidth}x{game.GridHeight}");
Console.WriteLine($"Player:   ({initialState.PlayerX}, {initialState.PlayerY})");
Console.WriteLine($"Monster:  ({initialState.MonsterX}, {initialState.MonsterY}) - moves towards player!");
Console.WriteLine($"Treasure: ({initialState.TreasureX}, {initialState.TreasureY})");
Console.WriteLine($"Exit:     ({initialState.ExitX}, {initialState.ExitY})");
Console.WriteLine($"Max Turns: {game.MaxTurns}");
Console.WriteLine($"Obstacles: {obstacles.Count} (shown as #)");
Console.WriteLine($"Mines:     {mines.Count} (shown as *)");
Console.WriteLine($"\nRewards: Exit={game.ExitReward}, Treasure={game.TreasureReward}");
Console.WriteLine($"Death Penalty: {game.DeathPenalty}");
Console.WriteLine($"Optimal score: {game.ExitReward + game.TreasureReward} (exit + treasure)");
Console.WriteLine($"TIP: You can lure the monster onto a mine to eliminate the threat!\n");

// Setup MCTS
var selection = new Ucb1Selection<GameState, GameAction>(explorationC: 1.414);
var expansion = new UniformSingleExpansion<GameState, GameAction>(deterministicRollForward: true);
var simulation = new UniformRandomSimulation<GameState, GameAction>();
var backprop = new SumBackpropagation<GameState, GameAction>();

var options = new MctsOptions
{
    Iterations = 50000,
    RolloutDepth = 40,
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Seed = 42,
    Verbose = false
};

var mcts = new Mcts<GameState, GameAction>(game, selection, expansion, simulation, backprop, options);

// Run simulation
Console.WriteLine("=== Running MCTS Simulation ===\n");
var currentState = initialState;
int step = 0;

DisplayGrid(currentState, game);

while (!game.IsTerminal(currentState, out var termValue) && step < 60)
{
    var previousState = currentState;
    var (action, stats) = mcts.Search(currentState, out var rootNode);
    currentState = game.Step(currentState, action);
    step++;

    Console.WriteLine($"Turn {currentState.TurnCount}: {action}");
    Console.WriteLine($"  Player:  ({currentState.PlayerX}, {currentState.PlayerY})");
    if (!currentState.MonsterDead)
        Console.WriteLine($"  Monster: ({currentState.MonsterX}, {currentState.MonsterY})");
    else
        Console.WriteLine($"  Monster: DEAD (stepped on mine!)");
    
    if (action == GameAction.PickupTreasure)
        Console.WriteLine("  *** Picked up treasure! ***");
    if (action == GameAction.Exit)
        Console.WriteLine("  *** Exited safely! ***");
    if (currentState.IsDead)
        Console.WriteLine("  *** PLAYER DIED! ***");
    if (currentState.MonsterDead && !previousState.MonsterDead)
        Console.WriteLine("  *** MONSTER DESTROYED BY MINE! ***");
    
    DisplayGrid(currentState, game);
    
    // Stop if player died
    if (currentState.IsDead)
        break;
}

// Display final results
Console.WriteLine("\n=== Game Over ===");
game.IsTerminal(currentState, out var finalValue);
Console.WriteLine($"Final Score: {finalValue}");
Console.WriteLine($"Turns Used: {currentState.TurnCount}/{game.MaxTurns}");
Console.WriteLine($"Collected Treasure: {currentState.HasTreasure}");
Console.WriteLine($"Exited: {currentState.HasExited}");
Console.WriteLine($"Died: {currentState.IsDead}");

void DisplayGrid(GameState state, MonsterMazeGame g)
{
    Console.WriteLine("  +--------------+");
    for (int y = 0; y < g.GridHeight; y++)
    {
        Console.Write("  |");
        for (int x = 0; x < g.GridWidth; x++)
        {
            char symbol = '.';
            
            // Player and monster on same spot (dead)
            if (x == state.PlayerX && y == state.PlayerY && 
                x == state.MonsterX && y == state.MonsterY && !state.MonsterDead)
                symbol = 'X';  // Death
            else if (x == state.PlayerX && y == state.PlayerY)
                symbol = '@';
            else if (x == state.MonsterX && y == state.MonsterY && !state.MonsterDead)
                symbol = 'M';
            else if (g.Obstacles.Contains((x, y)))
                symbol = '#';
            else if (g.Mines.Contains((x, y)))
                symbol = '*';
            else if (!state.HasTreasure && x == state.TreasureX && y == state.TreasureY)
                symbol = '$';
            else if (x == state.ExitX && y == state.ExitY)
                symbol = 'E';
            
            Console.Write(symbol);
            Console.Write(" ");
        }
        Console.WriteLine("|");
    }
    Console.WriteLine("  +--------------+\n");
}
