using System;
using System.Collections.Generic;
using System.Linq;
using GenericMcts;

namespace TinyQuestDemo
{
    // =============================================================
    // TinyQuest: A cooperative hex-based adventure game for MCTS
    // =============================================================

    public enum HeroType { Warrior, Elf, Thief, Mage }

    public enum QuestAction
    {
        // Hero activation selection
        ActivateWarrior,
        ActivateElf,
        ActivateThief,
        ActivateMage,

        // Movement actions (30 hexes in 5x6 grid)
        MoveToHex0, MoveToHex1, MoveToHex2, MoveToHex3, MoveToHex4,
        MoveToHex5, MoveToHex6, MoveToHex7, MoveToHex8, MoveToHex9,
        MoveToHex10, MoveToHex11, MoveToHex12, MoveToHex13, MoveToHex14,
        MoveToHex15, MoveToHex16, MoveToHex17, MoveToHex18, MoveToHex19,
        MoveToHex20, MoveToHex21, MoveToHex22, MoveToHex23, MoveToHex24,
        MoveToHex25, MoveToHex26, MoveToHex27, MoveToHex28, MoveToHex29,

        // Other actions
        OpenChest,
        EndActivation,

        // Chance outcomes (for chest item selection)
        GiveItem1,
        GiveItem2,
        GiveItem3,
        GiveNothing
    }

    public readonly record struct Hero(
        HeroType Type,
        int CurrentHex,   // Which hex the hero is on (0-29), -1 if dead/exited
        bool HasExited,   // Has this hero reached the exit?
        bool IsDead,      // Has this hero died?
        bool IsInjured,   // Is this hero injured?
        bool HasMoved,    // Has this hero moved this activation?
                          // Special items (3 per hero)
        bool HasItem1,
        bool HasItem2,
        bool HasItem3
    );

    public readonly record struct QuestState(
        Hero Warrior,
        Hero Elf,
        Hero Thief,
        Hero Mage,
        int ActiveHeroIndex, // 0 = Warrior, 1 = Elf, 2 = Thief, 3 = Mage, -1 = need to select hero
        int ActionsRemaining, // Actions remaining for active hero (max 2)
        int ExitHex,          // Which hex is the exit (0-29)
        int Chest0Hex,        // Location of chest 0 (0-29)
        int Chest1Hex,        // Location of chest 1 (0-29)
        int Chest2Hex,        // Location of chest 2 (0-29)
        int TurnCount,        // Number of complete turns (all heroes activated)
        bool WarriorActivatedThisTurn, // Has warrior activated this turn?
        bool ElfActivatedThisTurn,     // Has elf activated this turn?
        bool ThiefActivatedThisTurn,   // Has thief activated this turn?
        bool MageActivatedThisTurn,    // Has mage activated this turn?
                                       // Chest availability (contents determined by who opens them via chance nodes)
        bool Chest0Present,
        bool Chest1Present,
        bool Chest2Present,
        bool PendingChestItem // Are we waiting for a chance node to determine chest item?
    );

    public sealed class TinyQuestGame : IGameModel<QuestState, QuestAction>
    {
        private const int MaxTurns = 20;
        private const double ExitReward = 1.0;      // Reward for survival
        private const double ItemReward = 0.5;      // Reward per special item collected
        private const double InjuryPenalty = -0.3;  // Penalty for being injured
        private const double DeathPenalty = -1.0;   // Penalty for death

        public bool IsTerminal(in QuestState s, out double terminalValue)
        {
            // Game ends if all heroes have exited or died
            bool allGone = (s.Warrior.HasExited || s.Warrior.IsDead) &&
                          (s.Elf.HasExited || s.Elf.IsDead) &&
                          (s.Thief.HasExited || s.Thief.IsDead) &&
                          (s.Mage.HasExited || s.Mage.IsDead);

            if (allGone)
            {
                terminalValue = CalculateScore(s);
                return true;
            }

            // Game ends after max turns
            if (s.TurnCount >= MaxTurns)
            {
                terminalValue = CalculateScore(s);
                return true;
            }

            terminalValue = 0;
            return false;
        }

