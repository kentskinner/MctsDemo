using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Hero classes with different stats and abilities
/// </summary>
public enum HeroClass
{
    Warrior,    // High HP, high damage, low speed
    Rogue,      // Medium HP, medium damage, high speed
    Mage,       // Low HP, high damage, medium speed
    Elf         // Medium HP, medium damage, long range
}

/// <summary>
/// Represents a hero's current state
/// </summary>
public record Hero(
    int Id,
    HeroClass Class,
    int X,
    int Y,
    int Health,
    int MaxHealth,
    int Damage,
    int AttackRange,      // How far the hero can attack (1 for melee, 2+ for ranged)
    int ActionsRemaining  // Each hero gets 2 actions per turn
);

/// <summary>
/// Monster AI behavior type
/// </summary>
public enum MonsterBehavior
{
    Random,   // Moves randomly
    Hunter    // Moves toward nearest hero
}

/// <summary>
/// Represents a monster's current state
/// </summary>
public record Monster(
    int Id,
    int X,
    int Y,
    int Health,
    int MaxHealth,
    int Damage,
    MonsterBehavior Behavior
);

/// <summary>
/// Player actions in the tactical squad game
/// </summary>
public enum SquadAction
{
    MoveNorth,
    MoveSouth,
    MoveEast,
    MoveWest,
    Attack,      // Attack adjacent monster
    EndTurn      // End current hero's turn early
}

/// <summary>
/// Game state for tactical squad game
/// </summary>
public record GameState(
    int GridWidth,
    int GridHeight,
    ImmutableArray<Hero> Heroes,
    ImmutableArray<Monster> Monsters,
    ImmutableHashSet<(int X, int Y)> Walls,
    int ExitX,
    int ExitY,
    int CurrentHeroIndex,  // Which hero is currently acting
    int TurnCount,
    bool IsChance,  // True when at turn boundary or after attack
    ChanceType ChanceNodeType,  // What kind of chance event is pending
    double AccumulatedReward  // Running total of intermediate rewards for reward shaping
);

/// <summary>
/// Type of chance event
/// </summary>
public enum ChanceType
{
    None,           // Not a chance node
    MonsterSpawn,   // Monster spawn location choice
    MonsterPhase,   // Monster movement/attacks (after spawn resolved)
    AttackOutcome   // Hero attack outcome (hit/miss/damage variation)
}

/// <summary>
/// Tactical squad game where n heroes with different stats take turns.
/// Each hero gets 2 actions before the next hero acts.
/// Heroes must defeat monsters and reach the exit.
/// </summary>
public class TacticalSquadGame : IGameModel<GameState, SquadAction>
{
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly int _numHeroes;
    private readonly int _maxTurns;
    private readonly int _seed;

    public TacticalSquadGame(
        int gridWidth = 10,
        int gridHeight = 10,
        int numHeroes = 3,
        int maxTurns = 50,
        int? seed = null)
    {
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
        _numHeroes = numHeroes;
        _maxTurns = maxTurns;
        _seed = seed ?? Random.Shared.Next();
    }

    public GameState InitialState()
    {
        // Create heroes with different classes and stats
        var heroes = new List<Hero>();
        var heroClasses = new[] { HeroClass.Warrior, HeroClass.Rogue, HeroClass.Mage };
        
        for (int i = 0; i < _numHeroes; i++)
        {
            var heroClass = heroClasses[i % heroClasses.Length];
            var (maxHp, damage, attackRange) = heroClass switch
            {
                HeroClass.Warrior => (15, 4, 1),  // Melee warrior
                HeroClass.Rogue => (10, 3, 2),    // Rogue with bow - can attack at range 2
                HeroClass.Mage => (8, 5, 2),      // Mage with spells - can attack at range 2
                _ => (10, 3, 1)
            };

            heroes.Add(new Hero(
                Id: i,
                Class: heroClass,
                X: 1 + i,  // Start heroes in a row
                Y: _gridHeight - 2,
                Health: maxHp,
                MaxHealth: maxHp,
                Damage: damage,
                AttackRange: attackRange,
                ActionsRemaining: 2
            ));
        }

        // Create walls - simple pattern with corridors
        var walls = new HashSet<(int X, int Y)>();
        
        // Add some horizontal walls with gaps
        for (int x = 0; x < _gridWidth; x++)
        {
            // Top horizontal wall with gap in middle
            if (x != _gridWidth / 2 && x != _gridWidth / 2 + 1)
                walls.Add((x, 2));
            
            // Middle horizontal wall with gaps
            if (x < 2 || x > _gridWidth - 3)
                walls.Add((x, _gridHeight / 2));
        }
        
        // Add some vertical walls
        for (int y = 3; y < _gridHeight - 2; y++)
        {
            // Left vertical wall with gaps
            if (y != _gridHeight / 2 && y != _gridHeight / 2 + 1)
                walls.Add((2, y));
            
            // Right vertical wall with gaps
            if (y != _gridHeight / 2 - 1 && y != _gridHeight / 2)
            {
                int wallX = _gridWidth - 3;
                walls.Add((wallX, y));
            }
        }

        // Create monsters - start with none, they spawn each turn!
        var monsters = new List<Monster>();

        return new GameState(
            GridWidth: _gridWidth,
            GridHeight: _gridHeight,
            Heroes: heroes.ToImmutableArray(),
            Monsters: monsters.ToImmutableArray(),
            Walls: walls.ToImmutableHashSet(),
            ExitX: _gridWidth / 2,
            ExitY: 0,
            CurrentHeroIndex: 0,
            TurnCount: 0,
            IsChance: false,
            ChanceNodeType: ChanceType.None,
            AccumulatedReward: 0.0
        );
    }

