namespace Herdr.Tests;

public class AgentPaletteTests
{
    [Fact]
    public void UnseenCompletionIsBlueAndBeatsWorkingInTheFolder()
    {
        var working = new Agent { AgentStatus = AgentStatus.Working };
        var unread = new Agent { AgentStatus = AgentStatus.Done };
        var selected = AgentPriority.MostUrgent([working, unread]);
        Assert.Same(unread, selected);
        Assert.Equal(new AgentColor(110, 160, 230), AgentPalette.For(selected!.AgentStatus));
    }
}