        private double CalculateScore(QuestState s)
        {
            double score = 0.0;

            // Warrior scoring
            if (s.Warrior.HasExited) score += ExitReward;
            if (s.Warrior.IsDead) score += DeathPenalty;
            if (s.Warrior.IsInjured) score += InjuryPenalty;
            if (s.Warrior.HasItem1) score += ItemReward;
            if (s.Warrior.HasItem2) score += ItemReward;
            if (s.Warrior.HasItem3) score += ItemReward;

            // Elf scoring
            if (s.Elf.HasExited) score += ExitReward;
            if (s.Elf.IsDead) score += DeathPenalty;
            if (s.Elf.IsInjured) score += InjuryPenalty;
            if (s.Elf.HasItem1) score += ItemReward;
            if (s.Elf.HasItem2) score += ItemReward;
            if (s.Elf.HasItem3) score += ItemReward;

            // Thief scoring
            if (s.Thief.HasExited) score += ExitReward;
            if (s.Thief.IsDead) score += DeathPenalty;
            if (s.Thief.IsInjured) score += InjuryPenalty;
            if (s.Thief.HasItem1) score += ItemReward;
            if (s.Thief.HasItem2) score += ItemReward;
            if (s.Thief.HasItem3) score += ItemReward;

            // Mage scoring
            if (s.Mage.HasExited) score += ExitReward;
            if (s.Mage.IsDead) score += DeathPenalty;
            if (s.Mage.IsInjured) score += InjuryPenalty;
            if (s.Mage.HasItem1) score += ItemReward;
            if (s.Mage.HasItem2) score += ItemReward;
            if (s.Mage.HasItem3) score += ItemReward;

            return score;
        }

        public bool IsChanceNode(in QuestState s)
        {
            // We're in a chance node if we're waiting to determine what item a chest gives
            return s.PendingChestItem;
        }

        public QuestState SampleChance(in QuestState s, Random rng, out double logProb)
        {
            if (!s.PendingChestItem)
            {
                logProb = 0.0;
                return s;
            }

            // Determine which items the active hero doesn't have
            var activeHero = GetActiveHero(s);
            var availableItems = new List<QuestAction>();

            if (!activeHero.HasItem1) availableItems.Add(QuestAction.GiveItem1);
            if (!activeHero.HasItem2) availableItems.Add(QuestAction.GiveItem2);
            if (!activeHero.HasItem3) availableItems.Add(QuestAction.GiveItem3);

            if (availableItems.Count == 0)
            {
                // Hero has all items, give nothing
                logProb = 0.0; // log(1.0) = 0
                return Step(s, QuestAction.GiveNothing);
            }

            // Randomly pick one of the available items
            int choiceIndex = rng.Next(availableItems.Count);
            var chosenAction = availableItems[choiceIndex];
            logProb = -Math.Log(availableItems.Count); // log(1/n) = -log(n)

            return Step(s, chosenAction);
        }

