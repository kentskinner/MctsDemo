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
    int TeleportRange,    // Mage only: range for Teleport ability
    bool HasExited        // True if hero has exited the map
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
    PendingAttack? AttackResolution,
    bool ActiveHeroHasMoved  // Track if active hero used a move action this activation
);

public class MageTacticalGame : IGameModel<MageGameState, MageAction>
{
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly int _maxTurns;

    // === Diagnostics ===
    private readonly bool _invariantStrict;           // throw if invariants fail
    private readonly Action<string>? _log;            // where to write diagnostics
    private const double ProbEpsilon = 1e-9;

    public MageTacticalGame(int gridWidth = 5, int gridHeight = 5, int maxTurns = 20, int? seed = null,
        bool invariantStrict = false,
        Action<string>? logger = null)
    {
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
        _maxTurns = maxTurns;

        _invariantStrict = invariantStrict;
        _log = logger ?? Console.WriteLine;  // default to Console if not provided
    }

    public MageGameState InitialState()
    {
        // Create heroes - including a Mage with special abilities
        // Mage: AttackScore=0 (can't attack), ZapRange=2, TeleportRange=2
        var heroes = ImmutableList.Create(
            new MageHero(0, HeroClass.Warrior, 0, 0, HeroStatus.Healthy, 7, 1, 2, 0, 0, false),
            new MageHero(1, HeroClass.Mage, 1, 0, HeroStatus.Healthy, 0, 1, 2, 3, 2, false),
            new MageHero(2, HeroClass.Rogue, 0, 1, HeroStatus.Healthy, 8, 2, 2, 0, 0, false)
        );

        // 5x5 grid with central wall
        var walls = ImmutableHashSet.CreateRange(new[]
        {
            (2, 1), (2, 2), (2, 3)
        });

        var s = new MageGameState(
            Heroes: heroes,
            Monsters: ImmutableList<MageMonster>.Empty,
            TurnCount: 0,
            ActiveHeroIndex: -1,
            CurrentPhase: Phase.MonsterSpawn,
            ExitX: 4,
            ExitY: 4,
            AccumulatedReward: 0.0,
            Walls: walls,
            AttackResolution: null,
            ActiveHeroHasMoved: false
        );

        ValidateDecisionStateHasActions(s, "InitialState()");
        return s;
    }
    // Summaries help you see why a state has no actions.
    private string SummarizeState(MageGameState s)
    {
        string heroes = string.Join(",",
            s.Heroes.Select(h => $"{h.Index}:{h.Class}@({h.X},{h.Y}) S={h.Status} AP={h.ActionsRemaining} Ex={h.HasExited}"));
        string mons = string.Join(",",
            s.Monsters.Where(m => m.IsAlive).Select(m => $"{m.Index}:{m.Type}@({m.X},{m.Y})"));
        return $"Phase={s.CurrentPhase} Turn={s.TurnCount} AH={s.ActiveHeroIndex} " +
               $"Moved?={s.ActiveHeroHasMoved} AttackRes?={(s.AttackResolution != null)} " +
               $"Exit=({s.ExitX},{s.ExitY}) | H=[{heroes}] | M=[{mons}]";
    }

