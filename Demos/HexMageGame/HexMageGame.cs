using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mcts;

namespace MageGame;

/// <summary>
/// HEX GRID tactical combat game - like MageGame but on hexagons with terrain!
/// Uses axial coordinates (q, r) for hex positions.
/// Terrain: Water (impassable), Hill (+defense, costs 2 AP), Tree (+defense, blocks LOS),
///          TreeOnHill (+3 defense, costs 2 AP, blocks LOS), Building (+3 defense, blocks LOS, costs 2 AP)
/// Walls exist on edges between hexes.
/// </summary>

// ============== ENUMS ==============

public enum HeroClass { Warrior, Mage, Rogue }

public enum HeroStatus { Healthy, Injured, Dead }

public enum Phase { HeroAction, MonsterAction, MonsterSpawn }

public enum HexActionType
{
    ActivateHero,
    MoveNE, MoveE, MoveSE, MoveSW, MoveW, MoveNW,  // Six hex directions
    Attack,           // Regular heroes only
    Cast,             // Mage only - roll 1d6 for spell points
    ZapMonster,       // Mage only - weak, 9+ to hit, costs 2 SP
    FireballMonster,  // Mage only - strong, 6+ to hit, costs 5 SP
    TeleportHero,     // Mage only - costs 4 SP
    EndTurn
}

// ============== HEX COORDINATE SYSTEM ==============

/// <summary>
/// Axial coordinates for hex grid. q = column, r = row.
/// Six directions: NE(+1,-1), E(+1,0), SE(0,+1), SW(-1,+1), W(-1,0), NW(0,-1)
/// </summary>
public record HexCoord(int Q, int R)
{
    // Six hex directions (clockwise from NE)
    public static readonly HexCoord[] Directions = new[]
    {
        new HexCoord(+1, -1),  // NE
        new HexCoord(+1,  0),  // E
        new HexCoord( 0, +1),  // SE
        new HexCoord(-1, +1),  // SW
        new HexCoord(-1,  0),  // W
        new HexCoord( 0, -1)   // NW
    };

    public static HexCoord operator +(HexCoord a, HexCoord b) => new(a.Q + b.Q, a.R + b.R);
    
    /// <summary>Hex distance (number of hex steps)</summary>
    public int DistanceTo(HexCoord other)
    {
        int dq = Math.Abs(Q - other.Q);
        int dr = Math.Abs(R - other.R);
        int ds = Math.Abs(Q + R - other.Q - other.R);
        return (dq + dr + ds) / 2;
    }

    /// <summary>Get all hexes within given distance (ring)</summary>
    public IEnumerable<HexCoord> HexesInRange(int range)
    {
        for (int q = -range; q <= range; q++)
        {
            for (int r = Math.Max(-range, -q - range); r <= Math.Min(range, -q + range); r++)
            {
                yield return new HexCoord(Q + q, R + r);
            }
        }
    }

    /// <summary>Get normalized edge identifier for wall storage (edges between this hex and neighbor in direction)</summary>
    public (HexCoord, HexCoord) GetEdge(int direction)
    {
        var neighbor = this + Directions[direction];
        // Normalize edge so same edge always has same representation
        return (Q < neighbor.Q || (Q == neighbor.Q && R < neighbor.R)) 
            ? (this, neighbor) 
            : (neighbor, this);
    }
}

// ============== RECORDS ==============

public record HexHero(
    int Index,
    HeroClass Class,
    HexCoord Position,
    HeroStatus Status,
    int AttackScore,
    int Range,
    int ActionsRemaining,
    int ZapRange,
    int TeleportRange,
    bool HasExited,
    int SpellPoints,      // Mage only
    bool HasCast          // Mage only
)
{
    public bool IsAlive => Status != HeroStatus.Dead;
}

public record HexMonster(
    int Index,
    HexCoord Position,
    bool IsAlive
);

public record HexAction(
    HexActionType Type,
    int TargetIndex = -1,
    HexCoord? TargetPosition = null
);

public record PendingHexAttack(int AttackerIndex, int DefenderIndex, int AttackScore);

