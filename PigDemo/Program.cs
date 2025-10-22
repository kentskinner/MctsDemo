using System;
using GenericMcts;
using PigDemo;

var game = new PigGame(targetScore: 20);
var selection = new Ucb1Selection<PigState, PigAction>(explorationC: 1.2);
var expansion = new UniformSingleExpansion<PigState, PigAction>(deterministicRollForward: true);
var simulation = new UniformRandomSimulation<PigState, PigAction>();
var backprop = new SumBackpropagation<PigState, PigAction>();

var options = new MctsOptions
{
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