        public IEnumerable<QuestAction> LegalActions(QuestState s)
        {
            // If we need to select which hero to activate, only activation actions are available
            if (s.ActiveHeroIndex == -1)
            {
                // Only offer activation for heroes that are alive, haven't exited, and haven't activated this turn
                if (!s.Warrior.HasExited && !s.Warrior.IsDead && !s.WarriorActivatedThisTurn)
                    yield return QuestAction.ActivateWarrior;
                if (!s.Elf.HasExited && !s.Elf.IsDead && !s.ElfActivatedThisTurn)
                    yield return QuestAction.ActivateElf;
                if (!s.Thief.HasExited && !s.Thief.IsDead && !s.ThiefActivatedThisTurn)
                    yield return QuestAction.ActivateThief;
                if (!s.Mage.HasExited && !s.Mage.IsDead && !s.MageActivatedThisTurn)
                    yield return QuestAction.ActivateMage;
                yield break;
            }

            var activeHero = s.ActiveHeroIndex switch
            {
                0 => s.Warrior,
                1 => s.Elf,
                2 => s.Thief,
                3 => s.Mage,
                _ => throw new InvalidOperationException()
            };

            // Can end activation only if: hero has no actions left, OR hero has already moved, OR hero has exited/died
            if (s.ActionsRemaining <= 0 || activeHero.HasMoved || activeHero.HasExited || activeHero.IsDead)
                yield return QuestAction.EndActivation;

            // If hero has exited, is dead, or no actions remaining, that's the only option (EndActivation)
            if (activeHero.HasExited || activeHero.IsDead || s.ActionsRemaining <= 0)
                yield break;

            // Movement actions (can only move once per activation) - 5x6 grid
            if (!activeHero.HasMoved)
            {
                foreach (int adjacentHex in GetAdjacentHexes(activeHero.CurrentHex))
                {
                    // Convert hex number to correct enum value (MoveToHex0 starts at enum index 4)
                    yield return (QuestAction)((int)QuestAction.MoveToHex0 + adjacentHex);
                }
            }

            // Open chest action (if chest is present at hero's current hex AND hero doesn't have all 3 items)
            bool heroHasAllItems = activeHero.HasItem1 && activeHero.HasItem2 && activeHero.HasItem3;
            if (!heroHasAllItems)
            {
                if ((activeHero.CurrentHex == s.Chest0Hex && s.Chest0Present) ||
                    (activeHero.CurrentHex == s.Chest1Hex && s.Chest1Present) ||
                    (activeHero.CurrentHex == s.Chest2Hex && s.Chest2Present))
                {
                    yield return QuestAction.OpenChest;
                }
            }
        }