public record HexGameState(
    ImmutableList<HexHero> Heroes,
    ImmutableList<HexMonster> Monsters,
    int TurnCount,
    int ActiveHeroIndex,
    Phase CurrentPhase,
    HexCoord ExitPosition,
    double AccumulatedReward,
    ImmutableHashSet<HexCoord> WaterHexes,
    ImmutableHashSet<HexCoord> HillHexes,
    ImmutableHashSet<HexCoord> TreeHexes,
    ImmutableHashSet<HexCoord> BuildingHexes,
    ImmutableHashSet<(HexCoord, HexCoord)> Walls,
    PendingHexAttack? AttackResolution,
    bool ActiveHeroHasMoved,
    int? PendingCastHeroIndex
);

// ============== GAME LOGIC ==============

public class HexTacticalGame : IGameModel<HexGameState, HexAction>
{
    private readonly int _maxTurns;

    public HexTacticalGame(int maxTurns = 15)
    {
        _maxTurns = maxTurns;
    }

    public HexGameState InitialState()
    {
        // Create separate sets for each terrain feature
        var waterHexes = new HashSet<HexCoord>
        {
            new HexCoord(1, 1),
            new HexCoord(2, 1),
            new HexCoord(1, 2)
        };

        var hillHexes = new HashSet<HexCoord>
        {
            new HexCoord(3, 2),
            new HexCoord(4, 3),
            new HexCoord(5, 2)  // Hill with tree
        };

        var treeHexes = new HashSet<HexCoord>
        {
            new HexCoord(2, 4),
            new HexCoord(3, 5),
            new HexCoord(5, 2)  // Tree on hill
        };

        var buildingHexes = new HashSet<HexCoord>
        {
            new HexCoord(2, 3),
            new HexCoord(4, 4)
        };

        // Add walls (on edges between hexes)
        var walls = new HashSet<(HexCoord, HexCoord)>
        {
            new HexCoord(2, 2).GetEdge(1),  // Wall east of (2,2)
            new HexCoord(3, 3).GetEdge(2)   // Wall southeast of (3,3)
        };

        // Create heroes (all start at origin)
        var heroes = ImmutableList.Create(
            new HexHero(0, HeroClass.Warrior, new HexCoord(0, 3), HeroStatus.Healthy, 
                AttackScore: 6, Range: 1, ActionsRemaining: 2, ZapRange: 0, TeleportRange: 0,
                HasExited: false, SpellPoints: 0, HasCast: false),
            
            new HexHero(1, HeroClass.Mage, new HexCoord(0, 3), HeroStatus.Healthy,
                AttackScore: 0, Range: 0, ActionsRemaining: 2, ZapRange: 2, TeleportRange: 2,
                HasExited: false, SpellPoints: 0, HasCast: false),
            
            new HexHero(2, HeroClass.Rogue, new HexCoord(0, 4), HeroStatus.Healthy,
                AttackScore: 7, Range: 1, ActionsRemaining: 2, ZapRange: 0, TeleportRange: 0,
                HasExited: false, SpellPoints: 0, HasCast: false)
        );

        // Initial monster
        var monsters = ImmutableList.Create(
            new HexMonster(0, new HexCoord(3, 4), IsAlive: true)
        );

        return new HexGameState(
            Heroes: heroes,
            Monsters: monsters,
            TurnCount: 1,
            ActiveHeroIndex: -1,
            CurrentPhase: Phase.HeroAction,
            ExitPosition: new HexCoord(6, 6),
            AccumulatedReward: 0,
            WaterHexes: waterHexes.ToImmutableHashSet(),
            HillHexes: hillHexes.ToImmutableHashSet(),
            TreeHexes: treeHexes.ToImmutableHashSet(),
            BuildingHexes: buildingHexes.ToImmutableHashSet(),
            Walls: walls.ToImmutableHashSet(),
            AttackResolution: null,
            ActiveHeroHasMoved: false,
            PendingCastHeroIndex: null
        );
    }

    public bool IsTerminal(in HexGameState state, out double terminalValue)
    {
        if (state.TurnCount > _maxTurns)
        {
            terminalValue = state.AccumulatedReward;
            return true;
        }

        var aliveHeroes = state.Heroes.Count(h => h.IsAlive && !h.HasExited);
        bool allExitedOrDead = state.Heroes.All(h => h.HasExited || h.Status == HeroStatus.Dead);
        
        if (aliveHeroes == 0 || allExitedOrDead)
        {
            terminalValue = state.AccumulatedReward;
            return true;
        }

        terminalValue = 0;
        return false;
    }

