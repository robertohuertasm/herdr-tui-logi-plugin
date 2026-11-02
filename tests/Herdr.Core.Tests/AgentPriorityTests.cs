namespace Herdr.Tests;

public class AgentPriorityTests
{
    [Theory]
    [InlineData("blocked", "done")]
    [InlineData("done", "working")]
    [InlineData("working", "idle")]
    [InlineData("idle", "unknown")]
    [InlineData("idle", "future-status")]
    public void HigherPriorityWinsEvenIfLessRecent(string urgent, string lessUrgent)
    {
        var recent = new Agent { PaneId = "recent", AgentStatus = lessUrgent };
        var important = new Agent { PaneId = "important", AgentStatus = urgent };
        Assert.Same(important, AgentPriority.MostUrgent([recent, important]));
    }

    [Fact]
    public void EqualPriorityPreservesRecencyAndEmptyListHasNoAgent()
    {
        var recent = new Agent { PaneId = "recent", AgentStatus = "working" };
        var old = new Agent { PaneId = "old", AgentStatus = "working" };
        Assert.Same(recent, AgentPriority.MostUrgent([recent, old]));
        Assert.Null(AgentPriority.MostUrgent([]));
    }
}
