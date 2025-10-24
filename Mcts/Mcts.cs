using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

// =============================================================
// Generic, game-neutral MCTS with pluggable policies
// + A small "Pig Dice" game wired in (has CHANCE nodes)
// =============================================================

namespace Mcts;

// ----------------------------
// Core game/model abstractions
// ----------------------------

/// <summary>
/// Game adapter the MCTS engine talks to. Keep values from the ROOT player's perspective.
/// If your game alternates players, you should embed that in TState and make Evaluate/terminal
/// return value from the root's perspective (e.g., negate when it's the opponent to move).
/// </summary>
public interface IGameModel<TState, TAction>
{
    /// <summary>Is this state terminal? If yes, set value in [-inf, +inf] from ROOT perspective.</summary>
    bool IsTerminal(in TState state, out double terminalValue);

    /// <summary>True if this node is a chance node (random event to sample rather than choose).</summary>
    bool IsChanceNode(in TState state);

    /// <summary>
    /// Enumerate all possible outcomes for a chance node with their probabilities.
    /// MCTS will use this to either:
    /// 1. Expand all outcomes as children (if outcome count is reasonable)
    /// 2. Sample one outcome using the probabilities (during rollouts or when outcome space is large)
    /// 
    /// This is the ONLY method you need to implement for chance nodes.
    /// </summary>
    IEnumerable<(TState outcome, double probability)> ChanceOutcomes(TState state);

    /// <summary>Enumerate legal actions at a decision node.</summary>
    IEnumerable<TAction> LegalActions(TState state);

    /// <summary>Apply an action to get the next state.</summary>
    TState Step(in TState state, in TAction action);
}    // ----------------------------
     // Policies (swappable)
     // ----------------------------

/// <summary>
/// Helper methods for working with game models
/// </summary>
public static class GameModelExtensions
{
    /// <summary>
    /// Sample one outcome from ChanceOutcomes using the provided probabilities.
    /// This is used during rollouts and when falling back from enumeration.
    /// </summary>
    public static TState SampleChanceOutcome<TState, TAction>(
        this IGameModel<TState, TAction> game,
        TState state,
        Random rng)
    {
        var outcomes = game.ChanceOutcomes(state).ToList();
        if (outcomes.Count == 0)
            throw new InvalidOperationException("ChanceOutcomes returned empty - chance nodes must return at least one outcome");

        if (outcomes.Count == 1)
            return outcomes[0].outcome;

        // Sample using cumulative probabilities
        double r = rng.NextDouble();
        double cumulative = 0.0;
        foreach (var (outcome, probability) in outcomes)
        {
            cumulative += probability;
            if (r < cumulative)
                return outcome;
        }

        // Fallback to last outcome (handles floating point rounding)
        return outcomes[outcomes.Count - 1].outcome;
    }
}

public interface ISelectionPolicy<TState, TAction>
{
    /// <summary>
    /// Choose the next child to descend to (among ALREADY-EXPANDED children).
    /// If the node has unexpanded actions, selection should stop and let expansion handle it.
    /// </summary>
    Node<TState, TAction> SelectChild(Node<TState, TAction> node, Random rng);
}

public interface IExpansionPolicy<TState, TAction>
{
    /// <summary>Pick which UNTRIED action to expand next (if any). Return false if none chosen.</summary>
    bool TryPickUntriedAction(Node<TState, TAction> node, IGameModel<TState, TAction> game, Random rng, out TAction action);

    /// <summary>
    /// Optionally roll forward through chains of deterministic single-move steps to the next branching/chance point.
    /// </summary>
    bool EnableDeterministicRollForward { get; }
}

public interface ISimulationPolicy<TState, TAction>
{
    /// <summary>Perform a playout/rollout from state and return a value from ROOT perspective.</summary>
    double Simulate(in TState state, IGameModel<TState, TAction> game, Random rng, int maxDepth);
}

public interface IBackpropagationPolicy<TState, TAction>
{
    /// <summary>Update statistics along the path. Default is sum-of-values and visit counts.</summary>
    void Backpropagate(Node<TState, TAction> leaf, double value);
}

// ---------------------------------
// Default policies: UCB1 + Uniform
// ---------------------------------