    public bool IsChanceNode(in HexGameState state)
    {
        return state.AttackResolution != null || state.PendingCastHeroIndex != null;
    }

    public IEnumerable<(HexGameState outcome, double probability)> ChanceOutcomes(HexGameState state)
    {
        // Attack resolution (d6 roll)
        if (state.AttackResolution != null)
        {
            for (int roll = 1; roll <= 6; roll++)
            {
                var newState = ResolveAttack(state, roll);
                yield return (newState, 1.0 / 6.0);
            }
            yield break;
        }

        // Cast resolution (d6 roll for spell points)
        if (state.PendingCastHeroIndex != null)
        {
            for (int roll = 1; roll <= 6; roll++)
            {
                var newState = ResolveCast(state, roll);
                yield return (newState, 1.0 / 6.0);
            }
        }
    }

    public IEnumerable<HexAction> LegalActions(HexGameState state)
    {
        var actions = new List<HexAction>();

        // Terminal state
        if (IsTerminal(state, out _))
            return actions;

        // Monster phase
        if (state.CurrentPhase == Phase.MonsterAction)
        {
            return new[] { new HexAction(HexActionType.EndTurn) };
        }

        // Spawn phase
        if (state.CurrentPhase == Phase.MonsterSpawn)
        {
            return new[] { new HexAction(HexActionType.EndTurn) };
        }

        // Hero phase - no active hero means choose who to activate
        if (state.ActiveHeroIndex == -1)
        {
            for (int i = 0; i < state.Heroes.Count; i++)
            {
                var hero = state.Heroes[i];
                if (hero.IsAlive && !hero.HasExited && (hero.ActionsRemaining > 0 || hero.SpellPoints > 0))
                {
                    actions.Add(new HexAction(HexActionType.ActivateHero, TargetIndex: i));
                }
            }
            
            if (actions.Count == 0)
                actions.Add(new HexAction(HexActionType.EndTurn));
            
            return actions;
        }

        // Active hero actions
        var activeHero = state.Heroes[state.ActiveHeroIndex];
        
        // Movement actions (if hero hasn't moved and has AP)
        if (!state.ActiveHeroHasMoved && activeHero.ActionsRemaining > 0)
        {
            for (int dir = 0; dir < 6; dir++)
            {
                var newPos = activeHero.Position + HexCoord.Directions[dir];
                
                // Check walls
                if (state.Walls.Contains(activeHero.Position.GetEdge(dir)))
                    continue;

                // Check terrain (water is impassable)
                if (state.WaterHexes.Contains(newPos))
                    continue;

                // All non-water hexes cost 1 AP
                if (activeHero.ActionsRemaining < 1)
                    continue;

                actions.Add(new HexAction((HexActionType)(HexActionType.MoveNE + dir)));
            }
        }

        // Attack (regular heroes only)
        if (activeHero.Class != HeroClass.Mage && activeHero.ActionsRemaining > 0)
        {
            foreach (var monster in state.Monsters.Where(m => m.IsAlive))
            {
                int distance = activeHero.Position.DistanceTo(monster.Position);
                if (distance <= activeHero.Range && HasLineOfSight(state, activeHero.Position, monster.Position))
                {
                    actions.Add(new HexAction(HexActionType.Attack, TargetIndex: monster.Index));
                }
            }
        }

        // Mage spells
        if (activeHero.Class == HeroClass.Mage)
        {
            // Cast (once per activation, costs 1 AP)
            if (!activeHero.HasCast && activeHero.ActionsRemaining > 0)
            {
                actions.Add(new HexAction(HexActionType.Cast));
            }

            // Zap (costs 2 SP, range 2, requires LOS)
            if (activeHero.SpellPoints >= 2)
            {
                foreach (var monster in state.Monsters.Where(m => m.IsAlive))
                {
                    int distance = activeHero.Position.DistanceTo(monster.Position);
                    if (distance > 0 && distance <= activeHero.ZapRange && 
                        HasLineOfSight(state, activeHero.Position, monster.Position))
                    {
                        actions.Add(new HexAction(HexActionType.ZapMonster, TargetIndex: monster.Index));
                    }
                }
            }

            // Fireball (costs 5 SP, range 2, requires LOS)
            if (activeHero.SpellPoints >= 5)
            {
                foreach (var monster in state.Monsters.Where(m => m.IsAlive))
                {
                    int distance = activeHero.Position.DistanceTo(monster.Position);
                    if (distance > 0 && distance <= activeHero.ZapRange && 
                        HasLineOfSight(state, activeHero.Position, monster.Position))
                    {
                        actions.Add(new HexAction(HexActionType.FireballMonster, TargetIndex: monster.Index));
                    }
                }
            }

            // Teleport (costs 4 SP, range 2)
            if (activeHero.SpellPoints >= 4)
            {
                // Can teleport self or co-located heroes
                var teleportTargets = new List<int> { activeHero.Index };
                for (int i = 0; i < state.Heroes.Count; i++)
                {
                    if (i != activeHero.Index && state.Heroes[i].Position == activeHero.Position 
                        && state.Heroes[i].IsAlive && !state.Heroes[i].HasExited)
                    {
                        teleportTargets.Add(i);
                    }
                }

                foreach (int heroIdx in teleportTargets)
                {
                    foreach (var destPos in activeHero.Position.HexesInRange(activeHero.TeleportRange))
                    {
                        // Skip current position
                        if (destPos == activeHero.Position)
                            continue;

                        // Check terrain (can't teleport into water)
                        if (state.WaterHexes.Contains(destPos))
                            continue;

                        actions.Add(new HexAction(HexActionType.TeleportHero, 
                            TargetIndex: heroIdx, 
                            TargetPosition: destPos));
                    }
                }
            }
        }

        // End turn
        actions.Add(new HexAction(HexActionType.EndTurn));
        return actions;
    }

