using Xunit;
using Xunit.Abstractions;
using TinyQuestDemo;

namespace TinyQuestDemo.Tests;

/// <summary>
/// Minimal tests to isolate specific bugs in the game logic
/// </summary>
public class MinimalBugTests
{
    private readonly ITestOutputHelper _output;

    public MinimalBugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MovingToExitHex_ShouldSetHasExitedToTrue()
    {
        // This is the simplest possible test of exit mechanics
        // Setup: Warrior at hex 9, exit at hex 4, they are adjacent
        var game = new TinyQuestGame();
        
        var state = new QuestState(
            Warrior: new Hero(HeroType.Warrior, CurrentHex: 9, HasExited: false, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Elf: new Hero(HeroType.Elf, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Thief: new Hero(HeroType.Thief, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Mage: new Hero(HeroType.Mage, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            ActiveHeroIndex: 0,    // Warrior is active
            ActionsRemaining: 2,
            ExitHex: 4,
            Chest0Hex: 3,
            Chest1Hex: 15,
            Chest2Hex: 25,
            TurnCount: 0,
            WarriorActivatedThisTurn: true,
            ElfActivatedThisTurn: false,
            ThiefActivatedThisTurn: false,
            MageActivatedThisTurn: false,
            Chest0Present: true,
            Chest1Present: true,
            Chest2Present: true,
            PendingChestItem: false
        );

        _output.WriteLine("=== BEFORE: Moving to exit ===");
        _output.WriteLine($"Warrior.CurrentHex: {state.Warrior.CurrentHex}");
        _output.WriteLine($"Warrior.HasExited: {state.Warrior.HasExited}");
        _output.WriteLine($"ExitHex: {state.ExitHex}");

        // Act: Move warrior from hex 9 to hex 4 (the exit)
        state = game.Step(state, QuestAction.MoveToHex4);

        _output.WriteLine("\n=== AFTER: Moving to exit ===");
        _output.WriteLine($"Warrior.CurrentHex: {state.Warrior.CurrentHex}");
        _output.WriteLine($"Warrior.HasExited: {state.Warrior.HasExited}");
        
        // Assert: Warrior should now have exited
        Assert.Equal(4, state.Warrior.CurrentHex);
        Assert.True(state.Warrior.HasExited, 
            "BUG: Moving to the exit hex should set HasExited=true, but it's still false!");
    }

    [Fact]
    public void SampleChance_AfterOpeningChest_ShouldClearPendingChestItem()
    {
        // This tests whether the chance node resolution properly clears the pending flag
        var game = new TinyQuestGame();
        
        var state = new QuestState(
            Warrior: new Hero(HeroType.Warrior, CurrentHex: 3, HasExited: false, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Elf: new Hero(HeroType.Elf, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Thief: new Hero(HeroType.Thief, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Mage: new Hero(HeroType.Mage, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            ActiveHeroIndex: 0,    // Warrior is active
            ActionsRemaining: 1,   // Has 1 action left
            ExitHex: 4,
            Chest0Hex: 3,          // Chest at warrior's current position
            Chest1Hex: 15,
            Chest2Hex: 25,
            TurnCount: 0,
            WarriorActivatedThisTurn: true,
            ElfActivatedThisTurn: false,
            ThiefActivatedThisTurn: false,
            MageActivatedThisTurn: false,
            Chest0Present: true,   // Chest is present
            Chest1Present: true,
            Chest2Present: true,
            PendingChestItem: false
        );

        _output.WriteLine("=== BEFORE: Opening chest ===");
        _output.WriteLine($"PendingChestItem: {state.PendingChestItem}");
        _output.WriteLine($"Chest0Present: {state.Chest0Present}");
        _output.WriteLine($"Warrior.HasItem1: {state.Warrior.HasItem1}");

        // Act 1: Open the chest
        state = game.Step(state, QuestAction.OpenChest);

        _output.WriteLine("\n=== AFTER: OpenChest (creates chance node) ===");
        _output.WriteLine($"PendingChestItem: {state.PendingChestItem}");
        _output.WriteLine($"Chest0Present: {state.Chest0Present}");
        
        Assert.True(state.PendingChestItem, "Opening chest should set PendingChestItem=true");
        Assert.False(state.Chest0Present, "Opening chest should remove the chest");

        // Act 2: Resolve the chance node
        var rng = new Random(42);
        state = game.SampleChance(state, rng, out var logProb);

        _output.WriteLine("\n=== AFTER: SampleChance (resolves chance node) ===");
        _output.WriteLine($"PendingChestItem: {state.PendingChestItem}");
        _output.WriteLine($"Warrior.HasItem1: {state.Warrior.HasItem1}");
        _output.WriteLine($"Warrior.HasItem2: {state.Warrior.HasItem2}");
        _output.WriteLine($"Warrior.HasItem3: {state.Warrior.HasItem3}");
        
        // Assert: PendingChestItem should be cleared
        Assert.False(state.PendingChestItem, 
            "BUG: SampleChance should clear PendingChestItem=false, but it's still true!");
        
        // Assert: Warrior should have received an item
        var hasAnyItem = state.Warrior.HasItem1 || state.Warrior.HasItem2 || state.Warrior.HasItem3;
        Assert.True(hasAnyItem, "Warrior should have received an item from the chest");
    }

    [Fact]
    public void GameShouldBeTerminal_WhenAllHeroesExited()
    {
        // Tests that the game correctly identifies terminal states
        var game = new TinyQuestGame();
        
        var state = new QuestState(
            Warrior: new Hero(HeroType.Warrior, CurrentHex: 4, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: true, HasItem2: false, HasItem3: false),
            Elf: new Hero(HeroType.Elf, CurrentHex: 4, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Thief: new Hero(HeroType.Thief, CurrentHex: 4, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Mage: new Hero(HeroType.Mage, CurrentHex: 4, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            ActiveHeroIndex: -1,
            ActionsRemaining: 0,
            ExitHex: 4,
            Chest0Hex: 3,
            Chest1Hex: 15,
            Chest2Hex: 25,
            TurnCount: 5,
            WarriorActivatedThisTurn: false,
            ElfActivatedThisTurn: false,
            ThiefActivatedThisTurn: false,
            MageActivatedThisTurn: false,
            Chest0Present: false,  // Chest was opened
            Chest1Present: true,
            Chest2Present: true,
            PendingChestItem: false
        );

        // Act: Check if terminal
        var isTerminal = game.IsTerminal(state, out var terminalValue);

        _output.WriteLine("=== Terminal State Check ===");
        _output.WriteLine($"IsTerminal: {isTerminal}");
        _output.WriteLine($"TerminalValue: {terminalValue}");
        _output.WriteLine("\nExpected value breakdown:");
        _output.WriteLine("  4 heroes exited @ 1.0 each = 4.0");
        _output.WriteLine("  1 item collected @ 0.5     = 0.5");
        _output.WriteLine("  Total                      = 4.5");

        // Assert
        Assert.True(isTerminal, "All heroes exited, game should be terminal");
        Assert.Equal(4.5, terminalValue, precision: 1);
    }
}
