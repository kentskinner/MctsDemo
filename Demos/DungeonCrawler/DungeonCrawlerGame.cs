using Mcts;

namespace DungeonCrawler;

/// <summary>
/// A dungeon crawler with chance nodes for combat and loot discovery.
/// Player explores a grid, fights monsters (with dice rolls), and collects treasure.
/// </summary>
public enum PlayerAction
{
    MoveNorth,
    MoveSouth,
    MoveEast,
    MoveWest,
    SearchRoom,    // Search current room for treasure (chance-based)
    FightMonster,  // Attack monster in current room (chance-based)
    UsePotion,     // Heal if you have a potion
    Exit           // Exit the dungeon with collected treasure
}

public enum ChanceOutcome
{
    // Combat outcomes
    CombatCriticalHit,   // Deal 3 damage
    CombatHit,           // Deal 2 damage
    CombatMiss,          // Deal 0 damage, monster counters
    
    // Search outcomes
    SearchTreasure,      // Find 10 gold
    SearchPotion,        // Find healing potion
    SearchMonster,       // Disturb a monster
    SearchNothing        // Find nothing
}

public readonly record struct GameState(
    int PlayerX,
    int PlayerY,
    int PlayerHealth,
    int Gold,
    int Potions,
    bool HasExited,
    int TurnCount,
    Dictionary<(int X, int Y), RoomState> Rooms,  // State of each room
    double RewardCollected
);

public readonly record struct RoomState(
    bool Searched,
    int MonsterHealth  // 0 = no monster, >0 = monster present
);

public class DungeonCrawlerGame : IGameModel<GameState, PlayerAction>
{
    public int GridWidth { get; }
    public int GridHeight { get; }
    public int MaxTurns { get; }
    public (int X, int Y) ExitLocation { get; }
    
    private readonly Random _random;

    public DungeonCrawlerGame(
        int gridWidth = 5,
        int gridHeight = 5,
        int maxTurns = 30,
        int seed = 42)
    {
        GridWidth = gridWidth;
        GridHeight = gridHeight;
        MaxTurns = maxTurns;
        ExitLocation = (gridWidth / 2, 0);  // Exit at top center
        _random = new Random(seed);
    }