    public HexGameState Step(in HexGameState state, in HexAction action)
    {
        var newState = state;

        switch (action.Type)
        {
            case HexActionType.ActivateHero:
                var heroToActivate = newState.Heroes[action.TargetIndex];
                newState = newState with
                {
                    ActiveHeroIndex = action.TargetIndex,
                    ActiveHeroHasMoved = false,
                    Heroes = newState.Heroes.SetItem(action.TargetIndex, heroToActivate with
                    {
                        SpellPoints = 0,
                        HasCast = false
                    })
                };
                break;

            case HexActionType.MoveNE:
            case HexActionType.MoveE:
            case HexActionType.MoveSE:
            case HexActionType.MoveSW:
            case HexActionType.MoveW:
            case HexActionType.MoveNW:
                int direction = action.Type - HexActionType.MoveNE;
                var hero = newState.Heroes[newState.ActiveHeroIndex];
                var newPos = hero.Position + HexCoord.Directions[direction];
                
                // All non-water hexes cost 1 AP
                int apCost = 1;

                // Check if exiting
                bool exitingNow = (newPos == newState.ExitPosition);
                
                // Clear spell points on movement
                int newSpellPoints = hero.SpellPoints > 0 ? 0 : hero.SpellPoints;

                newState = newState with
                {
                    Heroes = newState.Heroes.SetItem(newState.ActiveHeroIndex, hero with
                    {
                        Position = newPos,
                        ActionsRemaining = hero.ActionsRemaining - apCost,
                        HasExited = exitingNow || hero.HasExited,
                        SpellPoints = newSpellPoints
                    }),
                    ActiveHeroHasMoved = true,
                    AccumulatedReward = newState.AccumulatedReward + (exitingNow ? 0 : 0.05),
                    ActiveHeroIndex = exitingNow ? -1 : newState.ActiveHeroIndex
                };

                if (exitingNow)
                {
                    newState = newState with
                    {
                        AccumulatedReward = newState.AccumulatedReward + 
                            (hero.Status == HeroStatus.Healthy ? 15 : 10)
                    };
                }
                break;

            case HexActionType.Attack:
                var attacker = newState.Heroes[newState.ActiveHeroIndex];
                var targetPos = newState.Monsters[action.TargetIndex].Position;
                
                // Calculate attack modifier based on terrain and positioning
                int modifier = GetAttackModifier(newState, attacker.Position, targetPos);
                int effectiveAttackScore = Math.Max(2, attacker.AttackScore + modifier);

                newState = newState with
                {
                    AttackResolution = new PendingHexAttack(newState.ActiveHeroIndex, action.TargetIndex, effectiveAttackScore),
                    Heroes = newState.Heroes.SetItem(newState.ActiveHeroIndex, attacker with
                    {
                        ActionsRemaining = attacker.ActionsRemaining - 1
                    })
                };
                break;

            case HexActionType.Cast:
                var caster = newState.Heroes[newState.ActiveHeroIndex];
                newState = newState with
                {
                    PendingCastHeroIndex = newState.ActiveHeroIndex,
                    Heroes = newState.Heroes.SetItem(newState.ActiveHeroIndex, caster with { HasCast = true })
                };
                break;

            case HexActionType.ZapMonster:
                var zapper = newState.Heroes[newState.ActiveHeroIndex];
                var zapTarget = newState.Monsters[action.TargetIndex].Position;
                int zapModifier = GetAttackModifier(newState, zapper.Position, zapTarget);
                int zapAttackScore = Math.Max(2, 9 + zapModifier);
                
                newState = newState with
                {
                    AttackResolution = new PendingHexAttack(newState.ActiveHeroIndex, action.TargetIndex, zapAttackScore),
                    Heroes = newState.Heroes.SetItem(newState.ActiveHeroIndex, zapper with
                    {
                        SpellPoints = zapper.SpellPoints - 2
                    })
                };
                break;

            case HexActionType.FireballMonster:
                var fireballHero = newState.Heroes[newState.ActiveHeroIndex];
                var fireballTarget = newState.Monsters[action.TargetIndex].Position;
                int fireballModifier = GetAttackModifier(newState, fireballHero.Position, fireballTarget);
                int fireballAttackScore = Math.Max(2, 6 + fireballModifier);
                
                newState = newState with
                {
                    AttackResolution = new PendingHexAttack(newState.ActiveHeroIndex, action.TargetIndex, fireballAttackScore),
                    Heroes = newState.Heroes.SetItem(newState.ActiveHeroIndex, fireballHero with
                    {
                        SpellPoints = fireballHero.SpellPoints - 5
                    })
                };
                break;

            case HexActionType.TeleportHero:
                var teleporter = newState.Heroes[newState.ActiveHeroIndex];
                var teleportTarget = newState.Heroes[action.TargetIndex];
                
                // Check if teleporting to exit
                bool teleportingToExit = (action.TargetPosition == newState.ExitPosition);
                
                newState = newState with
                {
                    Heroes = newState.Heroes.SetItem(action.TargetIndex, teleportTarget with
                    {
                        Position = action.TargetPosition!,
                        HasExited = teleportingToExit || teleportTarget.HasExited
                    })
                };

                if (teleportingToExit)
                {
                    newState = newState with
                    {
                        AccumulatedReward = newState.AccumulatedReward + 
                            (teleportTarget.Status == HeroStatus.Healthy ? 15 : 10)
                    };
                    
                    // If teleported self, deactivate
                    if (action.TargetIndex == newState.ActiveHeroIndex)
                    {
                        newState = newState with { ActiveHeroIndex = -1 };
                    }
                }

                // Deduct spell points from caster
                var teleporterAfter = newState.Heroes[newState.ActiveHeroIndex];
                newState = newState with
                {
                    Heroes = newState.Heroes.SetItem(newState.ActiveHeroIndex, teleporterAfter with
                    {
                        SpellPoints = teleporterAfter.SpellPoints - 4
                    })
                };
                break;

            case HexActionType.EndTurn:
                if (newState.CurrentPhase == Phase.HeroAction)
                {
                    // Clear active hero
                    if (newState.ActiveHeroIndex != -1)
                    {
                        var endingHero = newState.Heroes[newState.ActiveHeroIndex];
                        newState = newState with
                        {
                            Heroes = newState.Heroes.SetItem(newState.ActiveHeroIndex, endingHero with
                            {
                                SpellPoints = 0
                            }),
                            ActiveHeroIndex = -1
                        };
                    }

                    // Check if any heroes can still act
                    bool anyCanAct = newState.Heroes.Any(h => 
                        h.IsAlive && !h.HasExited && (h.ActionsRemaining > 0 || h.SpellPoints > 0));

                    if (!anyCanAct)
                    {
                        newState = newState with { CurrentPhase = Phase.MonsterAction };
                    }
                }
                else if (newState.CurrentPhase == Phase.MonsterAction)
                {
                    newState = ProcessMonsterPhase(newState);
                    newState = newState with { CurrentPhase = Phase.MonsterSpawn };
                }
                else if (newState.CurrentPhase == Phase.MonsterSpawn)
                {
                    newState = ProcessSpawnPhase(newState);
                    newState = StartNewTurn(newState);
                }
                break;
        }

        return newState;
    }