    // True if the state *could* produce at least one legal action during HeroAction.
    // This mirrors the structure of LegalActions but stops at first positive.
    private bool HasAnyLegalAction(MageGameState s)
    {
        if (s.CurrentPhase != Phase.HeroAction) return false;

        // Hero selection step
        if (s.ActiveHeroIndex < 0)
            return s.Heroes.Any(h => h.Status != HeroStatus.Dead && !h.HasExited && h.ActionsRemaining > 0);

        if (s.ActiveHeroIndex >= s.Heroes.Count) return false;

        var h = s.Heroes[s.ActiveHeroIndex];
        if (h.Status == HeroStatus.Dead || h.HasExited || h.ActionsRemaining <= 0) return false;

        // Movement (blocked by ActiveHeroHasMoved)
        if (!s.ActiveHeroHasMoved)
        {
            var dirs = new (int dx, int dy)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
            foreach (var (dx, dy) in dirs)
            {
                int nx = h.X + dx, ny = h.Y + dy;
                if (IsValidPosition(s, nx, ny) &&
                    !s.Monsters.Any(m => m.IsAlive && m.X == nx && m.Y == ny))
                    return true;
            }
        }

        if (h.Class == HeroClass.Mage)
        {
            // Any zap target?
            if (s.Monsters.Any(m => m.IsAlive &&
                    (Math.Abs(h.X - m.X) + Math.Abs(h.Y - m.Y)) <= h.ZapRange &&
                    (h.X != m.X || h.Y != m.Y)))
                return true;

            // Any teleport option?
            foreach (var t in s.Heroes.Where(x => x.Status != HeroStatus.Dead && !x.HasExited))
            {
                bool canTp = (t.Index == h.Index) || (t.X == h.X && t.Y == h.Y);
                if (!canTp) continue;

                // Search a small grid for a single valid destination (<=3 away)
                for (int x = 0; x < _gridWidth; x++)
                    for (int y = 0; y < _gridHeight; y++)
                    {
                        int dist = Math.Abs(t.X - x) + Math.Abs(t.Y - y);
                        if (dist > 0 && dist <= 3 &&
                            IsValidPosition(s, x, y) &&
                            !s.Heroes.Any(u => (u.Status != HeroStatus.Dead && !u.HasExited) && u.X == x && u.Y == y) &&
                            !s.Monsters.Any(m => m.IsAlive && m.X == x && m.Y == y))
                            return true;
                    }
            }
        }
        else
        {
            // Any attack target?
            if (s.Monsters.Any(m => m.IsAlive &&
                    (Math.Abs(h.X - m.X) + Math.Abs(h.Y - m.Y)) <= h.Range))
                return true;
        }

        // EndTurn is always available for an active hero with AP>0
        return true;
    }

    private void FailOrLog(string msg)
    {
        if (_invariantStrict) throw new InvalidOperationException(msg);
        _log?.Invoke(msg);
    }

    private void ValidateDecisionStateHasActions(MageGameState s, string where)
    {
        var term = IsTerminal(s, out _);
        var chance = IsChanceNode(s);

        if (!term && !chance)
        {
            bool any = HasAnyLegalAction(s);
            if (!any)
            {
                FailOrLog($"[Invariant] Dead-end DECISION state with no legal actions at {where}.\n{SummarizeState(s)}");
            }
        }
    }

    private void ValidateChanceOutcomeList(
        MageGameState s,
        List<(MageGameState outcome, double probability)> outcomes,
        string where)
    {
        if (outcomes.Count == 0)
        {
            FailOrLog($"[Invariant] ChanceOutcomes returned EMPTY at {where}.\n{SummarizeState(s)}");
            return;
        }

        double sum = outcomes.Sum(o => o.probability);
        if (double.IsNaN(sum) || Math.Abs(sum - 1.0) > 1e-6)
        {
            FailOrLog($"[Invariant] ChanceOutcomes probabilities sum to {sum:F12} (≠ 1) at {where}.\n{SummarizeState(s)}");
        }
    }

