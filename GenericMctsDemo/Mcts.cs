using System;
using System.Collections.Generic;
using System.Linq;

// =============================================================
// Generic, game-neutral MCTS with pluggable policies
// + A small "Pig Dice" game wired in (has CHANCE nodes)
// =============================================================

namespace GenericMcts
{
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
        /// Sample one random outcome for a chance node and return the next state.
        /// Return also the log-probability if you want to use it (not required by default).
        /// </summary>
        TState SampleChance(in TState state, Random rng, out double logProb);

        /// <summary>Enumerate legal actions at a decision node.</summary>
        IEnumerable<TAction> LegalActions(TState state);

        /// <summary>Apply an action to get the next state.</summary>
        TState Step(in TState state, in TAction action);
    }

    // ----------------------------
    // Policies (swappable)
    // ----------------------------

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
                    s = game.SampleChance(s, rng, out _);
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
        public readonly List<Node<TState, TAction>> Children = new();
        public readonly List<TAction> Untried = new();

        public int Visits;
        public double TotalValue;

        public Node(TState state, NodeKind kind, Node<TState, TAction>? parent, TAction? incomingAction)
        {
            State = state;
            Kind = kind;
            Parent = parent;
            IncomingAction = incomingAction;
        }

        public Node<TState, TAction> AddChild(TState childState, NodeKind kind, TAction incoming)
        {
            var ch = new Node<TState, TAction>(childState, kind, this, incoming);
            Children.Add(ch);
            return ch;
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
            var root = MakeNode(rootState, parent: null, incoming: default!);

            // Initialize Untried set for the root (if decision node)
            if (root.Kind == NodeKind.Decision)
                root.Untried.AddRange(_game.LegalActions(root.State));

            for (int i = 0; i < _opt.Iterations; i++)
            {
                // 1) Selection (down to a leaf that has untried or is terminal/chance)
                var leaf = SelectDown(root);

                // 2) Handle terminal immediately
                if (leaf.Kind == NodeKind.Terminal)
                {
                    _backprop.Backpropagate(leaf, ValueOf(leaf.State));
                    continue;
                }

                // 3) Chance node: sample (no expansion)
                if (leaf.Kind == NodeKind.Chance)
                {
                    var s2 = _game.SampleChance(leaf.State, _rng, out _);
                    var rolled = RollForwardIfEnabled(s2);
                    var kind = Classify(rolled);
                    var ch = AttachIfNeeded(leaf, rolled, kind, default!);
                    var v = SimulateFrom(ch.State);
                    _backprop.Backpropagate(ch, v);
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

                    if (nodeForPlayout.Kind == NodeKind.Decision)
                        nodeForPlayout.Untried.AddRange(_game.LegalActions(nodeForPlayout.State));
                }
                else
                {
                    nodeForPlayout = _selection.SelectChild(leaf, _rng);
                }

                // 5) Simulation
                var value = SimulateFrom(nodeForPlayout.State);

                // 6) Backpropagation
                _backprop.Backpropagate(nodeForPlayout, value);
            }

            if (root.Children.Count == 0)
                throw new InvalidOperationException("Root has no children; no legal actions?");

            var scored = root.Children
                .Select((ch, idx) => (idx, action: ch.IncomingAction!, visits: ch.Visits, total: ch.TotalValue))
                .Select(x => (x.action, x.visits, x.total, score: _opt.FinalActionSelector!.Invoke(new NodeStats(x.idx, x.visits, x.total))))
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
            var s = RollForwardIfEnabled(state);
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
            // Simple identity check here; for full TT add a state hasher and map.
            var existing = parent.Children.FirstOrDefault(c => ReferenceEquals(c.State, childState));
            if (existing != null) return existing;

            var ch = parent.AddChild(childState, kind, incoming);
            if (kind == NodeKind.Decision)
                ch.Untried.AddRange(_game.LegalActions(ch.State));
            return ch;
        }

        private double SimulateFrom(in TState state) => _simulation.Simulate(state, _game, _rng, _opt.RolloutDepth);
        private double ValueOf(in TState state) => _game.IsTerminal(state, out var v) ? v : 0.0;
    }

    // =============================================================
    // Example Game: Pig Dice (CHANCE nodes)
    // =============================================================

    // Actions: player can Roll (which creates a CHANCE state) or Hold (bank turn points)
    public enum PigAction { Roll, Hold }

    public readonly record struct PigState(
        int P0, // score of root player
        int P1, // score of opponent
        int TurnTotal,
        int PlayerToMove, // 0 = root, 1 = opponent
        bool AwaitingRoll // if true => CHANCE node
    );

    public sealed class PigGame : IGameModel<PigState, PigAction>
    {
        private readonly int _target;
        public PigGame(int targetScore = 20) { _target = targetScore; }

        public bool IsTerminal(in PigState s, out double terminalValue)
        {
            if (s.P0 >= _target)
            {
                terminalValue = +1.0; // root wins
                return true;
            }
            if (s.P1 >= _target)
            {
                terminalValue = -1.0; // root loses
                return true;
            }
            terminalValue = 0;
            return false;
        }

        public bool IsChanceNode(in PigState s) => s.AwaitingRoll;

        public PigState SampleChance(in PigState s, Random rng, out double logProb)
        {
            // Roll a fair d6
            int die = rng.Next(1, 7);
            logProb = Math.Log(1.0 / 6.0);

            if (die == 1)
            {
                // Bust: lose turn total, pass turn
                return s.PlayerToMove == 0
                    ? new PigState(s.P0, s.P1, 0, 1, false)
                    : new PigState(s.P0, s.P1, 0, 0, false);
            }
            else
            {
                // Add to turn total and continue player's turn
                return new PigState(s.P0, s.P1, s.TurnTotal + die, s.PlayerToMove, false);
            }
        }

        public IEnumerable<PigAction> LegalActions(PigState s)
        {
            if (s.AwaitingRoll) yield break; // CHANCE state has no player actions

            // Always allow Roll; allow Hold if there's something to hold (optional rule: allow Hold anytime)
            yield return PigAction.Roll;
            if (s.TurnTotal > 0) yield return PigAction.Hold;
        }

        public PigState Step(in PigState s, in PigAction a)
        {
            if (a == PigAction.Roll)
            {
                // Move to CHANCE node: awaiting die result
                return new PigState(s.P0, s.P1, s.TurnTotal, s.PlayerToMove, true);
            }
            else // Hold
            {
                if (s.PlayerToMove == 0)
                {
                    return new PigState(s.P0 + s.TurnTotal, s.P1, 0, 1, false);
                }
                else
                {
                    return new PigState(s.P0, s.P1 + s.TurnTotal, 0, 0, false);
                }
            }
        }
    }

    // =============================================================
    // Demo: run MCTS from start state and print the chosen action
    // =============================================================

    public static class Demo
    {
        public static void Main()
        {
            var game = new PigGame(targetScore: 20);
            var selection = new Ucb1Selection<PigState, PigAction>(explorationC: 1.2);
            var expansion = new UniformSingleExpansion<PigState, PigAction>(deterministicRollForward: true);
            var simulation = new UniformRandomSimulation<PigState, PigAction>();
            var backprop   = new SumBackpropagation<PigState, PigAction>();

            var options = new MctsOptions {
                Iterations = 20_000,
                RolloutDepth = 200,
                FinalActionSelector = NodeStats.SelectByMaxVisit,
                Seed = 42
            };

            var mcts = new Mcts<PigState, PigAction>(game, selection, expansion, simulation, backprop, options);

            var root = new PigState(P0: 0, P1: 0, TurnTotal: 0, PlayerToMove: 0, AwaitingRoll: false);
            var (best, stats) = mcts.Search(root);

            Console.WriteLine($"Best root action: {best}");
            foreach (var (a, n, w) in stats)
            {
                var mean = n > 0 ? w / n : 0.0;
                Console.WriteLine($"  {a,-5}  visits={n,6}  total={w,8:F2}  mean={mean,7:F3}");
            }

            // Quick play-one-step to show chance behavior
            var next = game.Step(root, best);
            if (game.IsChanceNode(next))
            {
                next = game.SampleChance(next, new Random(123), out _);
                Console.WriteLine($"After sampling chance (die roll), state = P0={next.P0} P1={next.P1} Turn={next.TurnTotal} Player={next.PlayerToMove}");
            }
        }
    }
}
