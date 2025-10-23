# Monte Carlo Tree Search (MCTS) Demo# Monte Carlo Tree Search (MCTS) Demo# MCTS Demo Projects



A generic, game-neutral implementation of Monte Carlo Tree Search in C# with pluggable policies and multiple demo games.



## Project StructureA generic, game-neutral implementation of Monte Carlo Tree Search in C# with pluggable policies and multiple demo games.This solution contains a generic Monte Carlo Tree Search (MCTS) engine with multiple game demos.



```

MctsDemo/

├── Mcts/                     # Core MCTS library## Project Structure## Project Structure

│   ├── Mcts.cs              # Main MCTS implementation

│   └── MctsTreeVisualizer.cs # Tree visualization utilities

│

└── Demos/                    # Demo games and examples```### GenericMctsDemo (Library)

    ├── PigDemo/             # Dice game with chance nodes

    ├── TreasureHunt/        # Simple grid navigationMctsDemo/The core MCTS engine with pluggable policies:

    ├── ObstacleHunt/        # Pathfinding with obstacles

    ├── TinyQuestDemo/       # 4-hero cooperative game├── Mcts/                    # Core MCTS library- **Game Model Interface**: Define any game state and actions

    └── TinyQuestDemo.Tests/ # Unit tests for TinyQuest

```│   ├── Mcts.cs             # Main MCTS implementation- **Selection Policy**: UCB1 (Upper Confidence Bound)



## Core Library: Mcts│   └── MctsTreeVisualizer.cs  # Tree visualization utilities- **Expansion Policy**: Uniform random expansion with deterministic roll-forward



The `Mcts` project provides a generic MCTS implementation with:│- **Simulation Policy**: Random playouts



- **Pluggable Policies**: Selection, expansion, simulation, and backpropagation└── Demos/                   # Demo games and examples- **Backpropagation Policy**: Standard visit count and value accumulation

- **Chance Node Support**: For stochastic games

- **Deterministic Roll-forward**: Optional lookahead optimization    ├── TreasureHunt/       # Simple grid navigation

- **Root Preservation**: Reuse search trees across moves

- **Progressive Bias**: Threshold-based heuristic guidance    ├── ObstacleHunt/       # Pathfinding with obstaclesSupports:

- **Configurable Options**: Iterations, rollout depth, action selection, verbosity

    ├── TinyQuestDemo/      # 4-hero cooperative game- Deterministic and stochastic (chance nodes) games

### Key Interfaces

    └── TinyQuestDemo.Tests/ # Unit tests for TinyQuest- Two-player alternating turn games

```csharp

IGameModel<TState, TAction>       // Game rules and mechanics```- Configurable search parameters

ISelectionPolicy<TState, TAction> // UCB1, progressive bias, etc.

IExpansionPolicy<TState, TAction> // How to expand nodes

ISimulationPolicy<TState, TAction> // Random playout, etc.

IBackpropPolicy<TState, TAction>  // Value propagation## Core Library: Mcts### PigDemo

```

A dice game example demonstrating **chance nodes**:

## Demo Games

The `Mcts` project provides a generic MCTS implementation with:- Players roll dice or hold to bank points

### PigDemo

Classic dice game demonstrating stochastic decision-making.- Rolling a 1 loses the turn total

- **Players**: 2 (alternating turns)

- **Actions**: Roll dice or Hold to bank points- **Pluggable Policies**: Selection, expansion, simulation, and backpropagation- First to reach target score wins

- **Objective**: First to target score wins

- **Features**: Chance nodes, risk/reward tradeoffs- **Chance Node Support**: For stochastic games- Shows MCTS handling of stochastic outcomes



### TreasureHunt- **Deterministic Roll-forward**: Optional lookahead optimization

Simple grid-based game where a character navigates to collect treasure and reach an exit.

- **Grid**: 5×5- **Root Preservation**: Reuse search trees across moves**Run**: `dotnet run --project PigDemo`

- **Objective**: Collect treasure (2 points) and exit (1 point)

- **Features**: Clean example of deterministic pathfinding- **Progressive Bias**: Threshold-based heuristic guidance



### ObstacleHunt- **Configurable Options**: Iterations, rollout depth, action selection, verbosity### TinyQuestDemo

Extension of TreasureHunt with wall obstacles.

- **Grid**: 5×5 with vertical wall and gapA simple dungeon crawler demonstrating **deterministic sequential decision-making**:

- **Objective**: Navigate around obstacles to collect treasure and exit

- **Features**: Demonstrates MCTS pathfinding around barriers### Key Interfaces- Grid-based movement



### TinyQuestDemo- Combat system with HP tracking

Complex cooperative game with 4 heroes, items, and chance nodes.

- **Grid**: 5×6 hexagonal grid```csharp- Resource management (potions)