    public IEnumerable<SquadAction> LegalActions(GameState state)
    {
        if (IsTerminal(in state, out _))
            yield break;

        var hero = state.Heroes[state.CurrentHeroIndex];
        
        if (hero.Health <= 0)
        {
            // Dead heroes can only end turn
            yield return SquadAction.EndTurn;
            yield break;
        }

        // Movement actions
        if (hero.Y > 0)
            yield return SquadAction.MoveNorth;
        if (hero.Y < state.GridHeight - 1)
            yield return SquadAction.MoveSouth;
        if (hero.X < state.GridWidth - 1)
            yield return SquadAction.MoveEast;
        if (hero.X > 0)
            yield return SquadAction.MoveWest;

        // Attack action if there's a monster within attack range
        if (HasMonsterInRange(state, hero))
            yield return SquadAction.Attack;

        // Can always end turn early
        yield return SquadAction.EndTurn;
    }

    private bool HasMonsterInRange(GameState state, Hero hero)
    {
        return state.Monsters.Any(m => m.Health > 0 &&
            Math.Abs(m.X - hero.X) + Math.Abs(m.Y - hero.Y) <= hero.AttackRange);
    }

    private double CalculateMoveReward(GameState oldState, GameState newState, SquadAction action)
    {
        double reward = 0.0;

        // Reward for moving closer to exit
        var hero = newState.Heroes[newState.CurrentHeroIndex];
        var oldHero = oldState.Heroes[oldState.CurrentHeroIndex];

        int oldDistToExit = Math.Abs(oldHero.X - oldState.ExitX) + Math.Abs(oldHero.Y - oldState.ExitY);
        int newDistToExit = Math.Abs(hero.X - newState.ExitX) + Math.Abs(hero.Y - newState.ExitY);

        if (newDistToExit < oldDistToExit)
            reward += 2.0;  // Significant reward for moving closer to exit
        else if (newDistToExit > oldDistToExit)
            reward -= 1.5;  // Penalty for moving away from exit

        // Small penalty for passing time (encourages faster solutions)
        reward -= 0.05;

        return reward;
    }