    public bool IsTerminal(in MageGameState state, out double terminalValue)
    {
        var s = state; // Copy to avoid ref issues in lambdas

        // Win: All living heroes exited
        var livingHeroes = s.Heroes.Where(h => h.Status != HeroStatus.Dead).ToList();
        if (livingHeroes.Count > 0 && livingHeroes.All(h => h.HasExited))
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
        var list = new List<(MageGameState, double)>();

        // Attack/Zap resolution
        if (state.AttackResolution != null)
        {
            var attack = state.AttackResolution;
            double hitChance = GetHitChance(attack.AttackScore);

            var hitState = state with
            {
                Monsters = state.Monsters.SetItem(
                    attack.TargetIndex,
                    state.Monsters[attack.TargetIndex] with { IsAlive = false }
                ),
                AttackResolution = null,
                AccumulatedReward = state.AccumulatedReward + 5.0
            };
            list.Add((AdvanceToNextPhaseOrHero(hitState), hitChance));

            var missState = state with { AttackResolution = null };
            list.Add((AdvanceToNextPhaseOrHero(missState), 1.0 - hitChance));

            ValidateChanceOutcomeList(state, list, "Attack/Zap resolution");
            return list;
        }

        // Monster spawn
        if (state.CurrentPhase == Phase.MonsterSpawn)
        {
            list.AddRange(EnumerateMonsterSpawnOutcomes(state));
            ValidateChanceOutcomeList(state, list, "MonsterSpawn");
            return list;
        }

        // Monster movement
        if (state.CurrentPhase == Phase.MonsterAction)
        {
            list.AddRange(EnumerateMonsterMovementOutcomes(state));
            ValidateChanceOutcomeList(state, list, "MonsterAction");
            return list;
        }

        // Defensive: if IsChanceNode returned false but we got here,
        // just return a single outcome (identity) to avoid breaking callers.
        FailOrLog($"[Invariant] ChanceOutcomes called for non-chance state.\n{SummarizeState(state)}");
        list.Add((state, 1.0));
        return list;
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

        // Prioritize spawn locations near the exit (within distance 4)
        var nearExit = emptyPositions
            .Where(pos => Math.Abs(pos.x - state.ExitX) + Math.Abs(pos.y - state.ExitY) <= 4)
            .ToList();
        
        // Use near-exit positions if available, otherwise use all positions
        var spawnPositions = nearExit.Count > 0 ? nearExit : emptyPositions;

        if (spawnPositions.Count == 0)
        {
            var noSpawnState = state with { CurrentPhase = Phase.HeroAction, ActiveHeroIndex = -1 };
            foreach (var hero in noSpawnState.Heroes)
            {
                if (hero.Status != HeroStatus.Dead && !hero.HasExited)
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

        // 80% spawn rate: 60% Random, 40% Chaser
        double probSpawn = 0.8;
        double probNoSpawn = 1.0 - probSpawn;
        double probPerLocation = probSpawn / spawnPositions.Count;

        // No spawn outcome
        var noSpawn = state with { CurrentPhase = Phase.HeroAction, ActiveHeroIndex = -1 };
        foreach (var hero in noSpawn.Heroes)
        {
            if (hero.Status != HeroStatus.Dead && !hero.HasExited)
            {
                noSpawn = noSpawn with
                {
                    Heroes = noSpawn.Heroes.SetItem(hero.Index, hero with { ActionsRemaining = 2 })
                };
            }
        }
        yield return (noSpawn, probNoSpawn);

        // Spawn Random monster at each location
        foreach (var (x, y) in spawnPositions)
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
                if (hero.Status != HeroStatus.Dead && !hero.HasExited)
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
        foreach (var (x, y) in spawnPositions)
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
                if (hero.Status != HeroStatus.Dead && !hero.HasExited)
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
        // Collect, then validate/log before returning
        var actions = new List<MageAction>();

        if (state.CurrentPhase != Phase.HeroAction)
            return actions; // empty (and SelectDown shouldn't ask for actions in non-hero phases)

        // Hero activation selection
        if (state.ActiveHeroIndex < 0)
        {
            foreach (var hero in state.Heroes)
            {
                if (hero.Status != HeroStatus.Dead && !hero.HasExited && hero.ActionsRemaining > 0)
                    actions.Add(new MageAction(ActionType.ActivateHero, TargetIndex: hero.Index));
            }

            if (actions.Count == 0)
            {
                // This is a classic dead-end source: no heroes with AP while phase == HeroAction.
                FailOrLog($"[LegalActions] No heroes can be activated. {SummarizeState(state)}");
            }

            return actions;
        }

        if (state.ActiveHeroIndex >= state.Heroes.Count)
        {
            FailOrLog($"[LegalActions] ActiveHeroIndex out of range. {SummarizeState(state)}");
            return actions;
        }

        var activeHero = state.Heroes[state.ActiveHeroIndex];
        if (activeHero.Status == HeroStatus.Dead || activeHero.HasExited || activeHero.ActionsRemaining <= 0)
        {
            // Should have been advanced by AdvanceToNextPhaseOrHero; log if we still see it.
            FailOrLog($"[LegalActions] Active hero cannot act (dead/exited/no AP). {SummarizeState(state)}");
            return actions;
        }

        // Movement
        if (!state.ActiveHeroHasMoved)
        {
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
                    actions.Add(new MageAction(actionType));
                }
            }
        }

        if (activeHero.Class == HeroClass.Mage)
        {
            // Zap
            foreach (var monster in state.Monsters.Where(m => m.IsAlive))
            {
                int distance = Math.Abs(activeHero.X - monster.X) + Math.Abs(activeHero.Y - monster.Y);
                if (distance <= activeHero.ZapRange && distance > 0)
                {
                    actions.Add(new MageAction(ActionType.ZapMonster, TargetIndex: monster.Index));
                }
            }

            // Teleport (self or co-located ally)
            foreach (var targetHero in state.Heroes.Where(h => h.Status != HeroStatus.Dead && !h.HasExited))
            {
                bool canTeleport = (targetHero.Index == activeHero.Index) ||
                                   (targetHero.X == activeHero.X && targetHero.Y == activeHero.Y);

                if (!canTeleport) continue;

                for (int x = 0; x < _gridWidth; x++)
                    for (int y = 0; y < _gridHeight; y++)
                    {
                        int destDistance = Math.Abs(targetHero.X - x) + Math.Abs(targetHero.Y - y);
                        if (destDistance > 0 && destDistance <= activeHero.TeleportRange &&
                            IsValidPosition(state, x, y) &&
                            !state.Heroes.Any(h => (h.Status != HeroStatus.Dead && !h.HasExited) && h.X == x && h.Y == y) &&
                            !state.Monsters.Any(m => m.IsAlive && m.X == x && m.Y == y))
                        {
                            actions.Add(new MageAction(ActionType.TeleportHero, x, y, targetHero.Index));
                        }
                    }
            }
        }
        else
        {
            // Melee attack
            foreach (var monster in state.Monsters.Where(m => m.IsAlive))
            {
                int distance = Math.Abs(activeHero.X - monster.X) + Math.Abs(activeHero.Y - monster.Y);
                if (distance <= activeHero.Range)
                {
                    actions.Add(new MageAction(ActionType.Attack, TargetIndex: monster.Index));
                }
            }
        }

        // End turn is always legal for an active hero with AP>0
        actions.Add(new MageAction(ActionType.EndTurn));

        // Final decision-state validation
        if (actions.Count == 0)
        {
            FailOrLog($"[LegalActions] Empty action list produced. {SummarizeState(state)}");
        }
        else
        {
            // If this is a decision state, confirm we *do* have at least one action
            ValidateDecisionStateHasActions(state, "LegalActions()");
        }

        return actions;
    }


