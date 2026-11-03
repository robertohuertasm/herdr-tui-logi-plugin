namespace Loupedeck.HerdrKeypad;

using Herdr;

public sealed class FixedSlotCommand : PluginDynamicCommand
{
    private readonly HerdrKeypadPlugin owner;
    private readonly int slot;
    private readonly bool isSpace;
    private (string Status, string Label, bool Remote)? lastFace;

    public FixedSlotCommand(HerdrKeypadPlugin owner, int slot, bool isSpace)
        : base(isSpace ? $"Space {slot}" : $"Recent Agent {slot}",
            isSpace ? "Show and focus this workspace; press again to cycle its panes" : "Show and focus a recent Herdr agent",
            isSpace ? "Spaces" : "Recent Agents", DeviceType.All)
    {
        this.owner = owner;
        this.slot = slot;
        this.isSpace = isSpace;
        this.Name = isSpace ? $"herdr_keypad_space_{slot}" : $"herdr_keypad_recent_{slot}";
    }

    private (string Status, string Label, bool Remote) Face()
    {
        if (!this.owner.Board.Online) return ("offline", "Offline", false);
        var snapshot = this.owner.Board.Snapshot;
        if (this.isSpace)
        {
            var space = FixedSlots.Space(snapshot, this.slot);
            return space is null ? ("none", $"Space {this.slot}", false) : (space.AgentStatus, FixedSlots.SpaceLabel(space, this.slot), false);
        }
        var agent = FixedSlots.Recent(snapshot, this.slot);
        return agent is null ? ("none", $"Recent {this.slot}", false) : (agent.AgentStatus, FixedSlots.AgentLabel(agent, this.slot), agent.MachineId is not null);
    }

    public void Refresh()
    {
        var next = this.Face();
        if (next == this.lastFace) return;
        this.lastFace = next;
        try { this.ActionImageChanged(); }
        catch (Exception e) { this.Log.Error(e, "Could not refresh Herdr key"); }
    }

    protected override void RunCommand(string actionParameter)
    {
        if (!this.owner.Board.Online) return;
        if (this.isSpace) _ = this.PressSpaceAsync();
        else if (FixedSlots.Recent(this.owner.Board.Snapshot, this.slot) is { } agent) _ = this.PressAgentAsync(agent.Key);
    }

    private async Task PressSpaceAsync()
    {
        try { await this.owner.FixedSlotActions.FocusSpaceAsync(this.slot); }
        catch (Exception e) { this.Log.Warning($"Could not focus Herdr workspace: {e.Message}"); }
    }

    private async Task PressAgentAsync(string paneId)
    {
        try { await this.owner.FixedSlotActions.FocusRecentAsync(paneId); }
        catch (Exception e) { this.Log.Warning($"Could not focus Herdr agent: {e.Message}"); }
    }

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => this.Face().Label;
    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) => KeyFaces.State(this.Face().Status, imageSize, this.Face().Remote);
}
