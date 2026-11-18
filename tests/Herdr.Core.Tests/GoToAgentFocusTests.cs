using System.Text.Json;

namespace Herdr.Tests;

public class GoToAgentFocusTests
{
    [Theory]
    [InlineData("w1:p1", "[]", true)]
    [InlineData("w1:p1", """[{"id":"build","enabled":true,"selected":false}]""", true)]
    [InlineData("remote:build:w1:p1", """[{"id":"build","enabled":true,"selected":true}]""", true)]
    [InlineData("w1:p1", """[{"id":"build","enabled":true,"selected":true}]""", false)]
    [InlineData("remote:other:w1:p1", """[{"id":"build","enabled":true,"selected":true}]""", false)]
    [InlineData("w1:p1", """[{"id":"build","enabled":true}]""", false)]
    [InlineData("w1:p1", """[{"id":"build","selected":"false"}]""", false)]
    [InlineData("remote:build:w1:p1", """[{"id":"build","enabled":false,"selected":true}]""", false)]
    [InlineData("remote:build:w1:p1", """[{"id":"build","enabled":true,"selected":true},{"id":"other","enabled":true,"selected":true}]""", false)]
    public void FastRouteRequiresAKnownMatchingSelection(string key, string profiles, bool expected)
    {
        using var document = JsonDocument.Parse(profiles);
        Assert.Equal(expected, GoToAgentFocus.IsOnSelectedMachine(key, document.RootElement));
    }

