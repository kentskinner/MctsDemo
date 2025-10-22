using System;
using System.Collections.Generic;
using System.Linq;
using GenericMcts;

namespace TinyQuestDemo
{
    // =============================================================
    // TinyQuest: A cooperative hex-based adventure game for MCTS
    // =============================================================

    public enum HeroType { Warrior, Elf }

    public enum QuestAction
    {
        // Hero activation selection
        ActivateWarrior,
        ActivateElf,
        
        // Movement actions
        MoveToHex0,
        MoveToHex1,
        MoveToHex2,
        
        // Other actions
        OpenChest,
        EndActivation
    }

    public readonly record struct Hero(
        HeroType Type,
        int CurrentHex, // Which hex the hero is on (0-2)
        bool HasExited, // Has this hero reached the exit?
        bool HasMoved   // Has this hero moved this activation?
    );

    public readonly record struct QuestState(
        Hero Warrior,
        Hero Elf,
        int ActiveHeroIndex, // 0 = Warrior, 1 = Elf, -1 = need to select hero
        int ActionsRemaining, // Actions remaining for active hero (max 2)
        bool ChestPresent,    // Is the chest still available?
        bool ItemRetrieved,   // Has the item been retrieved?
        int ExitHex,          // Which hex is the exit (0-2)
        int ChestHex,         // Which hex has the chest (0-2)
        int TurnCount,        // Number of complete turns (both heroes activated)
        bool WarriorActivatedThisTurn, // Has warrior activated this turn?
        bool ElfActivatedThisTurn      // Has elf activated this turn?
    );

    public sealed class TinyQuestGame : IGameModel<QuestState, QuestAction>
    {
        private const int MaxTurns = 5;
        private const double ExitReward = 1.0;  // High reward for survival
        private const double ItemReward = 0.3;   // Lower reward for the item

        public bool IsTerminal(in QuestState s, out double terminalValue)
        {
            // Game ends if both heroes have exited
            if (s.Warrior.HasExited && s.Elf.HasExited)
            {
                terminalValue = 2 * ExitReward + (s.ItemRetrieved ? ItemReward : 0.0);
                return true;
            }

            // Game ends after max turns
            if (s.TurnCount >= MaxTurns)
            {
                terminalValue =
                    (s.Warrior.HasExited ? ExitReward : 0.0) +
                    (s.Elf.HasExited ? ExitReward : 0.0) +
                    (s.ItemRetrieved ? ItemReward : 0.0);
                return true;
            }

            terminalValue = 0;
            return false;
        }

        public bool IsChanceNode(in QuestState s) => false;

        public QuestState SampleChance(in QuestState s, Random rng, out double logProb)
        {
            logProb = 0.0;
            return s;
        }

