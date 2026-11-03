namespace Herdr.Tests;

public class KeyboardShortcutTests
{
    [Theory]
    [InlineData("ctrl+b,")]
    [InlineData("ctrl+b,return")]
    [InlineData("ctrl+b,\"")]
    [InlineData("unknown+j")]
    public void InvalidSequencesAreRejectedBeforeExecution(string sequence)
    {
        Assert.Throws<HerdrException>(() => KeyboardShortcut.Script("Terminal", sequence));
    }

    [Fact]
    public void ScriptGuardsForegroundAndPreservesSequentialModifiers()
    {
        var script = KeyboardShortcut.Script("/Applications/Ghostty.app", "ctrl+b,shift+a");
        Assert.Contains("Terminal lost focus", script);
        Assert.Contains("bundle identifier of (first application process whose frontmost is true)", script);
        Assert.Contains("if foregroundBundle is not targetBundle then error", script);
        Assert.Contains("keystroke \"b\" using {control down}", script);
        Assert.Contains("keystroke \"a\" using {shift down}", script);
        Assert.True(script.IndexOf("Terminal lost focus", StringComparison.Ordinal) < script.IndexOf("keystroke", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GoToDoesNotNeedALocalSocket()
    {
        var terminal = new KeyboardTerminal();
        await new Navigation(new HerdrClient("/missing-socket", terminal)).ExecuteAsync("go-to");
        Assert.Equal("ctrl+b,g", terminal.Sequence);
    }

    private sealed class KeyboardTerminal : Terminal
    {
        public string? Sequence;
        public override Task SendShortcutAsync(string sequence) { Sequence = sequence; return Task.CompletedTask; }
    }
}
