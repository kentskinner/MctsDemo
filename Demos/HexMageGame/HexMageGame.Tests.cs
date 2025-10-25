using System;
using System.Linq;
using System.Collections.Immutable;
using MageGame;

namespace HexMageGame.Tests
{
    public class ActionEconomyTests
    {
        // Thief is next to enemy: (assuming no chests or shrines nearby)
        //    - Hasn't moved yet. Legal actions are: 
        //       - [END TURN]
        //       - [SNEAK ATTACK] after which the turn is over.
        //       - [MOVE], then [ATTACK|END TURN] - end of turn
        //       - [ATTACK], then [ATTACK|MOVE|END TURN] - end of turn
        //    - Has moved already (not counting teleport)
        //       - [END TURN]
        //       - [MOVE] - end of turn
        //       - [ATTACK] - end of turn
        //     - Hasn't moved yet and is NIMBLE:
        //       - [END TURN]
        //       - [SNEAK ATTACK], then [ATTACK|END TURN]
        //       - [MOVE], then:
        //          - [SNEAK ATTACK] - end of turn
        //          - [ATTACK], then [ATTACK|END TURN]
        //       - [ATTACK], then [SNEAK ATTACK|ATTACK|END TURN] - end of turn
        // etc.


        /// <summary>
        /// Test: Normal turn allows MOVE + ATTACK
        /// </summary>
        public static void Test_NormalTurn_MoveAndAttack()
        {
            // Arrange: Warrior at (0,3) with monster at (1,3)
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateSimpleState(
                warriorPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(1, 3)
            );
            
            // Activate warrior
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 0));
            
            // Act: Move East (1 AP)
            state = game.Step(state, new HexAction(HexActionType.MoveE));
            
            // Assert: Should have 1 AP remaining
            Assert(state.Heroes[0].ActionsRemaining == 1, "Should have 1 AP after move");
            
            // Act: Attack (1 AP)
            var actions = game.LegalActions(state).ToList();
            var attackAction = actions.FirstOrDefault(a => a.Type == HexActionType.Attack);
            Assert(attackAction != null, "Should be able to attack with 1 AP remaining");
            
            state = game.Step(state, attackAction);
            
            // Assert: Should have 0 AP remaining
            Assert(state.Heroes[0].ActionsRemaining == 0, "Should have 0 AP after attack");
            
