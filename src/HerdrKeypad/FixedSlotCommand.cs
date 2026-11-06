namespace Loupedeck.HerdrKeypad;

using Herdr;

public sealed class FixedSlotCommand : PluginDynamicCommand
{
    private readonly HerdrKeypadPlugin owner;
    private readonly int slot;
    private readonly bool isSpace;
    private readonly AgentCloseConfirmation closeConfirmation = new();
    private readonly ClosePaneGesture closeGesture = new();
    private (string Status, string Label, bool Remote)? lastFace;

    public FixedSlotCommand(HerdrKeypadPlugin owner, int slot, bool isSpace)
        : base(isSpace ? $"Space {slot}" : $"Recent Agent {slot}",
            isSpace ? "Show and focus this workspace; press again to cycle its panes" : "Show and focus a recent Herdr agent; hold, then press again to close its pane",
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
        if (this.closeConfirmation.IsArmed) return ("confirm", "Close?", false);
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

    // Recent Agent keys share the close gesture of the Agents folder: the held
    // agent is pinned, and the key shows Close? until confirmed or expired.
    protected override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        if (this.isSpace) return base.ProcessButtonEvent2(actionParameter, buttonEvent);
        var gestureEvent = buttonEvent.EventType switch
        {
            DeviceButtonEventType.Press => ClosePaneButtonEvent.Press,
            DeviceButtonEventType.LongPress => ClosePaneButtonEvent.LongPress,
            DeviceButtonEventType.RepeatPress => ClosePaneButtonEvent.RepeatPress,
            DeviceButtonEventType.Release => ClosePaneButtonEvent.Release,
            _ => (ClosePaneButtonEvent?)null,
        };
        if (gestureEvent is null) return base.ProcessButtonEvent2(actionParameter, buttonEvent);
        switch (this.closeGesture.Process(gestureEvent.Value, this.closeConfirmation.IsArmed))
        {
            case ClosePaneGestureAction.Tap:
                this.RunCommand(actionParameter);
                break;
            case ClosePaneGestureAction.Arm when this.owner.Board.Online && this.closeConfirmation.Arm(FixedSlots.Recent(this.owner.Board.Snapshot, this.slot)):
                this.Refresh();
                _ = this.ExpireCloseAsync();
                break;
            case ClosePaneGestureAction.Confirm:
                _ = this.CloseAsync();
                break;
        }
        return true;
    }

    private async Task ExpireCloseAsync()
    {
        await Task.Delay(ClosePaneConfirmation.Window + TimeSpan.FromMilliseconds(100));
        if (this.closeConfirmation.Expire()) this.Refresh();
    }

    private async Task CloseAsync()
    {
        var target = this.closeConfirmation.Take();
        this.Refresh();
        if (target is null) return;
        try
        {
            if (!await new CloseAgent(this.owner.Client).CloseIfStillPresentAsync(target))
                this.Log.Warning("Herdr agent changed; close cancelled");
        }
        catch (Exception e) { this.Log.Warning($"Could not close Herdr pane: {e.Message}"); }
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
    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        var face = this.Face();
        return face.Status == "confirm" ? KeyFaces.Confirm(imageSize) : KeyFaces.State(face.Status, imageSize, face.Remote);
    }
}