    public GameState InitialState()
    {
        // Start at bottom center
        var startX = GridWidth / 2;
        var startY = GridHeight - 1;
        
        // Initialize rooms with some monsters
        var rooms = new Dictionary<(int X, int Y), RoomState>();
        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                // Don't put monsters at start or exit
                if ((x == startX && y == startY) || (x == ExitLocation.X && y == ExitLocation.Y))
                {
                    rooms[(x, y)] = new RoomState(Searched: false, MonsterHealth: 0);
                }
                else
                {
                    // 30% chance of monster with 2-4 health
                    var hasMonster = _random.NextDouble() < 0.3;
                    var health = hasMonster ? _random.Next(2, 5) : 0;
                    rooms[(x, y)] = new RoomState(Searched: false, MonsterHealth: health);
                }
            }
        }

        return new GameState(
            PlayerX: startX,
            PlayerY: startY,
            PlayerHealth: 10,
            Gold: 0,
            Potions: 1,  // Start with one potion
            HasExited: false,
            TurnCount: 0,
            Rooms: rooms,
            RewardCollected: 0
        );
    }

    public bool IsTerminal(in GameState state, out double terminalValue)
    {
        if (state.HasExited)
        {
            terminalValue = state.RewardCollected;
            return true;
        }
        if (state.PlayerHealth <= 0)
        {
            terminalValue = state.RewardCollected;
            return true;
        }
        if (state.TurnCount >= MaxTurns)
        {
            terminalValue = state.RewardCollected;
            return true;
        }
        
        terminalValue = 0;
        return false;
    }

    public bool IsChanceNode(in GameState state)
    {
        // Not using explicit chance nodes - randomness in Step method
        return false;
    }

    public GameState SampleChance(in GameState state, Random rng, out double logProb)
    {
        logProb = 0;
        return state;
    }

    public IEnumerable<PlayerAction> LegalActions(GameState state)
    {
        if (IsTerminal(in state, out _))
            yield break;
        var (x, y) = (state.PlayerX, state.PlayerY);
        var room = state.Rooms[(x, y)];

        // Movement actions
        if (y > 0) yield return PlayerAction.MoveNorth;
        if (y < GridHeight - 1) yield return PlayerAction.MoveSouth;
        if (x < GridWidth - 1) yield return PlayerAction.MoveEast;
        if (x > 0) yield return PlayerAction.MoveWest;

        // Search if room not yet searched
        if (!room.Searched)
            yield return PlayerAction.SearchRoom;

        // Fight if monster present
        if (room.MonsterHealth > 0)
            yield return PlayerAction.FightMonster;

        // Use potion if injured and have potions
        if (state.Potions > 0 && state.PlayerHealth < 10)
            yield return PlayerAction.UsePotion;

        // Exit if at exit location
        if (x == ExitLocation.X && y == ExitLocation.Y)
            yield return PlayerAction.Exit;
    }

    public GameState Step(in GameState state, in PlayerAction action)
    {
        if (IsTerminal(in state, out _))
            return state;

        var newTurnCount = state.TurnCount + 1;
        var reward = state.RewardCollected;
        var newRooms = new Dictionary<(int X, int Y), RoomState>(state.Rooms);
        var currentRoom = newRooms[(state.PlayerX, state.PlayerY)];

        switch (action)
        {
            case PlayerAction.MoveNorth:
                return state with { PlayerY = state.PlayerY - 1, TurnCount = newTurnCount };
            
            case PlayerAction.MoveSouth:
                return state with { PlayerY = state.PlayerY + 1, TurnCount = newTurnCount };
            
            case PlayerAction.MoveEast:
                return state with { PlayerX = state.PlayerX + 1, TurnCount = newTurnCount };
            
            case PlayerAction.MoveWest:
                return state with { PlayerX = state.PlayerX - 1, TurnCount = newTurnCount };

            case PlayerAction.SearchRoom:
                // Chance outcomes for searching
                var searchRoll = _random.NextDouble();
                var newGold = state.Gold;
                var newPotions = state.Potions;
                
                if (searchRoll < 0.4)  // 40% treasure
                {
                    newGold += 10;
                    reward += 10;
                }
                else if (searchRoll < 0.6)  // 20% potion
                {
                    newPotions += 1;
                    reward += 5;
                }
                else if (searchRoll < 0.75)  // 15% spawn monster
                {
                    currentRoom = currentRoom with { MonsterHealth = _random.Next(2, 4) };
                    reward -= 2;  // Small penalty for bad luck
                }
                // else 25% nothing
                
                currentRoom = currentRoom with { Searched = true };
                newRooms[(state.PlayerX, state.PlayerY)] = currentRoom;
                
                return state with 
                { 
                    Gold = newGold, 
                    Potions = newPotions,
                    Rooms = newRooms,
                    TurnCount = newTurnCount,
                    RewardCollected = reward
                };

            case PlayerAction.FightMonster:
                // Chance outcomes for combat
                var combatRoll = _random.NextDouble();
                var damage = 0;
                var newHealth = state.PlayerHealth;
                
                if (combatRoll < 0.15)  // 15% critical hit
                {
                    damage = 3;
                    reward += 3;
                }
                else if (combatRoll < 0.65)  // 50% normal hit
                {
                    damage = 2;
                    reward += 2;
                }
                else  // 35% miss - monster counters
                {
                    newHealth -= 1;
                    reward -= 1;
                }
                
                var newMonsterHealth = Math.Max(0, currentRoom.MonsterHealth - damage);
                
                // Bonus for killing monster
                if (newMonsterHealth == 0 && currentRoom.MonsterHealth > 0)
                {
                    reward += 10;  // Bonus for defeating monster
                }
                
                currentRoom = currentRoom with { MonsterHealth = newMonsterHealth };
                newRooms[(state.PlayerX, state.PlayerY)] = currentRoom;
                
                return state with
                {
                    PlayerHealth = newHealth,
                    Rooms = newRooms,
                    TurnCount = newTurnCount,
                    RewardCollected = reward
                };

            case PlayerAction.UsePotion:
                return state with
                {
                    PlayerHealth = Math.Min(10, state.PlayerHealth + 5),
                    Potions = state.Potions - 1,
                    TurnCount = newTurnCount
                };

            case PlayerAction.Exit:
                reward += state.Gold;  // Get all gold value when exiting
                reward += 20;  // Bonus for successful exit
                return state with
                {
                    HasExited = true,
                    TurnCount = newTurnCount,
                    RewardCollected = reward
                };

            default:
                return state with { TurnCount = newTurnCount };
        }
    }

    public string StateToString(GameState state)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"  Turn {state.TurnCount}/{MaxTurns}");
        sb.AppendLine($"  Health: {state.PlayerHealth}/10  Gold: {state.Gold}  Potions: {state.Potions}");
        sb.AppendLine();
        
        // Draw grid
        sb.AppendLine("  +" + new string('-', GridWidth * 2) + "+");
        for (int y = 0; y < GridHeight; y++)
        {
            sb.Append("  |");
            for (int x = 0; x < GridWidth; x++)
            {
                if (x == state.PlayerX && y == state.PlayerY)
                {
                    sb.Append("@ ");
                }
                else if (x == ExitLocation.X && y == ExitLocation.Y)
                {
                    sb.Append("E ");
                }
                else
                {
                    var room = state.Rooms[(x, y)];
                    if (room.MonsterHealth > 0)
                        sb.Append("M ");
                    else if (room.Searched)
                        sb.Append(". ");
                    else
                        sb.Append("? ");
                }
            }
            sb.AppendLine("|");
        }
        sb.AppendLine("  +" + new string('-', GridWidth * 2) + "+");
        
        // Current room info
        var currentRoom = state.Rooms[(state.PlayerX, state.PlayerY)];
        if (currentRoom.MonsterHealth > 0)
            sb.AppendLine($"  Monster here! (HP: {currentRoom.MonsterHealth})");
        if (!currentRoom.Searched)
            sb.AppendLine($"  Room not searched yet");
        
        return sb.ToString();
    }
}
