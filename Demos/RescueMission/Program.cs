using Mcts;
using RescueMission;

Console.WriteLine("=== Rescue Mission MCTS Demo ===\n");

var game = new RescueMissionGame();

// Setup initial state
var initialState = new GameState(
    Hero1X: 0, Hero1Y: 0,      // Hero 1 starts top-left
    Hero2X: 8, Hero2Y: 0,      // Hero 2 starts top-right
    MonsterX: 4, MonsterY: 8,  // Monster at bottom center
    KeyX: 1, KeyY: 4,          // Key on left side
    DoorX: 7, DoorY: 4,        // Door on right side
    ExitX: 4, ExitY: 0,        // Exit at top center
    HasKey: false,
    DoorOpen: false,
    Hero1Exited: false,
    Hero2Exited: false,
    Hero1Dead: false,
    Hero2Dead: false,
    MonsterDead: false,
    TurnCount: 0,
    RewardCollected: 0.0
);

Console.WriteLine("=== Game Setup ===");
Console.WriteLine($"Grid: {game.GridWidth}x{game.GridHeight}");
Console.WriteLine($"Hero 1:  ({initialState.Hero1X}, {initialState.Hero1Y}) - needs to get KEY");
Console.WriteLine($"Hero 2:  ({initialState.Hero2X}, {initialState.Hero2Y}) - needs to open DOOR");
Console.WriteLine($"Monster: ({initialState.MonsterX}, {initialState.MonsterY}) - chases heroes!");
Console.WriteLine($"Key:     ({initialState.KeyX}, {initialState.KeyY})");
Console.WriteLine($"Door:    ({initialState.DoorX}, {initialState.DoorY})");
Console.WriteLine($"Exit:    ({initialState.ExitX}, {initialState.ExitY})");
Console.WriteLine($"Max Turns: {game.MaxTurns}");
Console.WriteLine($"Obstacles: {game.Obstacles.Count}");
Console.WriteLine($"Mines:     {game.Mines.Count}");
Console.WriteLine();
Console.WriteLine("Rewards: Key=10, Door=15, Exit=30");
Console.WriteLine("Death Penalty: -20");
Console.WriteLine("Optimal score: 55 (key + door + exit)");
Console.WriteLine("GOAL: Get key, open door, both heroes exit together!\n");

// Heuristic function
double Heuristic(GameState state, GameAction action)
{
    var obstacles = game.Obstacles;
    var mines = game.Mines;
    
    // Terminal states
    if (state.Hero1Dead || state.Hero2Dead) return -50.0;
    
    // Exit bonus
    if (action == GameAction.BothExit && state.Hero1Exited && state.Hero2Exited)
        return 100.0;
    
    if (state.Hero1Exited && state.Hero2Exited)
        return 50.0;
    
    double value = 0;
    
    // Penalize mines
    if (mines.Contains((state.Hero1X, state.Hero1Y))) value -= 15.0;
    if (mines.Contains((state.Hero2X, state.Hero2Y))) value -= 15.0;
    
    // Penalize wait when should be moving
    if ((action == GameAction.Hero1Wait || action == GameAction.Hero2Wait) && state.MonsterDead)
        value -= 3.0;
    
    // Progress rewards
    if (state.HasKey)
    {
        value += 15.0;
        
        if (state.DoorOpen)
        {
            value += 20.0;
            
            // Both should head to exit
            int h1ToExit = Math.Abs(state.Hero1X - state.ExitX) + Math.Abs(state.Hero1Y - state.ExitY);
            int h2ToExit = Math.Abs(state.Hero2X - state.ExitX) + Math.Abs(state.Hero2Y - state.ExitY);
            value += 15.0 / (1.0 + h1ToExit + h2ToExit);
        }
        else
        {
            // Hero 2 should go to door
            int h2ToDoor = Math.Abs(state.Hero2X - state.DoorX) + Math.Abs(state.Hero2Y - state.DoorY);
            value += 12.0 / (1.0 + h2ToDoor);
        }
    }
    else
    {
        // Hero 1 should go to key
        int h1ToKey = Math.Abs(state.Hero1X - state.KeyX) + Math.Abs(state.Hero1Y - state.KeyY);
        value += 10.0 / (1.0 + h1ToKey);
    }
    
    // Avoid monster
    if (!state.MonsterDead)
    {
        int h1ToMonster = Math.Abs(state.Hero1X - state.MonsterX) + Math.Abs(state.Hero1Y - state.MonsterY);
        int h2ToMonster = Math.Abs(state.Hero2X - state.MonsterX) + Math.Abs(state.Hero2Y - state.MonsterY);
        
        if (h1ToMonster < 3) value -= 5.0 / (1.0 + h1ToMonster);
        if (h2ToMonster < 3) value -= 5.0 / (1.0 + h2ToMonster);
    }
    
    return value;
}

Console.WriteLine("Running Rescue Mission with MCTS AI...\n");

