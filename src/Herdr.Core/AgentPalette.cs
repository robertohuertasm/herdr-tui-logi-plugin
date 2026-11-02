namespace Herdr;

public readonly record struct AgentColor(byte Red, byte Green, byte Blue);

// Shared by the folder, agent keys and workspace keys.
public static class AgentPalette
{
    public static AgentColor For(string? status) => status switch
    {
        AgentStatus.Blocked => new(220, 40, 40),
        AgentStatus.Done => new(110, 160, 230),
        AgentStatus.Working => new(240, 170, 30),
        AgentStatus.Idle => new(60, 140, 100),
        "offline" => new(130, 35, 35),
        _ => new(110, 110, 110),
    };
}
