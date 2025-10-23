using Mcts;
using DungeonCrawler;

Console.WriteLine("=== Dungeon Crawler MCTS Demo ===\n");

// Create game instance with chance-based mechanics
var game = new DungeonCrawlerGame(
    gridWidth: 5,
    gridHeight: 5,
    maxTurns: 30,
    seed: 42
);

var initialState = game.InitialState();

Console.WriteLine("=== Game Setup ===");
Console.WriteLine($"Grid: {game.GridWidth}x{game.GridHeight}");
Console.WriteLine($"Player Start: ({initialState.PlayerX}, {initialState.PlayerY})");
Console.WriteLine($"Exit: ({game.ExitLocation.X}, {game.ExitLocation.Y})");
Console.WriteLine($"Max Turns: {game.MaxTurns}");
Console.WriteLine($"Starting Health: {initialState.PlayerHealth}/10");
Console.WriteLine($"Starting Potions: {initialState.Potions}");
Console.WriteLine();
Console.WriteLine("Chance Mechanics:");
Console.WriteLine("  Search Room: 40% treasure(+10g), 20% potion(+5r), 15% monster(-2r), 25% nothing");
Console.WriteLine("  Combat: 15% critical(3dmg,+3r), 50% hit(2dmg,+2r), 35% miss(player-1hp,-1r)");
Console.WriteLine("  Kill Monster: +10 reward bonus");
Console.WriteLine("  Exit: +gold value + 20 bonus");
Console.WriteLine();

// Heuristic function
double Heuristic(GameState state, PlayerAction action)
{
    if (game.IsTerminal(in state, out _))
        return 0;

    var (x, y) = (state.PlayerX, state.PlayerY);
    var room = state.Rooms[(x, y)];
    
    // Exit action is very valuable if we have gold
    if (action == PlayerAction.Exit)
    {
        return 100.0 + state.Gold * 2;  // Strong incentive to exit with treasure
    }
    
    // Use potion if health is low
    if (action == PlayerAction.UsePotion)
    {
        if (state.PlayerHealth <= 3)
            return 50.0;  // Critical health
        if (state.PlayerHealth <= 6)
            return 20.0;  // Low health
        return 5.0;
    }
    
    // Fighting monster is good if we're healthy
    if (action == PlayerAction.FightMonster)
    {
        if (state.PlayerHealth >= 7)
            return 30.0;  // Healthy - go for it
        if (state.PlayerHealth >= 4)
            return 15.0;  // Okay health
        return -10.0;  // Too risky when low health
    }
    
    // Searching unsearched rooms is valuable
    if (action == PlayerAction.SearchRoom)
    {
        return 25.0;  // Good chance of reward
    }
    
    // Movement heuristics
    var exitDist = Math.Abs(x - game.ExitLocation.X) + Math.Abs(y - game.ExitLocation.Y);
    
    // If we have good gold and healthy, move toward exit
    if (state.Gold >= 20 && state.PlayerHealth >= 5)
    {
        var newX = x;
        var newY = y;
        
        switch (action)
        {
            case PlayerAction.MoveNorth: newY--; break;
            case PlayerAction.MoveSouth: newY++; break;
            case PlayerAction.MoveEast: newX++; break;
            case PlayerAction.MoveWest: newX--; break;
        }
        
        var newDist = Math.Abs(newX - game.ExitLocation.X) + Math.Abs(newY - game.ExitLocation.Y);
        if (newDist < exitDist)
            return 15.0;  // Moving closer to exit
    }
    
    // Otherwise explore - prefer moving to unsearched rooms
    if (action >= PlayerAction.MoveNorth && action <= PlayerAction.MoveWest)
    {
        var newX = x;
        var newY = y;
        
        switch (action)
        {
            case PlayerAction.MoveNorth: newY--; break;
            case PlayerAction.MoveSouth: newY++; break;
            case PlayerAction.MoveEast: newX++; break;
            case PlayerAction.MoveWest: newX--; break;
        }
        
        if (newY >= 0 && newY < game.GridHeight && newX >= 0 && newX < game.GridWidth)
        {
            var targetRoom = state.Rooms[(newX, newY)];
            if (!targetRoom.Searched)
                return 10.0;  // Prefer exploring new rooms
            if (targetRoom.MonsterHealth == 0)
                return 5.0;   // Safe cleared rooms
            return 2.0;       // Room with monster
        }
    }
    
    return 1.0;
}

