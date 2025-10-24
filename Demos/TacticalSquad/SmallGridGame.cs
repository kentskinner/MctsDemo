using System;
using System.Collections.Generic;
using System.Linq;
using Mcts;

namespace TacticalSquad;

/// <summary>
/// Simplified 4x4 grid version of TacticalSquad for debugging
/// - Single hero starting at (0,3)
/// - Exit at (3,0)
/// - No monsters, no combat
/// - Just movement with reward shaping
/// </summary>

public enum SmallAction
{
    MoveNorth,
    MoveSouth,
    MoveEast,
    MoveWest,
    EndTurn
}

public record SmallState(
    int HeroX,
    int HeroY,
    int Turn,
    double AccumulatedReward,
    int ExitX = 3,
    int ExitY = 0
);

public class SmallGridGame : IGameModel<SmallState, SmallAction>
{
    private const int GridSize = 4;
    private const int MaxTurns = 10;
    
    public SmallState InitialState()
    {
        return new SmallState(
            HeroX: 0,
            HeroY: 3,
            Turn: 0,
            AccumulatedReward: 0.0
        );
    }

    public IEnumerable<SmallAction> LegalActions(SmallState state)
    {
        if (IsTerminal(state, out _))
            yield break;

        // Always allow EndTurn
        yield return SmallAction.EndTurn;

        // Movement actions if in bounds
        if (state.HeroY > 0) yield return SmallAction.MoveNorth;
        if (state.HeroY < GridSize - 1) yield return SmallAction.MoveSouth;
        if (state.HeroX < GridSize - 1) yield return SmallAction.MoveEast;
        if (state.HeroX > 0) yield return SmallAction.MoveWest;
    }

    public SmallState Step(in SmallState state, in SmallAction action)
    {
        int newX = state.HeroX;
        int newY = state.HeroY;

        switch (action)
        {
            case SmallAction.MoveNorth:
                newY = Math.Max(0, state.HeroY - 1);
                break;
            case SmallAction.MoveSouth:
                newY = Math.Min(GridSize - 1, state.HeroY + 1);
                break;
            case SmallAction.MoveEast:
                newX = Math.Min(GridSize - 1, state.HeroX + 1);
                break;
            case SmallAction.MoveWest:
                newX = Math.Max(0, state.HeroX - 1);
                break;
            case SmallAction.EndTurn:
                // Just advance turn
                break;
        }

        var newState = state with
        {
            HeroX = newX,
            HeroY = newY,
            Turn = state.Turn + 1
        };

        // Calculate reward for this move
        double reward = CalculateMoveReward(state, newState, action);
        return newState with { AccumulatedReward = state.AccumulatedReward + reward };
    }

    private double CalculateMoveReward(SmallState oldState, SmallState newState, SmallAction action)
    {
        double reward = 0.0;

        // Manhattan distance to exit
        int oldDist = Math.Abs(oldState.HeroX - oldState.ExitX) + Math.Abs(oldState.HeroY - oldState.ExitY);
        int newDist = Math.Abs(newState.HeroX - newState.ExitX) + Math.Abs(newState.HeroY - newState.ExitY);

        if (newDist < oldDist)
            reward += 2.0;  // Moved closer
        else if (newDist > oldDist)
            reward -= 1.5;  // Moved away

        reward -= 0.05;  // Time penalty

        return reward;
    }

    public bool IsTerminal(in SmallState state, out double value)
    {
        // Win: reached exit
        if (state.HeroX == state.ExitX && state.HeroY == state.ExitY)
        {
            value = 100.0;
            return true;
        }

        // Timeout
        if (state.Turn >= MaxTurns)
        {
            value = -50.0;
            return true;
        }

        value = 0.0;
        return false;
    }

    public bool IsChanceNode(in SmallState state) => false;

    public IEnumerable<(SmallState outcome, double probability)> ChanceOutcomes(SmallState state)
    {
        yield break; // No chance nodes
    }

    public SmallState SampleChanceOutcome(in SmallState state, Random rng)
    {
        throw new InvalidOperationException("No chance nodes in this game");
    }

    public void PrintState(in SmallState state)
    {
        Console.WriteLine($"\n=== Turn {state.Turn} ===");
        Console.WriteLine($"Hero at ({state.HeroX},{state.HeroY})");
        Console.WriteLine($"Exit at ({state.ExitX},{state.ExitY})");
        Console.WriteLine($"Accumulated Reward: {state.AccumulatedReward:F2}");
        Console.WriteLine();

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (x == state.HeroX && y == state.HeroY)
                    Console.Write(" H ");
                else if (x == state.ExitX && y == state.ExitY)
                    Console.Write(" E ");
                else
                    Console.Write(" . ");
            }
            Console.WriteLine();
        }
        Console.WriteLine();
    }
}
