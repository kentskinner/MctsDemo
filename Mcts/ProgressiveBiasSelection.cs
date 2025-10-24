using System;
using System.Linq;

namespace Mcts;

/// <summary>
/// Selection policy that uses progressive bias (heuristic guidance that decays with visits)
/// combined with UCB1 exploration.
/// </summary>
/// <typeparam name="TState">The game state type</typeparam>
/// <typeparam name="TAction">The action type</typeparam>
public class ProgressiveBiasSelection<TState, TAction> : ISelectionPolicy<TState, TAction>
    where TState : notnull
    where TAction : notnull
{
    private readonly Func<TState, TAction, double> _heuristicFunc;
    private readonly int _visitThreshold;
    private readonly double _biasStrength;
    private readonly double _explorationConstant;

    /// <summary>
    /// Creates a new progressive bias selection policy.
    /// </summary>
    /// <param name="heuristicFunc">Function that evaluates state-action pairs</param>
    /// <param name="visitThreshold">Number of visits after which progressive bias becomes zero</param>
    /// <param name="biasStrength">Multiplier for the heuristic bias term</param>
    /// <param name="explorationConstant">UCB1 exploration constant (typically sqrt(2) or higher)</param>
    public ProgressiveBiasSelection(
        Func<TState, TAction, double> heuristicFunc,
        int visitThreshold = 30,
        double biasStrength = 0.5,
        double explorationConstant = 1.414)
    {
        _heuristicFunc = heuristicFunc ?? throw new ArgumentNullException(nameof(heuristicFunc));
        _visitThreshold = visitThreshold;
        _biasStrength = biasStrength;
        _explorationConstant = explorationConstant;
    }

    public Node<TState, TAction> SelectChild(Node<TState, TAction> node, Random rng)
    {
        if (node.Children.Count == 0)
        {
            var parentInfo = node.Parent == null ? "null" :
                $"Kind={node.Parent.Kind}, Untried={node.Parent.Untried.Count}, Children={node.Parent.Children.Count}";
            var msg =
                $"No children to select.\n" +
                $"Node: Kind={node.Kind}, Visits={node.Visits}, TotalValue={node.TotalValue}, " +
                $"Untried={node.Untried.Count}, Children={node.Children.Count}\n" +
                $"IncomingAction={node.IncomingAction}\n" +
                $"Parent: {parentInfo}";
            throw new InvalidOperationException(msg);
        }

        var state = node.State;
        var totalVisits = node.Visits;
        var logTotal = Math.Log(totalVisits);

        Node<TState, TAction>? bestChild = null;
        double bestValue = double.NegativeInfinity;

        foreach (var child in node.Children)
        {
            var action = child.IncomingAction!;
            var visits = child.Visits;

            double ucbValue;
            
            if (visits == 0)
            {
                // Unvisited node - use pure heuristic value
                ucbValue = _heuristicFunc(state, action);
            }
            else
            {
                // Exploitation term (average value)
                var exploitation = child.TotalValue / visits;

                // UCB1 exploration term
                var exploration = _explorationConstant * Math.Sqrt(logTotal / visits);

                // Progressive bias term - decays to zero as visits increase
                var progressiveBias = visits < _visitThreshold
                    ? _biasStrength * _heuristicFunc(state, action) / (1 + visits)
                    : 0.0;

                ucbValue = exploitation + exploration + progressiveBias;
            }

            if (ucbValue > bestValue)
            {
                bestValue = ucbValue;
                bestChild = child;
            }
        }

        return bestChild ?? throw new InvalidOperationException("Failed to select child");
    }
}
