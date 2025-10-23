using Mcts;

namespace ObstacleHunt;

/// <summary>
/// Grid-based treasure hunt game with obstacles.
/// Character starts at position, must reach exit within N turns while avoiding obstacles.
/// Can collect treasure along the way for bonus points.
/// </summary>
public enum GameAction
{
    MoveNorth,
    MoveSouth,
    MoveEast,
    MoveWest,
    PickupTreasure,
    Exit
}

public readonly record struct GameState(
    int CharacterX,      // Character's X coordinate (0-based)
    int CharacterY,      // Character's Y coordinate (0-based)
    int TreasureX,       // Treasure X coordinate
    int TreasureY,       // Treasure Y coordinate
    int ExitX,           // Exit X coordinate
    int ExitY,           // Exit Y coordinate
    bool HasTreasure,    // Has character collected the treasure?
    bool HasExited,      // Has character exited?
    int TurnCount        // Number of turns taken
);

public class ObstacleHuntGame : IGameModel<GameState, GameAction>
{
    public int GridWidth { get; }
    public int GridHeight { get; }
    public int MaxTurns { get; }
    public double TreasureReward { get; }
    public double ExitReward { get; }
    public HashSet<(int X, int Y)> Obstacles { get; }

    public ObstacleHuntGame(int gridWidth = 5, int gridHeight = 5, int maxTurns = 20, 
        double treasureReward = 2.0, double exitReward = 1.0,
        HashSet<(int X, int Y)>? obstacles = null)
    {
        GridWidth = gridWidth;
        GridHeight = gridHeight;
        MaxTurns = maxTurns;
        TreasureReward = treasureReward;
        ExitReward = exitReward;
        Obstacles = obstacles ?? new HashSet<(int X, int Y)>();
    }

    public bool IsTerminal(in GameState state, out double terminalValue)
    {
        // Game ends if character exited or ran out of turns
        if (state.HasExited)
        {
            terminalValue = ExitReward + (state.HasTreasure ? TreasureReward : 0);
            return true;
        }

        if (state.TurnCount >= MaxTurns)
        {
            // Failed to exit in time - only get treasure reward if collected
            terminalValue = state.HasTreasure ? TreasureReward : 0;
            return true;
        }

        terminalValue = 0;
        return false;
    }

    public bool IsChanceNode(in GameState state)
    {
        // This is a fully deterministic game - no chance nodes
        return false;
    }

    public GameState SampleChance(in GameState state, Random rng, out double logProb)
    {
        // No chance nodes in this game
        logProb = 0;
        return state;
    }

    private bool IsBlocked(int x, int y)
    {
        return Obstacles.Contains((x, y));
    }

    public IEnumerable<GameAction> LegalActions(GameState state)
    {
        // Can't take actions if already exited or out of turns
        if (state.HasExited || state.TurnCount >= MaxTurns)
            yield break;

        // Movement actions (only if within bounds and not blocked)
        if (state.CharacterY > 0 && !IsBlocked(state.CharacterX, state.CharacterY - 1))
            yield return GameAction.MoveNorth;
        
        if (state.CharacterY < GridHeight - 1 && !IsBlocked(state.CharacterX, state.CharacterY + 1))
            yield return GameAction.MoveSouth;
        
        if (state.CharacterX < GridWidth - 1 && !IsBlocked(state.CharacterX + 1, state.CharacterY))
            yield return GameAction.MoveEast;
        
        if (state.CharacterX > 0 && !IsBlocked(state.CharacterX - 1, state.CharacterY))
            yield return GameAction.MoveWest;

        // Pickup treasure (only if at treasure location and haven't picked it up yet)
        if (!state.HasTreasure && 
            state.CharacterX == state.TreasureX && 
            state.CharacterY == state.TreasureY)
        {
            yield return GameAction.PickupTreasure;
        }

        // Exit (only if at exit location)
        if (state.CharacterX == state.ExitX && state.CharacterY == state.ExitY)
        {
            yield return GameAction.Exit;
        }
    }

    public GameState Step(in GameState state, in GameAction action)
    {
        // Increment turn counter for all actions
        var newTurnCount = state.TurnCount + 1;
        
        return action switch
        {
            GameAction.MoveNorth => state with { CharacterY = state.CharacterY - 1, TurnCount = newTurnCount },
            GameAction.MoveSouth => state with { CharacterY = state.CharacterY + 1, TurnCount = newTurnCount },
            GameAction.MoveEast => state with { CharacterX = state.CharacterX + 1, TurnCount = newTurnCount },
            GameAction.MoveWest => state with { CharacterX = state.CharacterX - 1, TurnCount = newTurnCount },
            GameAction.PickupTreasure => state with { HasTreasure = true, TurnCount = newTurnCount },
            GameAction.Exit => state with { HasExited = true, TurnCount = newTurnCount },
            _ => state
        };
    }
}
