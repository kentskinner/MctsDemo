using Mcts;

namespace MonsterMaze;

/// <summary>
/// Grid-based game with a moving monster, mines, treasure, and obstacles.
/// Player must navigate to collect treasure and reach exit while avoiding the monster and mines.
/// Monster moves towards the player each turn.
/// </summary>
public enum GameAction
{
    MoveNorth,
    MoveSouth,
    MoveEast,
    MoveWest,
    PickupTreasure,
    Exit,
    Wait  // Stay in place (monster still moves)
}

public readonly record struct GameState(
    int PlayerX,         // Player's X coordinate
    int PlayerY,         // Player's Y coordinate
    int MonsterX,        // Monster's X coordinate
    int MonsterY,        // Monster's Y coordinate
    int TreasureX,       // Treasure X coordinate
    int TreasureY,       // Treasure Y coordinate
    int ExitX,           // Exit X coordinate
    int ExitY,           // Exit Y coordinate
    bool HasTreasure,    // Has player collected the treasure?
    bool HasExited,      // Has player exited?
    bool IsDead,         // Did player die (monster or mine)?
    bool MonsterDead,    // Did monster step on a mine?
    int TurnCount,       // Number of turns taken
    double RewardCollected  // Cumulative reward collected so far
);

public class MonsterMazeGame : IGameModel<GameState, GameAction>
{
    public int GridWidth { get; }
    public int GridHeight { get; }
    public int MaxTurns { get; }
    public double TreasureReward { get; }
    public double ExitReward { get; }
    public double DeathPenalty { get; }
    public HashSet<(int X, int Y)> Obstacles { get; }
    public HashSet<(int X, int Y)> Mines { get; }

    public MonsterMazeGame(
        int gridWidth = 7, 
        int gridHeight = 7, 
        int maxTurns = 40, 
        double treasureReward = 2.0, 
        double exitReward = 1.0,
        double deathPenalty = -5.0,
        HashSet<(int X, int Y)>? obstacles = null,
        HashSet<(int X, int Y)>? mines = null)
    {
        GridWidth = gridWidth;
        GridHeight = gridHeight;
        MaxTurns = maxTurns;
        TreasureReward = treasureReward;
        ExitReward = exitReward;
        DeathPenalty = deathPenalty;
        Obstacles = obstacles ?? new HashSet<(int X, int Y)>();
        Mines = mines ?? new HashSet<(int X, int Y)>();
    }

    public bool IsTerminal(in GameState state, out double terminalValue)
    {
        // Death
        if (state.IsDead)
        {
            terminalValue = state.RewardCollected;
            return true;
        }

        // Victory - reached exit
        if (state.HasExited)
        {
            terminalValue = state.RewardCollected;
            return true;
        }

        // Timeout
        if (state.TurnCount >= MaxTurns)
        {
            terminalValue = state.RewardCollected;  // Keep whatever rewards collected
            return true;
        }

        terminalValue = 0;
        return false;
    }

    public bool IsChanceNode(in GameState state)
    {
        // Deterministic game - monster moves predictably towards player
        return false;
    }

    public GameState SampleChance(in GameState state, Random rng, out double logProb)
    {
        logProb = 0;
        return state;
    }

    private bool IsBlocked(int x, int y)
    {
        return Obstacles.Contains((x, y));
    }

    private bool IsMine(int x, int y)
    {
        return Mines.Contains((x, y));
    }

    public IEnumerable<GameAction> LegalActions(GameState state)
    {
        // Can't take actions if dead, exited, or out of turns
        if (state.IsDead || state.HasExited || state.TurnCount >= MaxTurns)
            yield break;

        // Movement actions (only if within bounds and not blocked)
        if (state.PlayerY > 0 && !IsBlocked(state.PlayerX, state.PlayerY - 1))
            yield return GameAction.MoveNorth;
        
        if (state.PlayerY < GridHeight - 1 && !IsBlocked(state.PlayerX, state.PlayerY + 1))
            yield return GameAction.MoveSouth;
        
        if (state.PlayerX < GridWidth - 1 && !IsBlocked(state.PlayerX + 1, state.PlayerY))
            yield return GameAction.MoveEast;
        
        if (state.PlayerX > 0 && !IsBlocked(state.PlayerX - 1, state.PlayerY))
            yield return GameAction.MoveWest;

        // Wait action (stay in place, but monster still moves)
        yield return GameAction.Wait;

        // Pickup treasure (only if at treasure location and haven't picked it up yet)
        if (!state.HasTreasure && 
            state.PlayerX == state.TreasureX && 
            state.PlayerY == state.TreasureY)
        {
            yield return GameAction.PickupTreasure;
        }

        // Exit (only if at exit location)
        if (state.PlayerX == state.ExitX && state.PlayerY == state.ExitY)
        {
            yield return GameAction.Exit;
        }
    }

