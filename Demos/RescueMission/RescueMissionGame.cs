using Mcts;

namespace RescueMission;

public enum GameAction
{
    // Hero 1 actions
    Hero1North, Hero1South, Hero1East, Hero1West, Hero1Wait, Hero1PickupKey,
    // Hero 2 actions  
    Hero2North, Hero2South, Hero2East, Hero2West, Hero2Wait, Hero2OpenDoor,
    // Both exit together
    BothExit
}

public readonly record struct GameState(
    int Hero1X, int Hero1Y,
    int Hero2X, int Hero2Y,
    int MonsterX, int MonsterY,
    int KeyX, int KeyY,
    int DoorX, int DoorY,
    int ExitX, int ExitY,
    bool HasKey,
    bool DoorOpen,
    bool Hero1Exited,
    bool Hero2Exited,
    bool Hero1Dead,
    bool Hero2Dead,
    bool MonsterDead,
    int TurnCount,
    double RewardCollected
);

public class RescueMissionGame : IGameModel<GameState, GameAction>
{
    public int GridWidth { get; } = 9;
    public int GridHeight { get; } = 9;
    public int MaxTurns { get; } = 50;
    
    public HashSet<(int x, int y)> Obstacles { get; }
    public HashSet<(int x, int y)> Mines { get; }
    
    // Rewards
    public double KeyReward { get; } = 10.0;
    public double DoorReward { get; } = 15.0;
    public double ExitReward { get; } = 30.0;  // Both heroes exit together
    public double DeathPenalty { get; } = -20.0;

    public RescueMissionGame()
    {
        // Create obstacles (walls)
        Obstacles = new HashSet<(int, int)>
        {
            // Central wall
            (4, 2), (4, 3), (4, 4), (4, 5), (4, 6),
            // Side obstacles
            (2, 4), (6, 4)
        };
        
        // Mines scattered around
        Mines = new HashSet<(int, int)>
        {
            (3, 2), (5, 6), (7, 3), (1, 7)
        };
    }

    public (double value, bool isTerminal) IsTerminal(in GameState state)
    {
        // Game over conditions
        if (state.Hero1Dead || state.Hero2Dead)
            return (state.RewardCollected, true);
        
        if (state.Hero1Exited && state.Hero2Exited)
            return (state.RewardCollected, true);
        
        if (state.TurnCount >= MaxTurns)
            return (state.RewardCollected, true);
        
        return (0, false);
    }

    public bool IsTerminal(in GameState state, out double value)
    {
        var (v, term) = IsTerminal(state);
        value = v;
        return term;
    }

    public bool IsChanceNode(in GameState state)
    {
        return false;  // Deterministic
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
        // Can't act if game is over
        if (state.Hero1Dead || state.Hero2Dead || 
            (state.Hero1Exited && state.Hero2Exited) || 
            state.TurnCount >= MaxTurns)
            yield break;

        // Special case: both on exit and door is open -> can exit together
        if (!state.Hero1Exited && !state.Hero2Exited &&
            state.Hero1X == state.ExitX && state.Hero1Y == state.ExitY &&
            state.Hero2X == state.ExitX && state.Hero2Y == state.ExitY &&
            state.DoorOpen)
        {
            yield return GameAction.BothExit;
        }

        // Hero 1 actions (if not exited)
        if (!state.Hero1Exited)
        {
            if (state.Hero1Y > 0 && !IsBlocked(state.Hero1X, state.Hero1Y - 1))
                yield return GameAction.Hero1North;
            if (state.Hero1Y < GridHeight - 1 && !IsBlocked(state.Hero1X, state.Hero1Y + 1))
                yield return GameAction.Hero1South;
            if (state.Hero1X < GridWidth - 1 && !IsBlocked(state.Hero1X + 1, state.Hero1Y))
                yield return GameAction.Hero1East;
            if (state.Hero1X > 0 && !IsBlocked(state.Hero1X - 1, state.Hero1Y))
                yield return GameAction.Hero1West;
            
            yield return GameAction.Hero1Wait;
            
            // Can pickup key if at key location
            if (!state.HasKey && state.Hero1X == state.KeyX && state.Hero1Y == state.KeyY)
                yield return GameAction.Hero1PickupKey;
        }

        // Hero 2 actions (if not exited)
        if (!state.Hero2Exited)
        {
            if (state.Hero2Y > 0 && !IsBlocked(state.Hero2X, state.Hero2Y - 1))
                yield return GameAction.Hero2North;
            if (state.Hero2Y < GridHeight - 1 && !IsBlocked(state.Hero2X, state.Hero2Y + 1))
                yield return GameAction.Hero2South;
            if (state.Hero2X < GridWidth - 1 && !IsBlocked(state.Hero2X + 1, state.Hero2Y))
                yield return GameAction.Hero2East;
            if (state.Hero2X > 0 && !IsBlocked(state.Hero2X - 1, state.Hero2Y))
                yield return GameAction.Hero2West;
            
            yield return GameAction.Hero2Wait;
            
            // Can open door if at door location and has key
            if (!state.DoorOpen && state.HasKey && 
                state.Hero2X == state.DoorX && state.Hero2Y == state.DoorY)
                yield return GameAction.Hero2OpenDoor;
        }
    }