        public QuestState Step(in QuestState s, in QuestAction a)
        {
            // Handle hero activation selection
            if (a == QuestAction.ActivateWarrior)
            {
                return s with
                {
                    ActiveHeroIndex = 0,
                    ActionsRemaining = 2,
                    WarriorActivatedThisTurn = true
                };
            }
            
            if (a == QuestAction.ActivateElf)
            {
                return s with
                {
                    ActiveHeroIndex = 1,
                    ActionsRemaining = 2,
                    ElfActivatedThisTurn = true
                };
            }

            if (a == QuestAction.ActivateThief)
            {
                return s with
                {
                    ActiveHeroIndex = 2,
                    ActionsRemaining = 2,
                    ThiefActivatedThisTurn = true
                };
            }

            if (a == QuestAction.ActivateMage)
            {
                return s with
                {
                    ActiveHeroIndex = 3,
                    ActionsRemaining = 2,
                    MageActivatedThisTurn = true
                };
            }

            // Need a hero to be activated for other actions
            if (s.ActiveHeroIndex == -1)
                return s;

            var activeHero = s.ActiveHeroIndex switch
            {
                0 => s.Warrior,
                1 => s.Elf,
                2 => s.Thief,
                3 => s.Mage,
                _ => throw new InvalidOperationException()
            };

            if (a == QuestAction.EndActivation)
            {
                // Check if turn should complete: all alive, non-exited heroes have activated
                int heroesRemaining = 0;
                int heroesActivated = 0;

                if (!s.Warrior.HasExited && !s.Warrior.IsDead)
                {
                    heroesRemaining++;
                    if (s.WarriorActivatedThisTurn) heroesActivated++;
                }
                if (!s.Elf.HasExited && !s.Elf.IsDead)
                {
                    heroesRemaining++;
                    if (s.ElfActivatedThisTurn) heroesActivated++;
                }
                if (!s.Thief.HasExited && !s.Thief.IsDead)
                {
                    heroesRemaining++;
                    if (s.ThiefActivatedThisTurn) heroesActivated++;
                }
                if (!s.Mage.HasExited && !s.Mage.IsDead)
                {
                    heroesRemaining++;
                    if (s.MageActivatedThisTurn) heroesActivated++;
                }

                bool turnComplete = heroesActivated >= heroesRemaining;

                // Update the active hero and reset flags
                return s.ActiveHeroIndex switch
                {
                    0 => s with
                    {
                        Warrior = s.Warrior with { HasMoved = false },
                        ActiveHeroIndex = -1,
                        ActionsRemaining = 0,
                        TurnCount = turnComplete ? s.TurnCount + 1 : s.TurnCount,
                        WarriorActivatedThisTurn = !turnComplete,
                        ElfActivatedThisTurn = turnComplete ? false : s.ElfActivatedThisTurn,
                        ThiefActivatedThisTurn = turnComplete ? false : s.ThiefActivatedThisTurn,
                        MageActivatedThisTurn = turnComplete ? false : s.MageActivatedThisTurn
                    },
                    1 => s with
                    {
                        Elf = s.Elf with { HasMoved = false },
                        ActiveHeroIndex = -1,
                        ActionsRemaining = 0,
                        TurnCount = turnComplete ? s.TurnCount + 1 : s.TurnCount,
                        WarriorActivatedThisTurn = turnComplete ? false : s.WarriorActivatedThisTurn,
                        ElfActivatedThisTurn = !turnComplete,
                        ThiefActivatedThisTurn = turnComplete ? false : s.ThiefActivatedThisTurn,
                        MageActivatedThisTurn = turnComplete ? false : s.MageActivatedThisTurn
                    },
                    2 => s with
                    {
                        Thief = s.Thief with { HasMoved = false },
                        ActiveHeroIndex = -1,
                        ActionsRemaining = 0,
                        TurnCount = turnComplete ? s.TurnCount + 1 : s.TurnCount,
                        WarriorActivatedThisTurn = turnComplete ? false : s.WarriorActivatedThisTurn,
                        ElfActivatedThisTurn = turnComplete ? false : s.ElfActivatedThisTurn,
                        ThiefActivatedThisTurn = !turnComplete,
                        MageActivatedThisTurn = turnComplete ? false : s.MageActivatedThisTurn
                    },
                    3 => s with
                    {
                        Mage = s.Mage with { HasMoved = false },
                        ActiveHeroIndex = -1,
                        ActionsRemaining = 0,
                        TurnCount = turnComplete ? s.TurnCount + 1 : s.TurnCount,
                        WarriorActivatedThisTurn = turnComplete ? false : s.WarriorActivatedThisTurn,
                        ElfActivatedThisTurn = turnComplete ? false : s.ElfActivatedThisTurn,
                        ThiefActivatedThisTurn = turnComplete ? false : s.ThiefActivatedThisTurn,
                        MageActivatedThisTurn = !turnComplete
                    },
                    _ => throw new InvalidOperationException()
                };
            }

            // If hero has exited or no actions remaining, ignore other actions
            if (activeHero.HasExited || s.ActionsRemaining <= 0)
                return s;

            // Handle movement actions (MoveToHex0 through MoveToHex29)
            // Note: MoveToHex0 is the 5th enum value (index 4) after the 4 activation actions
            int actionValue = (int)a;
            if (actionValue >= (int)QuestAction.MoveToHex0 && actionValue <= (int)QuestAction.MoveToHex29)
            {
                // Can't move if already moved this activation
                if (activeHero.HasMoved)
                    return s;

                int targetHex = actionValue - (int)QuestAction.MoveToHex0; // Convert enum value to hex number (0-29)
                bool hasExited = targetHex == s.ExitHex;

                return s.ActiveHeroIndex switch
                {
                    0 => s with
                    {
                        Warrior = s.Warrior with
                        {
                            CurrentHex = targetHex,
                            HasMoved = true,
                            HasExited = hasExited || s.Warrior.HasExited
                        },
                        ActionsRemaining = s.ActionsRemaining - 1
                    },
                    1 => s with
                    {
                        Elf = s.Elf with
                        {
                            CurrentHex = targetHex,
                            HasMoved = true,
                            HasExited = hasExited || s.Elf.HasExited
                        },
                        ActionsRemaining = s.ActionsRemaining - 1
                    },
                    2 => s with
                    {
                        Thief = s.Thief with
                        {
                            CurrentHex = targetHex,
                            HasMoved = true,
                            HasExited = hasExited || s.Thief.HasExited
                        },
                        ActionsRemaining = s.ActionsRemaining - 1
                    },
                    3 => s with
                    {
                        Mage = s.Mage with
                        {
                            CurrentHex = targetHex,
                            HasMoved = true,
                            HasExited = hasExited || s.Mage.HasExited
                        },
                        ActionsRemaining = s.ActionsRemaining - 1
                    },
                    _ => throw new InvalidOperationException()
                };
            }

            // Handle open chest action
            if (a == QuestAction.OpenChest)
            {
                int chestHex = activeHero.CurrentHex;

                // Determine which chest is being opened and remove it
                QuestState newState = s;
                if (chestHex == s.Chest0Hex && s.Chest0Present)
                {
                    newState = s with { Chest0Present = false, ActionsRemaining = s.ActionsRemaining - 1, PendingChestItem = true };
                }
                else if (chestHex == s.Chest1Hex && s.Chest1Present)
                {
                    newState = s with { Chest1Present = false, ActionsRemaining = s.ActionsRemaining - 1, PendingChestItem = true };
                }
                else if (chestHex == s.Chest2Hex && s.Chest2Present)
                {
                    newState = s with { Chest2Present = false, ActionsRemaining = s.ActionsRemaining - 1, PendingChestItem = true };
                }
                else
                {
                    // No chest at this location
                    return s;
                }

                return newState;
            }

            // Handle chance outcomes (item from chest)
            if (a == QuestAction.GiveItem1 || a == QuestAction.GiveItem2 || a == QuestAction.GiveItem3 || a == QuestAction.GiveNothing)
            {
                if (!s.PendingChestItem)
                    return s; // Not in a chance node state

                Hero updatedHero = activeHero;

                if (a == QuestAction.GiveItem1 && !activeHero.HasItem1)
                    updatedHero = activeHero with { HasItem1 = true };
                else if (a == QuestAction.GiveItem2 && !activeHero.HasItem2)
                    updatedHero = activeHero with { HasItem2 = true };
                else if (a == QuestAction.GiveItem3 && !activeHero.HasItem3)
                    updatedHero = activeHero with { HasItem3 = true };
                // GiveNothing or already has that item = no change

                return s.ActiveHeroIndex switch
                {
                    0 => s with { Warrior = updatedHero, PendingChestItem = false },
                    1 => s with { Elf = updatedHero, PendingChestItem = false },
                    2 => s with { Thief = updatedHero, PendingChestItem = false },
                    3 => s with { Mage = updatedHero, PendingChestItem = false },
                    _ => s with { PendingChestItem = false }
                };
            }

            return s;
        }