- **Heroes**: Warrior, Elf, Thief, Mage

- **Features**: Turn-based, chest items, stochastic elementsIGameModel<TState, TAction>      // Game rules and mechanics- Monster AI opponent

- **Status**: Has some known bugs (preserved for testing)

ISelectionPolicy<TState, TAction> // UCB1, progressive bias, etc.- Win/loss/timeout conditions

## Building and Running

IExpansionPolicy<TState, TAction> // How to expand nodes

Build the entire solution:

```bashISimulationPolicy<TState, TAction> // Random playout, etc.**Run**: `dotnet run --project TinyQuestDemo`

dotnet build

```IBackpropPolicy<TState, TAction>  // Value propagation



Run a demo:```## Building and Running

```bash

cd Demos/TreasureHunt

dotnet run

```## Demo GamesBuild all projects:



Or from solution root:```powershell

```bash

dotnet run --project Demos/TreasureHunt### TreasureHuntdotnet build

```

Simple grid-based game where a character navigates to collect treasure and reach an exit.```

Run tests:

```bash- **Grid**: 5×5

cd Demos/TinyQuestDemo.Tests

dotnet test- **Objective**: Collect treasure (2 points) and exit (1 point)Run a specific demo:

```

- **Features**: Clean example of deterministic pathfinding```powershell

## MCTS Configuration

dotnet run --project PigDemo

```csharp

var options = new MctsOptions### ObstacleHuntdotnet run --project TinyQuestDemo

{

    Iterations = 50000,        // Number of MCTS iterationsExtension of TreasureHunt with wall obstacles.```

    RolloutDepth = 30,         // Max depth for simulations

    FinalActionSelector = NodeStats.SelectByMaxVisit,- **Grid**: 5×5 with vertical wall and gap

    Seed = 42,                 // For reproducibility

    Verbose = false            // Enable debug output- **Objective**: Navigate around obstacles to collect treasure and exit## Creating New Demos

};

```- **Features**: Demonstrates MCTS pathfinding around barriers



## Features Demonstrated1. Create a new console project



- ✅ UCB1 selection with exploration constant### TinyQuestDemo2. Reference `GenericMctsDemo`

- ✅ Progressive bias (threshold-based)

- ✅ Deterministic roll-forward optimizationComplex cooperative game with 4 heroes, items, and chance nodes.3. Implement `IGameModel<TState, TAction>`

- ✅ Root preservation for search reuse

- ✅ Chance node sampling- **Grid**: 5×6 hexagonal grid4. Instantiate MCTS with desired policies

- ✅ Configurable verbosity for debugging

- ✅ Tree visualization (DOT/Graphviz export)- **Heroes**: Warrior, Elf, Thief, Mage5. Call `Search(initialState)` to get best action



## Creating New Games- **Features**: Turn-based, chest items, stochastic elements



1. Create a new console project in `Demos/`- **Status**: Has some known bugs (preserved for testing)See `PigDemo` and `TinyQuestDemo` for examples.

2. Reference the `Mcts` project

3. Implement `IGameModel<TState, TAction>`

4. Instantiate MCTS with desired policies## Building and Running

5. Call `Search(initialState)` to get best action

Build the entire solution:

See existing demos for examples.```bash

dotnet build

## License```



Educational project - free to use and modify.Run a demo:

```bash
cd Demos/TreasureHunt
dotnet run
```

Run tests:
```bash
cd Demos/TinyQuestDemo.Tests
dotnet test
```

## MCTS Configuration

```csharp
var options = new MctsOptions
{
    Iterations = 50000,        // Number of MCTS iterations
    RolloutDepth = 30,         // Max depth for simulations
    FinalActionSelector = NodeStats.SelectByMaxVisit,
    Seed = 42,                 // For reproducibility
    Verbose = false            // Enable debug output
};
```

## Features Demonstrated

- ✅ UCB1 selection with exploration constant
- ✅ Progressive bias (threshold-based)
- ✅ Deterministic roll-forward optimization
- ✅ Root preservation for search reuse
- ✅ Chance node sampling
- ✅ Configurable verbosity for debugging
- ✅ Tree visualization (DOT/Graphviz export)

## License

Educational project - free to use and modify.