    private (int newMonsterX, int newMonsterY) MoveMonster(GameState state)
    {
        if (state.MonsterDead) return (state.MonsterX, state.MonsterY);
        
        int mx = state.MonsterX;
        int my = state.MonsterY;
        
        // Chase nearest hero
        int dist1 = Math.Abs(state.Hero1X - mx) + Math.Abs(state.Hero1Y - my);
        int dist2 = Math.Abs(state.Hero2X - mx) + Math.Abs(state.Hero2Y - my);
        
        int targetX = dist1 <= dist2 ? state.Hero1X : state.Hero2X;
        int targetY = dist1 <= dist2 ? state.Hero1Y : state.Hero2Y;
        
        int dx = targetX - mx;
        int dy = targetY - my;
        
        // Try to move closer
        if (Math.Abs(dx) > Math.Abs(dy))
        {
            int newX = mx + Math.Sign(dx);
            if (newX >= 0 && newX < GridWidth && !IsBlocked(newX, my))
                return (newX, my);
        }
        else if (dy != 0)
        {
            int newY = my + Math.Sign(dy);
            if (newY >= 0 && newY < GridHeight && !IsBlocked(mx, newY))
                return (mx, newY);
        }
        
        // Try other direction
        if (dy != 0)
        {
            int newY = my + Math.Sign(dy);
            if (newY >= 0 && newY < GridHeight && !IsBlocked(mx, newY))
                return (mx, newY);
        }
        else if (dx != 0)
        {
            int newX = mx + Math.Sign(dx);
            if (newX >= 0 && newX < GridWidth && !IsBlocked(newX, my))
                return (newX, my);
        }
        
        return (mx, my);  // Can't move
    }

    public GameState Step(in GameState state, in GameAction action)
    {
        int newH1X = state.Hero1X, newH1Y = state.Hero1Y;
        int newH2X = state.Hero2X, newH2Y = state.Hero2Y;
        bool newHasKey = state.HasKey;
        bool newDoorOpen = state.DoorOpen;
        bool newH1Exited = state.Hero1Exited;
        bool newH2Exited = state.Hero2Exited;
        double newReward = state.RewardCollected;

        // Apply action
        switch (action)
        {
            // Hero 1 movement
            case GameAction.Hero1North: newH1Y--; break;
            case GameAction.Hero1South: newH1Y++; break;
            case GameAction.Hero1East: newH1X++; break;
            case GameAction.Hero1West: newH1X--; break;
            case GameAction.Hero1PickupKey:
                if (!newHasKey)
                {
                    newHasKey = true;
                    newReward += KeyReward;
                }
                break;
            
            // Hero 2 movement
            case GameAction.Hero2North: newH2Y--; break;
            case GameAction.Hero2South: newH2Y++; break;
            case GameAction.Hero2East: newH2X++; break;
            case GameAction.Hero2West: newH2X--; break;
            case GameAction.Hero2OpenDoor:
                if (!newDoorOpen && newHasKey)
                {
                    newDoorOpen = true;
                    newReward += DoorReward;
                }
                break;
            
            // Both exit
            case GameAction.BothExit:
                if (!newH1Exited && !newH2Exited && newDoorOpen)
                {
                    newH1Exited = true;
                    newH2Exited = true;
                    newReward += ExitReward;
                }
                break;
        }

        // Move monster
        var (newMX, newMY) = MoveMonster(state);
        bool newMonsterDead = state.MonsterDead;
        
        // Check if monster stepped on mine
        if (IsMine(newMX, newMY) && !newMonsterDead)
        {
            newMonsterDead = true;
        }

        // Check hero deaths
        bool newH1Dead = state.Hero1Dead;
        bool newH2Dead = state.Hero2Dead;
        
        // Heroes die if on mine
        if (IsMine(newH1X, newH1Y) && !newH1Dead)
        {
            newH1Dead = true;
            newReward += DeathPenalty;
        }
        if (IsMine(newH2X, newH2Y) && !newH2Dead)
        {
            newH2Dead = true;
            newReward += DeathPenalty;
        }
        
        // Heroes die if monster catches them
        if (!newMonsterDead)
        {
            if (newMX == newH1X && newMY == newH1Y && !newH1Dead)
            {
                newH1Dead = true;
                newReward += DeathPenalty;
            }
            if (newMX == newH2X && newMY == newH2Y && !newH2Dead)
            {
                newH2Dead = true;
                newReward += DeathPenalty;
            }
        }

        return new GameState(
            newH1X, newH1Y,
            newH2X, newH2Y,
            newMX, newMY,
            state.KeyX, state.KeyY,
            state.DoorX, state.DoorY,
            state.ExitX, state.ExitY,
            newHasKey,
            newDoorOpen,
            newH1Exited,
            newH2Exited,
            newH1Dead,
            newH2Dead,
            newMonsterDead,
            state.TurnCount + 1,
            newReward
        );
    }
}