    // ============== HELPER METHODS ==============

    private int GetAttackModifier(HexGameState state, HexCoord attackerPos, HexCoord targetPos)
    {
        int modifier = 0;

        // Attacker on hill, target not: -1 (easier to hit)
        bool attackerOnHill = state.HillHexes.Contains(attackerPos);
        bool targetOnHill = state.HillHexes.Contains(targetPos);
        if (attackerOnHill && !targetOnHill)
            modifier -= 1;

        // Attacking on or through trees: +1 (harder to hit)
        bool attackerInTrees = state.TreeHexes.Contains(attackerPos);
        bool targetInTrees = state.TreeHexes.Contains(targetPos);
        if (attackerInTrees || targetInTrees)
            modifier += 1;

        // Target in building: +1 (harder to hit)
        if (state.BuildingHexes.Contains(targetPos))
            modifier += 1;

        // Adjacent allies with LOS (flanking bonus): -1 each
        var attacker = state.Heroes.FirstOrDefault(h => h.Position == attackerPos);
        if (attacker != null)
        {
            foreach (var ally in state.Heroes.Where(h => h.IsAlive && h.Index != attacker.Index))
            {
                // Check if ally is adjacent to target
                if (ally.Position.DistanceTo(targetPos) == 1)
                {
                    // Check if ally has LOS to target
                    if (HasLineOfSight(state, ally.Position, targetPos))
                    {
                        // Check if ally is not in attacker's hex
                        if (ally.Position != attackerPos)
                        {
                            modifier -= 1;
                        }
                    }
                }
            }
        }

        return modifier;
    }