/// <summary>Classic UCB1: Q/N + c*sqrt(ln(N_parent)/N_child)</summary>
public sealed class Ucb1Selection<TState, TAction> : ISelectionPolicy<TState, TAction>
{
    private readonly double _c;
    public Ucb1Selection(double explorationC = 1.41421356237) => _c = explorationC;

    public Node<TState, TAction> SelectChild(Node<TState, TAction> node, Random rng)
    {
        if (node.Children.Count == 0) throw new InvalidOperationException("No children to select.");

        double lnN = Math.Log(Math.Max(1, node.Visits));
        Node<TState, TAction>? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (var ch in node.Children)
        {
            if (ch.Visits == 0)
                return ch; // expand just created child first

            double q = ch.TotalValue / ch.Visits;
            double u = _c * Math.Sqrt(lnN / ch.Visits);
            double score = q + u;

            if (score > bestScore) { bestScore = score; best = ch; }
        }
        return best!;
    }
}

/// <summary>Expansion policy: expand exactly one untried action uniformly at random.</summary>
public sealed class UniformSingleExpansion<TState, TAction> : IExpansionPolicy<TState, TAction>
{
    public bool EnableDeterministicRollForward { get; }
    public UniformSingleExpansion(bool deterministicRollForward = true) => EnableDeterministicRollForward = deterministicRollForward;

    public bool TryPickUntriedAction(Node<TState, TAction> node, IGameModel<TState, TAction> game, Random rng, out TAction action)
    {
        if (node.Untried.Count == 0)
        {
            action = default!;
            return false;
        }

        int i = rng.Next(node.Untried.Count);
        action = node.Untried[i];
        node.Untried.RemoveAt(i);
        return true;
    }
}

/// <summary>Uniform random rollout until depth or terminal.</summary>
public sealed class UniformRandomSimulation<TState, TAction> : ISimulationPolicy<TState, TAction>
{
    public double Simulate(in TState state, IGameModel<TState, TAction> game, Random rng, int maxDepth)
    {
        var s = state;
        for (int d = 0; d < maxDepth; d++)
        {
            if (game.IsTerminal(s, out var value))
                return value;

            if (game.IsChanceNode(s))
            {
                s = game.SampleChanceOutcome(s, rng);
                continue;
            }

            var actions = game.LegalActions(s);
            if (actions is IList<TAction> listA)
            {
                if (listA.Count == 0) return 0.0;
                s = game.Step(s, listA[rng.Next(listA.Count)]);
            }
            else
            {
                var list = actions.ToList();
                if (list.Count == 0) return 0.0;
                s = game.Step(s, list[rng.Next(list.Count)]);
            }
        }
        return 0.0; // cutoff heuristic
    }
}

/// <summary>Default backprop: add value to TotalValue and increment Visits up the path.</summary>
public sealed class SumBackpropagation<TState, TAction> : IBackpropagationPolicy<TState, TAction>
{
    public void Backpropagate(Node<TState, TAction> leaf, double value)
    {
        var n = leaf;
        while (n != null)
        {
            n.Visits++;
            n.TotalValue += value;
            n = n.Parent;
        }
    }
}

// ----------------------------
// Tree structures
// ----------------------------

public enum NodeKind { Decision, Chance, Terminal }

public sealed class Node<TState, TAction>
{
    public readonly TState State;
    public readonly NodeKind Kind;
    public readonly Node<TState, TAction>? Parent;
    public readonly TAction? IncomingAction;
    public readonly double Probability;  // For chance node children: the probability of this outcome
    public readonly List<Node<TState, TAction>> Children = new();
    public readonly List<TAction> Untried = new();

    public int Visits;
    public double TotalValue;
    private static int _nextId = 1;
    public readonly int Id;
    public Node(TState state, NodeKind kind, Node<TState, TAction>? parent, TAction? incomingAction, double probability = 1.0)
    {
        Id = _nextId++;
        State = state;
        Kind = kind;
        Parent = parent;
        IncomingAction = incomingAction;
        Probability = probability;
    }

    public Node<TState, TAction> AddChild(TState childState, NodeKind kind, TAction incoming, double probability = 1.0)
    {
        var ch = new Node<TState, TAction>(childState, kind, this, incoming, probability);
        Children.Add(ch);
        return ch;
    }

