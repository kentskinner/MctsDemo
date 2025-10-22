# MCTS Demo Projects

This solution contains a generic Monte Carlo Tree Search (MCTS) engine with multiple game demos.

## Project Structure

### GenericMctsDemo (Library)
The core MCTS engine with pluggable policies:
- **Game Model Interface**: Define any game state and actions
- **Selection Policy**: UCB1 (Upper Confidence Bound)
- **Expansion Policy**: Uniform random expansion with deterministic roll-forward
- **Simulation Policy**: Random playouts
- **Backpropagation Policy**: Standard visit count and value accumulation

Supports:
- Deterministic and stochastic (chance nodes) games
- Two-player alternating turn games
- Configurable search parameters

### PigDemo
A dice game example demonstrating **chance nodes**:
- Players roll dice or hold to bank points
- Rolling a 1 loses the turn total
- First to reach target score wins
- Shows MCTS handling of stochastic outcomes

**Run**: `dotnet run --project PigDemo`

### TinyQuestDemo
A simple dungeon crawler demonstrating **deterministic sequential decision-making**:
- Grid-based movement
- Combat system with HP tracking
- Resource management (potions)
- Monster AI opponent
- Win/loss/timeout conditions

**Run**: `dotnet run --project TinyQuestDemo`

## Building and Running

Build all projects:
```powershell
dotnet build
```

Run a specific demo:
```powershell
dotnet run --project PigDemo
dotnet run --project TinyQuestDemo
```

## Creating New Demos

1. Create a new console project
2. Reference `GenericMctsDemo`
3. Implement `IGameModel<TState, TAction>`
4. Instantiate MCTS with desired policies
5. Call `Search(initialState)` to get best action

See `PigDemo` and `TinyQuestDemo` for examples.