        public IEnumerable<QuestAction> LegalActions(QuestState s)
        {
            // If we need to select which hero to activate, only activation actions are available
            if (s.ActiveHeroIndex == -1)
            {
                // Only offer activation for heroes that haven't exited and haven't activated this turn
                if (!s.Warrior.HasExited && !s.WarriorActivatedThisTurn)
                    yield return QuestAction.ActivateWarrior;
                if (!s.Elf.HasExited && !s.ElfActivatedThisTurn)
                    yield return QuestAction.ActivateElf;
                yield break;
            }

            var activeHero = s.ActiveHeroIndex == 0 ? s.Warrior : s.Elf;

            // Can always end activation
            yield return QuestAction.EndActivation;

            // If hero has exited or no actions remaining, that's the only option
            if (activeHero.HasExited || s.ActionsRemaining <= 0)
                yield break;

            // Movement actions (can only move once per activation)
            if (!activeHero.HasMoved)
            {
                for (int hex = 0; hex < 3; hex++)
                {
                    if (hex != activeHero.CurrentHex)
                    {
                        yield return hex switch
                        {
                            0 => QuestAction.MoveToHex0,
                            1 => QuestAction.MoveToHex1,
                            2 => QuestAction.MoveToHex2,
                            _ => throw new InvalidOperationException()
                        };
                    }
                }
            }

            // Open chest action (if chest is present and hero is on chest hex)
            if (s.ChestPresent && activeHero.CurrentHex == s.ChestHex)
            {
                yield return QuestAction.OpenChest;
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

            // Need a hero to be activated for other actions
            if (s.ActiveHeroIndex == -1)
                return s;

            var activeHero = s.ActiveHeroIndex == 0 ? s.Warrior : s.Elf;

            if (a == QuestAction.EndActivation)
            {
                // End current hero's activation, reset to hero selection mode
                if (s.ActiveHeroIndex == 0)
                {
                    // Warrior is ending activation
                    // Check if we should complete the turn:
                    // - Both activated: turn complete
                    // - Elf exited: turn complete (only one hero left)
                    bool turnComplete = s.ElfActivatedThisTurn || s.Elf.HasExited;
                    
                    return s with
                    {
                        Warrior = s.Warrior with { HasMoved = false },
                        ActiveHeroIndex = -1,
                        ActionsRemaining = 0,
                        TurnCount = turnComplete ? s.TurnCount + 1 : s.TurnCount,
                        WarriorActivatedThisTurn = turnComplete ? false : true,
                        ElfActivatedThisTurn = false  // Always reset
                    };
                }
                else
                {
                    // Elf is ending activation
                    // Check if we should complete the turn:
                    // - Both activated: turn complete
                    // - Warrior exited: turn complete (only one hero left)
                    bool turnComplete = s.WarriorActivatedThisTurn || s.Warrior.HasExited;
                    
                    return s with
                    {
                        Elf = s.Elf with { HasMoved = false },
                        ActiveHeroIndex = -1,
                        ActionsRemaining = 0,
                        TurnCount = turnComplete ? s.TurnCount + 1 : s.TurnCount,
                        WarriorActivatedThisTurn = false,  // Always reset
                        ElfActivatedThisTurn = turnComplete ? false : true
                    };
                }
            }

            // If hero has exited or no actions remaining, ignore other actions
            if (activeHero.HasExited || s.ActionsRemaining <= 0)
                return s;

            // Handle movement actions
            if (a is QuestAction.MoveToHex0 or QuestAction.MoveToHex1 or QuestAction.MoveToHex2)
            {
                // Can't move if already moved this activation
                if (activeHero.HasMoved)
                    return s;

                int targetHex = a switch
                {
                    QuestAction.MoveToHex0 => 0,
                    QuestAction.MoveToHex1 => 1,
                    QuestAction.MoveToHex2 => 2,
                    _ => activeHero.CurrentHex
                };

                bool hasExited = targetHex == s.ExitHex;

                if (s.ActiveHeroIndex == 0)
                {
                    return s with
                    {
                        Warrior = s.Warrior with
                        {
                            CurrentHex = targetHex,
                            HasMoved = true,
                            HasExited = hasExited || s.Warrior.HasExited
                        },
                        ActionsRemaining = s.ActionsRemaining - 1
                    };
                }
                else
                {
                    return s with
                    {
                        Elf = s.Elf with
                        {
                            CurrentHex = targetHex,
                            HasMoved = true,
                            HasExited = hasExited || s.Elf.HasExited
                        },
                        ActionsRemaining = s.ActionsRemaining - 1
                    };
                }
            }

            // Handle open chest action
            if (a == QuestAction.OpenChest)
            {
                // Can only open chest if it's present and hero is on the chest hex
                if (!s.ChestPresent || activeHero.CurrentHex != s.ChestHex)
                    return s;

                return s with
                {
                    ChestPresent = false,
                    ItemRetrieved = true,
                    ActionsRemaining = s.ActionsRemaining - 1
                };
            }

            return s;
        }
    }
}