        private Hero GiveRandomItemToHero(Hero hero)
        {
            // Deterministically give the first item the hero doesn't have
            // (In a real game with randomness, we'd use IsChanceNode/SampleChance)
            if (!hero.HasItem1) return hero with { HasItem1 = true };
            if (!hero.HasItem2) return hero with { HasItem2 = true };
            if (!hero.HasItem3) return hero with { HasItem3 = true };

            // Hero already has all items, chest is "empty" for them
            return hero;
        }

        private Hero GetActiveHero(in QuestState s)
        {
            return s.ActiveHeroIndex switch
            {
                0 => s.Warrior,
                1 => s.Elf,
                2 => s.Thief,
                3 => s.Mage,
                _ => throw new InvalidOperationException("No active hero")
            };
        }

        private IEnumerable<int> GetAdjacentHexes(int hex)
        {
            // 5x6 rectangular grid (30 hexes total) with simple adjacency
            // Hex layout (row-major):
            //  0  1  2  3  4
            //  5  6  7  8  9
            // 10 11 12 13 14
            // 15 16 17 18 19
            // 20 21 22 23 24
            // 25 26 27 28 29
            //
            // Each hex has up to 4 neighbors (N, S, E, W)

            int col = hex % 5;
            int row = hex / 5;

            // North
            if (row > 0)
                yield return hex - 5;

            // South
            if (row < 5)
                yield return hex + 5;

            // West
            if (col > 0)
                yield return hex - 1;

            // East
            if (col < 4)
                yield return hex + 1;
        }
    }
}