    private bool HasLineOfSight(HexGameState state, HexCoord from, HexCoord to)
    {
        int distance = from.DistanceTo(to);
        if (distance <= 1) return true;  // Adjacent always has LOS

        // LOS only works along hex axes (not diagonal)
        // Check if the path is along a single axis direction
        int deltaQ = to.Q - from.Q;
        int deltaR = to.R - from.R;

        // Check if it's along one of the 6 hex directions
        int dirIndex = -1;
        for (int i = 0; i < 6; i++)
        {
            var direction = HexCoord.Directions[i];
            // Check if delta is a multiple of this direction
            if (deltaQ != 0 && direction.Q != 0 && deltaR != 0 && direction.R != 0)
            {
                // Both components non-zero - check if they're proportional
                if (deltaQ * direction.R == deltaR * direction.Q)
                {
                    dirIndex = i;
                    break;
                }
            }
            else if (deltaQ == 0 && direction.Q == 0 && deltaR != 0 && direction.R != 0)
            {
                // Q is zero for both
                dirIndex = i;
                break;
            }
            else if (deltaR == 0 && direction.R == 0 && deltaQ != 0 && direction.Q != 0)
            {
                // R is zero for both
                dirIndex = i;
                break;
            }
        }

        // If not along an axis, no LOS
        if (dirIndex == -1)
            return false;

        // Trace along the axis checking for walls, buildings, and hills
        var dir = HexCoord.Directions[dirIndex];
        var current = from;

        bool fromOnHill = state.HillHexes.Contains(from);
        bool toOnHill = state.HillHexes.Contains(to);

        for (int i = 1; i < distance; i++)
        {
            var next = current + dir;

            // Check for wall blocking LOS
            if (state.Walls.Contains(current.GetEdge(dirIndex)))
                return false;

            // Buildings block LOS through (but not into the target hex)
            if (i < distance - 1 && state.BuildingHexes.Contains(next))
                return false;

            // Hills block LOS between ground hexes
            bool nextOnHill = state.HillHexes.Contains(next);
            if (nextOnHill && !fromOnHill && !toOnHill)
                return false;

            current = next;
        }

        return true;
    }