    public GameState Step(in GameState state, in SquadAction action)
    {
        // Should not call Step on a chance node
        if (state.IsChance)
            throw new InvalidOperationException("Cannot call Step on a chance node. Use SampleChance instead.");
        
        var hero = state.Heroes[state.CurrentHeroIndex];
        var newHeroes = state.Heroes;
        var newMonsters = state.Monsters;
        int newHeroIndex = state.CurrentHeroIndex;
        int newTurnCount = state.TurnCount;

        // Process action
        switch (action)
        {
            case SquadAction.MoveNorth:
            case SquadAction.MoveSouth:
            case SquadAction.MoveEast:
            case SquadAction.MoveWest:
                newHeroes = MoveHero(state, hero, action);
                break;

            case SquadAction.Attack:
                // Attack creates a chance node - don't resolve it here
                // Just consume the action and transition to attack chance node
                newHeroes = newHeroes.SetItem(state.CurrentHeroIndex,
                    newHeroes[state.CurrentHeroIndex] with { ActionsRemaining = hero.ActionsRemaining - 1 });
                
                return state with
                {
                    Heroes = newHeroes,
                    IsChance = true,
                    ChanceNodeType = ChanceType.AttackOutcome
                };

            case SquadAction.EndTurn:
                // Just consume remaining actions
                break;
        }

        // Consume one action
        int actionsLeft = action == SquadAction.EndTurn ? 0 : hero.ActionsRemaining - 1;
        
        // Update hero's actions remaining
        newHeroes = newHeroes.SetItem(state.CurrentHeroIndex,
            newHeroes[state.CurrentHeroIndex] with { ActionsRemaining = actionsLeft });

        // If hero is out of actions, move to next hero
        if (actionsLeft <= 0)
        {
            newHeroIndex = (state.CurrentHeroIndex + 1) % state.Heroes.Length;
            
            // If we've cycled back to first hero, it's a new turn - transition to chance node
            if (newHeroIndex == 0)
            {
                newTurnCount++;
                
                // Reset first hero's actions
                newHeroes = newHeroes.SetItem(0, newHeroes[0] with { ActionsRemaining = 2 });
                
                return state with
                {
                    Heroes = newHeroes,
                    Monsters = newMonsters,
                    CurrentHeroIndex = 0,
                    TurnCount = newTurnCount,
                    IsChance = true,  // Transition to chance node for monster spawn
                    ChanceNodeType = ChanceType.MonsterSpawn
                };
            }
            
            // Reset new hero's actions
            newHeroes = newHeroes.SetItem(newHeroIndex,
                newHeroes[newHeroIndex] with { ActionsRemaining = 2 });
        }

        var newState = state with
        {
            Heroes = newHeroes,
            Monsters = newMonsters,
            CurrentHeroIndex = newHeroIndex,
            TurnCount = newTurnCount,
            IsChance = false,
            ChanceNodeType = ChanceType.None
        };

        // Add reward shaping for moves
        double moveReward = CalculateMoveReward(state, newState, action);

        return newState with
        {
            AccumulatedReward = state.AccumulatedReward + moveReward
        };
    }

    private ImmutableArray<Hero> MoveHero(GameState state, Hero hero, SquadAction action)
    {
        int newX = hero.X;
        int newY = hero.Y;

        switch (action)
        {
            case SquadAction.MoveNorth: newY--; break;
            case SquadAction.MoveSouth: newY++; break;
            case SquadAction.MoveEast: newX++; break;
            case SquadAction.MoveWest: newX--; break;
        }

        // Check bounds
        if (newX < 0 || newX >= state.GridWidth || newY < 0 || newY >= state.GridHeight)
            return state.Heroes;

        // Don't move into walls
        if (state.Walls.Contains((newX, newY)))
            return state.Heroes;

        // Don't move into another hero
        if (state.Heroes.Any(h => h.Id != hero.Id && h.X == newX && h.Y == newY))
            return state.Heroes;

        // Update hero position
        return state.Heroes.SetItem(state.CurrentHeroIndex,
            hero with { X = newX, Y = newY });
    }

    private ImmutableArray<Hero> ProcessMonsterAttacks(ImmutableArray<Hero> heroes, ImmutableArray<Monster> monsters)
    {
        var newHeroes = heroes;

        foreach (var monster in monsters.Where(m => m.Health > 0))
        {
            // Find adjacent hero
            var targetHero = newHeroes
                .Where(h => h.Health > 0 &&
                    Math.Abs(h.X - monster.X) + Math.Abs(h.Y - monster.Y) == 1)
                .OrderBy(h => h.Health)  // Target weakest
                .FirstOrDefault();

            if (targetHero != null)
            {
                int newHealth = targetHero.Health - monster.Damage;
                newHeroes = newHeroes.SetItem(targetHero.Id,
                    targetHero with { Health = Math.Max(0, newHealth) });
            }
        }

        return newHeroes;
    }

    public bool IsChanceNode(in GameState state)
    {
        return state.IsChance;
    }