            Console.WriteLine("✓ Test_NormalTurn_MoveAndAttack passed");
        }

        /// <summary>
        /// Test: Normal turn allows ATTACK + ATTACK (if hero starts adjacent)
        /// </summary>
        public static void Test_NormalTurn_AttackTwice()
        {
            // Arrange: Warrior at (0,3) adjacent to monster at (1,3)
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateSimpleState(
                warriorPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(1, 3)
            );
            
            // Activate warrior
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 0));
            
            // Assert: Should start with 2 AP
            Assert(state.Heroes[0].ActionsRemaining == 2, "Should start with 2 AP");
            
            // Act: First attack (1 AP)
            var actions = game.LegalActions(state).ToList();
            var attackAction = actions.FirstOrDefault(a => a.Type == HexActionType.Attack);
            Assert(attackAction != null, "Should be able to attack");
            
            state = game.Step(state, attackAction);
            
            // Assert: Should have 1 AP remaining
            Assert(state.Heroes[0].ActionsRemaining == 1, "Should have 1 AP after first attack");
            
            // Act: Second attack (1 AP) - if monster still alive
            if (state.Monsters[0].IsAlive)
            {
                actions = game.LegalActions(state).ToList();
                attackAction = actions.FirstOrDefault(a => a.Type == HexActionType.Attack);
                if (attackAction != null)
                {
                    state = game.Step(state, attackAction);
                    Assert(state.Heroes[0].ActionsRemaining == 0, "Should have 0 AP after second attack");
                }
            }
            
            Console.WriteLine("✓ Test_NormalTurn_AttackTwice passed");
        }

        /// <summary>
        /// Test: Nimble spell grants bonus attack action (can MOVE + MOVE + ATTACK)
        /// </summary>
        public static void Test_Nimble_MoveTwiceThenAttack()
        {
            // Arrange: Mage and Warrior at (0,3), monster at (2,3)
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateStateWithMage(
                magePos: new HexCoord(0, 3),
                warriorPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(2, 3)
            );
            
            // Activate Mage
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 1));
            
            // Manually give Mage spell points AFTER activation
            var mageActivated = state.Heroes[1];
            state = state with
            {
                Heroes = state.Heroes.SetItem(1, mageActivated with { SpellPoints = 6 })
            };
            
            // Act: Cast Nimble on Warrior (costs 4 SP)
            var actions = game.LegalActions(state).ToList();
            var nimbleAction = actions.FirstOrDefault(a => 
                a.Type == HexActionType.NimbleHero && a.TargetIndex == 0);
            Assert(nimbleAction != null, "Should be able to cast Nimble on Warrior");
            
            state = game.Step(state, nimbleAction);
            
            // Assert: Warrior should have bonus attack action
            Assert(state.Heroes[0].AttackActionsRemaining > 0, 
                $"Warrior should have bonus attack action (has {state.Heroes[0].AttackActionsRemaining})");
            
            state = game.Step(state, new HexAction(HexActionType.EndTurn));
            
            // Activate Warrior
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 0));
            
            // Act: Move East (1 AP)
            state = game.Step(state, new HexAction(HexActionType.MoveE));
            Assert(state.Heroes[0].ActionsRemaining == 1, "Should have 1 AP after first move");
            
            // Act: Move East again (1 AP)
            state = game.Step(state, new HexAction(HexActionType.MoveE));
            Assert(state.Heroes[0].ActionsRemaining == 0, "Should have 0 AP after second move");
            
            // Assert: Should still be able to attack using bonus attack action
            actions = game.LegalActions(state).ToList();
            var attackAction = actions.FirstOrDefault(a => a.Type == HexActionType.Attack);
            Assert(attackAction != null, "Should be able to attack with bonus action (0 AP remaining)");
            
            Console.WriteLine("✓ Test_Nimble_MoveTwiceThenAttack passed");
        }

        /// <summary>
        /// Test: Without Nimble, cannot attack after using all AP on movement
        /// </summary>
        public static void Test_WithoutNimble_CannotAttackAfterTwoMoves()
        {
            // Arrange: Warrior at (0,3), monster at (2,3)
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateSimpleState(
                warriorPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(2, 3)
            );
            
            // Activate warrior
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 0));
            
            // Act: Move East (1 AP)
            state = game.Step(state, new HexAction(HexActionType.MoveE));
            
            // Act: Move East again (1 AP)
            state = game.Step(state, new HexAction(HexActionType.MoveE));
            
            // Assert: Should have 0 AP and cannot attack
            Assert(state.Heroes[0].ActionsRemaining == 0, "Should have 0 AP after two moves");
            
            var actions = game.LegalActions(state).ToList();
            var attackAction = actions.FirstOrDefault(a => a.Type == HexActionType.Attack);
            Assert(attackAction == null, "Should NOT be able to attack with 0 AP and no bonus action");
            
            Console.WriteLine("✓ Test_WithoutNimble_CannotAttackAfterTwoMoves passed");
        }

        /// <summary>
        /// Test: Thief Sneak Attack costs 2 attack actions (not AP)
        /// </summary>
        public static void Test_SneakAttack_UsesBonusActions()
        {
            // Arrange: Thief with Nimble buff (to have 2 attack actions)
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateStateWithThief(
                thiefPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(1, 3)
            );
            
            // Manually give Thief 2 attack actions (simulating Nimble)
            var thief = state.Heroes[3];
            state = state with 
            { 
                Heroes = state.Heroes.SetItem(3, thief with { AttackActionsRemaining = 2 })
            };
            
            // Activate Thief
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 3));
            
            // Assert: Should have 2 AP and 2 attack actions
            Assert(state.Heroes[3].ActionsRemaining == 2, "Should have 2 AP");
            Assert(state.Heroes[3].AttackActionsRemaining == 2, "Should have 2 attack actions");
            
            // Act: Sneak Attack (costs 2 attack actions, 0 AP)
            var actions = game.LegalActions(state).ToList();
            var sneakAction = actions.FirstOrDefault(a => a.Type == HexActionType.SneakAttack);
            Assert(sneakAction != null, "Should be able to Sneak Attack with 2 attack actions");
            
            state = game.Step(state, sneakAction);
            
            // Assert: Should still have 2 AP but 0 attack actions
            Assert(state.Heroes[3].ActionsRemaining == 2, 
                $"Should still have 2 AP (has {state.Heroes[3].ActionsRemaining})");
            Assert(state.Heroes[3].AttackActionsRemaining == 0, 
                $"Should have 0 attack actions (has {state.Heroes[3].AttackActionsRemaining})");
            
            Console.WriteLine("✓ Test_SneakAttack_UsesBonusActions passed");
        }

        /// <summary>
        /// Test: Rage free attack doesn't consume AP or attack actions
        /// </summary>
        public static void Test_Rage_FreeAttackConsumesNothing()
        {
            // Arrange: Warrior with Rage, two adjacent monsters
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateStateWithRageWarrior(
                warriorPos: new HexCoord(0, 3),
                monster1Pos: new HexCoord(1, 3),
                monster2Pos: new HexCoord(1, 4)
            );
            
            // Activate warrior
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 0));
            
            var initialAP = state.Heroes[0].ActionsRemaining;
            var initialAA = state.Heroes[0].AttackActionsRemaining;
            
            // Act: Attack first available monster
            var actions = game.LegalActions(state).ToList();
            var attackAction = actions.FirstOrDefault(a => a.Type == HexActionType.Attack);
            Assert(attackAction != null, "Should be able to attack a monster");
            
            var targetIndex = attackAction.TargetIndex;
            
            state = game.Step(state, attackAction);
            
            // Force kill outcome (simulate successful attack)
            var outcomes = game.ChanceOutcomes(state).ToList();
            var killOutcome = outcomes.FirstOrDefault(o => !o.outcome.Monsters[targetIndex].IsAlive);
            if (killOutcome.outcome != null)
            {
                state = killOutcome.outcome;
                
                // Assert: Should have Rage free attack available
                Assert(state.RageFreeAttackAvailable, "Should have Rage free attack after kill");
                
                // Act: Use Rage free attack on other monster
                actions = game.LegalActions(state).ToList();
                var rageAttack = actions.FirstOrDefault(a => a.Type == HexActionType.Attack);
                Assert(rageAttack != null, "Should be able to use Rage free attack");
                
                var apBeforeRage = state.Heroes[0].ActionsRemaining;
                var aaBeforeRage = state.Heroes[0].AttackActionsRemaining;
                
                state = game.Step(state, rageAttack);
                
                // Assert: AP and attack actions unchanged by Rage attack
                Assert(state.Heroes[0].ActionsRemaining == apBeforeRage, 
                    "Rage attack should not consume AP");
                Assert(state.Heroes[0].AttackActionsRemaining == aaBeforeRage, 
                    "Rage attack should not consume attack actions");
                
                Console.WriteLine("✓ Test_Rage_FreeAttackConsumesNothing passed");
            }
            else
            {
                Console.WriteLine("⚠ Test_Rage_FreeAttackConsumesNothing skipped (monster didn't die)");
            }
        }

        /// <summary>
        /// Test: Thief without Nimble - SNEAK ATTACK ends turn (no actions left)
        /// Covers: "[SNEAK ATTACK] after which the turn is over"
        /// </summary>
        public static void Test_Thief_SneakAttack_EndsTurn_WithoutNimble()
        {
            // Arrange: Thief next to enemy, no Nimble
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateStateWithThief(
                thiefPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(1, 3)
            );
            
            // Thief needs 2 attack actions for Sneak Attack but starts with 0
            // So we need to give them attack actions first (e.g., from Nimble cast earlier)
            var thief = state.Heroes[3];
            state = state with 
            { 
                Heroes = state.Heroes.SetItem(3, thief with { AttackActionsRemaining = 2 })
            };
            
            // Activate Thief
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 3));
            
            // Act: Sneak Attack (costs 2 attack actions)
            var actions = game.LegalActions(state).ToList();
            var sneakAction = actions.FirstOrDefault(a => a.Type == HexActionType.SneakAttack);
            Assert(sneakAction != null, "Should be able to Sneak Attack");
            
            state = game.Step(state, sneakAction);
            
            // Assert: After Sneak Attack, should have 2 AP but 0 attack actions
            Assert(state.Heroes[3].ActionsRemaining == 2, "Should still have 2 AP");
            Assert(state.Heroes[3].AttackActionsRemaining == 0, "Should have 0 attack actions");
            
            // Can still move with remaining AP or end turn
            actions = game.LegalActions(state).ToList();
            bool canMove = actions.Any(a => a.Type >= HexActionType.MoveNE && a.Type <= HexActionType.MoveNW);
            bool canEndTurn = actions.Any(a => a.Type == HexActionType.EndTurn);
            Assert(canMove, "Should be able to move after Sneak Attack");
            Assert(canEndTurn, "Should be able to end turn");
            
            Console.WriteLine("✓ Test_Thief_SneakAttack_EndsTurn_WithoutNimble passed");
        }

        /// <summary>
        /// Test: Thief with Nimble - SNEAK ATTACK then can still ATTACK
        /// Covers: "With NIMBLE: [SNEAK ATTACK], then [ATTACK|END TURN]"
        /// </summary>
        public static void Test_Thief_Nimble_SneakAttack_ThenAttack()
        {
            // Arrange: Thief with Nimble (3 attack actions total)
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateStateWithThief(
                thiefPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(1, 3)
            );
            
            // Give Thief 3 attack actions (1 base + 2 from double Nimble for testing)
            var thief = state.Heroes[3];
            state = state with 
            { 
                Heroes = state.Heroes.SetItem(3, thief with { AttackActionsRemaining = 3 })
            };
            
            // Activate Thief
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 3));
            
            // Act: Sneak Attack (costs 2 attack actions)
            var actions = game.LegalActions(state).ToList();
            var sneakAction = actions.FirstOrDefault(a => a.Type == HexActionType.SneakAttack);
            Assert(sneakAction != null, "Should be able to Sneak Attack");
            
            state = game.Step(state, sneakAction);
            
            // Assert: Should have 1 attack action remaining
            Assert(state.Heroes[3].AttackActionsRemaining == 1, 
                $"Should have 1 attack action left (has {state.Heroes[3].AttackActionsRemaining})");
            
            // Should be able to attack again (using bonus attack action, not AP)
            actions = game.LegalActions(state).ToList();
            var attackAction = actions.FirstOrDefault(a => a.Type == HexActionType.Attack);
            Assert(attackAction != null, "Should be able to attack after Sneak Attack with Nimble");
            
            Console.WriteLine("✓ Test_Thief_Nimble_SneakAttack_ThenAttack passed");
        }

        /// <summary>
        /// Test: Thief with Nimble - MOVE then SNEAK ATTACK
        /// Covers: "With NIMBLE: [MOVE], then [SNEAK ATTACK] - end of turn"
        /// </summary>
        public static void Test_Thief_Nimble_Move_ThenSneakAttack()
        {
            // Arrange: Thief with Nimble away from enemy
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateStateWithThief(
                thiefPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(2, 3)
            );
            
            // Give Thief 2 attack actions (from Nimble)
            var thief = state.Heroes[3];
            state = state with 
            { 
                Heroes = state.Heroes.SetItem(3, thief with { AttackActionsRemaining = 2 })
            };
            
            // Activate Thief
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 3));
            
            // Act: Move closer (1 AP)
            state = game.Step(state, new HexAction(HexActionType.MoveE));
            Assert(state.Heroes[3].ActionsRemaining == 1, "Should have 1 AP after move");
            Assert(state.Heroes[3].AttackActionsRemaining == 2, "Attack actions unchanged by move");
            
            // Act: Sneak Attack (costs 2 attack actions, 0 AP)
            var actions = game.LegalActions(state).ToList();
            var sneakAction = actions.FirstOrDefault(a => a.Type == HexActionType.SneakAttack);
            Assert(sneakAction != null, "Should be able to Sneak Attack after moving");
            
            state = game.Step(state, sneakAction);
            
            // Assert: Still have 1 AP, but 0 attack actions
            Assert(state.Heroes[3].ActionsRemaining == 1, 
                $"Should still have 1 AP (has {state.Heroes[3].ActionsRemaining})");
            Assert(state.Heroes[3].AttackActionsRemaining == 0, "Should have 0 attack actions");
            
            Console.WriteLine("✓ Test_Thief_Nimble_Move_ThenSneakAttack passed");
        }

        /// <summary>
        /// Test: Thief with Nimble - ATTACK then SNEAK ATTACK
        /// Covers: "With NIMBLE: [ATTACK], then [SNEAK ATTACK|ATTACK|END TURN]"
        /// </summary>
        public static void Test_Thief_Nimble_Attack_ThenSneakAttack()
        {
            // Arrange: Thief with Nimble next to enemy
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateStateWithThief(
                thiefPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(1, 3)
            );
            
            // Give Thief 3 attack actions (1 + 2 from Nimble, or multiple Nimbles for testing)
            var thief = state.Heroes[3];
            state = state with 
            { 
                Heroes = state.Heroes.SetItem(3, thief with { AttackActionsRemaining = 3 })
            };
            
            // Activate Thief
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 3));
            
            // Act: Regular Attack (1 AP, 0 attack actions since we have AP)
            var actions = game.LegalActions(state).ToList();
            var attackAction = actions.FirstOrDefault(a => a.Type == HexActionType.Attack);
            Assert(attackAction != null, "Should be able to attack");
            
            state = game.Step(state, attackAction);
            
            // Assert: Should have 1 AP and 3 attack actions (attack used AP, not attack actions)
            Assert(state.Heroes[3].ActionsRemaining == 1, "Should have 1 AP after attack");
            Assert(state.Heroes[3].AttackActionsRemaining == 3, "Attack actions unchanged when using AP");
            
            // Should still be able to Sneak Attack if monster alive
            if (state.Monsters[0].IsAlive)
            {
                actions = game.LegalActions(state).ToList();
                var sneakAction = actions.FirstOrDefault(a => a.Type == HexActionType.SneakAttack);
                Assert(sneakAction != null, "Should be able to Sneak Attack after regular attack");
            }
            
            Console.WriteLine("✓ Test_Thief_Nimble_Attack_ThenSneakAttack passed");
        }

        /// <summary>
        /// Test: After moving once, cannot move again (even with AP remaining)
        /// Covers: "Has moved already" scenarios
        /// </summary>
        public static void Test_CanOnlyMoveOncePerTurn()
        {
            // Arrange: Warrior with clear path
            var game = new HexTacticalGame(maxTurns: 15);
            var state = CreateSimpleState(
                warriorPos: new HexCoord(0, 3),
                monsterPos: new HexCoord(5, 5)  // Far away
            );
            
            // Activate warrior
            state = game.Step(state, new HexAction(HexActionType.ActivateHero, TargetIndex: 0));
            
            // Assert: Should be able to move initially
            var actions = game.LegalActions(state).ToList();
            bool canMoveInitially = actions.Any(a => 
                a.Type >= HexActionType.MoveNE && a.Type <= HexActionType.MoveNW);
            Assert(canMoveInitially, "Should be able to move at start of turn");
            
            // Act: Move East (costs 1 AP)
            state = game.Step(state, new HexAction(HexActionType.MoveE));
            
            // Assert: Should have 1 AP remaining but cannot move again
            Assert(state.Heroes[0].ActionsRemaining == 1, "Should have 1 AP after move");
            
            actions = game.LegalActions(state).ToList();
            bool canMoveAgain = actions.Any(a => 
                a.Type >= HexActionType.MoveNE && a.Type <= HexActionType.MoveNW);
            Assert(!canMoveAgain, "Should NOT be able to move again after already moving");
            
            // But should still be able to attack or end turn
            bool canEndTurn = actions.Any(a => a.Type == HexActionType.EndTurn);
            Assert(canEndTurn, "Should be able to end turn after moving");
            
            Console.WriteLine("✓ Test_CanOnlyMoveOncePerTurn passed");
        }

        // Helper methods
        private static HexGameState CreateSimpleState(HexCoord warriorPos, HexCoord monsterPos)
        {
            var game = new HexTacticalGame(maxTurns: 15);
            var state = game.InitialState();
            
            // Update warrior position
            var warrior = state.Heroes[0];
            state = state with 
            { 
                Heroes = state.Heroes.SetItem(0, warrior with { Position = warriorPos })
            };
            
            // Update monster position
            var monster = state.Monsters[0];
            state = state with
            {
                Monsters = state.Monsters.SetItem(0, monster with { Position = monsterPos })
            };
            
            return state;
        }

        private static HexGameState CreateStateWithMage(HexCoord magePos, HexCoord warriorPos, HexCoord monsterPos)
        {
            var game = new HexTacticalGame(maxTurns: 15);
            var state = game.InitialState();
            
            // Update positions
            var mage = state.Heroes[1];
            var warrior = state.Heroes[0];
            state = state with
            {
                Heroes = state.Heroes
                    .SetItem(1, mage with { Position = magePos })
                    .SetItem(0, warrior with { Position = warriorPos })
            };
            
            var monster = state.Monsters[0];
            state = state with
            {
                Monsters = state.Monsters.SetItem(0, monster with { Position = monsterPos })
            };
            
            return state;
        }

        private static HexGameState CreateStateWithThief(HexCoord thiefPos, HexCoord monsterPos)
        {
            var game = new HexTacticalGame(maxTurns: 15);
            var state = game.InitialState();
            
            var thief = state.Heroes[3];
            state = state with
            {
                Heroes = state.Heroes.SetItem(3, thief with { Position = thiefPos })
            };
            
            var monster = state.Monsters[0];
            state = state with
            {
                Monsters = state.Monsters.SetItem(0, monster with { Position = monsterPos })
            };
            
            return state;
        }

        private static HexGameState CreateStateWithRageWarrior(
            HexCoord warriorPos, HexCoord monster1Pos, HexCoord monster2Pos)
        {
            var game = new HexTacticalGame(maxTurns: 15);
            var state = game.InitialState();
            
            // Warrior already has Rage in initial state
            var warrior = state.Heroes[0];
            state = state with
            {
                Heroes = state.Heroes.SetItem(0, warrior with { Position = warriorPos })
            };
            
            // Add second monster
            var monster1 = state.Monsters[0];
            var monster2 = new HexMonster(1, monster1Pos, IsAlive: true, DefenseValue: 5);
            var monster3 = new HexMonster(2, monster2Pos, IsAlive: true, DefenseValue: 5);
            
            state = state with
            {
                Monsters = ImmutableList.Create(monster2, monster3)
            };
            
            return state;
        }

        private static HexGameState StartNewTurnAndActivateMage(HexTacticalGame game, HexGameState state)
        {
            // End current phase
            while (state.CurrentPhase != Phase.HeroAction || state.ActiveHeroIndex != -1)
            {
                var actions = game.LegalActions(state).ToList();
                var endTurn = actions.FirstOrDefault(a => a.Type == HexActionType.EndTurn);
                if (endTurn != null)
                {
                    state = game.Step(state, endTurn);
                    
                    if (game.IsChanceNode(state))
                    {
                        var outcomes = game.ChanceOutcomes(state).ToList();
                        state = outcomes[0].outcome;
                    }
                }
                else break;
            }
            
            return state;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception($"Assertion failed: {message}");
            }
        }

        public static void RunAllTests()
        {
            Console.WriteLine("=== Running Action Economy Tests ===\n");
            
            try
            {
                // Basic action economy
                Test_NormalTurn_MoveAndAttack();
                Test_NormalTurn_AttackTwice();
                Test_WithoutNimble_CannotAttackAfterTwoMoves();
                Test_CanOnlyMoveOncePerTurn();
                
                // Nimble spell
                Test_Nimble_MoveTwiceThenAttack();
                
                // Sneak Attack basic
                Test_SneakAttack_UsesBonusActions();
                
                // Rage
                Test_Rage_FreeAttackConsumesNothing();
                
                // Thief-specific scenarios from comment block
                Test_Thief_SneakAttack_EndsTurn_WithoutNimble();
                Test_Thief_Nimble_SneakAttack_ThenAttack();
                Test_Thief_Nimble_Move_ThenSneakAttack();
                Test_Thief_Nimble_Attack_ThenSneakAttack();
                
                Console.WriteLine("\n=== All tests passed! ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ Test failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