    private HexGameState ResolveAttack(HexGameState state, int roll)
    {
        var attack = state.AttackResolution!;
        
        var newState = state with { AttackResolution = null };
        
        if (roll >= attack.AttackScore)
        {
            // Hit! Monster dies
            var deadMonster = newState.Monsters[attack.DefenderIndex];
            newState = newState with
            {
                Monsters = newState.Monsters.SetItem(attack.DefenderIndex, 
                    deadMonster with { IsAlive = false }),
                AccumulatedReward = newState.AccumulatedReward + 5
            };
        }

        return AdvanceToNextPhaseOrHero(newState);
    }

    private HexGameState ResolveCast(HexGameState state, int roll)
    {
        var heroIdx = state.PendingCastHeroIndex!.Value;
        var hero = state.Heroes[heroIdx];
        
        // Roll 1 = miscast (0 points), 2-6 = that many points
        int spellPoints = (roll == 1) ? 0 : roll;
        
        var newState = state with
        {
            Heroes = state.Heroes.SetItem(heroIdx, hero with
            {
                SpellPoints = spellPoints,
                ActionsRemaining = hero.ActionsRemaining - 1
            }),
            PendingCastHeroIndex = null
        };

        return AdvanceToNextPhaseOrHero(newState);
    }

    private HexGameState AdvanceToNextPhaseOrHero(HexGameState state)
    {
        if (state.ActiveHeroIndex == -1)
            return state;

        var hero = state.Heroes[state.ActiveHeroIndex];
        
        // Check if hero can still act
        bool canAct = hero.ActionsRemaining > 0 || hero.SpellPoints > 0;
        
        if (!canAct || hero.HasExited)
        {
            return state with { ActiveHeroIndex = -1 };
        }

        return state;
    }

    private HexGameState ProcessMonsterPhase(HexGameState state)
    {
        // Monsters attack adjacent heroes
        foreach (var monster in state.Monsters.Where(m => m.IsAlive))
        {
            for (int i = 0; i < state.Heroes.Count; i++)
            {
                var hero = state.Heroes[i];
                if (!hero.IsAlive || hero.HasExited)
                    continue;

                int distance = monster.Position.DistanceTo(hero.Position);
                if (distance == 1)  // Adjacent
                {
                    // Monster attacks (50% chance to hit)
                    if (Random.Shared.Next(2) == 0)
                    {
                        var newStatus = hero.Status == HeroStatus.Healthy 
                            ? HeroStatus.Injured 
                            : HeroStatus.Dead;

                        state = state with
                        {
                            Heroes = state.Heroes.SetItem(i, hero with { Status = newStatus }),
                            AccumulatedReward = state.AccumulatedReward - 5
                        };
                    }
                }
            }
        }

        return state;
    }

    private HexGameState ProcessSpawnPhase(HexGameState state)
    {
        // 80% chance to spawn
        if (Random.Shared.NextDouble() > 0.8)
            return state;

        // Find valid spawn positions (not water, not occupied, within map bounds)
        var validPositions = new List<HexCoord>();
        
        for (int q = 0; q <= 6; q++)
        {
            for (int r = 0; r <= 6; r++)
            {
                var pos = new HexCoord(q, r);
                
                // Skip water hexes
                if (state.WaterHexes.Contains(pos))
                    continue;
            
                // Check not occupied
                bool occupied = state.Heroes.Any(h => h.IsAlive && !h.HasExited && h.Position == pos) ||
                               state.Monsters.Any(m => m.IsAlive && m.Position == pos);
            
                if (!occupied && pos != state.ExitPosition)
                    validPositions.Add(pos);
            }
        }

        if (validPositions.Count == 0)
            return state;

        // Prioritize positions near exit (distance <= 4)
        var nearExit = validPositions.Where(p => p.DistanceTo(state.ExitPosition) <= 4).ToList();
        var spawnPos = nearExit.Count > 0 
            ? nearExit[Random.Shared.Next(nearExit.Count)]
            : validPositions[Random.Shared.Next(validPositions.Count)];

        var newMonster = new HexMonster(state.Monsters.Count, spawnPos, true);
        
        return state with
        {
            Monsters = state.Monsters.Add(newMonster)
        };
    }