    public IEnumerable<(GameState outcome, double probability)> ChanceOutcomes(GameState state)
    {
        if (!state.IsChance)
            yield break;

        if (state.ChanceNodeType == ChanceType.AttackOutcome)
        {
            // Enumerate all 3 attack outcomes
            var hero = state.Heroes[state.CurrentHeroIndex];

            // Find closest monster within attack range (prefer closer targets)
            var monster = state.Monsters
                .Where(m => m.Health > 0 &&
                    Math.Abs(m.X - hero.X) + Math.Abs(m.Y - hero.Y) <= hero.AttackRange)
                .OrderBy(m => Math.Abs(m.X - hero.X) + Math.Abs(m.Y - hero.Y))  // Prefer closer
                .ThenBy(m => m.Health)  // Then prefer weaker
                .FirstOrDefault();

            if (monster == null)
            {
                // No monster to attack - just return to decision state
                yield return (state with { IsChance = false, ChanceNodeType = ChanceType.None }, 1.0);
                yield break;
            }

            // Generate all three outcomes
            yield return (ApplyAttackDamage(state, hero, monster, 0), 0.10);           // Miss
            yield return (ApplyAttackDamage(state, hero, monster, hero.Damage), 0.70);  // Hit
            yield return (ApplyAttackDamage(state, hero, monster, hero.Damage * 2), 0.20); // Crit
        }
        else if (state.ChanceNodeType == ChanceType.MonsterSpawn)
        {
            // Enumerate spawn locations (3 fixed locations + no spawn option)
            var spawnLocations = GetSpawnLocations();

            if (state.Monsters.Length >= 8)
            {
                // At cap - no spawn, transition to monster phase
                yield return (state with
                {
                    IsChance = true,
                    ChanceNodeType = ChanceType.MonsterPhase
                }, 1.0);
                yield break;
            }

            // Check which spawn locations are available
            var availableLocations = spawnLocations
                .Where(loc => !state.Heroes.Any(h => h.X == loc.X && h.Y == loc.Y) &&
                             !state.Monsters.Any(m => m.X == loc.X && m.Y == loc.Y))
                .ToList();

            if (availableLocations.Count == 0)
            {
                // No valid spawn locations - transition to monster phase without spawning
                yield return (state with
                {
                    IsChance = true,
                    ChanceNodeType = ChanceType.MonsterPhase
                }, 1.0);
                yield break;
            }

            // Each available location has equal probability
            double probPerLocation = 1.0 / (availableLocations.Count + 1);  // +1 for no-spawn option

            // Option 1: Don't spawn (20% base chance, adjusted by available locations)
            yield return (state with
            {
                IsChance = true,
                ChanceNodeType = ChanceType.MonsterPhase
            }, probPerLocation);

            // Options 2-4: Spawn at each available location
            foreach (var loc in availableLocations)
            {
                // Randomly choose between Hunter (60%) and Random (40%) behavior
                var behavior = _seed % 2 == 0 ? MonsterBehavior.Hunter : MonsterBehavior.Random;

                var newMonster = new Monster(
                    Id: state.Monsters.Length,
                    X: loc.X,
                    Y: loc.Y,
                    Health: 6,
                    MaxHealth: 6,
                    Damage: 2,
                    Behavior: behavior
                );

                yield return (state with
                {
                    Monsters = state.Monsters.Add(newMonster),
                    IsChance = true,
                    ChanceNodeType = ChanceType.MonsterPhase
                }, probPerLocation);
            }
        }
        else if (state.ChanceNodeType == ChanceType.MonsterPhase)
        {
            // Monster phase: enumerate all possible random monster movements
            // This can be exponential with many random monsters
            var outcomes = EnumerateMonsterMovements(state, 0, state.Monsters, state.Heroes);
            foreach (var (outcome, prob) in outcomes)
            {
                yield return (outcome, prob);
            }
        }
        else
        {
            // Unknown chance type
            yield break;
        }
    }

