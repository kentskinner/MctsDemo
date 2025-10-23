using Mcts;
using TacticalSquad;
using System;
using System.Collections.Generic;
using System.Linq;
using static TacticalSquad.ChanceType;

Console.WriteLine("=== Tactical Squad MCTS Demo ===");
Console.WriteLine();

var game = new TacticalSquadGame(
    gridWidth: 7,
    gridHeight: 7,
    numHeroes: 2,
    maxTurns: 30,
    seed: null  // Random seed each run
);

var initialState = game.InitialState();

Console.WriteLine("=== Game Setup ===");
Console.WriteLine($"Grid: {initialState.GridWidth}x{initialState.GridHeight}");
Console.WriteLine($"Heroes: {initialState.Heroes.Length}");
foreach (var hero in initialState.Heroes)
{
    Console.WriteLine($"  Hero {hero.Id} ({hero.Class}): HP={hero.MaxHealth}, Damage={hero.Damage}, Start=({hero.X},{hero.Y})");
}
Console.WriteLine($"Monsters: 0 (spawn 1 per turn!)");
Console.WriteLine($"Exit: ({initialState.ExitX}, {initialState.ExitY})");
Console.WriteLine($"Max Turns: 30");
Console.WriteLine();
Console.WriteLine("Mechanics:");
Console.WriteLine("  - Each hero gets 2 actions per turn");
Console.WriteLine("  - Heroes act sequentially (one finishes before next starts)");
Console.WriteLine("  - After all heroes act, a new monster spawns!");
Console.WriteLine("  - Monsters move randomly, then counter-attack adjacent heroes");
Console.WriteLine("  - Win by getting all living heroes to exit");
Console.WriteLine();

// Heuristic function for progressive bias
double Heuristic(GameState state, SquadAction action)
{
    var hero = state.Heroes[state.CurrentHeroIndex];
    
    if (hero.Health <= 0)
        return 0;  // Dead heroes don't matter
    
    double score = 0;
    
    // Simulate the action to see the result
    int newX = hero.X;
    int newY = hero.Y;
    
    switch (action)
    {
        case SquadAction.MoveNorth: newY--; break;
        case SquadAction.MoveSouth: newY++; break;
        case SquadAction.MoveEast: newX++; break;
        case SquadAction.MoveWest: newX--; break;
    }
    
    // Strongly prefer moving toward exit
    if (action >= SquadAction.MoveNorth && action <= SquadAction.MoveWest)
    {
        int currentDistToExit = Math.Abs(hero.X - state.ExitX) + Math.Abs(hero.Y - state.ExitY);
        int newDistToExit = Math.Abs(newX - state.ExitX) + Math.Abs(newY - state.ExitY);
        
        if (newDistToExit < currentDistToExit)
            score += 30;  // Moving closer to exit
        else if (newDistToExit > currentDistToExit)
            score -= 20;  // Moving away from exit
        
        // Bonus for low HP heroes getting closer to exit
        if (hero.Health <= hero.MaxHealth / 2)
            score += (currentDistToExit - newDistToExit) * 10;
    }
    
    // Attacking monsters is good
    if (action == SquadAction.Attack)
    {
        // Find adjacent monster
        var monster = state.Monsters
            .Where(m => m.Health > 0 &&
                Math.Abs(m.X - hero.X) + Math.Abs(m.Y - hero.Y) == 1)
            .OrderBy(m => m.Health)
            .FirstOrDefault();
        
        if (monster != null)
        {
            score += 60;  // High base attack value (was 40)
            
            // Bonus for killing blow
            if (monster.Health <= hero.Damage)
                score += 50;  // Big bonus for finishing a monster (was 30)
            
            // Warriors and mages should attack more
            if (hero.Class == HeroClass.Warrior || hero.Class == HeroClass.Mage)
                score += 20;  // Increased from 10
                
            // Healthy heroes should be aggressive
            if (hero.Health >= hero.MaxHealth * 2 / 3)
                score += 20;
        }
    }
    
    // Avoid monsters for low HP heroes
    if (action >= SquadAction.MoveNorth && action <= SquadAction.MoveWest)
    {
        bool movingNearMonster = state.Monsters.Any(m => m.Health > 0 &&
            Math.Abs(m.X - newX) + Math.Abs(m.Y - newY) <= 1);
        
        if (movingNearMonster && hero.Health <= hero.MaxHealth / 3)
            score -= 25;  // Low HP heroes should avoid monsters
    }
    
    // End turn is neutral but slightly discouraged
    if (action == SquadAction.EndTurn)
    {
        // Only end turn if we're already at exit
        if (hero.X == state.ExitX && hero.Y == state.ExitY)
            score += 20;
        else
            score -= 5;
    }
    
    return score;
}