    public override string ToString()
    {
        var avg = Visits > 0 ? TotalValue / Visits : 0.0;
        return $"V:{Visits} Q:{TotalValue:F1} Avg:{avg:F2} {Kind} Action:{IncomingAction}";
    }
}

// ----------------------------
// Options & helpers
// ----------------------------

public sealed class MctsOptions
{
    public int Iterations = 10_000;
    public int RolloutDepth = 256;
    public int RollForwardDeterministicMaxSteps = 10_000; // safety fuse
    public Func<NodeStats, double>? FinalActionSelector = NodeStats.SelectByMaxVisit; // how to choose root action
    public int? Seed = null;
    public bool Verbose = false; // controls debug output
}

public readonly struct NodeStats
{
    public readonly int Index;
    public readonly int Visits;
    public readonly double TotalValue;
    public readonly double MeanValue;

    public NodeStats(int index, int visits, double total)
    {
        Index = index; Visits = visits; TotalValue = total;
        MeanValue = visits > 0 ? total / visits : double.NegativeInfinity;
    }

    public static double SelectByMaxVisit(NodeStats s) => s.Visits;
    public static double SelectByMeanValue(NodeStats s) => s.MeanValue;
}

// ----------------------------
// The MCTS engine
// ----------------------------

public sealed class Mcts<TState, TAction>
{
    private readonly IGameModel<TState, TAction> _game;
    private readonly ISelectionPolicy<TState, TAction> _selection;
    private readonly IExpansionPolicy<TState, TAction> _expansion;
    private readonly ISimulationPolicy<TState, TAction> _simulation;
    private readonly IBackpropagationPolicy<TState, TAction> _backprop;
    private readonly MctsOptions _opt;
    private readonly Random _rng;

    public Mcts(
        IGameModel<TState, TAction> game,
        ISelectionPolicy<TState, TAction> selection,
        IExpansionPolicy<TState, TAction> expansion,
        ISimulationPolicy<TState, TAction> simulation,
        IBackpropagationPolicy<TState, TAction>? backprop = null,
        MctsOptions? options = null)
    {
        _game = game;
        _selection = selection;
        _expansion = expansion;
        _simulation = simulation;
        _backprop = backprop ?? new SumBackpropagation<TState, TAction>();
        _opt = options ?? new MctsOptions();
        _rng = _opt.Seed.HasValue ? new Random(_opt.Seed.Value) : new Random();
    }

    public (TAction action, IReadOnlyList<(TAction action, int visits, double value)> stats) Search(TState rootState)
    {
        return Search(rootState, out _);
    }