    private List<(GameState state, double probability)> EnumerateMonsterMovements(
        GameState state,
        int monsterIndex,
        ImmutableArray<Monster> currentMonsters,
        ImmutableArray<Hero> heroes)
    {
        // Base case: all monsters moved
        if (monsterIndex >= currentMonsters.Length)
        {
            var finalHeroes = ProcessMonsterAttacks(heroes, currentMonsters);
            var finalState = state with
            {
                Heroes = finalHeroes,
                Monsters = currentMonsters,
                IsChance = false,
                ChanceNodeType = ChanceType.None
            };
            return new List<(GameState, double)> { (finalState, 1.0) };
        }

        var monster = currentMonsters[monsterIndex];
        if (monster.Health <= 0)
        {
            // Dead monster, skip
            return EnumerateMonsterMovements(state, monsterIndex + 1, currentMonsters, heroes);
        }

        if (monster.Behavior == MonsterBehavior.Hunter)
        {
            // Deterministic: single outcome
            var newMonsters = MoveMonsterTowardNearestHero(state, currentMonsters, monsterIndex);
            return EnumerateMonsterMovements(state, monsterIndex + 1, newMonsters, heroes);
        }
        else
        {
            // Random: enumerate all possible moves
            var possibleMoves = GetPossibleMoves(state, monster, currentMonsters);
            var results = new List<(GameState, double)>();

            double probPerMove = 1.0 / possibleMoves.Count;
            foreach (var (newX, newY) in possibleMoves)
            {
                var movedMonsters = currentMonsters.SetItem(monsterIndex,
                    monster with { X = newX, Y = newY });
                var subResults = EnumerateMonsterMovements(
                    state, monsterIndex + 1, movedMonsters, heroes);

                // Multiply probabilities
                foreach (var (subState, subProb) in subResults)
                {
                    results.Add((subState, probPerMove * subProb));
                }
            }
            return results;
        }
    }

    private List<(int X, int Y)> GetPossibleMoves(GameState state, Monster monster, ImmutableArray<Monster> monsters)
    {
        var possibleMoves = new List<(int X, int Y)>();

        // Can stay in place
        possibleMoves.Add((monster.X, monster.Y));

        // Try all four directions
        var directions = new[] { (0, -1), (0, 1), (1, 0), (-1, 0) };
        foreach (var (dirX, dirY) in directions)
        {
            int newX = monster.X + dirX;
            int newY = monster.Y + dirY;

            if (newX >= 0 && newX < state.GridWidth &&
                newY >= 0 && newY < state.GridHeight &&
                !state.Walls.Contains((newX, newY)) &&
                !monsters.Any(m => m.Id != monster.Id && m.X == newX && m.Y == newY))
            {
                possibleMoves.Add((newX, newY));
            }
        }

        return possibleMoves;
    }

    private List<(int X, int Y)> GetSpawnLocations()
    {
        // Fixed spawn locations in corners and center-ish
        return new List<(int X, int Y)>
        {
            (1, 2),  // Top-left area
            (5, 2),  // Top-right area  
            (3, 5)   // Bottom-center area
        };
    }

    private GameState ApplyAttackDamage(GameState state, Hero hero, Monster monster, int damage)
    {
        var newHeroes = state.Heroes;
        var newMonsters = state.Monsters;

        // Apply damage to monster
        int newHealth = monster.Health - damage;
        newMonsters = state.Monsters.SetItem(monster.Id,
            monster with { Health = Math.Max(0, newHealth) });

        // Handle hero turn advancement
        int actionsLeft = hero.ActionsRemaining;
        int newHeroIndex = state.CurrentHeroIndex;
        int newTurnCount = state.TurnCount;
        bool isChance = false;
        var chanceType = ChanceType.None;

        if (actionsLeft <= 0)
        {
            newHeroIndex = (state.CurrentHeroIndex + 1) % state.Heroes.Length;

            if (newHeroIndex == 0)
            {
                // New turn - transition to monster spawn
                newTurnCount++;
                newHeroes = newHeroes.SetItem(0, newHeroes[0] with { ActionsRemaining = 2 });
                isChance = true;
                chanceType = ChanceType.MonsterSpawn;
            }
            else
            {
                // Reset new hero's actions
                newHeroes = newHeroes.SetItem(newHeroIndex,
                    newHeroes[newHeroIndex] with { ActionsRemaining = 2 });
            }
        }

        return state with
        {
            Heroes = newHeroes,
            Monsters = newMonsters,
            CurrentHeroIndex = newHeroIndex,
            TurnCount = newTurnCount,
            IsChance = isChance,
            ChanceNodeType = chanceType
        };
    }