Console.WriteLine("Running Tactical Squad with MCTS AI...");
Console.WriteLine();

// Create MCTS with progressive bias
var selection = new ProgressiveBiasSelection<GameState, SquadAction>(
    heuristicFunc: Heuristic,
    visitThreshold: 30,
    biasStrength: 0.5,
    explorationConstant: 20.0  // Higher for larger reward scale
);

var expansion = new UniformSingleExpansion<GameState, SquadAction>(deterministicRollForward: true);
var simulation = new UniformRandomSimulation<GameState, SquadAction>();
var backprop = new SumBackpropagation<GameState, SquadAction>();

var options = new MctsOptions
{
    Iterations = 500,     // Very low for testing while implementing chance nodes
    RolloutDepth = 20,
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Verbose = false
};

var mcts = new Mcts<GameState, SquadAction>(game, selection, expansion, simulation, backprop, options);

// Run game simulation
Console.WriteLine("=== Running MCTS Simulation ===");
Console.WriteLine();

var currentState = initialState;
var actionHistory = new List<(SquadAction action, GameState resultState)>();

void PrintState(GameState state)
{
    Console.WriteLine($"  Turn {state.TurnCount}/50");
    Console.WriteLine($"  Current Hero: {state.CurrentHeroIndex} ({state.Heroes[state.CurrentHeroIndex].Class})");
    Console.WriteLine();
    
    // Draw grid
    Console.WriteLine("  +" + new string('-', state.GridWidth) + "+");
    for (int y = 0; y < state.GridHeight; y++)
    {
        Console.Write("  |");
        for (int x = 0; x < state.GridWidth; x++)
        {
            if (x == state.ExitX && y == state.ExitY)
                Console.Write("E");
            else if (state.Walls.Contains((x, y)))
                Console.Write("#");
            else if (state.Heroes.Any(h => h.X == x && h.Y == y && h.Health > 0))
            {
                var hero = state.Heroes.First(h => h.X == x && h.Y == y);
                Console.Write(hero.Id);
            }
            else if (state.Monsters.Any(m => m.X == x && m.Y == y && m.Health > 0))
                Console.Write("M");
            else
                Console.Write(" ");
        }
        Console.WriteLine("|");
    }
    Console.WriteLine("  +" + new string('-', state.GridWidth) + "+");
    Console.WriteLine();
    
    // Print hero stats
    Console.WriteLine("  Heroes:");
    foreach (var hero in state.Heroes)
    {
        string status = hero.Health > 0 ? $"HP:{hero.Health}/{hero.MaxHealth}" : "DEAD";
        string actions = hero.Id == state.CurrentHeroIndex ? $" [{hero.ActionsRemaining} actions left]" : "";
        Console.WriteLine($"    {hero.Id} ({hero.Class}): {status} at ({hero.X},{hero.Y}){actions}");
    }
    
    // Print monster stats
    var aliveMonsters = state.Monsters.Where(m => m.Health > 0).ToList();
    Console.WriteLine($"  Monsters Alive: {aliveMonsters.Count}/{state.Monsters.Length}");
    
    Console.WriteLine();
}

PrintState(currentState);

