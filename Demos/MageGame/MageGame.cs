using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mcts;

namespace MageGame;

/// <summary>
/// Tactical game with Mage hero and special abilities:
/// - Warrior/Rogue/Elf: Standard melee/ranged combat
/// - Mage: Can't attack, but can Zap (ranged damage) or Teleport heroes
/// - Enemies die in one hit
/// - Heroes: Healthy -> Injured -> Dead (2 hits to kill)
/// - Turn phases: 1) Monster spawn, 2) Heroes act, 3) Monsters act
/// </summary>

public enum HeroClass { Warrior, Rogue, Elf, Mage }

public enum HeroStatus { Healthy, Injured, Dead }

public enum Phase { MonsterSpawn, HeroAction, MonsterAction }

public enum MonsterType { Random, Chaser }

public record MageHero(
    int Index,
    HeroClass Class,
    int X,
    int Y,
    HeroStatus Status,
    int AttackScore,      // Target number on 2d6 to hit (0 for Mage - can't attack)
    int Range,            // Movement/ability range
    int ActionsRemaining,
    int ZapRange,         // Mage only: range for Zap ability
    int TeleportRange     // Mage only: range for Teleport ability
);

public record MageMonster(
    int Index,
    int X,
    int Y,
    bool IsAlive,
    MonsterType Type
);

/// <summary>
/// Represents a pending attack or zap that needs to be resolved as a chance node
/// </summary>
public record PendingAttack(
    int HeroIndex,
    int TargetIndex,
    int AttackScore,
    bool IsZap  // True if Mage zap, false if regular attack
);

public enum ActionType
{
    ActivateHero,   // Choose which hero to activate
    MoveNorth, MoveSouth, MoveEast, MoveWest,
    Attack,         // Regular heroes only
    ZapMonster,     // Mage only - ranged attack with hit chance
    TeleportHero,   // Mage only - teleport ally
    EndTurn
}

public record MageAction(
    ActionType Type,
    int TargetX = -1,     // For teleport destination
    int TargetY = -1,     // For teleport destination
    int TargetIndex = -1  // For zap/teleport target
);

public record MageGameState(
    ImmutableList<MageHero> Heroes,
    ImmutableList<MageMonster> Monsters,
    int TurnCount,
    int ActiveHeroIndex,
    Phase CurrentPhase,
    int ExitX,
    int ExitY,
    double AccumulatedReward,
    ImmutableHashSet<(int, int)> Walls,
    PendingAttack? AttackResolution
);

public class MageTacticalGame : IGameModel<MageGameState, MageAction>
{
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly int _maxTurns;
    private readonly Random _setupRng;

