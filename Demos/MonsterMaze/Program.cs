using Mcts;
using MonsterMaze;

Console.WriteLine("=== Monster Maze MCTS Demo ===\n");

// Create obstacles - walls forming a maze (easier layout with clear path to exit)
var obstacles = new HashSet<(int X, int Y)>
{
    // Scattered obstacles instead of blocking walls
    (2, 2), (2, 5),
    (5, 3), (5, 5),
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
    treasureReward: 20.0,  // Extremely high reward to overcome risk aversion
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
    TurnCount: 0,
    RewardCollected: 0.0
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

// Heuristic function to guide MCTS
double Heuristic(GameState state, GameAction action)
{
    // If dead, terrible
    if (state.IsDead) return -10.0;

    // Exit action bonus (state will have HasExited=true after taking Exit)
    if (action == GameAction.Exit && state.HasExited)
    {
        return state.HasTreasure ? 50.0 : 30.0;  // High enough to dominate other options
    }

    // If exited, good
    if (state.HasExited)
        return state.HasTreasure ? 10.0 : 5.0;

    double value = 0;

    // Penalize being on a mine
    if (mines.Contains((state.PlayerX, state.PlayerY)))
        value -= 10.0;

    // Penalize Wait action when monster is dead (should be moving!)
    if (state.MonsterDead && action == GameAction.Wait)
        value -= 5.0;

    // Generally penalize Wait action (but less severely)
    if (action == GameAction.Wait)
        value -= 1.0;

    // Reward for having treasure
    if (state.HasTreasure)
    {
        value += 10.0;  // Moderate reward for having treasure

        // When carrying treasure, prioritize reaching exit
        int distToExit = Math.Abs(state.PlayerX - state.ExitX) + Math.Abs(state.PlayerY - state.ExitY);
        value += 10.0 / (1.0 + distToExit);  // Distance-based guidance to exit
    }
    else
    {
        // Don't have treasure yet - prioritize getting it
        int distToTreasure = Math.Abs(state.PlayerX - state.TreasureX) + Math.Abs(state.PlayerY - state.TreasureY);

        if (state.MonsterDead)
        {
            // Monster is dead - treasure is priority
            value += 8.0 / (1.0 + distToTreasure);
        }
        else
        {
            // Monster alive - still want treasure but be careful
            value += 3.0 / (1.0 + distToTreasure);
        }

        // Also consider exit proximity but with lower weight
        int distToExit = Math.Abs(state.PlayerX - state.ExitX) + Math.Abs(state.PlayerY - state.ExitY);
        value += 0.5 / (1.0 + distToExit);
    }

    // Penalize being near the monster (if it's alive)
    if (!state.MonsterDead)
    {
        int distToMonster = Math.Abs(state.PlayerX - state.MonsterX) + Math.Abs(state.PlayerY - state.MonsterY);
        if (distToMonster < 3)
            value -= 2.0 / (1.0 + distToMonster);
    }

    return value;
}

Console.WriteLine("Running MonsterMaze with MCTS AI...\n");

// Setup MCTS with progressive bias
var selection = new ProgressiveBiasSelection<GameState, GameAction>(
    heuristicFunc: Heuristic,
    visitThreshold: 50,
    biasStrength: 1.0,
    explorationConstant: 10.0  // Increased to match reward scale (treasure=20, exit=1)
);
var expansion = new UniformSingleExpansion<GameState, GameAction>(deterministicRollForward: true);
var simulation = new UniformRandomSimulation<GameState, GameAction>();
var backprop = new SumBackpropagation<GameState, GameAction>();

var options = new MctsOptions
{
    Iterations = 75000,
    RolloutDepth = 40,
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Seed = null,  // Random seed to test consistency
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
