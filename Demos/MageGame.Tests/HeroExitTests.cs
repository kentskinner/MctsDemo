using Xunit;
using System.Collections.Immutable;
using System.Linq;
using MageGame;

namespace MageGame.Tests;

public class HeroExitTests
{
    [Fact]
    public void MageExitsWithActionRemaining_ShouldDeactivate()
    {
        var game = new MageTacticalGame(gridWidth: 2, gridHeight: 1);
        var state = StateWith(mageAt: (0, 0), exitAt: (1, 0), actionsLeft: 1);
        
        var next = game.Step(state, new MageAction(ActionType.MoveEast));
        
        Assert.True(next.Heroes[0].HasExited);
        Assert.Equal(-1, next.ActiveHeroIndex);
    }

    [Fact]
    public void AfterHeroExits_LegalActionsShouldNotCrash()
    {
        var game = new MageTacticalGame(gridWidth: 2, gridHeight: 1);
        var state = StateWith(mageAt: (0, 0), exitAt: (1, 0), actionsLeft: 1);
        var afterExit = game.Step(state, new MageAction(ActionType.MoveEast));
        
        var actions = game.LegalActions(afterExit).ToList();
        
        Assert.DoesNotContain(actions, a => a.Type == ActionType.ActivateHero && a.TargetIndex == 0);
    }

    static MageGameState StateWith((int x, int y) mageAt, (int x, int y) exitAt, int actionsLeft) =>
        new(ImmutableList.Create(new MageHero(0, HeroClass.Mage, mageAt.x, mageAt.y, HeroStatus.Healthy, 0, 1, actionsLeft, 3, 4, false)),
            ImmutableList<MageMonster>.Empty, 0, 0, Phase.HeroAction, exitAt.x, exitAt.y, 0, ImmutableHashSet<(int, int)>.Empty, null, false);
}