    /// <summary>
    /// Monster moves one step towards player using Manhattan distance (simple AI)
    /// </summary>
    private (int newMonsterX, int newMonsterY) MoveMonster(GameState state)
    {
        int mx = state.MonsterX;
        int my = state.MonsterY;
        
        // Calculate distance to player in each direction
        int dx = state.PlayerX - mx;
        int dy = state.PlayerY - my;

        // Try to move closer on the axis with greatest distance
        if (Math.Abs(dx) > Math.Abs(dy))
        {
            // Move horizontally towards player
            int newX = mx + Math.Sign(dx);
            if (newX >= 0 && newX < GridWidth && !IsBlocked(newX, my))
                return (newX, my);
        }
        else if (dy != 0)
        {
            // Move vertically towards player
            int newY = my + Math.Sign(dy);
            if (newY >= 0 && newY < GridHeight && !IsBlocked(mx, newY))
                return (mx, newY);
        }

        // If preferred direction is blocked, try the other direction
        if (dx != 0)
        {
            int newX = mx + Math.Sign(dx);
            if (newX >= 0 && newX < GridWidth && !IsBlocked(newX, my))
                return (newX, my);
        }
        
        if (dy != 0)
        {
            int newY = my + Math.Sign(dy);
            if (newY >= 0 && newY < GridHeight && !IsBlocked(mx, newY))
                return (mx, newY);
        }

        // If completely blocked, stay in place
        return (mx, my);
    }

    public GameState Step(in GameState state, in GameAction action)
    {
        var newTurnCount = state.TurnCount + 1;
        int newPlayerX = state.PlayerX;
        int newPlayerY = state.PlayerY;
        bool newHasTreasure = state.HasTreasure;
        bool newHasExited = state.HasExited;

        // Execute player action
        switch (action)
        {
            case GameAction.MoveNorth:
                newPlayerY = state.PlayerY - 1;
                break;
            case GameAction.MoveSouth:
                newPlayerY = state.PlayerY + 1;
                break;
            case GameAction.MoveEast:
                newPlayerX = state.PlayerX + 1;
                break;
            case GameAction.MoveWest:
                newPlayerX = state.PlayerX - 1;
                break;
            case GameAction.PickupTreasure:
                newHasTreasure = true;
                break;
            case GameAction.Exit:
                newHasExited = true;
                break;
            case GameAction.Wait:
                // Player doesn't move
                break;
        }

        // Check if player stepped on a mine
        bool isDead = IsMine(newPlayerX, newPlayerY);

        // Move monster towards player (only if monster and player are both alive)
        bool monsterDead = state.MonsterDead;
        int newMonsterX = state.MonsterX;
        int newMonsterY = state.MonsterY;
        
        if (!isDead && !monsterDead)
        {
            (newMonsterX, newMonsterY) = MoveMonster(state);
            
            // Check if monster stepped on a mine
            if (IsMine(newMonsterX, newMonsterY))
            {
                monsterDead = true;
            }
        }

        // Check if monster caught player (only if monster is alive)
        if (!monsterDead && newPlayerX == newMonsterX && newPlayerY == newMonsterY)
        {
            isDead = true;
        }

        // Calculate immediate reward
        double newReward = state.RewardCollected;

        // Give treasure reward immediately when picked up
        if (newHasTreasure && !state.HasTreasure)
        {
            newReward += TreasureReward;
        }

        // Give exit reward when exiting
        if (newHasExited && !state.HasExited)
        {
            newReward += ExitReward;
        }

        // Apply death penalty
        if (isDead && !state.IsDead)
        {
            newReward += DeathPenalty;
        }

        return new GameState(
            PlayerX: newPlayerX,
            PlayerY: newPlayerY,
            MonsterX: newMonsterX,
            MonsterY: newMonsterY,
            TreasureX: state.TreasureX,
            TreasureY: state.TreasureY,
            ExitX: state.ExitX,
            ExitY: state.ExitY,
            HasTreasure: newHasTreasure,
            HasExited: newHasExited,
            IsDead: isDead,
            MonsterDead: monsterDead,
            TurnCount: newTurnCount,
            RewardCollected: newReward
        );
    }
}
