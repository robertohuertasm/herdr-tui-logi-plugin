# Herdr TUI Keypad

Monitor **local and remote agents together** on the Logitech MX Creative Keypad
with this agent-first plugin for [Herdr TUI](https://github.com/herdrdev/herdr).
Get live status, pane focus and navigation, with a small **R** badge marking
agents on saved SSH machines.

<p align="center">
  <img src="docs/images/keypad.png" width="420" alt="Herdr agents on the Keypad, with state colors">
</p>

> The images are diagrams drawn from the plugin's colors and icons, not photos.

The Agents folder shows Shortcuts and Cycle panes followed by live agents with
state colors and session titles. Shortcuts opens the shared shortcut menu; its
Back button returns to the agents.
The folder's own key shows the most urgent agent: blocked, done (unseen),
working, then idle. Ties follow the same recent-state-change order.
Blocked is red, done is blue, working is amber, and idle is green.
Press to focus; hold and then press **Close?** within three seconds to close the
pane. Herdr closes the pane, which stops its running process. The Shortcuts folder
shows Zoom pane on the top row, Cycle panes, Cycle tabs and Go to on the
middle row, then Layout, Tabs and Workspaces on the bottom row. When opened from
Agents, Back beside the native Back returns to the agent list; the standalone
Shortcuts folder leaves that position blank. Each shortcut is independently assignable.

**Recent Agent 1–9** are individual keys for the agents in recent-state-change
order; press one to focus its pane, or hold it and then press **Close?** within
three seconds to close that pane. The held agent is pinned, so a reorder during
confirmation cannot close a different one. **Space 1–9** show each numbered workspace's
status: press to focus the workspace, then press again to cycle panes in its
active tab. Unoccupied slots and an offline server have distinct indicators.

**Close pane** is available under Layout and as an individual action. Hold it,
then press **Close?** within three seconds. The action cancels if the focused
pane or its terminal has changed before confirmation.

<img src="docs/images/states.png" width="840" alt="Blocked, unread, working, idle, empty and offline key faces">

| Action | What it does |
|---|---|
| **Recent Agent 1–9** | Shows agents in recent state-change order; press to focus, hold and confirm to close its pane. |
| **Space 1–9** | Shows a workspace's status; press to focus, then cycle its active tab's panes. |
| **Herdr TUI** | Open shortcuts, cycle panes and browse live agents. Hold an agent and confirm to close its pane. |
| **Herdr shortcuts** | Zoom, cycle panes, and open Layout, Tabs or Workspaces. Each shortcut is also assignable separately. |

<p align="center"><img src="docs/images/agents-folder.png" width="420" alt="Agents folder with cycle controls and six agents"></p>

<p align="center">
  <img src="docs/images/shortcuts-folder.png" width="350" alt="Shortcuts folder">
  <img src="docs/images/layout-folder.png" width="350" alt="Layout controls">
</p>

Agents are ranked by Herdr's `state_change_seq` when available. This measures
reported state changes, not arbitrary output activity. Older servers fall back to
locally observed status changes. The plugin talks to the local Herdr socket and
raises the configured or detected terminal on agent focus.

The Agents folder and Recent Agent keys also discover enabled saved SSH machines
through `herdr machine list --json` and `herdr --machine <id> api snapshot`.
Remote entries show a small R badge on the status icon, keeping their titles
free of machine prefixes. Remote snapshots refresh in the
background every ten seconds; unavailable machines are omitted and failures are
logged without blocking local agents. Both the local CLI and remote installation
must support machine API forwarding. Set `HERDR_BIN_PATH` if the CLI is installed
outside `~/.local/bin`, standard Homebrew locations, or the service's PATH.

Agent focus and confirmed close are routed to the originating server. Server focus does not switch
the machine displayed by an already-open Herdr TUI.
Space keys and layout shortcuts operate on the local server. A combined list uses
locally observed status changes for recency because server sequence counters are
independent; it does not reproduce the TUI's custom filters or client-specific
Done/seen state.

## Go to and navigation limits

**Go to** opens Herdr's native picker (Ctrl+B, then G). It replaces Split right
in the Shortcuts menu; Split right remains under Layout. With Herdr's default
bindings, Go to needs no Herdr configuration change. It activates the terminal
and sends keys through System Events, requiring macOS Accessibility access for
Logi Plugin Service. Keep the intended Herdr TUI active in that terminal.

If you customize Herdr's prefix or Go to binding, set `goToShortcut` in
`~/.config/herdr-logi-plugin/config.json` (or `HERDR_LOGI_CONFIG`) to match, e.g.
`"ctrl+b,g"`. A comma separates successive strokes; supported modifiers are
`ctrl`, `alt`, `shift` and `cmd`, followed by a single letter or digit.

**Cross-machine navigation:** remote agents are listed and their focus/close
requests go to the producing server. A successful server focus does **not**
select that machine in an already-open TUI. Select the machine or agent in
Herdr's UI/Go to when moving between Local and a saved SSH machine.

Space keys, Previous/Next Workspace, Cycle panes/tabs and other layout shortcuts
operate through the **local server**, even when the TUI displays a remote
machine. They do not cycle across machines. Herdr's own native workspace/agent
navigation can cross connected machines, but those optional bindings are not
configured or provided by this plugin. You can assign your own keyboard macros
in Logi Options+ and configure matching native bindings in Herdr.

## Install

Requires macOS, Herdr TUI 0.9.5, Logi Options+ with Logi Plugin Service 6.4, and an
MX Creative Keypad or compatible Loupedeck device.

1. Download `HerdrKeypad_<version>.lplug4` from the [latest release](https://github.com/robertohuertasm/herdr-tui-logi-plugin/releases/latest).
2. Double-click the package to install it through Logi Options+.
3. Add **Herdr TUI**, **Herdr shortcuts**, or individual actions to the Keypad.

Remove any `HerdrKeypad.link` development link before installing a release.

## Build

Building requires the .NET 10 SDK.

```sh
dotnet test tests/Herdr.Core.Tests
dotnet build src/HerdrKeypad -c Release
dotnet run --project probes/HerdrProbe
```

To regenerate the documentation diagrams, run `npm install --prefix tools`, then
`node tools/render-images.cjs`. The diagrams use the plugin's colors and icons;
the Keypad frame comes from the MIT-licensed reference plugin.
To regenerate the Logi Options+ plugin icon, run `node tools/render-plugin-icon.cjs`.

For a release, set the manifest version and push a matching `vX.Y.Z` tag. GitHub
Actions tests, builds, packs, verifies and attaches the `.lplug4` to a release.
The repository is public. `docs/` contains the GitHub Pages site.

## Verification still needed

- Exercise paging, changing ranks, empty slots and repeated Space presses on the Keypad.
- Verify zoom, navigation and focus against real panes, tabs and workspaces.
- Verify working, done, blocked, offline, empty and stale agent states.
- Herdr offers no conditional `pane.close`: identity is checked immediately
  before close, but the pane can still change between those two requests.

The socket client and terminal detection are adapted from
[niklasberglund/herdr-logi-plugin](https://github.com/niklasberglund/herdr-logi-plugin),
MIT. Its license is included as `LICENSE-REFERENCE` and in the package metadata.
Shortcut icons are [Microsoft Codicons](https://github.com/microsoft/vscode-codicons)
0.0.45, licensed under CC BY 4.0; see `LICENSE-CODICONS`.
The plugin icon and status icon use the [Herdr TUI logo](https://github.com/herdrdev/herdr/blob/v0.9.5/assets/logo.svg)
(the status icon is cropped and recolored), licensed under Apache 2.0; see `LICENSE-HERDR` and
`NOTICE-HERDR`. This independent plugin is not endorsed by the Herdr project.