    public MageTacticalGame(int gridWidth = 5, int gridHeight = 5, int maxTurns = 20, int? seed = null)
    {
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
        _maxTurns = maxTurns;
        _setupRng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public MageGameState InitialState()
    {
        // Create heroes - including a Mage with special abilities
        // Mage: AttackScore=0 (can't attack), ZapRange=3, TeleportRange=4
        var heroes = ImmutableList.Create(
            new MageHero(0, HeroClass.Warrior, 0, 0, HeroStatus.Healthy, 7, 1, 2, 0, 0),
            new MageHero(1, HeroClass.Mage, 1, 0, HeroStatus.Healthy, 0, 1, 2, 3, 4),
            new MageHero(2, HeroClass.Rogue, 0, 1, HeroStatus.Healthy, 8, 2, 2, 0, 0)
        );

        // 5x5 grid with central wall
        var walls = ImmutableHashSet.CreateRange(new[]
        {
            (2, 1), (2, 2), (2, 3)
        });

        return new MageGameState(
            Heroes: heroes,
            Monsters: ImmutableList<MageMonster>.Empty,
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

    public bool IsTerminal(in MageGameState state, out double terminalValue)
    {
        var s = state; // Copy to avoid ref issues in lambdas
        
        // Win: All living heroes reached exit
        var livingHeroes = s.Heroes.Where(h => h.Status != HeroStatus.Dead).ToList();
        if (livingHeroes.Count > 0 && livingHeroes.All(h => h.X == s.ExitX && h.Y == s.ExitY))
        {
            terminalValue = 100.0;
            return true;
        }

        // Loss: All heroes dead
        if (s.Heroes.All(h => h.Status == HeroStatus.Dead))
        {
            terminalValue = 0.0;
            return true;
        }

        // Loss: Timeout
        if (s.TurnCount >= _maxTurns)
        {
            terminalValue = 0.0;
            return true;
        }

        terminalValue = 0.0;
        return false;
    }

    public bool IsChanceNode(in MageGameState state)
    {
        // Attack/Zap resolution
        if (state.AttackResolution != null)
            return true;

        // Monster spawn phase
        if (state.CurrentPhase == Phase.MonsterSpawn)
            return true;

        // Monster movement phase
        if (state.CurrentPhase == Phase.MonsterAction)
            return true;

        return false;
    }

    public IEnumerable<(MageGameState outcome, double probability)> ChanceOutcomes(MageGameState state)
    {
        // Attack/Zap resolution
        if (state.AttackResolution != null)
        {
            var attack = state.AttackResolution;
            double hitChance = GetHitChance(attack.AttackScore);

            // Hit outcome
            var hitState = state with
            {
                Monsters = state.Monsters.SetItem(
                    attack.TargetIndex,
                    state.Monsters[attack.TargetIndex] with { IsAlive = false }
                ),
                AttackResolution = null,
                AccumulatedReward = state.AccumulatedReward + 5.0
            };
            var hitFinal = AdvanceToNextPhaseOrHero(hitState);
            yield return (hitFinal, hitChance);

            // Miss outcome
            var missState = state with { AttackResolution = null };
            var missFinal = AdvanceToNextPhaseOrHero(missState);
            yield return (missFinal, 1.0 - hitChance);

            yield break;
        }

        // Monster spawn
        if (state.CurrentPhase == Phase.MonsterSpawn)
        {
            foreach (var outcome in EnumerateMonsterSpawnOutcomes(state))
                yield return outcome;
            yield break;
        }

        // Monster movement
        if (state.CurrentPhase == Phase.MonsterAction)
        {
            foreach (var outcome in EnumerateMonsterMovementOutcomes(state))
                yield return outcome;
            yield break;
        }
    }

    private IEnumerable<(MageGameState outcome, double probability)> EnumerateMonsterSpawnOutcomes(MageGameState state)
    {
        var emptyPositions = new List<(int x, int y)>();
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int y = 0; y < _gridHeight; y++)
            {
                if (IsValidPosition(state, x, y) &&
                    !state.Heroes.Any(h => h.Status != HeroStatus.Dead && h.X == x && h.Y == y) &&
                    !state.Monsters.Any(m => m.IsAlive && m.X == x && m.Y == y) &&
                    !(x == state.ExitX && y == state.ExitY))
                {
                    emptyPositions.Add((x, y));
                }
            }
        }

        if (emptyPositions.Count == 0)
        {
            var noSpawnState = state with { CurrentPhase = Phase.HeroAction, ActiveHeroIndex = -1 };
            foreach (var hero in noSpawnState.Heroes)
            {
                if (hero.Status != HeroStatus.Dead)
                {
                    noSpawnState = noSpawnState with
                    {
                        Heroes = noSpawnState.Heroes.SetItem(hero.Index, hero with { ActionsRemaining = 2 })
                    };
                }
            }
            yield return (noSpawnState, 1.0);
            yield break;
        }

        // 60% spawn rate: 60% Random, 40% Chaser
        double probSpawn = 0.6;
        double probNoSpawn = 1.0 - probSpawn;
        double probPerLocation = probSpawn / emptyPositions.Count;

        // No spawn outcome
        var noSpawn = state with { CurrentPhase = Phase.HeroAction, ActiveHeroIndex = -1 };
        foreach (var hero in noSpawn.Heroes)
        {
            if (hero.Status != HeroStatus.Dead)
            {
                noSpawn = noSpawn with
                {
                    Heroes = noSpawn.Heroes.SetItem(hero.Index, hero with { ActionsRemaining = 2 })
                };
            }
        }
        yield return (noSpawn, probNoSpawn);

        // Spawn Random monster at each location
        foreach (var (x, y) in emptyPositions)
        {
            int newIndex = state.Monsters.Count;
            var newMonster = new MageMonster(newIndex, x, y, true, MonsterType.Random);
            var spawnState = state with
            {
                Monsters = state.Monsters.Add(newMonster),
                CurrentPhase = Phase.HeroAction,
                ActiveHeroIndex = -1
            };
            foreach (var hero in spawnState.Heroes)
            {
                if (hero.Status != HeroStatus.Dead)
                {
                    spawnState = spawnState with
                    {
                        Heroes = spawnState.Heroes.SetItem(hero.Index, hero with { ActionsRemaining = 2 })
                    };
                }
            }
            yield return (spawnState, probPerLocation * 0.6);
        }

        // Spawn Chaser monster at each location
        foreach (var (x, y) in emptyPositions)
        {
            int newIndex = state.Monsters.Count;
            var newMonster = new MageMonster(newIndex, x, y, true, MonsterType.Chaser);
            var spawnState = state with
            {
                Monsters = state.Monsters.Add(newMonster),
                CurrentPhase = Phase.HeroAction,
                ActiveHeroIndex = -1
            };
            foreach (var hero in spawnState.Heroes)
            {
                if (hero.Status != HeroStatus.Dead)
                {
                    spawnState = spawnState with
                    {
                        Heroes = spawnState.Heroes.SetItem(hero.Index, hero with { ActionsRemaining = 2 })
                    };
                }
            }
            yield return (spawnState, probPerLocation * 0.4);
        }
    }

    private IEnumerable<(MageGameState outcome, double probability)> EnumerateMonsterMovementOutcomes(MageGameState state)
    {
        var aliveMonsters = state.Monsters.Where(m => m.IsAlive).ToList();
        if (aliveMonsters.Count == 0)
        {
            yield return (AdvanceToNextTurn(state), 1.0);
            yield break;
        }

        // One random monster moves per turn
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

    private List<(int x, int y)> GetMonsterMoves(MageGameState state, MageMonster monster)
    {
        var moves = new List<(int x, int y)>();

        // Check if adjacent to any hero
        var adjacentHeroes = state.Heroes
            .Where(h => h.Status != HeroStatus.Dead)
            .Where(h => Math.Abs(h.X - monster.X) + Math.Abs(h.Y - monster.Y) == 1)
            .ToList();

        if (adjacentHeroes.Any())
        {
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

        // Chaser monsters: prefer moves toward nearest hero
        if (monster.Type == MonsterType.Chaser && moves.Count > 0)
        {
            var nearestHero = state.Heroes
                .Where(h => h.Status != HeroStatus.Dead)
                .OrderBy(h => Math.Abs(h.X - monster.X) + Math.Abs(h.Y - monster.Y))
                .FirstOrDefault();

            if (nearestHero != null)
            {
                int currentDist = Math.Abs(monster.X - nearestHero.X) + Math.Abs(monster.Y - nearestHero.Y);
                var betterMoves = moves
                    .Where(m => Math.Abs(m.x - nearestHero.X) + Math.Abs(m.y - nearestHero.Y) < currentDist)
                    .ToList();

                if (betterMoves.Any())
                    moves = betterMoves;
            }
        }

        // Always include "stay in place" for Random monsters (groups blocked directions)
        if (monster.Type == MonsterType.Random || moves.Count == 0)
        {
            moves.Add((monster.X, monster.Y));
        }

        return moves;
    }

    public IEnumerable<MageAction> LegalActions(MageGameState state)
    {
        if (state.CurrentPhase != Phase.HeroAction)
            yield break;

        // Hero activation selection - choose which hero acts next
        if (state.ActiveHeroIndex < 0)
        {
            foreach (var hero in state.Heroes)
            {
                if (hero.Status != HeroStatus.Dead && hero.ActionsRemaining > 0)
                {
                    yield return new MageAction(ActionType.ActivateHero, TargetIndex: hero.Index);
                }
            }
            yield break;
        }

        if (state.ActiveHeroIndex >= state.Heroes.Count)
            yield break;

        var activeHero = state.Heroes[state.ActiveHeroIndex];
        if (activeHero.Status == HeroStatus.Dead || activeHero.ActionsRemaining <= 0)
            yield break;

        // Movement actions
        var moves = new[]
        {
            (ActionType.MoveNorth, 0, -1),
            (ActionType.MoveSouth, 0, 1),
            (ActionType.MoveWest, -1, 0),
            (ActionType.MoveEast, 1, 0)
        };

        foreach (var (actionType, dx, dy) in moves)
        {
            int newX = activeHero.X + dx;
            int newY = activeHero.Y + dy;

            if (IsValidPosition(state, newX, newY) &&
                !state.Monsters.Any(m => m.IsAlive && m.X == newX && m.Y == newY))
            {
                yield return new MageAction(actionType);
            }
        }

        // Mage special abilities
        if (activeHero.Class == HeroClass.Mage)
        {
            // Zap: Ranged attack on monsters within range
            foreach (var monster in state.Monsters.Where(m => m.IsAlive))
            {
                int distance = Math.Abs(activeHero.X - monster.X) + Math.Abs(activeHero.Y - monster.Y);
                if (distance <= activeHero.ZapRange && distance > 0)
                {
                    yield return new MageAction(ActionType.ZapMonster, TargetIndex: monster.Index);
                }
            }

            // Teleport: Move another hero within range
            foreach (var targetHero in state.Heroes.Where(h => h.Status != HeroStatus.Dead && h.Index != activeHero.Index))
            {
                int heroDistance = Math.Abs(activeHero.X - targetHero.X) + Math.Abs(activeHero.Y - targetHero.Y);
                if (heroDistance <= activeHero.TeleportRange)
                {
                    // Find valid teleport destinations (within TeleportRange of target hero's current position)
                    for (int x = 0; x < _gridWidth; x++)
                    {
                        for (int y = 0; y < _gridHeight; y++)
                        {
                            int destDistance = Math.Abs(targetHero.X - x) + Math.Abs(targetHero.Y - y);
                            if (destDistance > 0 && destDistance <= 3 && // Teleport up to 3 squares
                                IsValidPosition(state, x, y) &&
                                !state.Heroes.Any(h => h.Status != HeroStatus.Dead && h.X == x && h.Y == y) &&
                                !state.Monsters.Any(m => m.IsAlive && m.X == x && m.Y == y))
                            {
                                yield return new MageAction(ActionType.TeleportHero, x, y, targetHero.Index);
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // Regular Attack action (non-Mage heroes only)
            foreach (var monster in state.Monsters.Where(m => m.IsAlive))
            {
                int distance = Math.Abs(activeHero.X - monster.X) + Math.Abs(activeHero.Y - monster.Y);
                if (distance <= activeHero.Range)
                {
                    yield return new MageAction(ActionType.Attack, TargetIndex: monster.Index);
                }
            }
        }

        // End turn
        yield return new MageAction(ActionType.EndTurn);
    }

    public MageGameState Step(in MageGameState state, in MageAction action)
    {
        if (state.CurrentPhase != Phase.HeroAction)
            return state;

        // Hero activation - just set the active hero, don't consume actions
        if (action.Type == ActionType.ActivateHero)
        {
            return state with { ActiveHeroIndex = action.TargetIndex };
        }

        var hero = state.Heroes[state.ActiveHeroIndex];

        double reward = -0.05; // Time penalty

        MageGameState newState = state;

        switch (action.Type)
        {
            case ActionType.MoveNorth:
                newState = MoveHero(state, hero.Index, hero.X, hero.Y - 1);
                reward += CalculateMovementReward(state, hero.X, hero.Y, hero.X, hero.Y - 1);
                break;

            case ActionType.MoveSouth:
                newState = MoveHero(state, hero.Index, hero.X, hero.Y + 1);
                reward += CalculateMovementReward(state, hero.X, hero.Y, hero.X, hero.Y + 1);
                break;

            case ActionType.MoveWest:
                newState = MoveHero(state, hero.Index, hero.X - 1, hero.Y);
                reward += CalculateMovementReward(state, hero.X, hero.Y, hero.X - 1, hero.Y);
                break;

            case ActionType.MoveEast:
                newState = MoveHero(state, hero.Index, hero.X + 1, hero.Y);
                reward += CalculateMovementReward(state, hero.X, hero.Y, hero.X + 1, hero.Y);
                break;

            case ActionType.Attack:
                if (hero.Class != HeroClass.Mage)
                {
                    newState = ProcessAttack(state, hero.Index, action.TargetIndex, hero.AttackScore, false);
                }
                break;

            case ActionType.ZapMonster:
                if (hero.Class == HeroClass.Mage)
                {
                    // Mage zap uses lower attack score (harder to hit)
                    newState = ProcessAttack(state, hero.Index, action.TargetIndex, 6, true);
                }
                break;

            case ActionType.TeleportHero:
                if (hero.Class == HeroClass.Mage)
                {
                    newState = TeleportHero(state, action.TargetIndex, action.TargetX, action.TargetY);
                    reward += CalculateMovementReward(state,
                        state.Heroes[action.TargetIndex].X, state.Heroes[action.TargetIndex].Y,
                        action.TargetX, action.TargetY);
                }
                break;

            case ActionType.EndTurn:
                newState = state with
                {
                    Heroes = state.Heroes.SetItem(hero.Index, hero with { ActionsRemaining = 0 })
                };
                break;
        }

        newState = newState with { AccumulatedReward = newState.AccumulatedReward + reward };
        return AdvanceToNextPhaseOrHero(newState);
    }

    private MageGameState MoveHero(MageGameState state, int heroIndex, int newX, int newY)
    {
        var hero = state.Heroes[heroIndex];
        return state with
        {
            Heroes = state.Heroes.SetItem(heroIndex, hero with
            {
                X = newX,
                Y = newY,
                ActionsRemaining = hero.ActionsRemaining - 1
            })
        };
    }

    private MageGameState TeleportHero(MageGameState state, int targetHeroIndex, int newX, int newY)
    {
        var mageHero = state.Heroes[state.ActiveHeroIndex];
        var targetHero = state.Heroes[targetHeroIndex];

        var newState = state with
        {
            Heroes = state.Heroes
                .SetItem(state.ActiveHeroIndex, mageHero with { ActionsRemaining = mageHero.ActionsRemaining - 1 })
                .SetItem(targetHeroIndex, targetHero with { X = newX, Y = newY })
        };

        return newState;
    }

    private MageGameState ProcessAttack(MageGameState state, int heroIndex, int monsterIndex, int attackScore, bool isZap)
    {
        var hero = state.Heroes[heroIndex];

        // Create pending attack to be resolved as chance node
        var attack = new PendingAttack(heroIndex, monsterIndex, attackScore, isZap);

        return state with
        {
            Heroes = state.Heroes.SetItem(heroIndex, hero with { ActionsRemaining = hero.ActionsRemaining - 1 }),
            AttackResolution = attack
        };
    }

    private double GetHitChance(int attackScore)
    {
        // Probability of rolling >= N on 2d6
        var probabilities = new Dictionary<int, double>
        {
            { 2, 36.0/36.0 },  // 100%
            { 3, 35.0/36.0 },  // 97.2%
            { 4, 33.0/36.0 },  // 91.7%
            { 5, 30.0/36.0 },  // 83.3%
            { 6, 26.0/36.0 },  // 72.2%
            { 7, 21.0/36.0 },  // 58.3%
            { 8, 15.0/36.0 },  // 41.7%
            { 9, 10.0/36.0 },  // 27.8%
            { 10, 6.0/36.0 },  // 16.7%
            { 11, 3.0/36.0 },  // 8.3%
            { 12, 1.0/36.0 }   // 2.8%
        };

        return probabilities.GetValueOrDefault(attackScore, 0.0);
    }

    private double CalculateMovementReward(MageGameState state, int oldX, int oldY, int newX, int newY)
    {
        int oldDist = Math.Abs(oldX - state.ExitX) + Math.Abs(oldY - state.ExitY);
        int newDist = Math.Abs(newX - state.ExitX) + Math.Abs(newY - state.ExitY);

        if (newDist < oldDist)
            return 2.0;
        else if (newDist > oldDist)
            return -1.5;
        else
            return 0.0;
    }

    private MageGameState AdvanceToNextPhaseOrHero(MageGameState state)
    {
        if (state.CurrentPhase != Phase.HeroAction)
            return state;

        // If no active hero or active hero still has actions, no change
        if (state.ActiveHeroIndex < 0)
            return state;

        var currentHero = state.Heroes[state.ActiveHeroIndex];
        if (currentHero.ActionsRemaining > 0)
            return state;

        // Current hero finished - check if any heroes still have actions
        bool anyHeroesRemaining = state.Heroes.Any(h =>
            h.Status != HeroStatus.Dead && h.ActionsRemaining > 0);

        if (anyHeroesRemaining)
        {
            // Reset to hero selection
            return state with { ActiveHeroIndex = -1 };
        }

        // All heroes done - move to monster phase
        return state with
        {
            CurrentPhase = Phase.MonsterAction,
            ActiveHeroIndex = -1
        };
    }

    private MageGameState AdvanceToNextTurn(MageGameState state)
    {
        return state with
        {
            TurnCount = state.TurnCount + 1,
            CurrentPhase = Phase.MonsterSpawn,
            ActiveHeroIndex = -1
        };
    }

    private MageGameState MoveMonster(MageGameState state, int monsterIndex, int newX, int newY)
    {
        var monster = state.Monsters[monsterIndex];

        // Check if attacking a hero
        var targetHero = state.Heroes.FirstOrDefault(h =>
            h.Status != HeroStatus.Dead && h.X == newX && h.Y == newY);

        if (targetHero != null)
        {
            var newStatus = targetHero.Status == HeroStatus.Healthy ? HeroStatus.Injured : HeroStatus.Dead;
            double penalty = newStatus == HeroStatus.Dead ? -10.0 : -5.0;

            return state with
            {
                Heroes = state.Heroes.SetItem(targetHero.Index, targetHero with { Status = newStatus }),
                AccumulatedReward = state.AccumulatedReward + penalty
            };
        }

        // Regular movement
        return state with
        {
            Monsters = state.Monsters.SetItem(monsterIndex, monster with { X = newX, Y = newY })
        };
    }

    private bool IsValidPosition(MageGameState state, int x, int y)
    {
        if (x < 0 || x >= _gridWidth || y < 0 || y >= _gridHeight)
            return false;

        if (state.Walls.Contains((x, y)))
            return false;

        return true;
    }
}