    [Fact]
    public async Task LocalToLocalUsesOnlyDirectFocusEvenIfARemoteCannotBeRead()
    {
        using var server = new FakeHerdr();
        server.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            var method = request.RootElement.GetProperty("method").GetString();
            if (method == "session.snapshot")
                return FakeHerdr.SendAndClose(socket, """{"result":{"snapshot":{"version":"0.9.5","protocol":22}}}""" + "\n");
            Assert.Equal("pane.focus", method);
            return FakeHerdr.SendAndClose(socket, """{"result":{}}""" + "\n");
        };
        var remote = new RemoteAgents(args =>
        {
            Assert.Equal(new[] { "machine", "list", "--json" }, args);
            return Task.FromResult("""[{"id":"offline","enabled":true,"selected":false}]""");
        });
        var terminal = new RecordingTerminal();
        await new HerdrClient(server.SocketPath, terminal, remote)
            .FocusAgentAsync("w1:p1", new Config("", ExperimentalGoToAgentFocus: true));
        Assert.Empty(terminal.Queries);
    }

    [Fact]
    public async Task SameRemoteUsesDirectFocusWithoutLocalOrRemoteSnapshots()
    {
        var calls = new List<string[]>();
        var remote = new RemoteAgents(args =>
        {
            calls.Add(args);
            if (args[0] == "machine") return Task.FromResult("""[{"id":"build","enabled":true,"selected":true}]""");
            Assert.Equal(new[] { "--machine", "build", "agent", "focus", "w2:p1" }, args);
            return Task.FromResult("{}");
        });
        var terminal = new RecordingTerminal();
        await new HerdrClient("/missing", terminal, remote)
            .FocusAgentAsync("remote:build:w2:p1", new Config("", ExperimentalGoToAgentFocus: true));
        Assert.Equal(2, calls.Count);
        Assert.Empty(terminal.Queries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossMachineOrUnknownSelectionUsesGoToAndReusesTheProfileRead(bool unknownSelection)
    {
        using var server = SnapshotServer();
        var profileReads = 0;
        var remote = new RemoteAgents(args =>
        {
            if (args[0] == "machine")
            {
                profileReads++;
                return Task.FromResult(unknownSelection
                    ? """[{"id":"build","label":"Build","enabled":true}]"""
                    : """[{"id":"build","label":"Build","enabled":true,"selected":false}]""");
            }
            Assert.Equal(new[] { "--machine", "build", "api", "snapshot" }, args);
            return Task.FromResult("""{"snapshot":{"panes":[{"pane_id":"w2:p1"}],"agents":[{"pane_id":"w2:p1"}]}}""");
        });
        var terminal = new RecordingTerminal();
        await new HerdrClient(server.SocketPath, terminal, remote)
            .FocusAgentAsync("remote:build:w2:p1", new Config("", ExperimentalGoToAgentFocus: true));
        Assert.Equal(1, profileReads);
        Assert.Equal(new[] { "w2:p1" }, terminal.Queries);
    }

    [Fact]
    public async Task FailedDirectFocusFallsBackToValidatedGoTo()
    {
        using var log = new LogCapture();
        using var server = SnapshotServer(failFocus: true);
        var terminal = new RecordingTerminal();
        var remote = new RemoteAgents(_ => Task.FromResult("[]"));
        await new HerdrClient(server.SocketPath, terminal, remote)
            .FocusAgentAsync("w1:p1", new Config("", ExperimentalGoToAgentFocus: true));
        Assert.Equal(new[] { "w1:p1" }, terminal.Queries);
        Assert.Contains(log.Warnings, text => text.Contains("Direct agent focus failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisablingTheFlagDoesNotReadMachineSelection()
    {
        using var server = new FakeHerdr();
        server.Behaviour = (socket, _) => FakeHerdr.SendAndClose(socket, """{"result":{}}""" + "\n");
        var remote = new RemoteAgents(_ => throw new InvalidOperationException("No profile lookup expected"));
        var terminal = new RecordingTerminal();
        await new HerdrClient(server.SocketPath, terminal, remote).FocusAgentAsync("w1:p1", new Config("", ExperimentalGoToAgentFocus: false));
        Assert.Empty(terminal.Queries);
    }

    [Fact]
    public async Task ImmediatelyAfterAGoToSwitchFurtherPressesAreDiscarded()
    {
        using var server = SnapshotServer();
        var selected = false;
        var remote = new RemoteAgents(args => args[0] == "machine"
            ? Task.FromResult(JsonSerializer.Serialize(new[] { new { id = "build", label = "Build", enabled = true, selected } }))
            : args[2] == "api"
                ? Task.FromResult("""{"snapshot":{"panes":[{"pane_id":"w2:p1"}],"agents":[{"pane_id":"w2:p1"}]}}""")
                : throw new InvalidOperationException("Fast focus must wait for the previous Go to to settle"));
        var terminal = new RecordingTerminal();
        var client = new HerdrClient(server.SocketPath, terminal, remote);
        var config = new Config("", ExperimentalGoToAgentFocus: true);
        await client.FocusAgentAsync("remote:build:w2:p1", config);
        selected = true;
        await client.FocusAgentAsync("remote:build:w2:p1", config);
        Assert.Single(terminal.Queries);
    }

    [Fact]
    public async Task CurrentLocalAgentDoesNotSendFocusOrKeyboardInput()
    {
        using var server = new FakeHerdr();
        server.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            Assert.Equal("session.snapshot", request.RootElement.GetProperty("method").GetString());
            return FakeHerdr.SendAndClose(socket,
                """{"result":{"snapshot":{"version":"0.9.5","protocol":22,"focused_pane_id":"w1:p1","agents":[{"pane_id":"w1:p1"}]}}}""" + "\n");
        };
        var terminal = new RecordingTerminal();
        var remote = new RemoteAgents(_ => Task.FromResult("[]"));
        var client = new HerdrClient(server.SocketPath, terminal, remote);
        await client.FocusAgentAsync("w1:p1", new Config("", ExperimentalGoToAgentFocus: true));
        Assert.Empty(terminal.Queries);
        Assert.Null(client.PendingAgentKey);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task RepeatingARemoteTargetChecksFreshFocusRatherThanRememberingIt(bool stillFocused, int expectedFocusCalls)
    {
        var focusCalls = 0;
        var remote = new RemoteAgents(args =>
        {
            if (args[0] == "machine") return Task.FromResult("""[{"id":"build","enabled":true,"selected":true}]""");
            if (args[2] == "api") return Task.FromResult(JsonSerializer.Serialize(new
            {
                snapshot = new { focused_pane_id = stillFocused ? "w2:p1" : "w2:p2", agents = new[] { new { pane_id = "w2:p1" } } },
            }));
            Assert.Equal(new[] { "--machine", "build", "agent", "focus", "w2:p1" }, args);
            focusCalls++;
            return Task.FromResult("{}");
        });
        var terminal = new RecordingTerminal();
        var client = new HerdrClient("/missing", terminal, remote);
        var config = new Config("", ExperimentalGoToAgentFocus: true);
        await client.FocusAgentAsync("remote:build:w2:p1", config);
        await client.FocusAgentAsync("remote:build:w2:p1", config);
        Assert.Equal(expectedFocusCalls, focusCalls);
        Assert.Empty(terminal.Queries);
    }

    [Fact]
    public async Task RecentButtonsDoNotQueueRepeatedPressesWhileFocusIsPending()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var focusCalls = 0;
        var remote = new RemoteAgents(args =>
        {
            if (args[0] == "machine") return Task.FromResult("""[{"id":"build","enabled":true,"selected":true}]""");
            Interlocked.Increment(ref focusCalls);
            started.TrySetResult();
            return release.Task;
        });
        var client = new HerdrClient("/missing", new RecordingTerminal(), remote);
        var actions = new FixedSlotActions(client);
        var pending = actions.FocusRecentAsync("remote:build:w2:p1");
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("remote:build:w2:p1", client.PendingAgentKey);
            await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => actions.FocusRecentAsync("remote:build:w2:p1")))
                .WaitAsync(TimeSpan.FromSeconds(2));
            await actions.FocusRecentAsync("remote:build:w2:p2").WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, focusCalls);
        }
        finally { release.TrySetResult("{}"); await pending; }
        Assert.Null(client.PendingAgentKey);
    }

    [Fact]
    public async Task FailedNavigationClearsPendingStateAndAllowsRetry()
    {
        var fail = true;
        var remote = new RemoteAgents(_ => fail ? throw new HerdrException("offline") : Task.FromResult("{}"));
        var client = new HerdrClient("/missing", new RecordingTerminal(), remote);
        var changes = new List<string?>();
        client.AgentFocusChanged += () => changes.Add(client.PendingAgentKey);
        var config = new Config("", ExperimentalGoToAgentFocus: false);
        await Assert.ThrowsAsync<HerdrException>(() => client.FocusAgentAsync("remote:build:w2:p1", config));
        Assert.Null(client.PendingAgentKey);
        fail = false;
        await client.FocusAgentAsync("remote:build:w2:p1", config);
        Assert.Equal(new string?[] { "remote:build:w2:p1", null, "remote:build:w2:p1", null }, changes);
    }

    [Fact]
    public async Task PickerOpensAndTypesBeforeTheRemoteCatalogFinishesButDoesNotConfirm()
    {
        using var server = SnapshotServer();
        var catalogStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new RemoteAgents(args =>
        {
            if (args[0] == "machine")
                return Task.FromResult("""[{"id":"build","label":"Build","enabled":true,"selected":false}]""");
            catalogStarted.TrySetResult();
            return catalog.Task;
        });
        var terminal = new RecordingTerminal();
        var client = new HerdrClient(server.SocketPath, terminal, remote);
        var pending = client.FocusAgentAsync("remote:build:w2:p1", new Config("", ExperimentalGoToAgentFocus: true));
        try
        {
            await terminal.Opened.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await catalogStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, terminal.OpenCalls);
            Assert.Equal(new[] { "w2:p1" }, terminal.TypedQueries);
            Assert.Empty(terminal.Queries);
            Assert.False(pending.IsCompleted);
            Assert.Equal("remote:build:w2:p1", client.PendingAgentKey);
        }
        finally
        {
            catalog.TrySetResult("""{"snapshot":{"panes":[{"pane_id":"w2:p1"}],"agents":[{"pane_id":"w2:p1"}]}}""");
            await pending;
        }
        Assert.Equal(new[] { "w2:p1" }, terminal.Queries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningFailureNeverTypesAndValidationFailureNeverConfirmsATarget(bool failOpening)
    {
        using var server = SnapshotServer();
        var remote = new RemoteAgents(args => args[0] == "machine"
            ? Task.FromResult("""[{"id":"build","label":"Build","enabled":true,"selected":false}]""")
            : failOpening ? Task.FromResult("""{"snapshot":{"panes":[{"pane_id":"w2:p1"}],"agents":[{"pane_id":"w2:p1"}]}}""")
                : throw new HerdrException("catalog unavailable"));
        var terminal = new RecordingTerminal { OpenError = failOpening ? new HerdrException("terminal unavailable") : null };
        var client = new HerdrClient(server.SocketPath, terminal, remote);
        await Assert.ThrowsAsync<HerdrException>(() =>
            client.FocusAgentAsync("remote:build:w2:p1", new Config("", ExperimentalGoToAgentFocus: true)));
        Assert.Equal(1, terminal.OpenCalls);
        Assert.Equal(failOpening ? 0 : 1, terminal.TypedQueries.Count);
        Assert.Empty(terminal.Queries);
        Assert.Null(client.PendingAgentKey);
    }

    [Fact]
    public void SamePaneIdOnTwoMachinesIsRejected()
    {
        var local = Endpoint(null, "Local", "w1:p1");
        var remote = Endpoint("remote", "Build", "w1:p1");
        Assert.Throws<HerdrException>(() => GoToAgentFocus.Query("remote:remote:w1:p1", [local, remote]));
    }

    [Theory]
    [InlineData("w1:p10", "")]
    [InlineData("w2:p1", "logs for w1:p1")]
    public void OrdinaryShellIdsAndAncestorLabelsCanMakeSearchAmbiguous(string otherPane, string workspaceLabel)
    {
        var local = Endpoint(null, "Local", "w1:p1");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            panes = new[] { new { pane_id = otherPane, workspace_id = "other" } },
            workspaces = new[] { new { workspace_id = "other", label = workspaceLabel } },
        }));
        var remote = new GoToEndpoint("remote", "Build", document.RootElement.Clone());
        Assert.Throws<HerdrException>(() => GoToAgentFocus.Query("w1:p1", [local, remote]));
    }

    [Fact]
    public void UniqueMachineQualifiedTargetCanBeSelected()
    {
        Assert.Equal("wF:p2", GoToAgentFocus.Query("remote:build:wF:p2",
            [Endpoint(null, "Local", "w1:p1"), Endpoint("build", "Build", "wF:p2")]));
        Assert.Throws<HerdrException>(() => GoToAgentFocus.Query("remote:build:wF:p3",
            [Endpoint("build", "Build", "wF:p2")]));
    }

    [Fact]
    public async Task PlanningUsesFreshAllPaneSnapshotsAndNeverFocusesAnAgent()
    {
        using var local = new FakeHerdr();
        local.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            Assert.Equal("session.snapshot", request.RootElement.GetProperty("method").GetString());
            return FakeHerdr.SendAndClose(socket, """{"result":{"snapshot":{"panes":[{"pane_id":"w1:p1"}],"agents":[{"pane_id":"w1:p1"}]}}}""" + "\n");
        };
        var remote = new RemoteAgents(args => Task.FromResult(args[0] == "machine"
            ? """[{"id":"build","label":"Build","enabled":true}]"""
            : """{"result":{"snapshot":{"panes":[{"pane_id":"w2:p1"}],"agents":[{"pane_id":"w2:p1"}]}}}"""));
        var client = new HerdrClient(local.SocketPath, remoteAgents: remote);
        Assert.Equal("w2:p1", await client.GetGoToAgentQueryAsync("remote:build:w2:p1"));
    }

    [Fact]
    public async Task LegacySnapshotFallbackQuotesSessionAndDoesNotInstallAnything()
    {
        string[]? ssh = null;
        var remote = new RemoteAgents(args => args[0] == "machine"
            ? Task.FromResult("""[{"id":"old","label":"Old","target":"host","session":"work's","enabled":true}]""")
            : throw new HerdrException("remote Herdr does not support machine API forwarding"),
            args => { ssh = args; return Task.FromResult("""{"result":{"snapshot":{"panes":[],"agents":[]}}}"""); });
        Assert.Single(await remote.GoToEndpointsAsync());
        Assert.Equal(new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=5", "--", "host", "herdr --session 'work'\\''s' api snapshot" }, ssh);
    }

    [Fact]
    public async Task UnreachableEnabledMachineAbortsPlanning()
    {
        var remote = new RemoteAgents(args => args[0] == "machine"
            ? Task.FromResult("""[{"id":"offline","label":"Offline","enabled":true}]""")
            : throw new HerdrException("connection failed"));
        await Assert.ThrowsAsync<HerdrException>(() => remote.GoToEndpointsAsync());
    }

    [Theory]
    [InlineData("w1:p1\nquit")]
    [InlineData("w1:p1\" & do shell script \"bad")]
    [InlineData("w1:p1,return")]
    public void QueryCannotInjectAppleScriptOrAdditionalKeyboardCommands(string query) =>
        Assert.Throws<HerdrException>(() => KeyboardShortcut.TypeGoToScript("Warp", query, 100));

    [Fact]
    public void TypingClearsSearchButOnlyConfirmationSendsEnter()
    {
        var script = KeyboardShortcut.TypeGoToScript("Warp", "w1:p1", 75);
        Assert.DoesNotContain("to activate", script);
        Assert.True(script.IndexOf("keystroke \"u\" using {control down}", StringComparison.Ordinal) < script.IndexOf("keystroke \"w1:p1\"", StringComparison.Ordinal));
        Assert.DoesNotContain("key code 36", script);
        Assert.Contains("delay 0.075", script);
        Assert.Contains("Terminal lost focus", script);
        var confirmation = KeyboardShortcut.ConfirmGoToScript("Warp", 75);
        Assert.Contains("key code 36", confirmation);
        Assert.DoesNotContain("to activate", confirmation);
        Assert.Contains("Terminal lost focus", confirmation);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5001)]
    public void InvalidInputDelaysCannotGenerateKeyboardCommands(int milliseconds)
    {
        Assert.Throws<HerdrException>(() => KeyboardShortcut.TypeGoToScript("Warp", "w1:p1", milliseconds));
        Assert.Throws<HerdrException>(() => KeyboardShortcut.ConfirmGoToScript("Warp", milliseconds));
    }

    [Fact]
    public void HybridDefaultsOnAndCanBeDisabledWithoutChangingOtherConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"herdr-spike-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, """{"terminalApp":"Warp","goToShortcut":"ctrl+a,g","experimentalGoToAgentFocus":true}""");
            Assert.Equal(new Config("Warp", "ctrl+a,g", true), Config.Load(path));
            File.WriteAllText(path, """{"terminalApp":"Warp"}""");
            Assert.True(Config.Load(path).ExperimentalGoToAgentFocus);
            File.WriteAllText(path, """{"terminalApp":"Warp","experimentalGoToAgentFocus":false}""");
            Assert.Equal(new Config("Warp", ExperimentalGoToAgentFocus: false), Config.Load(path));
            File.WriteAllText(path, """{"terminalApp":"Warp","goToInputDelayMs":250}""");
            Assert.Equal(250, Config.Load(path).GoToInputDelayMs);
            File.WriteAllText(path, """{"goToInputDelayMs":"bad"}""");
            Assert.Equal(100, Config.Load(path).GoToInputDelayMs);
            File.Delete(path);
            Assert.True(Config.Load(path).ExperimentalGoToAgentFocus);
        }
        finally { File.Delete(path); }
    }

    private static GoToEndpoint Endpoint(string? machine, string label, string pane)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            panes = new[] { new { pane_id = pane } }, agents = new[] { new { pane_id = pane } },
        }));
        return new GoToEndpoint(machine, label, document.RootElement.Clone());
    }

    private static FakeHerdr SnapshotServer(bool failFocus = false)
    {
        var server = new FakeHerdr();
        server.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            var method = request.RootElement.GetProperty("method").GetString();
            if (method == "pane.focus" && failFocus)
                return FakeHerdr.SendAndClose(socket, """{"error":{"message":"temporary failure"}}""" + "\n");
            Assert.Equal("session.snapshot", method);
            return FakeHerdr.SendAndClose(socket,
                """{"result":{"snapshot":{"panes":[{"pane_id":"w1:p1"}],"agents":[{"pane_id":"w1:p1"}]}}}""" + "\n");
        };
        return server;
    }

    private sealed class RecordingTerminal : Terminal
    {
        public List<string> Queries { get; } = [];
        public List<string> TypedQueries { get; } = [];
        public int OpenCalls { get; private set; }
        public Exception? OpenError { get; init; }
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<string> OpenGoToAsync(string shortcut)
        {
            OpenCalls++;
            Opened.TrySetResult();
            return OpenError is null ? Task.FromResult("test-terminal") : Task.FromException<string>(OpenError);
        }
        public override Task TypeGoToAgentQueryAsync(string application, string query, int inputDelayMs)
        {
            Assert.Equal("test-terminal", application);
            TypedQueries.Add(query);
            return Task.CompletedTask;
        }
        public override Task ConfirmGoToAgentAsync(string application, int inputDelayMs)
        {
            Assert.Equal("test-terminal", application);
            Queries.Add(TypedQueries[^1]);
            return Task.CompletedTask;
        }
        protected override Task<string> RunAsync(string command, params string[] args) => Task.FromResult("");
    }
}