while (!game.IsTerminal(in currentState, out var terminalValue))
{
    // Handle chance nodes
    if (game.IsChanceNode(in currentState))
    {
        var prevStateForChance = currentState;
        currentState = game.SampleChance(in currentState, Random.Shared, out var logProb);
        
        // Print chance node outcomes
        if (prevStateForChance.ChanceNodeType == MonsterPhase)
        {
            Console.WriteLine($"  *** Monster phase (logProb: {logProb:F2}) ***");
            if (currentState.Monsters.Length > 0)
                Console.WriteLine($"  *** Monsters: {currentState.Monsters.Count(m => m.Health > 0)} alive ***");
            
            // Check for hero damage
            if (actionHistory.Count > 0)
            {
                var prevHeroes = actionHistory.Last().resultState.Heroes;
                for (int i = 0; i < currentState.Heroes.Length; i++)
                {
                    if (currentState.Heroes[i].Health < prevHeroes[i].Health)
                    {
                        int damage = prevHeroes[i].Health - currentState.Heroes[i].Health;
                        Console.WriteLine($"  *** Hero {i} took {damage} damage from monster! ***");
                    }
                }
            }
        }
        else if (prevStateForChance.ChanceNodeType == AttackOutcome)
        {
            // Check attack outcome
            int attackingHeroId = prevStateForChance.CurrentHeroIndex;
            var attackingHero = prevStateForChance.Heroes[attackingHeroId];
            
            // Find which monster was attacked
            bool foundDamage = false;
            for (int i = 0; i < prevStateForChance.Monsters.Length; i++)
            {
                var prevMonster = prevStateForChance.Monsters[i];
                var currMonster = currentState.Monsters[i];
                
                if (prevMonster.Health > currMonster.Health)
                {
                    int damage = prevMonster.Health - currMonster.Health;
                    string outcomeType = damage == 0 ? "MISS" : 
                                       damage == attackingHero.Damage ? "HIT" : "CRITICAL";
                    Console.WriteLine($"  *** {outcomeType}! Hero {attackingHeroId} deals {damage} damage (logProb: {logProb:F2}) ***");
                    
                    if (currMonster.Health <= 0)
                        Console.WriteLine("  *** Monster defeated! ***");
                    foundDamage = true;
                    break;
                }
            }
            
            if (!foundDamage)
            {
                // Miss - no damage dealt
                Console.WriteLine($"  *** MISS! Hero {attackingHeroId} deals 0 damage (logProb: {logProb:F2}) ***");
            }
        }
        
        PrintState(currentState);
        continue;
    }
    
    var (action, stats) = mcts.Search(currentState);
    var prevState = currentState;
    currentState = game.Step(in currentState, in action);
    actionHistory.Add((action, currentState));
    
    // Determine which hero acted
    int actingHeroId = prevState.CurrentHeroIndex;
    var actingHero = prevState.Heroes[actingHeroId];
    
    // Print every action
    Console.WriteLine($"Hero {actingHeroId} ({actingHero.Class}) action {3 - actingHero.ActionsRemaining}: {action}");
    
    if (action == SquadAction.Attack)
    {
        // Check if monster was killed
        int prevMonsterCount = prevState.Monsters.Count(m => m.Health > 0);
        int currMonsterCount = currentState.Monsters.Count(m => m.Health > 0);
        if (currMonsterCount < prevMonsterCount)
            Console.WriteLine("  *** Monster defeated! ***");
    }
    
    // Check for monster attacks (when all heroes have finished their turns)
    if (currentState.CurrentHeroIndex == 0 && prevState.CurrentHeroIndex != 0)
    {
        // Check for new monster spawn
        if (currentState.Monsters.Length > prevState.Monsters.Length)
        {
            Console.WriteLine($"  *** Monster spawned! (Total: {currentState.Monsters.Count(m => m.Health > 0)}) ***");
        }
        
        foreach (var hero2 in currentState.Heroes)
        {
            var prevHero = prevState.Heroes[hero2.Id];
            if (hero2.Health < prevHero.Health)
            {
                Console.WriteLine($"  *** Hero {hero2.Id} took {prevHero.Health - hero2.Health} damage from monster! ***");
            }
        }
    }
    
    Console.WriteLine();
    
    // Print state when a new hero's turn starts
    if (currentState.Heroes[currentState.CurrentHeroIndex].ActionsRemaining == 2)
    {
        PrintState(currentState);
    }
}

game.IsTerminal(in currentState, out var finalValue);

Console.WriteLine("=== Game Over ===");
Console.WriteLine($"Final Score: {finalValue}");
Console.WriteLine($"Turns Used: {currentState.TurnCount}/50");
Console.WriteLine($"Heroes Alive: {currentState.Heroes.Count(h => h.Health > 0)}/{currentState.Heroes.Length}");
Console.WriteLine($"Monsters Defeated: {currentState.Monsters.Count(m => m.Health <= 0)}/{currentState.Monsters.Length}");
Console.WriteLine($"All Heroes at Exit: {currentState.Heroes.Where(h => h.Health > 0).All(h => h.X == currentState.ExitX && h.Y == currentState.ExitY)}");
