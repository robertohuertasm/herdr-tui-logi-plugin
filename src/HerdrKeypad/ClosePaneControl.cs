namespace Loupedeck.HerdrKeypad;

using Herdr;

// Shared gesture for the two folders and the standalone action. A release can
// confirm only after a separate press; a hold never closes a pane by itself.
internal sealed class ClosePaneControl(HerdrKeypadPlugin owner, Action refresh, Action<string> warning)
{
    private readonly ClosePaneConfirmation confirmation = new(owner.Client);
    private readonly ClosePaneGesture gesture = new();

    public bool IsArmed => this.confirmation.IsArmed;

    public bool Handle(DeviceButtonEvent2 buttonEvent)
    {
        var gestureEvent = buttonEvent.EventType switch
        {
            DeviceButtonEventType.Press => ClosePaneButtonEvent.Press,
            DeviceButtonEventType.LongPress => ClosePaneButtonEvent.LongPress,
            DeviceButtonEventType.RepeatPress => ClosePaneButtonEvent.RepeatPress,
            DeviceButtonEventType.Release => ClosePaneButtonEvent.Release,
            _ => (ClosePaneButtonEvent?)null,
        };
        if (gestureEvent is null) return false;
        switch (this.gesture.Process(gestureEvent.Value, this.IsArmed))
        {
            case ClosePaneGestureAction.Arm when owner.Board.Online && this.confirmation.Arm(owner.Board.Snapshot):
                refresh();
                _ = this.ExpireAsync();
                break;
            case ClosePaneGestureAction.Confirm:
                _ = this.ConfirmAsync();
                break;
        }
        return true;
    }

    public void Cancel()
    {
        this.gesture.Cancel();
        this.confirmation.Cancel();
    }

    private async Task ConfirmAsync()
    {
        try
        {
            var closing = this.confirmation.ConfirmAsync();
            refresh();
            if (!await closing) warning("Close pane cancelled: the focused pane changed or confirmation expired");
        }
        catch (Exception e) { warning($"Could not close Herdr pane: {e.Message}"); }
    }

    private async Task ExpireAsync()
    {
        await Task.Delay(ClosePaneConfirmation.Window + TimeSpan.FromMilliseconds(100));
        if (this.confirmation.Expire()) refresh();
    }
}
