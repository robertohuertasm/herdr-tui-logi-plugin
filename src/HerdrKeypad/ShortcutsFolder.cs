namespace Loupedeck.HerdrKeypad;

public sealed class ShortcutsFolder : PluginDynamicFolder
{
    private ShortcutMenu menu = null!;
    public ShortcutsFolder() { this.DisplayName = "Herdr shortcuts"; this.Description = "Herdr layout, tabs and workspaces"; this.GroupName = "Herdr"; }
    public override bool Load()
    {
        this.menu = new ShortcutMenu((HerdrKeypadPlugin)this.Plugin, this.Refresh);
        return true;
    }
    public override bool Unload() { this.menu.Reset(); return true; }
    public override bool Activate() { this.menu.Reset(); this.Refresh(); return true; }
    public override bool Deactivate() { this.menu.Reset(); return true; }
    public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType deviceType) => PluginDynamicFolderNavigation.None;
    public override string GetButtonDisplayName(PluginImageSize imageSize) => "Shortcuts";
    public override BitmapImage GetButtonImage(PluginImageSize imageSize) => KeyFaces.Icon("layout", "Shortcuts", imageSize);
    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType) => this.menu.Parameters().Select(this.CreateCommandName);
    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => this.menu.GetCommandDisplayName(actionParameter, imageSize);
    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) => this.menu.GetCommandImage(actionParameter, imageSize);
    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent) => this.menu.ProcessButtonEvent2(actionParameter, buttonEvent);
    public override void RunCommand(string actionParameter) => this.menu.RunCommand(actionParameter);
    private void Refresh()
    {
        try { this.ButtonActionNamesChanged(); }
        catch (Exception e) { this.Plugin.Log.Error(e, "Could not refresh shortcuts"); }
    }
}
