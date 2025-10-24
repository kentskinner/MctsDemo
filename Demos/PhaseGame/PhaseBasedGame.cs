using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Phase-based tactical game with simpler combat:
/// - Enemies die in one hit
/// - Heroes have 2 states: Healthy, Injured, Dead (2 hits to kill)
/// - Turn phases: 1) Monster spawn, 2) Heroes act, 3) Monsters act
/// - Always enumerate chance outcomes
/// </summary>

public enum HeroStatus { Healthy, Injured, Dead }

public enum Phase { MonsterSpawn, HeroAction, MonsterAction }

public record PhaseHero(
    int Index,
    HeroClass Class,
    int X,
    int Y,
    HeroStatus Status,
    int AttackScore,  // Target number on 2d6 to hit (2-12)
    int Range,
    int ActionsRemaining
);

public enum MonsterType { Random, Chaser }

public record PhaseMonster(
    int Index,
    int X,
    int Y,
    bool IsAlive,
    MonsterType Type
);

/// <summary>
/// Represents a pending attack that needs to be resolved as a chance node
/// </summary>
public record PendingAttack(
    int HeroIndex,
    int MonsterIndex,
    int AttackScore
);

public record PhaseGameState(
    ImmutableList<PhaseHero> Heroes,
    ImmutableList<PhaseMonster> Monsters,
    int TurnCount,
    int ActiveHeroIndex,
    Phase CurrentPhase,
    int ExitX,
    int ExitY,
    double AccumulatedReward,
    ImmutableHashSet<(int, int)> Walls,
    PendingAttack? AttackResolution  // If not null, this is a chance node for attack resolution
);

public class PhaseBasedGame : IGameModel<PhaseGameState, SquadAction>
{
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly int _maxTurns;
    private readonly Random _setupRng;