    private ImmutableArray<Monster> MoveMonsterTowardNearestHero(
        GameState state,
        ImmutableArray<Monster> monsters,
        int monsterIndex)
    {
        var monster = monsters[monsterIndex];

        // Find nearest living hero
        var nearestHero = state.Heroes
            .Where(h => h.Health > 0)
            .OrderBy(h => Math.Abs(h.X - monster.X) + Math.Abs(h.Y - monster.Y))
            .FirstOrDefault();

        if (nearestHero == null)
            return monsters;  // No living heroes, don't move

        // Calculate direction toward hero
        int dx = nearestHero.X - monster.X;
        int dy = nearestHero.Y - monster.Y;

        // Try to move in the direction that reduces distance the most
        var possibleMoves = new List<(int X, int Y, int distance)>();

        // Try all four directions
        var directions = new[] { (0, -1), (0, 1), (1, 0), (-1, 0) };

        foreach (var (dirX, dirY) in directions)
        {
            int newX = monster.X + dirX;
            int newY = monster.Y + dirY;

            if (newX >= 0 && newX < state.GridWidth &&
                newY >= 0 && newY < state.GridHeight &&
                !state.Walls.Contains((newX, newY)) &&
                !monsters.Any(m => m.Id != monster.Id && m.X == newX && m.Y == newY))
            {
                int distance = Math.Abs(nearestHero.X - newX) + Math.Abs(nearestHero.Y - newY);
                possibleMoves.Add((newX, newY, distance));
            }
        }

        if (possibleMoves.Count == 0)
            return monsters;  // Can't move, stay in place

        // Pick the move that gets closest to the hero
        var bestMove = possibleMoves.OrderBy(m => m.distance).First();

        var updatedMonsters = monsters.SetItem(monsterIndex,
            monster with { X = bestMove.X, Y = bestMove.Y });

        return updatedMonsters;
    }

    private (ImmutableArray<Monster>, double logProb) MoveMonsterRandomly(
        GameState state,
        ImmutableArray<Monster> monsters,
        int monsterIndex,
        Random rng)
    {
        var monster = monsters[monsterIndex];

        // Find all valid moves (including staying in place)
        var possibleMoves = new List<(int X, int Y)>();

        // Add current position (can stay in place)
        possibleMoves.Add((monster.X, monster.Y));

        // Try all four directions
        var directions = new[] { (0, -1), (0, 1), (1, 0), (-1, 0) };

        foreach (var (dirX, dirY) in directions)
        {
            int newX = monster.X + dirX;
            int newY = monster.Y + dirY;

            if (newX >= 0 && newX < state.GridWidth &&
                newY >= 0 && newY < state.GridHeight &&
                !state.Walls.Contains((newX, newY)) &&
                !monsters.Any(m => m.Id != monster.Id && m.X == newX && m.Y == newY))
            {
                possibleMoves.Add((newX, newY));
            }
        }

        // Pick a random move uniformly
        int index = rng.Next(possibleMoves.Count);
        var move = possibleMoves[index];
        double logProb = -Math.Log(possibleMoves.Count);

        var updatedMonsters = monsters.SetItem(monsterIndex,
            monster with { X = move.X, Y = move.Y });

        return (updatedMonsters, logProb);
    }

    public bool IsTerminal(in GameState state, out double terminalValue)
    {
        // Lose if all heroes are dead
        if (state.Heroes.All(h => h.Health <= 0))
        {
            terminalValue = -100;
            return true;
        }

        // Lose if out of turns
        if (state.TurnCount >= _maxTurns)
        {
            terminalValue = -50;
            return true;
        }

        // Win if all living heroes reach the exit
        int exitX = state.ExitX;
        int exitY = state.ExitY;
        var heroes = state.Heroes;
        var monsters = state.Monsters;
        
        if (heroes.Where(h => h.Health > 0).All(h => h.X == exitX && h.Y == exitY))
        {
            int reward = 100;
            
            // Bonus for surviving heroes
            reward += heroes.Count(h => h.Health > 0) * 20;
            
            // Bonus for total health remaining
            reward += heroes.Sum(h => h.Health);
            
            // Bonus for defeating monsters
            reward += monsters.Count(m => m.Health <= 0) * 10;
            
            // Penalty for turns used
            reward -= state.TurnCount;

            terminalValue = reward;
            return true;
        }

        terminalValue = 0;
        return false;
    }

    /// <summary>
    /// Heuristic value for non-terminal states (used during rollouts when depth limit is reached)
    /// </summary>
    public double HeuristicValue(in GameState state)
    {
        // Return accumulated reward plus distance-to-exit heuristic
        double value = state.AccumulatedReward;

        // Add heuristic based on closest hero to exit
        int exitX = state.ExitX;
        int exitY = state.ExitY;
        var livingHeroes = state.Heroes.Where(h => h.Health > 0).ToList();
        if (livingHeroes.Any())
        {
            int minDist = livingHeroes.Min(h => Math.Abs(h.X - exitX) + Math.Abs(h.Y - exitY));
            value -= minDist * 0.1;  // Penalty for being far from exit
        }

        return value;
    }
}
