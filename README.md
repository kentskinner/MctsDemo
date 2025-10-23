# Monte Carlo Tree Search (MCTS) Demo

A generic, game-neutral implementation of Monte Carlo Tree Search in C# with pluggable policies and multiple demo games.

## Project Structure

MctsDemo/
 Mcts/                     - Core MCTS library
    Mcts.cs              - Main MCTS implementation
    MctsTreeVisualizer.cs - Tree visualization utilities

 Demos/                    - Demo games and examples
     PigDemo/             - Dice game with chance nodes
     TreasureHunt/        - Simple grid navigation
     ObstacleHunt/        - Pathfinding with obstacles
     TinyQuestDemo/       - 4-hero cooperative game
     TinyQuestDemo.Tests/ - Unit tests for TinyQuest

## Core Library: Mcts

The Mcts project provides a generic MCTS implementation with:

- Pluggable Policies: Selection, expansion, simulation, and backpropagation
- Chance Node Support: For stochastic games
- Deterministic Roll-forward: Optional lookahead optimization
- Root Preservation: Reuse search trees across moves
- Progressive Bias: Threshold-based heuristic guidance
- Configurable Options: Iterations, rollout depth, action selection, verbosity

## Demo Games

### PigDemo
Classic dice game demonstrating stochastic decision-making with chance nodes and risk/reward tradeoffs.

### TreasureHunt
Simple grid-based game where a character collects treasure (2 pts) and reaches an exit (1 pt) on a 55 grid.

### ObstacleHunt
Extension of TreasureHunt with wall obstacles. Demonstrates MCTS pathfinding around barriers.

### TinyQuestDemo
Complex cooperative game with 4 heroes (Warrior, Elf, Thief, Mage) on a hexagonal grid with items and chance nodes.

## Building and Running

Build the solution:
    dotnet build

Run a demo:
    cd Demos/TreasureHunt
    dotnet run

Or from solution root:
    dotnet run --project Demos/TreasureHunt

Run tests:
    cd Demos/TinyQuestDemo.Tests
    dotnet test

## Features Demonstrated

- UCB1 selection with exploration constant
- Progressive bias (threshold-based)
- Deterministic roll-forward optimization
- Root preservation for search reuse
- Chance node sampling
- Configurable verbosity for debugging
- Tree visualization (DOT/Graphviz export)

## License

Educational project - free to use and modify.
