namespace Loupedeck.HerdrKeypad;

using Herdr;

public sealed class ShortcutsFolder : PluginDynamicFolder
{
    private string? level;
    private ClosePaneControl? closePane;
    private bool commandPressed;
    public ShortcutsFolder() { this.DisplayName = "Herdr shortcuts"; this.Description = "Herdr layout, tabs and workspaces"; this.GroupName = "Herdr"; }
    private HerdrKeypadPlugin Owner => (HerdrKeypadPlugin)this.Plugin;
    public override bool Load()
    {
        this.closePane = new ClosePaneControl(this.Owner, this.Refresh, text => this.Plugin.Log.Warning(text));
        return true;
    }
    public override bool Unload() { this.closePane?.Cancel(); return true; }
    public override bool Activate() { this.closePane?.Cancel(); this.commandPressed = false; this.level = null; this.ButtonActionNamesChanged(); return true; }
    public override bool Deactivate() { this.closePane?.Cancel(); this.commandPressed = false; this.level = null; return true; }
    public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType deviceType) => PluginDynamicFolderNavigation.None;
    public override string GetButtonDisplayName(PluginImageSize imageSize) => "Shortcuts";
    public override BitmapImage GetButtonImage(PluginImageSize imageSize) => KeyFaces.Icon("layout", "Shortcuts", imageSize);
    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        if (this.level is null)
        {
            yield return this.CreateCommandName("action:zoom");
            yield return this.CreateCommandName("action:cycle");
            foreach (var group in new[] { "Layout", "Tabs", "Workspaces" }) yield return this.CreateCommandName("group:" + group);
        }
        else
        {
            yield return this.CreateCommandName("back");
            foreach (var action in Herdr.Navigation.All.Where(a => a.Group == this.level))
                yield return this.CreateCommandName("action:" + (action.Id == "close-pane" && this.closePane?.IsArmed == true ? "close-pane-confirm" : action.Id));
        }
    }
    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => actionParameter switch
    {
        "back" => "Back",
        "action:close-pane-confirm" => "Close?",
        _ when actionParameter.StartsWith("group:", StringComparison.Ordinal) => actionParameter[6..],
        _ when actionParameter.StartsWith("action:", StringComparison.Ordinal) => Herdr.Navigation.All.FirstOrDefault(a => a.Id == actionParameter[7..])?.Label ?? " ",
        _ => " ",
    };
    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (actionParameter == "action:close-pane-confirm") return KeyFaces.Confirm(imageSize);
        if (actionParameter == "back") return KeyFaces.Icon("chevron-left", "Back", imageSize);
        if (actionParameter.StartsWith("group:", StringComparison.Ordinal)) return KeyFaces.Group(actionParameter[6..], imageSize);
        if (actionParameter.StartsWith("action:", StringComparison.Ordinal))
            return KeyFaces.Shortcut(actionParameter[7..], this.GetCommandDisplayName(actionParameter, imageSize), imageSize);
        return KeyFaces.Glyph(" ", imageSize);
    }
    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        if (IsClosePane(actionParameter)) return this.level == "Layout" && this.closePane?.Handle(buttonEvent) == true;
        switch (buttonEvent.EventType)
        {
            case DeviceButtonEventType.Press: this.commandPressed = true; return true;
            case DeviceButtonEventType.LongPress:
            case DeviceButtonEventType.RepeatPress: return true;
            case DeviceButtonEventType.Release:
                if (this.commandPressed) this.RunCommand(actionParameter);
                this.commandPressed = false;
                return true;
            default: return false;
        }
    }
    public override void RunCommand(string actionParameter)
    {
        if (actionParameter == "back") { this.closePane?.Cancel(); this.level = null; this.ButtonActionNamesChanged(); }
        else if (actionParameter.StartsWith("group:", StringComparison.Ordinal)) { this.closePane?.Cancel(); this.level = actionParameter[6..]; this.Plugin.Log.Info($"Shortcuts folder opened {this.level}"); this.ButtonActionNamesChanged(); }
        else if (IsClosePane(actionParameter)) { }
        else if (actionParameter.StartsWith("action:", StringComparison.Ordinal))
        {
            this.closePane?.Cancel();
            this.Refresh();
            this.Plugin.Log.Info($"Shortcuts folder ran {actionParameter[7..]}");
            _ = this.ExecuteAsync(actionParameter[7..]);
        }
    }
    private static bool IsClosePane(string parameter) => parameter is "action:close-pane" or "action:close-pane-confirm";
    private void Refresh()
    {
        try { this.ButtonActionNamesChanged(); }
        catch (Exception e) { this.Plugin.Log.Error(e, "Could not refresh close pane confirmation"); }
    }
    private async Task ExecuteAsync(string id)
    {
        try { await this.Owner.Navigation.ExecuteAsync(id); }
        catch (Exception e) { this.Plugin.Log.Error($"Could not run Herdr shortcut: {e.Message}"); }
    }
}