// Setup MCTS
var selection = new ProgressiveBiasSelection<GameState, GameAction>(
    heuristicFunc: Heuristic,
    visitThreshold: 50,
    biasStrength: 1.0,
    explorationConstant: 15.0  // Higher for larger reward scale
);
var expansion = new UniformSingleExpansion<GameState, GameAction>(deterministicRollForward: true);
var simulation = new UniformRandomSimulation<GameState, GameAction>();
var backprop = new SumBackpropagation<GameState, GameAction>();

var options = new MctsOptions
{
    Iterations = 100000,  // More iterations for 2-character coordination
    RolloutDepth = 50,
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Seed = null,
    Verbose = false
};

var mcts = new Mcts<GameState, GameAction>(game, selection, expansion, simulation, backprop, options);

// Display function
void DisplayGrid(GameState state, RescueMissionGame g)
{
    Console.WriteLine("  +" + new string('-', g.GridWidth * 2) + "+");
    for (int y = 0; y < g.GridHeight; y++)
    {
        Console.Write("  |");
        for (int x = 0; x < g.GridWidth; x++)
        {
            char c = '.';
            
            // Check what's at this position
            if (state.Hero1X == x && state.Hero1Y == y && !state.Hero1Dead && !state.Hero1Exited)
                c = '1';
            else if (state.Hero2X == x && state.Hero2Y == y && !state.Hero2Dead && !state.Hero2Exited)
                c = '2';
            else if (state.MonsterX == x && state.MonsterY == y && !state.MonsterDead)
                c = 'M';
            else if (!state.HasKey && state.KeyX == x && state.KeyY == y)
                c = 'K';
            else if (!state.DoorOpen && state.DoorX == x && state.DoorY == y)
                c = 'D';
            else if (state.ExitX == x && state.ExitY == y)
                c = 'E';
            else if (g.Obstacles.Contains((x, y)))
                c = '#';
            else if (g.Mines.Contains((x, y)))
                c = '*';
            
            Console.Write(c + " ");
        }
        Console.WriteLine("|");
    }
    Console.WriteLine("  +" + new string('-', g.GridWidth * 2) + "+");
    Console.WriteLine();
}

// Run simulation
Console.WriteLine("=== Running MCTS Simulation ===\n");
var currentState = initialState;
int step = 0;

DisplayGrid(currentState, game);

while (!game.IsTerminal(currentState, out var termValue) && step < 80)
{
    var previousState = currentState;
    var (action, stats) = mcts.Search(currentState);
    currentState = game.Step(currentState, action);
    step++;

    Console.WriteLine($"Turn {currentState.TurnCount}: {action}");
    Console.WriteLine($"  Hero 1: ({currentState.Hero1X}, {currentState.Hero1Y})");
    Console.WriteLine($"  Hero 2: ({currentState.Hero2X}, {currentState.Hero2Y})");
    if (!currentState.MonsterDead)
        Console.WriteLine($"  Monster: ({currentState.MonsterX}, {currentState.MonsterY})");
    else
        Console.WriteLine($"  Monster: DEAD");
    
    // Event notifications
    if (currentState.HasKey && !previousState.HasKey)
        Console.WriteLine("  *** Hero 1 picked up the KEY! ***");
    if (currentState.DoorOpen && !previousState.DoorOpen)
        Console.WriteLine("  *** Hero 2 opened the DOOR! ***");
    if (currentState.Hero1Exited && currentState.Hero2Exited && 
        !(previousState.Hero1Exited && previousState.Hero2Exited))
        Console.WriteLine("  *** BOTH HEROES ESCAPED! ***");
    if (currentState.Hero1Dead && !previousState.Hero1Dead)
        Console.WriteLine("  *** Hero 1 DIED! ***");
    if (currentState.Hero2Dead && !previousState.Hero2Dead)
        Console.WriteLine("  *** Hero 2 DIED! ***");
    if (currentState.MonsterDead && !previousState.MonsterDead)
        Console.WriteLine("  *** Monster hit a MINE! ***");
    
    DisplayGrid(currentState, game);
}

Console.WriteLine("\n=== Game Over ===");
Console.WriteLine($"Final Score: {currentState.RewardCollected}");
Console.WriteLine($"Turns Used: {currentState.TurnCount}/{game.MaxTurns}");
Console.WriteLine($"Has Key: {currentState.HasKey}");
Console.WriteLine($"Door Open: {currentState.DoorOpen}");
Console.WriteLine($"Hero 1 Status: {(currentState.Hero1Dead ? "Dead" : currentState.Hero1Exited ? "Escaped" : "Alive")}");
Console.WriteLine($"Hero 2 Status: {(currentState.Hero2Dead ? "Dead" : currentState.Hero2Exited ? "Escaped" : "Alive")}");
Console.WriteLine($"Monster: {(currentState.MonsterDead ? "Dead" : "Alive")}");