    public MageGameState Step(in MageGameState state, in MageAction action)
    {
        if (state.CurrentPhase != Phase.HeroAction)
        {
            FailOrLog($"[Step] Called in non-HeroAction phase. {SummarizeState(state)}");
            return state;
        }

        if (state.ActiveHeroIndex < 0)
        {
            if (action.Type != ActionType.ActivateHero)
                FailOrLog($"[Step] Expected ActivateHero but got {action.Type}. {SummarizeState(state)}");
        }

        // Hero activation - just set the active hero, don't consume actions, reset move flag
        if (action.Type == ActionType.ActivateHero)
        {
            return state with { ActiveHeroIndex = action.TargetIndex, ActiveHeroHasMoved = false };
        }

        var hero = state.Heroes[state.ActiveHeroIndex];

        double reward = -0.05; // Time penalty

        MageGameState newState = state;

        switch (action.Type)
        {
            case ActionType.MoveNorth:
                newState = MoveHero(state, hero.Index, hero.X, hero.Y - 1);
                reward += CalculateMovementReward(state, hero.X, hero.Y, hero.X, hero.Y - 1);
                newState = newState with { ActiveHeroHasMoved = true };
                break;

            case ActionType.MoveSouth:
                newState = MoveHero(state, hero.Index, hero.X, hero.Y + 1);
                reward += CalculateMovementReward(state, hero.X, hero.Y, hero.X, hero.Y + 1);
                newState = newState with { ActiveHeroHasMoved = true };
                break;

            case ActionType.MoveWest:
                newState = MoveHero(state, hero.Index, hero.X - 1, hero.Y);
                reward += CalculateMovementReward(state, hero.X, hero.Y, hero.X - 1, hero.Y);
                newState = newState with { ActiveHeroHasMoved = true };
                break;

            case ActionType.MoveEast:
                newState = MoveHero(state, hero.Index, hero.X + 1, hero.Y);
                reward += CalculateMovementReward(state, hero.X, hero.Y, hero.X + 1, hero.Y);
                newState = newState with { ActiveHeroHasMoved = true };
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
                    // Teleport doesn't count as movement - Mage can still move after teleporting
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
        var hasExited = (newX == state.ExitX && newY == state.ExitY);

        var newState = state with
        {
            Heroes = state.Heroes.SetItem(heroIndex, hero with
            {
                X = newX,
                Y = newY,
                ActionsRemaining = hero.ActionsRemaining - 1,
                HasExited = hasExited
            })
        };

        // If hero exited, deactivate them
        if (hasExited)
        {
            newState = newState with { ActiveHeroIndex = -1 };
        }

        return newState;
    }

    private MageGameState TeleportHero(MageGameState state, int targetHeroIndex, int newX, int newY)
    {
        var mageHero = state.Heroes[state.ActiveHeroIndex];
        var targetHero = state.Heroes[targetHeroIndex];
        var hasExited = (newX == state.ExitX && newY == state.ExitY);

        // Special case: Mage teleporting herself
        if (targetHeroIndex == state.ActiveHeroIndex)
        {
            var newState = state with
            {
                Heroes = state.Heroes.SetItem(targetHeroIndex, mageHero with
                {
                    X = newX,
                    Y = newY,
                    ActionsRemaining = mageHero.ActionsRemaining - 1,
                    HasExited = hasExited
                })
            };

            // If mage exited, deactivate
            if (hasExited)
            {
                newState = newState with { ActiveHeroIndex = -1 };
            }

            return newState;
        }

        // Normal case: Mage teleporting another hero
        var result = state with
        {
            Heroes = state.Heroes
                .SetItem(state.ActiveHeroIndex, mageHero with { ActionsRemaining = mageHero.ActionsRemaining - 1 })
                .SetItem(targetHeroIndex, targetHero with { X = newX, Y = newY, HasExited = hasExited })
        };

        return result;
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

        // If we have an active hero with actions remaining, no change
        if (state.ActiveHeroIndex >= 0)
        {
            var currentHero = state.Heroes[state.ActiveHeroIndex];
            if (currentHero.ActionsRemaining > 0)
                return state;
        }

        // Current hero finished (or no active hero) - check if any heroes still have actions
        bool anyHeroesRemaining = state.Heroes.Any(h =>
            h.Status != HeroStatus.Dead && !h.HasExited && h.ActionsRemaining > 0);

        if (anyHeroesRemaining)
        {
            // Reset to hero selection
            return state with { ActiveHeroIndex = -1, ActiveHeroHasMoved = false };
        }

        // All heroes done - move to monster phase
        return state with
        {
            CurrentPhase = Phase.MonsterAction,
            ActiveHeroIndex = -1,
            ActiveHeroHasMoved = false
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
