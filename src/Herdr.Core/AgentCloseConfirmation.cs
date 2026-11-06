namespace Herdr;

// Pins the agent under a key when it is held, so a reorder during the
// confirmation window cannot redirect the close to another agent.
public sealed class AgentCloseConfirmation(TimeProvider? clock = null)
{
    private readonly object sync = new();
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private CloseTarget? target;
    private long armedAt;

    public bool IsArmed
    {
        get { lock (this.sync) return this.target is not null && this.clock.GetElapsedTime(this.armedAt) < ClosePaneConfirmation.Window; }
    }

    public bool Arm(Agent? agent)
    {
        if (agent is null || string.IsNullOrWhiteSpace(agent.TerminalId)) return false;
        lock (this.sync)
        {
            this.target = new CloseTarget(agent.Key, agent.TerminalId, agent.WorkspaceId, agent.Kind);
            this.armedAt = this.clock.GetTimestamp();
        }
        return true;
    }

    public void Cancel() { lock (this.sync) this.target = null; }

    public bool Expire()
    {
        lock (this.sync)
        {
            if (this.target is null || this.clock.GetElapsedTime(this.armedAt) < ClosePaneConfirmation.Window) return false;
            this.target = null;
            return true;
        }
    }

    // Consumed before any I/O, so a second confirmation cannot close twice.
    public CloseTarget? Take()
    {
        lock (this.sync)
        {
            var selected = this.target is not null && this.clock.GetElapsedTime(this.armedAt) < ClosePaneConfirmation.Window ? this.target : null;
            this.target = null;
            return selected;
        }
    }
}
