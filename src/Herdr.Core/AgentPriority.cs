namespace Herdr;

public static class AgentPriority
{
    // The list is already ordered by recency; equal-priority agents retain that order.
    public static Agent? MostUrgent(IReadOnlyList<Agent> recent) =>
        recent.MinBy(agent => Priority(agent.AgentStatus));

    private static int Priority(string? status) => status switch
    {
        AgentStatus.Blocked => 0,
        AgentStatus.Done => 1,
        AgentStatus.Working => 2,
        AgentStatus.Idle => 3,
        _ => 4,
    };
}
