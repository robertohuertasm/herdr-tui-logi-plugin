namespace Herdr;

public enum ClosePaneButtonEvent { Press, LongPress, RepeatPress, Release }
public enum ClosePaneGestureAction { None, Arm, Confirm }

public sealed class ClosePaneGesture
{
    private bool pressed;
    private bool held;

    public ClosePaneGestureAction Process(ClosePaneButtonEvent buttonEvent, bool armed)
    {
        switch (buttonEvent)
        {
            case ClosePaneButtonEvent.Press:
                this.pressed = true;
                this.held = false;
                return ClosePaneGestureAction.None;
            case ClosePaneButtonEvent.LongPress:
                if (!this.pressed) return ClosePaneGestureAction.None;
                this.held = true;
                return armed ? ClosePaneGestureAction.None : ClosePaneGestureAction.Arm;
            case ClosePaneButtonEvent.Release:
                var confirm = this.pressed && !this.held && armed;
                this.Cancel();
                return confirm ? ClosePaneGestureAction.Confirm : ClosePaneGestureAction.None;
            default:
                return ClosePaneGestureAction.None;
        }
    }

    public void Cancel() { this.pressed = false; this.held = false; }
}