Console.WriteLine("Running Dungeon Crawler with MCTS AI...\n");

// Setup MCTS with progressive bias
var selection = new ProgressiveBiasSelection<GameState, PlayerAction>(
    heuristicFunc: Heuristic,
    visitThreshold: 50,
    biasStrength: 1.0,
    explorationConstant: 15.0  // Higher because rewards are larger (10s, 20s)
);

var expansion = new UniformSingleExpansion<GameState, PlayerAction>(deterministicRollForward: false);  // Stochastic!
var simulation = new UniformRandomSimulation<GameState, PlayerAction>();
var backprop = new SumBackpropagation<GameState, PlayerAction>();

var options = new MctsOptions
{
    Iterations = 100000,  // More iterations for stochastic game
    RolloutDepth = 30,
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Seed = null,  // Random seed
    Verbose = false
};

var mcts = new Mcts<GameState, PlayerAction>(game, selection, expansion, simulation, backprop, options);

Console.WriteLine("=== Running MCTS Simulation ===\n");
Console.WriteLine(game.StateToString(initialState));

var currentState = initialState;
var actionHistory = new List<(PlayerAction action, GameState resultState)>();

while (!game.IsTerminal(in currentState, out _))
{
    var searchResult = mcts.Search(currentState, out var rootNode);
    var action = searchResult.action;
    currentState = game.Step(in currentState, in action);
    actionHistory.Add((action, currentState));
    
    Console.WriteLine($"Turn {currentState.TurnCount}: {action}");

    // Show combat/search results (need at least 2 entries in history to compare)
    if (actionHistory.Count >= 2)
    {
        var room = currentState.Rooms[(currentState.PlayerX, currentState.PlayerY)];
        if (action == PlayerAction.SearchRoom)
        {
            if (currentState.Gold > actionHistory[^2].resultState.Gold)
                Console.WriteLine("  *** Found treasure! (+10 gold) ***");
            else if (currentState.Potions > actionHistory[^2].resultState.Potions)
                Console.WriteLine("  *** Found potion! ***");
            else if (room.MonsterHealth > 0)
                Console.WriteLine("  *** Disturbed a monster! ***");
            else
                Console.WriteLine("  Found nothing.");
        }
        else if (action == PlayerAction.FightMonster)
        {
            var prevMonsterHp = actionHistory[^2].resultState.Rooms[(currentState.PlayerX, currentState.PlayerY)].MonsterHealth;
            var damage = prevMonsterHp - room.MonsterHealth;

            if (damage == 3)
                Console.WriteLine($"  *** CRITICAL HIT! Monster took {damage} damage! ***");
            else if (damage == 2)
                Console.WriteLine($"  Hit! Monster took {damage} damage!");
            else if (damage == 0)
                Console.WriteLine("  MISS! Monster counters for 1 damage!");

            if (room.MonsterHealth == 0 && prevMonsterHp > 0)
                Console.WriteLine("  *** MONSTER DEFEATED! ***");
        }
        else if (action == PlayerAction.UsePotion)
        {
            Console.WriteLine("  Used potion - restored 5 HP!");
        }
        else if (action == PlayerAction.Exit)
        {
            Console.WriteLine("  *** ESCAPED THE DUNGEON! ***");
        }
    }

    Console.WriteLine(game.StateToString(currentState));
}

Console.WriteLine("\n=== Game Over ===");
Console.WriteLine($"Final Score: {currentState.RewardCollected:F1}");
Console.WriteLine($"Turns Used: {currentState.TurnCount}/{game.MaxTurns}");
Console.WriteLine($"Final Health: {currentState.PlayerHealth}/10");
Console.WriteLine($"Gold Collected: {currentState.Gold}");
Console.WriteLine($"Escaped: {currentState.HasExited}");
Console.WriteLine($"Died: {currentState.PlayerHealth <= 0}");