    public (TAction action, IReadOnlyList<(TAction action, int visits, double value)> stats) Search(TState rootState, out Node<TState, TAction> rootNode)
    {
        var root = MakeNode(rootState, parent: null, incoming: default!);
        rootNode = root;

        // Note: MakeNode already initializes Untried for decision nodes, so no need to do it again here

        for (int i = 0; i < _opt.Iterations; i++)
        {
            // 1) Selection (down to a leaf that has untried or is terminal/chance)
            var leaf = SelectDown(root);
            if (_opt.Verbose && i < 10)
            {
                static string Path(Node<TState, TAction> n)
                {
                    var stack = new List<string>();
                    var cur = n;
                    while (cur != null)
                    {
                        stack.Add(cur.ToString());
                        cur = cur.Parent;
                    }
                    stack.Reverse();
                    return string.Join(" ->\n", stack);
                }

                Console.WriteLine($"[Iter {i}] Path to leaf:\n{Path(leaf)}");
            }
            /*
            if (leaf.Kind == NodeKind.Decision && leaf.Untried.Count == 0 && leaf.Children.Count == 0)
            {
                if (_opt.Verbose) Console.WriteLine("[DeadEnd] Decision node has no Untried and no Children; backpropagating ValueOf(state).");
                _backprop.Backpropagate(leaf, ValueOf(leaf.State));
                continue;
            }*/

            if (_opt.Verbose && i <= 5)
            {
                Console.WriteLine($"[Iter {i}] Leaf: Kind={leaf.Kind}, Untried={leaf.Untried.Count}, Children={leaf.Children.Count}, HashCode={leaf.GetHashCode()}");
            }

            // 2) Handle terminal immediately
            if (leaf.Kind == NodeKind.Terminal)
            {
                _backprop.Backpropagate(leaf, ValueOf(leaf.State));
                if (_opt.Verbose && i <= 2) Console.WriteLine($"[Iter {i}] Terminal node, backpropagated");
                continue;
            }

            // 3) Chance node: try enumeration first, fall back to sampling
            if (leaf.Kind == NodeKind.Chance)
            {
                // Try to enumerate outcomes
                var outcomes = _game.ChanceOutcomes(leaf.State).ToList();

                if (outcomes.Count > 0)
                {
                    // Enumerable chance node - expand all outcomes like we do for actions
                    if (leaf.Children.Count == 0)
                    {
                        // First visit: expand all outcomes as children
                        foreach (var (outcome, probability) in outcomes)
                        {
                            var rolled = RollForwardIfEnabled(outcome);
                            var kind = Classify(rolled);
                            var child = leaf.AddChild(rolled, kind, default!, probability);

                            if (child.Kind == NodeKind.Decision)
                                child.Untried.AddRange(_game.LegalActions(child.State));

                            if (_opt.Verbose && i <= 10)
                                Console.WriteLine($"[Iter {i}] Enumerated chance outcome: prob={probability:F3}, kind={kind}");
                        }
                    }

                    // Now select one child based on exploration strategy
                    // For now, use simple proportional selection weighted by probability
                    Node<TState, TAction> selectedChild;
                    if (leaf.Children.All(ch => ch.Visits == 0))
                    {
                        // First selection: choose proportional to probability
                        var r = _rng.NextDouble();
                        var cumulative = 0.0;
                        selectedChild = leaf.Children[0];
                        foreach (var child in leaf.Children)
                        {
                            cumulative += child.Probability;
                            if (r < cumulative)
                            {
                                selectedChild = child;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Use selection policy (UCB will balance exploration)
                        selectedChild = _selection.SelectChild(leaf, _rng);
                    }

                    var v = SimulateFrom(selectedChild.State);
                    _backprop.Backpropagate(selectedChild, v);

                    if (_opt.Verbose && i <= 10)
                        Console.WriteLine($"[Iter {i}] Enumerated chance node, selected child with prob={selectedChild.Probability:F3}");
                }
                else
                {
                    // Not enumerable (too many outcomes) - sample one outcome
                    var s2 = _game.SampleChanceOutcome(leaf.State, _rng);
                    var rolled = RollForwardIfEnabled(s2);
                    var kind = Classify(rolled);
                    var ch = AttachIfNeeded(leaf, rolled, kind, default!);
                    var v = SimulateFrom(ch.State);
                    _backprop.Backpropagate(ch, v);
                    if (_opt.Verbose && i <= 10) Console.WriteLine($"[Iter {i}] Sampled chance node, backpropagated");
                }
                continue;
            }

            // 4) Expansion (Decision node): expand one untried action if any; otherwise select among children
            Node<TState, TAction> nodeForPlayout;
            if (_expansion.TryPickUntriedAction(leaf, _game, _rng, out var action))
            {
                var s2 = _game.Step(leaf.State, action);
                s2 = RollForwardIfEnabled(s2);
                var kind = Classify(s2);
                nodeForPlayout = leaf.AddChild(s2, kind, action);

                if (_opt.Verbose && i <= 5)
                {
                    Console.WriteLine($"[Iter {i}] Expanded action={action}, newNode.Kind={kind}");
                    Console.WriteLine($"[Iter {i}] After expansion: Leaf.Children={leaf.Children.Count}, Leaf.Untried={leaf.Untried.Count}");
                    if (kind == NodeKind.Terminal)
                    {
                        Console.WriteLine($"[Iter {i}] WARNING: Created TERMINAL child!");
                    }
                }

                if (nodeForPlayout.Kind == NodeKind.Decision)
                    nodeForPlayout.Untried.AddRange(_game.LegalActions(nodeForPlayout.State));
            }
            else
            {
                if (_opt.Verbose && i <= 2)
                {
                    Console.WriteLine($"[Iter {i}] No untried actions, calling SelectChild on leaf with {leaf.Children.Count} children");
                }
                nodeForPlayout = _selection.SelectChild(leaf, _rng);
            }

            // 5) Simulation
            var value = SimulateFrom(nodeForPlayout.State);

            // 6) Backpropagation
            _backprop.Backpropagate(nodeForPlayout, value);
        }

        if (root.Children.Count == 0)
        {
            var acts = _game.LegalActions(root.State)?.ToList() ?? new List<TAction>();
            _game.IsTerminal(root.State, out var tv);
            var why = acts.Count == 0
                ? $"LegalActions empty ({acts.Count}) while IsTerminal={_game.IsTerminal(root.State, out _)}"
                : $"LegalActions={acts.Count}";
            throw new InvalidOperationException($"Root has no children; {why}. TerminalValue={tv}.");
        }


        // Group children by action and sum their statistics
        var groupedByAction = root.Children
            .GroupBy(ch => ch.IncomingAction!)
            .Select(g => new
            {
                action = g.Key,
                visits = g.Sum(ch => ch.Visits),
                total = g.Sum(ch => ch.TotalValue)
            })
            .ToList();

        var scored = groupedByAction
            .Select((x, idx) => (x.action, x.visits, x.total, score: _opt.FinalActionSelector!.Invoke(new NodeStats(idx, x.visits, x.total))))
            .OrderByDescending(x => x.score)
            .ToList();

        var best = scored[0];
        var stats = scored.Select(s => (s.action, s.visits, s.total)).ToList().AsReadOnly();
        return (best.action, stats);
    }

    // ----------------------------
    // Internals
    // ----------------------------

    private Node<TState, TAction> MakeNode(TState state, Node<TState, TAction>? parent, TAction incoming)
    {
        // Do NOT roll forward at root; we still need to return the first action.
        // Only collapse deterministic chains for child nodes.
        var s = parent is null ? state : RollForwardIfEnabled(state);

        var kind = Classify(s);
        var n = new Node<TState, TAction>(s, kind, parent, parent == null ? default : incoming);
        if (kind == NodeKind.Decision)
            n.Untried.AddRange(_game.LegalActions(n.State));
        return n;
    }

    private NodeKind Classify(in TState s)
    {
        if (_game.IsTerminal(s, out _)) return NodeKind.Terminal;
        return _game.IsChanceNode(s) ? NodeKind.Chance : NodeKind.Decision;
    }

    private Node<TState, TAction> SelectDown(Node<TState, TAction> node)
    {
        var cur = node;
        while (true)
        {
            if (cur.Kind == NodeKind.Terminal) return cur;
            if (cur.Kind == NodeKind.Chance) return cur; // handled by sampling

            if (cur.Untried.Count > 0) return cur; // expand here
            if (cur.Children.Count == 0) return cur; // nothing to select

            cur = _selection.SelectChild(cur, _rng);
        }
    }

    private TState RollForwardIfEnabled(TState s)
    {
        if (!_expansion.EnableDeterministicRollForward) return s;

        int fuse = _opt.RollForwardDeterministicMaxSteps;
        while (fuse-- > 0)
        {
            if (_game.IsTerminal(s, out _)) break;
            if (_game.IsChanceNode(s)) break;

            var acts = _game.LegalActions(s);
            if (acts is IList<TAction> list)
            {
                if (list.Count != 1) break;
                s = _game.Step(s, list[0]);
                continue;
            }
            else
            {
                var tmp = acts.Take(2).ToList();
                if (tmp.Count != 1) break;
                s = _game.Step(s, tmp[0]);
                continue;
            }
        }
        return s;
    }

    private Node<TState, TAction> AttachIfNeeded(Node<TState, TAction> parent, TState childState, NodeKind kind, TAction incoming)
    {
        // Use Equals to support value-type or properly-overridden state equality
        var existing = parent.Children.FirstOrDefault(c => Equals(c.State, childState));
        if (existing != null) return existing;

        var ch = parent.AddChild(childState, kind, incoming);
        if (kind == NodeKind.Decision)
            ch.Untried.AddRange(_game.LegalActions(ch.State));
        return ch;
    }

    private double SimulateFrom(in TState state) => _simulation.Simulate(state, _game, _rng, _opt.RolloutDepth);
    private double ValueOf(in TState state) => _game.IsTerminal(state, out var v) ? v : 0.0;
}