    private HexGameState StartNewTurn(HexGameState state)
    {
        var newHeroes = state.Heroes;
        for (int i = 0; i < newHeroes.Count; i++)
        {
            var hero = newHeroes[i];
            if (hero.IsAlive && !hero.HasExited)
            {
                newHeroes = newHeroes.SetItem(i, hero with { ActionsRemaining = 2 });
            }
        }

        return state with
        {
            Heroes = newHeroes,
            TurnCount = state.TurnCount + 1,
            CurrentPhase = Phase.HeroAction,
            ActiveHeroIndex = -1
        };
    }

    // ============== DISPLAY ==============

    public static string DisplayState(HexGameState state)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- Turn {state.TurnCount} | Phase: {state.CurrentPhase} | ActiveHero: {state.ActiveHeroIndex} ---");
        
        // Display hex grid (offset for readability)
        // Determine map bounds from all terrain hexes
        int minQ = 0, maxQ = 6, minR = 0, maxR = 6;

        foreach (var h in state.Heroes.Where(h => h.IsAlive && !h.HasExited))
        {
            minQ = Math.Min(minQ, h.Position.Q);
            maxQ = Math.Max(maxQ, h.Position.Q);
            minR = Math.Min(minR, h.Position.R);
            maxR = Math.Max(maxR, h.Position.R);
        }

        foreach (var m in state.Monsters.Where(m => m.IsAlive))
        {
            minQ = Math.Min(minQ, m.Position.Q);
            maxQ = Math.Max(maxQ, m.Position.Q);
            minR = Math.Min(minR, m.Position.R);
            maxR = Math.Max(maxR, m.Position.R);
        }

        for (int r = minR; r <= maxR; r++)
        {
            // Offset even rows for hex display
            if (r % 2 == 0) sb.Append(" ");
            
            for (int q = minQ; q <= maxQ; q++)
            {
                var coord = new HexCoord(q, r);
                char c = GetDisplayChar(state, coord);
                sb.Append(c);
                sb.Append(' ');
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Heroes:");
        for (int i = 0; i < state.Heroes.Count; i++)
        {
            var h = state.Heroes[i];
            if (h.HasExited)
                sb.AppendLine($"  {i}: {h.Class} EXITED {h.Status}");
            else
                sb.AppendLine($"  {i}: {h.Class} at ({h.Position.Q},{h.Position.R}) {h.Status} AP:{h.ActionsRemaining}" +
                    (h.Class == HeroClass.Mage ? $" SP:{h.SpellPoints}" : ""));
        }

        sb.AppendLine($"Monsters: {state.Monsters.Count(m => m.IsAlive)} alive / {state.Monsters.Count} total");
        sb.AppendLine($"Accumulated Reward: {state.AccumulatedReward:F2}");

        return sb.ToString();
    }

    private static char GetDisplayChar(HexGameState state, HexCoord coord)
    {
        // Heroes (living, not exited)
        for (int i = 0; i < state.Heroes.Count; i++)
        {
            var h = state.Heroes[i];
            if (h.IsAlive && !h.HasExited && h.Position == coord)
            {
                return h.Class switch
                {
                    HeroClass.Warrior => 'W',
                    HeroClass.Mage => 'M',
                    HeroClass.Rogue => 'R',
                    _ => '?'
                };
            }
        }

        // Monsters
        var monster = state.Monsters.FirstOrDefault(m => m.IsAlive && m.Position == coord);
        if (monster != null)
            return 'r';

        // Exit
        if (coord == state.ExitPosition)
            return 'E';

        // Terrain - check for combinations
        bool isWater = state.WaterHexes.Contains(coord);
        bool isHill = state.HillHexes.Contains(coord);
        bool isTree = state.TreeHexes.Contains(coord);
        bool isBuilding = state.BuildingHexes.Contains(coord);

        if (isWater) return '~';
        if (isHill && isTree) return 'T';  // Tree on hill
        if (isHill && isBuilding) return 'B';  // Building on hill
        if (isTree) return '*';
        if (isBuilding) return '#';
        if (isHill) return '^';

        return '.';  // Ground
    }
}