    public PhaseBasedGame(int gridWidth = 5, int gridHeight = 5, int numHeroes = 2, int maxTurns = 20, int? seed = null)
    {
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
        _maxTurns = maxTurns;
        _setupRng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public PhaseGameState InitialState()
    {
        // Create heroes - start in top-left corner
        // Attack scores: Warrior=7 (58%), Rogue=8 (42%), Elf=9 (28%)
        var heroes = ImmutableList.Create(
            new PhaseHero(0, HeroClass.Warrior, 0, 0, HeroStatus.Healthy, 7, 1, 2),
            new PhaseHero(1, HeroClass.Rogue, 1, 0, HeroStatus.Healthy, 8, 2, 2),
            new PhaseHero(2, HeroClass.Elf, 0, 1, HeroStatus.Healthy, 9, 3, 2)
        );

        // Create walls - 5x5 grid with two paths to exit
        // Layout creates a central obstacle that forces heroes to choose left or right path
        var walls = ImmutableHashSet.CreateRange(new[]
        {
            (2, 1), (2, 2), (2, 3)  // Central vertical wall with gaps at top and bottom
        });

        return new PhaseGameState(
            Heroes: heroes,
            Monsters: ImmutableList<PhaseMonster>.Empty,
            TurnCount: 0,
            ActiveHeroIndex: -1,
            CurrentPhase: Phase.MonsterSpawn,
            ExitX: 4,
            ExitY: 4,
            AccumulatedReward: 0.0,
            Walls: walls,
            AttackResolution: null
        );
    }

    public bool IsChanceNode(in PhaseGameState state)
    {
        // Chance nodes: attack resolution, monster spawning, and monster movement
        return state.AttackResolution != null ||
               state.CurrentPhase == Phase.MonsterSpawn ||
               state.CurrentPhase == Phase.MonsterAction;
    }

    public IEnumerable<(PhaseGameState outcome, double probability)> ChanceOutcomes(PhaseGameState state)
    {
        // Attack resolution chance node
        if (state.AttackResolution != null)
        {
            var attack = state.AttackResolution;
            double hitChance = GetHitChance(attack.AttackScore);
            
            // Hit outcome: monster dies, get reward
            var monster = state.Monsters[attack.MonsterIndex];
            var killedMonster = monster with { IsAlive = false };
            var hitState = state with
            {
                Monsters = state.Monsters.SetItem(attack.MonsterIndex, killedMonster),
                AccumulatedReward = state.AccumulatedReward + 5.0,
                AttackResolution = null
            };
            yield return (hitState, hitChance);
            
            // Miss outcome: nothing happens
            var missState = state with { AttackResolution = null };
            yield return (missState, 1.0 - hitChance);
            
            yield break;
        }
        
        if (state.CurrentPhase == Phase.MonsterSpawn)
        {
            // Enumerate monster spawn outcomes
            var outcomes = EnumerateMonsterSpawnOutcomes(state);
            double totalProb = 0.0;
            foreach (var (outcome, prob) in outcomes)
            {
                totalProb += prob;
                yield return (outcome, prob);
            }
            
            // Ensure probabilities sum to 1.0
            if (totalProb < 0.99 || totalProb > 1.01)
                throw new InvalidOperationException($"Monster spawn probabilities sum to {totalProb}, not 1.0");
        }
        else if (state.CurrentPhase == Phase.MonsterAction)
        {
            // Enumerate all possible monster movements
            var outcomes = EnumerateMonsterMovementOutcomes(state);
            foreach (var outcome in outcomes)
            {
                yield return outcome;
            }
        }
    }

    private IEnumerable<(PhaseGameState, double)> EnumerateMonsterSpawnOutcomes(PhaseGameState state)
    {
        // 40% chance no spawn, 60% chance to spawn
        var noSpawn = state with 
        { 
            CurrentPhase = Phase.HeroAction,
            ActiveHeroIndex = GetNextActiveHeroIndex(state, -1)
        };
        yield return (noSpawn, 0.4);

        // 60% chance to spawn at a random valid location
        // Split between Random (60%) and Chaser (40%) types
        var validSpawnLocations = GetValidSpawnLocations(state).ToList();
        if (validSpawnLocations.Count > 0)
        {
            double probPerLocation = 0.6 / validSpawnLocations.Count;
            double probRandom = probPerLocation * 0.6;  // 60% of spawns are Random
            double probChaser = probPerLocation * 0.4;  // 40% of spawns are Chaser

            foreach (var (x, y) in validSpawnLocations)
            {
                // Random monster
                var randomMonster = new PhaseMonster(state.Monsters.Count, x, y, true, MonsterType.Random);
                var randomState = state with
                {
                    Monsters = state.Monsters.Add(randomMonster),
                    CurrentPhase = Phase.HeroAction,
                    ActiveHeroIndex = GetNextActiveHeroIndex(state, -1)
                };
                yield return (randomState, probRandom);

                // Chaser monster
                var chaserMonster = new PhaseMonster(state.Monsters.Count, x, y, true, MonsterType.Chaser);
                var chaserState = state with
                {
                    Monsters = state.Monsters.Add(chaserMonster),
                    CurrentPhase = Phase.HeroAction,
                    ActiveHeroIndex = GetNextActiveHeroIndex(state, -1)
                };
                yield return (chaserState, probChaser);
            }
        }
        else
        {
            // No valid spawn locations, just no spawn
            yield return (noSpawn, 0.6);
        }
    }

    private IEnumerable<(int x, int y)> GetValidSpawnLocations(PhaseGameState state)
    {
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int y = 0; y < _gridHeight; y++)
            {
                if (state.Walls.Contains((x, y))) continue;
                if (state.Heroes.Any(h => h.Status != HeroStatus.Dead && h.X == x && h.Y == y)) continue;
                if (state.Monsters.Any(m => m.IsAlive && m.X == x && m.Y == y)) continue;
                if (x == state.ExitX && y == state.ExitY) continue;
                
                // Only spawn in upper half of map
                if (y < _gridHeight / 2)
                    yield return (x, y);
            }
        }
    }

    private IEnumerable<(PhaseGameState outcome, double probability)> EnumerateMonsterMovementOutcomes(PhaseGameState state)
    {
        var aliveMonsters = state.Monsters.Where(m => m.IsAlive).ToList();
        if (aliveMonsters.Count == 0)
        {
            // No monsters, advance to next turn
            yield return (AdvanceToNextTurn(state), 1.0);
            yield break;
        }

        // To avoid combinatorial explosion, we enumerate moves for only ONE randomly chosen monster
        // Each monster has equal probability of being the one that moves
        double probPerMonster = 1.0 / aliveMonsters.Count;

        foreach (var monster in aliveMonsters)
        {
            var possibleMoves = GetMonsterMoves(state, monster);
            double probPerMove = probPerMonster / possibleMoves.Count;

            foreach (var (newX, newY) in possibleMoves)
            {
                var movedState = MoveMonster(state, monster.Index, newX, newY);
                var finalState = AdvanceToNextTurn(movedState);
                yield return (finalState, probPerMove);
            }
        }
    }

    private IEnumerable<PhaseGameState> EnumerateAllMonsterMoves(PhaseGameState state, List<PhaseMonster> monstersToMove, int monsterIndex)
    {
        if (monsterIndex >= monstersToMove.Count)
        {
            // All monsters moved, advance to next turn
            yield return AdvanceToNextTurn(state);
            yield break;
        }

        var monster = monstersToMove[monsterIndex];
        var possibleMoves = GetMonsterMoves(state, monster);

        foreach (var (newX, newY) in possibleMoves)
        {
            var movedState = MoveMonster(state, monster.Index, newX, newY);
            
            // Recursively enumerate remaining monsters
            foreach (var finalState in EnumerateAllMonsterMoves(movedState, monstersToMove, monsterIndex + 1))
            {
                yield return finalState;
            }
        }
    }

    private List<(int x, int y)> GetMonsterMoves(PhaseGameState state, PhaseMonster monster)
    {
        var moves = new List<(int x, int y)>();
        
        // Check if adjacent to any hero - if so, attack instead of move
        var adjacentHeroes = state.Heroes
            .Where(h => h.Status != HeroStatus.Dead)
            .Where(h => Math.Abs(h.X - monster.X) + Math.Abs(h.Y - monster.Y) == 1)
            .ToList();

        if (adjacentHeroes.Any())
        {
            // Monster attacks - stays in place
            moves.Add((monster.X, monster.Y));
            return moves;
        }

        // Get all valid adjacent positions
        var directions = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
        foreach (var (dx, dy) in directions)
        {
            int newX = monster.X + dx;
            int newY = monster.Y + dy;

            if (!IsValidPosition(state, newX, newY)) continue;
            if (state.Monsters.Any(m => m.IsAlive && m.X == newX && m.Y == newY && m.Index != monster.Index)) continue;

            moves.Add((newX, newY));
        }

        if (moves.Count == 0)
        {
            moves.Add((monster.X, monster.Y)); // Stay in place
            return moves;
        }

        // Chaser monsters: prefer moves toward nearest hero
        if (monster.Type == MonsterType.Chaser)
        {
            var nearestHero = state.Heroes
                .Where(h => h.Status != HeroStatus.Dead)
                .OrderBy(h => Math.Abs(h.X - monster.X) + Math.Abs(h.Y - monster.Y))
                .FirstOrDefault();

            if (nearestHero != null)
            {
                // Find moves that get closer to hero
                int currentDist = Math.Abs(monster.X - nearestHero.X) + Math.Abs(monster.Y - nearestHero.Y);
                var betterMoves = moves
                    .Where(m => Math.Abs(m.x - nearestHero.X) + Math.Abs(m.y - nearestHero.Y) < currentDist)
                    .ToList();

                if (betterMoves.Any())
                    return betterMoves;
            }
        }

        // Random monsters or Chaser with no better move: return all valid moves
        return moves;
    }

    private PhaseGameState MoveMonster(PhaseGameState state, int monsterIndex, int newX, int newY)
    {
        var monster = state.Monsters[monsterIndex];
        var movedMonster = monster with { X = newX, Y = newY };
        var newMonsters = state.Monsters.SetItem(monsterIndex, movedMonster);
        var newState = state with { Monsters = newMonsters };

        // Check if monster is adjacent to hero - deal damage
        var adjacentHeroes = newState.Heroes
            .Where(h => h.Status != HeroStatus.Dead)
            .Where(h => Math.Abs(h.X - newX) + Math.Abs(h.Y - newY) == 1)
            .ToList();

        foreach (var hero in adjacentHeroes)
        {
            var newStatus = hero.Status == HeroStatus.Healthy ? HeroStatus.Injured : HeroStatus.Dead;
            var updatedHero = hero with { Status = newStatus };
            newState = newState with { Heroes = newState.Heroes.SetItem(hero.Index, updatedHero) };
            
            // Apply damage penalty
            double damagePenalty = newStatus == HeroStatus.Dead ? -10.0 : -5.0;
            newState = newState with { AccumulatedReward = newState.AccumulatedReward + damagePenalty };
        }

        return newState;
    }

    private PhaseGameState AdvanceToNextTurn(PhaseGameState state)
    {
        return state with
        {
            TurnCount = state.TurnCount + 1,
            CurrentPhase = Phase.MonsterSpawn,
            ActiveHeroIndex = -1,
            Heroes = state.Heroes.Select(h => h with { ActionsRemaining = 2 }).ToImmutableList()
        };
    }

    private bool IsValidPosition(PhaseGameState state, int x, int y)
    {
        return x >= 0 && x < _gridWidth && y >= 0 && y < _gridHeight && !state.Walls.Contains((x, y));
    }

    private int GetNextActiveHeroIndex(PhaseGameState state, int currentIndex)
    {
        for (int i = currentIndex + 1; i < state.Heroes.Count; i++)
        {
            var hero = state.Heroes[i];
            if (hero.Status != HeroStatus.Dead && hero.ActionsRemaining > 0)
                return i;
        }
        return -1;
    }

    public IEnumerable<SquadAction> LegalActions(PhaseGameState state)
    {
        if (state.CurrentPhase != Phase.HeroAction || state.ActiveHeroIndex == -1)
            yield break;

        var hero = state.Heroes[state.ActiveHeroIndex];
        if (hero.Status == HeroStatus.Dead || hero.ActionsRemaining == 0)
            yield break;

        // Movement actions
        if (hero.Y > 0 && IsValidPosition(state, hero.X, hero.Y - 1))
            yield return SquadAction.MoveNorth;
        if (hero.Y < _gridHeight - 1 && IsValidPosition(state, hero.X, hero.Y + 1))
            yield return SquadAction.MoveSouth;
        if (hero.X < _gridWidth - 1 && IsValidPosition(state, hero.X + 1, hero.Y))
            yield return SquadAction.MoveEast;
        if (hero.X > 0 && IsValidPosition(state, hero.X - 1, hero.Y))
            yield return SquadAction.MoveWest;

        // Attack action if monster in range
        var monstersInRange = state.Monsters
            .Where(m => m.IsAlive)
            .Where(m => Math.Abs(m.X - hero.X) + Math.Abs(m.Y - hero.Y) <= hero.Range)
            .Any();

        if (monstersInRange)
            yield return SquadAction.Attack;

        yield return SquadAction.EndTurn;
    }

    public PhaseGameState Step(in PhaseGameState state, in SquadAction action)
    {
        if (state.CurrentPhase != Phase.HeroAction || state.ActiveHeroIndex == -1)
            return state;

        var hero = state.Heroes[state.ActiveHeroIndex];
        var newState = state;
        double moveReward = -0.05; // Time penalty

        switch (action)
        {
            case SquadAction.MoveNorth:
            case SquadAction.MoveSouth:
            case SquadAction.MoveEast:
            case SquadAction.MoveWest:
                newState = ProcessMove(state, hero, action, out moveReward);
                break;

            case SquadAction.Attack:
                newState = ProcessAttack(state, hero, out moveReward);
                break;

            case SquadAction.EndTurn:
                moveReward = -0.05;
                break;
        }

        // Update hero's actions remaining
        var updatedHero = newState.Heroes[hero.Index] with { ActionsRemaining = hero.ActionsRemaining - 1 };
        newState = newState with { Heroes = newState.Heroes.SetItem(hero.Index, updatedHero) };

        // Add move reward
        newState = newState with { AccumulatedReward = newState.AccumulatedReward + moveReward };

        // Check if hero needs to advance
        if (updatedHero.ActionsRemaining == 0)
        {
            int nextHero = GetNextActiveHeroIndex(newState, hero.Index);
            if (nextHero == -1)
            {
                // All heroes done, move to monster phase
                newState = newState with
                {
                    CurrentPhase = Phase.MonsterAction,
                    ActiveHeroIndex = -1
                };
            }
            else
            {
                newState = newState with { ActiveHeroIndex = nextHero };
            }
        }

        return newState;
    }

    private PhaseGameState ProcessMove(PhaseGameState state, PhaseHero hero, SquadAction action, out double reward)
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

        // Calculate reward based on distance to exit
        int oldDist = Math.Abs(hero.X - state.ExitX) + Math.Abs(hero.Y - state.ExitY);
        int newDist = Math.Abs(newX - state.ExitX) + Math.Abs(newY - state.ExitY);

        reward = -0.05; // Time penalty
        if (newDist < oldDist)
            reward += 2.0;
        else if (newDist > oldDist)
            reward -= 1.5;

        var movedHero = hero with { X = newX, Y = newY };
        return state with { Heroes = state.Heroes.SetItem(hero.Index, movedHero) };
    }

    private PhaseGameState ProcessAttack(PhaseGameState state, PhaseHero hero, out double reward)
    {
        reward = -0.05; // Time penalty

        // Find monsters in range
        var monstersInRange = state.Monsters
            .Where(m => m.IsAlive)
            .Where(m => Math.Abs(m.X - hero.X) + Math.Abs(m.Y - hero.Y) <= hero.Range)
            .OrderBy(m => Math.Abs(m.X - hero.X) + Math.Abs(m.Y - hero.Y))
            .ToList();

        if (monstersInRange.Count == 0)
            return state;

        // Attack becomes a chance node - create pending attack resolution
        // MCTS will enumerate hit/miss outcomes via ChanceOutcomes
        var target = monstersInRange[0];
        
        var newState = state with
        {
            AttackResolution = new PendingAttack(
                HeroIndex: hero.Index,
                MonsterIndex: target.Index,
                AttackScore: hero.AttackScore
            )
        };

        // The reward from killing will be added in the hit outcome
        return newState;
    }

    /// <summary>
    /// Calculate hit chance for an attack score on 2d6
    /// Attack score is the target number that must be rolled or higher
    /// </summary>
    private double GetHitChance(int attackScore)
    {
        // 2d6 probabilities: P(roll >= n)
        return attackScore switch
        {
            <= 2 => 1.0,      // Always hit (36/36)
            3 => 35.0/36,     // 35/36
            4 => 33.0/36,     // 33/36
            5 => 30.0/36,     // 30/36
            6 => 26.0/36,     // 26/36
            7 => 21.0/36,     // 21/36 = 58.3%
            8 => 15.0/36,     // 15/36 = 41.7%
            9 => 10.0/36,     // 10/36 = 27.8%
            10 => 6.0/36,     // 6/36 = 16.7%
            11 => 3.0/36,     // 3/36 = 8.3%
            12 => 1.0/36,     // 1/36 = 2.8%
            _ => 0.0          // Impossible
        };
    }

    public bool IsTerminal(in PhaseGameState state, out double value)
    {
        // Win: all living heroes at exit
        int exitX = state.ExitX;
        int exitY = state.ExitY;
        var livingHeroes = state.Heroes.Where(h => h.Status != HeroStatus.Dead).ToList();
        if (livingHeroes.Count > 0 && livingHeroes.All(h => h.X == exitX && h.Y == exitY))
        {
            value = 100.0;
            return true;
        }

        // Lose: all heroes dead
        if (state.Heroes.All(h => h.Status == HeroStatus.Dead))
        {
            value = -100.0;
            return true;
        }

        // Timeout
        if (state.TurnCount >= _maxTurns)
        {
            value = -50.0;
            return true;
        }

        value = 0.0;
        return false;
    }

    public PhaseGameState SampleChanceOutcome(in PhaseGameState state, Random rng)
    {
        var outcomes = ChanceOutcomes(state).ToList();
        if (outcomes.Count == 0)
            throw new InvalidOperationException("No chance outcomes available");

        if (outcomes.Count == 1)
            return outcomes[0].outcome;

        double r = rng.NextDouble();
        double cumulative = 0.0;
        foreach (var (outcome, probability) in outcomes)
        {
            cumulative += probability;
            if (r < cumulative)
                return outcome;
        }

        return outcomes[outcomes.Count - 1].outcome;
    }
}
