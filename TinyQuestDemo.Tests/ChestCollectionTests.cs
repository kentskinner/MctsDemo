using Xunit;
using Xunit.Abstractions;
using GenericMcts;
using TinyQuestDemo;

namespace TinyQuestDemo.Tests;

public class ChestCollectionTests
{
    private readonly ITestOutputHelper _output;

    public ChestCollectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ChestNextToExit_ShouldBeCollected_WithPlainUCB()
    {
        // Arrange: Simple scenario
        // Warrior at hex 8
        // Chest at hex 3 (adjacent to both 8 and exit)
        // Exit at hex 4
        // Optimal path: 8 -> 3 (chest) -> 4 (exit) = 1.5 reward
        // Suboptimal: 8 -> 9 -> 4 (exit) = 1.0 reward
        
        var game = new TinyQuestGame();
        
        var initialState = new QuestState(
            Warrior: new Hero(HeroType.Warrior, CurrentHex: 8, HasExited: false, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Elf: new Hero(HeroType.Elf, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Thief: new Hero(HeroType.Thief, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Mage: new Hero(HeroType.Mage, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            ActiveHeroIndex: -1,
            ActionsRemaining: 0,
            ExitHex: 4,
            Chest0Hex: 3,
            Chest1Hex: 15,  // Far away
            Chest2Hex: 25,  // Far away
            TurnCount: 0,
            WarriorActivatedThisTurn: false,
            ElfActivatedThisTurn: false,
            ThiefActivatedThisTurn: false,
            MageActivatedThisTurn: false,
            Chest0Present: true,
            Chest1Present: true,
            Chest2Present: true,
            PendingChestItem: false
        );

        // Act: Run MCTS with plain UCB1 (no progressive bias)
        var selection = new Ucb1Selection<QuestState, QuestAction>(explorationC: 1.414);
        var expansion = new UniformSingleExpansion<QuestState, QuestAction>(deterministicRollForward: true);
        var simulation = new UniformRandomSimulation<QuestState, QuestAction>();
        var backprop = new SumBackpropagation<QuestState, QuestAction>();

        var options = new MctsOptions
        {
            Iterations = 10_000,
            RolloutDepth = 100,
            FinalActionSelector = NodeStats.SelectByMaxVisit,
            Seed = 42
        };

        var mcts = new Mcts<QuestState, QuestAction>(game, selection, expansion, simulation, backprop, options);
        var (bestAction, stats) = mcts.Search(initialState, out var rootNode);

        // Debug output
        _output.WriteLine("=== Action Statistics ===");
        foreach (var (action, visits, value) in stats)
        {
            var mean = visits > 0 ? value / visits : 0.0;
            _output.WriteLine($"{action,-20} visits={visits,6}  total={value,8:F2}  mean={mean,7:F3}");
        }

        // Navigate to the first decision node (after ActivateWarrior)
        var warriorNode = rootNode.Children.FirstOrDefault(c => 
            c.IncomingAction.ToString() == "ActivateWarrior");
        Assert.NotNull(warriorNode);

        _output.WriteLine("\n=== Movement Choices from Hex 8 ===");
        foreach (var child in warriorNode.Children.OrderByDescending(c => c.Visits))
        {
            var action = child.IncomingAction.ToString();
            var q = child.Visits > 0 ? child.TotalValue / child.Visits : 0.0;
            _output.WriteLine($"{action,-20} visits={child.Visits,6}  Q={q,7:F3}");
        }

        // Find the two key branches
        var chestPath = warriorNode.Children.FirstOrDefault(c => 
            c.IncomingAction.ToString() == "MoveToHex3");
        var directPath = warriorNode.Children.FirstOrDefault(c => 
            c.IncomingAction.ToString() == "MoveToHex9");

        Assert.NotNull(chestPath);
        Assert.NotNull(directPath);

        _output.WriteLine($"\n=== Path Comparison ===");
        _output.WriteLine($"Chest path (8->3->4):  Visits={chestPath.Visits}, Q={chestPath.TotalValue / Math.Max(1, chestPath.Visits):F3}");
        _output.WriteLine($"Direct path (8->9->4): Visits={directPath.Visits}, Q={directPath.TotalValue / Math.Max(1, directPath.Visits):F3}");

        // Expected: Chest path should have higher Q-value (4.5 vs 4.0)
        var chestQ = chestPath.TotalValue / Math.Max(1, chestPath.Visits);
        var directQ = directPath.TotalValue / Math.Max(1, directPath.Visits);

        _output.WriteLine($"\nExpected chest Q: ~4.5 (3 already exited + warrior exit + item)");
        _output.WriteLine($"Expected direct Q: ~4.0 (3 already exited + warrior exit)");
        _output.WriteLine($"Actual difference: {chestQ - directQ:F3}");

        // Assert: Chest path should be explored significantly
        Assert.True(chestPath.Visits > 50, 
            $"Chest path barely explored! Only {chestPath.Visits} visits out of {options.Iterations}");

        // Assert: Chest path should have higher Q-value
        Assert.True(chestQ > directQ, 
            $"Chest path Q-value ({chestQ:F3}) should be higher than direct path ({directQ:F3})");
    }

    [Fact]
    public void ManualSimulation_ChestPath_ShouldGive4Point5()
    {
        // Manually trace the chest collection path to verify expected reward
        var game = new TinyQuestGame();
        
        var state = new QuestState(
            Warrior: new Hero(HeroType.Warrior, CurrentHex: 8, HasExited: false, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Elf: new Hero(HeroType.Elf, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Thief: new Hero(HeroType.Thief, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Mage: new Hero(HeroType.Mage, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            ActiveHeroIndex: -1,
            ActionsRemaining: 0,
            ExitHex: 4,
            Chest0Hex: 3,
            Chest1Hex: 15,
            Chest2Hex: 25,
            TurnCount: 0,
            WarriorActivatedThisTurn: false,
            ElfActivatedThisTurn: false,
            ThiefActivatedThisTurn: false,
            MageActivatedThisTurn: false,
            Chest0Present: true,
            Chest1Present: true,
            Chest2Present: true,
            PendingChestItem: false
        );

        _output.WriteLine("=== Manual Trace: Chest Collection Path ===");
        
        // Step 1: Activate Warrior
        state = game.Step(state, QuestAction.ActivateWarrior);
        _output.WriteLine($"After ActivateWarrior: ActiveHeroIndex={state.ActiveHeroIndex}, ActionsRemaining={state.ActionsRemaining}");
        
        // Step 2: Move to chest at hex 3
        state = game.Step(state, QuestAction.MoveToHex3);
        _output.WriteLine($"After MoveToHex3: Warrior at {state.Warrior.CurrentHex}, ActionsRemaining={state.ActionsRemaining}");
        
        // Step 3: Open chest
        state = game.Step(state, QuestAction.OpenChest);
        _output.WriteLine($"After OpenChest: PendingChestItem={state.PendingChestItem}, Chest0Present={state.Chest0Present}");
        
        // Step 4: Resolve chance node (give item)
        var rng = new Random(42);
        state = game.SampleChance(state, rng, out var logProb);
        _output.WriteLine($"After SampleChance: Warrior.HasItem1={state.Warrior.HasItem1}, PendingChestItem={state.PendingChestItem}");
        
        // Step 5: Activate Warrior again (new turn)
        state = game.Step(state, QuestAction.ActivateWarrior);
        _output.WriteLine($"After ActivateWarrior (turn 2): ActionsRemaining={state.ActionsRemaining}");
        
        // Step 6: Move to exit
        state = game.Step(state, QuestAction.MoveToHex4);
        _output.WriteLine($"After MoveToHex4: Warrior.HasExited={state.Warrior.HasExited}");
        
        // Check terminal value
        var isTerminal = game.IsTerminal(state, out var terminalValue);
        _output.WriteLine($"\nIsTerminal: {isTerminal}");
        _output.WriteLine($"Terminal Value: {terminalValue:F1}");
        _output.WriteLine($"Expected: 4.5 (3 exited heroes @ 1.0 + warrior exit @ 1.0 + item @ 0.5)");
        
        Assert.True(isTerminal);
        Assert.Equal(4.5, terminalValue, precision: 1);
    }

    [Fact]
    public void ManualSimulation_DirectPath_ShouldGive4Point0()
    {
        // Manually trace the direct exit path to verify expected reward
        var game = new TinyQuestGame();
        
        var state = new QuestState(
            Warrior: new Hero(HeroType.Warrior, CurrentHex: 8, HasExited: false, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Elf: new Hero(HeroType.Elf, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Thief: new Hero(HeroType.Thief, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            Mage: new Hero(HeroType.Mage, CurrentHex: 29, HasExited: true, IsDead: false, 
                IsInjured: false, HasMoved: false, HasItem1: false, HasItem2: false, HasItem3: false),
            ActiveHeroIndex: -1,
            ActionsRemaining: 0,
            ExitHex: 4,
            Chest0Hex: 3,
            Chest1Hex: 15,
            Chest2Hex: 25,
            TurnCount: 0,
            WarriorActivatedThisTurn: false,
            ElfActivatedThisTurn: false,
            ThiefActivatedThisTurn: false,
            MageActivatedThisTurn: false,
            Chest0Present: true,
            Chest1Present: true,
            Chest2Present: true,
            PendingChestItem: false
        );

        _output.WriteLine("=== Manual Trace: Direct Exit Path ===");
        
        // Step 1: Activate Warrior
        state = game.Step(state, QuestAction.ActivateWarrior);
        _output.WriteLine($"After ActivateWarrior: ActiveHeroIndex={state.ActiveHeroIndex}, ActionsRemaining={state.ActionsRemaining}");
        
        // Step 2: Move to hex 9
        state = game.Step(state, QuestAction.MoveToHex9);
        _output.WriteLine($"After MoveToHex9: Warrior at {state.Warrior.CurrentHex}, ActionsRemaining={state.ActionsRemaining}");
        
        // Step 3: Move to exit
        state = game.Step(state, QuestAction.MoveToHex4);
        _output.WriteLine($"After MoveToHex4: Warrior.HasExited={state.Warrior.HasExited}");
        
        // Check terminal value
        var isTerminal = game.IsTerminal(state, out var terminalValue);
        _output.WriteLine($"\nIsTerminal: {isTerminal}");
        _output.WriteLine($"Terminal Value: {terminalValue:F1}");
        _output.WriteLine($"Expected: 4.0 (3 exited heroes @ 1.0 + warrior exit @ 1.0)");
        
        Assert.True(isTerminal);
        Assert.Equal(4.0, terminalValue, precision: 1);
    }
}
