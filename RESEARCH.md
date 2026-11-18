# Herdr provider: initial investigation

## Go to response time and configurable typing pauses (2026-11-18)

The user reported that the standalone Go to button opens promptly, while an
agent jump waited before showing the picker. The main delay was fresh remote
catalog lookup, not a fixed UI timeout. Revision v4 opened Go to in parallel
with catalog validation and only then filled and confirmed the query. The user
physically confirmed that the picker appeared immediately and the jump worked,
but reported about two seconds before typing began.

Revision v5 now overlaps both picker opening and query typing with catalog
validation. The pane ID is syntactically validated first; filtering by that ID
does not select anything. Only Enter waits for the complete catalog/uniqueness
check. A failed check leaves the filtered picker available for manual use and
sends no Enter. Opening and typing failures also prevent confirmation. The
terminal app identity returned by the opening stage is retained through typing
and confirmation; neither later script reactivates an app that lost foreground.
Other agent presses remain discarded while the operation is pending.

Added `goToInputDelayMs`: default 100 ms, integer range 0–5000, invalid values
fall back to 100. It controls search/confirmation keyboard pauses and is read
on each press; it does not shorten SSH network timeout or skip validation.
The old initial search pause was 250 ms. README and GitHub Pages source explain
the setting, suggested 200–300 ms for missed characters, and the network/input
timing distinction. No local config rewrite is needed to use the new default.

All 80 core tests pass and Release build has zero warnings/errors. Tests hold
the remote catalog pending and assert that opening and typing have happened
while Enter has not; they also cover failure paths, foreground-only completion,
script input-delay bounds, decimal formatting, default-on and explicit opt-out.
Reloaded the development symlink and observed plugin 0.1.5 with the v5 startup
marker. User physical assessment of the v5 typing latency is pending.

The user explicitly requested updating GitHub Pages as well as README. Verified
that the site is a legacy Pages build from `main:/docs`. The personal GitHub
account has push/admin access; the active work account has read-only access, so
publication must use personal credentials without exposing them. Public-page
deployment and whether to publish a binary release remain pending; the user was
asked to clarify publication scope. No release or page deployment is claimed.

## Repeated presses and missing remote agent (2026-11-18)

The user confirmed the hybrid is fast within a machine, but found repeated
presses accumulating Go to calls and a fifth agent missing from a newer saved
machine. Reproduction evidence in the live plugin log showed repeated keyboard
jumps to the same target. Source had two queues: Recent Agent's wrapper gate and
the shared client gate. The post-Go-to settling guard also routed queued calls
back through Go to rather than suppressing them.

Removed the Recent Agent wrapper queue (Space navigation retains its own gate)
and made the shared agent gate non-waiting. While any agent navigation is in
flight, further presses are discarded, including presses for another target;
none are saved to run later. The one-second post-Go-to window also discards
presses instead of issuing another Go to. Pending state is cleared even after
failure, and its change event refreshes both agent entry points: the destination
key displays `Opening...` during the operation.

On a matching selected machine, a fresh local snapshot skips a confirmed
already-focused agent. Repeating the last remote target similarly checks a
fresh remote snapshot before skipping it; this is not a permanent last-target
cache, so manual navigation away is respected. Switching between different
remote agents keeps the existing one-call API route. The single-client and
saved-selection-versus-visible-selection limitations still apply.

The missing machine was already saved and enabled. Its CLI rejects machine API
forwarding, and background discovery previously omitted it. The read-only SSH
snapshot fallback used by the spike catalog is now shared with ordinary agent
discovery and fresh identity/focus reads. No remote binary or server was changed.
Focus/close commands still use their existing forwarding transport; native Go
to remains the experimental navigation fallback for older-machine agents.

Validation: all 75 core tests pass. New tests cover repeated/concurrent presses
through Recent Agent's real wrapper, requests during settling, current-local
no-op, fresh remote no-op versus manual navigation away, recovery after failure,
pending-state cleanup and background discovery on a legacy SSH machine. Plugin
Release build completed with zero warnings/errors. The macOS probe's read-only
`--agent-catalog` reported five agents: two local, two on one saved remote and
one on the other. No agent titles or private session identifiers are recorded.

Backed up the previous working hybrid build, reloaded the development symlink,
and observed plugin 0.1.5 plus the `2026-11-18 v2` startup marker in Logitech's
log. README and website source describe dropped presses, pending feedback and
legacy discovery. The config flag remains enabled locally, with published
default-on behavior still conditional on the user's physical validation. The
user then confirmed: "Veo los cinco y ya no se repiten los saltos". Live logs
also show current-agent skips and additional presses discarded during navigation
and settling. This completes the user's required physical validation of this
hybrid revision.

Following that confirmation, hybrid focus now defaults to enabled both in
`Config` and when its JSON flag is absent. Explicit `false` remains the opt-out.
README and website source document the enabled default, API/keyboard boundary,
pending indicator, discarded taps and read-only legacy discovery. All 75 tests
pass after the default change, including missing-config/default-on and explicit
opt-out, and the Release build is clean. No remote restart or upstream
contribution was performed. Release publication scope is being clarified.

## Hybrid focus follow-up (2026-11-18)

The user physically confirmed that the initial Go to spike works, but reported
slowness and requested direct API focus within the currently viewed machine.
They want the mode enabled by default in a published release only if the hybrid
behavior is validated, with the same config flag available to disable it.

Implemented a fast route under `experimentalGoToAgentFocus`: read the local
`herdr machine list --json` selection and compare its profile ID with the
machine-qualified agent key. No selected remote means Local only when all
profiles explicitly expose boolean selection metadata (or there are none).
Missing/malformed selection metadata, disabled selected profiles or multiple
selected profiles are treated as unknown. A matching selection uses the existing
local `pane.focus` or remote `agent focus` path, skipping all catalog snapshots
and keyboard automation. Cross-machine or unknown selection keeps the validated
Go to path. A failed direct focus also falls back to that path. The initial
profile read is reused when collecting a Go to catalog.

Saved selection remains a desired client state, persisted before visible
activation completes. For one second after an operation's Go to sequence, the
next operation therefore keeps the Go to route instead of trusting the fast
selection check. This is a conservative spike heuristic, not a live surface
acknowledgement. Manual machine changes can still race with a button press, and
the intended deployment remains a single TUI client. No server update, fork or
new upstream API was introduced.

Validation: all 69 core tests pass, including Local-to-Local with an unreadable
remote, same-remote routing without a local socket, cross-machine/unknown
selection, profile-read reuse, direct-focus error fallback, opt-out behavior and
the post-Go-to settling guard. Release build has zero warnings/errors. One live
read-only local selection lookup took 5.5 ms, compared with the initial 2.44 s
catalog/uniqueness measurement; neither measurement is end-to-end visible focus.
Reloaded the linked development plugin: its log confirms version 0.1.5 and the
new hybrid-mode startup marker.
Live Keypad logs subsequently showed direct API for Local-to-Local presses,
Go to for a Local-to-remote jump, direct API within that remote, and Go to back
to Local. This confirms routing choices, not the user's visible result or speed.

Hybrid physical validation is pending; the user has been asked to test both
same-machine speed and cross-machine correctness. The flag remains true in the
user's config and false by default in this development build while that check
is pending. Do not flip the published default or claim the hybrid was physically
validated yet. README and website source document the flag and hybrid behavior.

## Current Go to agent-focus spike (2026-11-18)

At the user's request, implemented and enabled an opt-in development spike in
the existing plugin. `experimentalGoToAgentFocus: true` routes Agents-folder and
Recent Agent presses through native Go to instead of server focus. Pane/layout
and Space actions keep their existing API route. The flag defaults to false and
is read per press, so disabling it restores ordinary focus without a rebuild.

The spike gathers fresh full snapshots for Local and all enabled saved machines,
requires the selected agent to still exist, and conservatively checks the pane
ID against all panes (including shells), ancestor labels and searchable fields.
Ambiguous substring searches or an unreadable enabled machine abort before any
keyboard input. The existing profiles now include two remotes; one lacks CLI
machine API forwarding. The spike's catalog has a read-only direct SSH fallback
for that specific error, with BatchMode, a connection timeout and shell-quoted
session identity. Neither remote executable nor persistent server was changed.
Ordinary background remote discovery remains unchanged.

One serialized agent-focus operation sends the configured Go to shortcut,
opens search, clears it, types the validated pane ID and presses Enter, with
short delays and terminal foreground guards. It does not pre-focus the server
or advertise API acknowledgement of visible success. As confirmed in official
`navigator_selected_index`, clearing/changing the query selects the first pane
row rather than a machine/workspace header; this is why no fixed arrow count is
needed. The intended Herdr TUI must be the front terminal tab, with no other
modal dialog consuming the configured shortcut.

Validation: all 53 core tests pass, including cross-machine and substring ID
collisions, ordinary shell matches, ancestor-label matches, missing targets,
unreachable machines, safe legacy SSH session quoting, query injection rejection
and opt-in configuration. Release plugin build completed without warnings or
errors. A self-contained macOS probe's `--go-to-plan` succeeded for one current
local agent and one remote agent against all three live catalogs; it sends no
keys or focus requests. The probe is under ignored `build/probe/`.
One subsequent read-only remote-target catalog/uniqueness check took 2.44 seconds;
that measurement excludes the keyboard sequence and visible endpoint activation.

Preserved the development symlink, backed up the previous Release directory and
plugin configuration, enabled the flag, and restarted Logi Plugin Service. Its
log confirms plugin 0.1.5 loaded from the linked checkout and the experimental
spike-enabled marker. No tag, release, push or upstream contribution was made.

The user subsequently confirmed this original Go to spike works, but is slow.
Computer Use explicitly refused access to Warp,
so no alternate tool was used to automate a test against that blocked app. A
subsequent attempt to launch the Logitech service through Computer Use timed
out; normal service lifecycle restart through LaunchServices succeeded. The
user has been asked to try Local-to-remote and remote-to-Local button presses.
README documents activation, prerequisites, dry-run probe and limitations.

## Remote focus across tabs and workspaces (2026-11-17)

Read-only live checks report local CLI/server 0.9.5 and remote persistent
server 0.9.0. The remote snapshot has agents in two separate workspaces;
two agents share one tab and another occupies the other workspace. No private
agent titles, paths or machine identifiers are recorded here.

The plugin uses local `pane.focus` but remote `herdr --machine <id> agent
focus <pane-id>` (`HerdrClient.FocusPaneAsync`, `RemoteAgents.PaneAsync`).
These operations differ in the running remote server:

- In official [0.9.0 client_views.rs](https://github.com/herdrdev/herdr/blob/v0.9.0/src/server/headless/client_views.rs#L803-L874),
  explicit `workspace.focus`, `tab.focus` and `pane.focus` project the target
  into attached shell clients. `agent.focus` is absent from that handling.
  Its internal pane focus can therefore change while the client keeps viewing
  a different tab/workspace. Same-tab pane focus does not require that location
  change. This is a source-confirmed defect matching the reported remote symptom;
  the physical Keypad failure was not independently reproduced in this turn.
- Official [0.9.5 client_views.rs](https://github.com/herdrdev/herdr/blob/v0.9.5/src/server/headless/client_views.rs#L809-L897)
  includes `AgentFocus` and synchronizes shell locations after successful focus.
  Its [regression test](https://github.com/herdrdev/herdr/blob/v0.9.5/src/server/headless/tests/mod.rs#L2659-L2740),
  `public_agent_focus_replaces_a_diverged_client_shell_projection`, checks
  replacing a client view in another workspace and rendering the target agent.
  These upstream tests were inspected, not executed locally.

Solutions: run the official fixed version in the persistent remote server
(replacing the CLI executable alone leaves the old server running), or add a
plugin compatibility step that uses the successful agent-focus response's
`tab_id` to issue remote `tab focus`. The latter is supported by the 0.9.0
source but remains a proposed, unimplemented workaround requiring verification.
Native Go to is the existing manual alternative. None of these server-focus
operations selects Local versus a saved machine in an already-open TUI.

Terminology matters: tabs/workspaces are within a session; named `--session`
sessions have separate sockets and runtime state. The plugin does not enumerate
arbitrary named sessions or select an outer terminal window/tab. The local
session listing contains only the default session. A failure between local
tabs/workspaces on server 0.9.5 would need separate reproduction; the remote
0.9.0 defect does not establish its cause.

The initial investigation changed only documentation and sent no focus requests.

### Applied fix and user confirmation (2026-11-17)

At the user's request, SSH checks confirmed that the installed remote executable
was still the verified official 0.9.5 asset, while the persistent server had
been running 0.9.0 since November 3. This was not an executable downgrade.
The old server's `ping` advertised `live_handoff: true`.

After a private remote backup of config/session files and baseline process
metadata, invoked the same `server.live_handoff` API used by the official
updater, specifying the existing 0.9.5 executable and expected protocol 22.
The normal updater would return early because the installed binary was already
current. The server replacement succeeded: both direct SSH status and the Mac's
`--machine ... status server` report 0.9.5. No binary or wrapper was changed.

Verified all four pane locations, original shell PIDs and process start times,
and foreground process group IDs were preserved. Terminal IDs were regenerated
by the new server; do not equate those runtime IDs changing with process loss.
One agent was briefly absent during detection; all four were present on recheck.
No stop/start fallback was used and no plugin change or rebuild was needed.
The user then confirmed that the change fixed the reported behavior on the
Keypad. Cross-machine selection remains a separate limitation.

## Cross-machine API feasibility and upstream status (2026-11-18)

Confirmed against public GitHub issues, comments, contribution policy and master
source; no external comments, issues or PRs were submitted.

- [Discussion #4396](https://github.com/herdrdev/herdr/discussions/4396)
  requests exactly this capability, including hardware decks: select a saved
  machine in the foreground client, optionally focusing a pane, or make an
  explicit focus operation activate that machine. The inspected discussion
  contains additional user requests through November 12; these are requests,
  not an accepted implementation plan or release commitment.
- [#3820](https://github.com/herdrdev/herdr/issues/3820) was closed as a feature
  request. The project response distinguishes the server-scoped API from a new
  supported interface for client-level aggregate state and focus.
  [#5789](https://github.com/herdrdev/herdr/issues/5789) and
  [#5904](https://github.com/herdrdev/herdr/issues/5904), the latter closed on
  November 9, were marked duplicate/not-planned and redirected to Ideas.
  These closures do not establish a permanent product rejection. No scheduled
  implementation or matching implementation PR was found in targeted searches.
- Master already has `ClientLoopEvent::ActivateEndpoint { endpoint_id, target,
  force }` in [client/events.rs](https://github.com/herdrdev/herdr/blob/master/src/client/events.rs).
  [client/mod.rs](https://github.com/herdrdev/herdr/blob/master/src/client/mod.rs)
  handles that event and calls `begin_endpoint_activation`.
  [endpoint/activation.rs](https://github.com/herdrdev/herdr/blob/master/src/client/endpoint/activation.rs)
  manages source/target connections, optional focus, fresh surface state,
  failures, cancellation and retargeting. No public machine-selection input
  was found in the examined client event surface.
- [CONTRIBUTING.md](https://github.com/herdrdev/herdr/blob/master/CONTRIBUTING.md)
  currently limits implementation PRs to maintainers and approved contributors;
  unapproved unsolicited implementation PRs are automatically closed. New
  feature proposals belong in Discussions. Contributor eligibility was not
  established; no implementation PR was attempted.

Engineering assessment, not an implemented or tested design: a supported local
client-control operation could reuse endpoint activation and take machine plus
pane identity together. An atomic jump avoids the handoff race of separately
selecting a machine then focusing a pane. The API needs a clear target-client
policy (foreground versus explicit client ID), timeout/offline behavior,
handling of competing user navigation, and acknowledgement only after the
target surface is actually ready. It should work when the local runtime server
is unavailable and avoid silently moving unrelated clients attached to the same
remote server. This is feasible, but more than adding a field to `agent.focus`.
Once upstream exposes such an operation, this plugin already retains the
originating machine and pane and can call it from a tile press.

For the official-build workflow, the next useful contribution is the concrete
MX Creative Keypad use case on the existing discussion, with a request for
maintainer direction. Keyboard/picker automation remains an unacknowledged
workaround, not equivalent to an exact machine-and-pane API.

## Native agent navigation as an exact-jump workaround (2026-11-18)

The user suggested making their Cycle Agents/Next agent action target a known
agent by name or ID. Current plugin source contains Cycle panes/tabs, not a
bundled Cycle Agents action; the user's Herdr configuration has native
`next_agent = "prefix+ctrl+k"` and `previous_agent = "prefix+ctrl+j"`.
The physical button mapping was not inspected or changed.

Confirmed in official 0.9.5
[endpoint_navigation.rs](https://github.com/herdrdev/herdr/blob/v0.9.5/src/client/shell/endpoint_navigation.rs#L160-L217):
Next/Previous Agent traverse endpoint-qualified online agent targets and call
`focus_or_activate`, so native keyboard navigation can cross machines. It
accepts a next/previous action (or indexed FocusAgent), not an arbitrary pane ID.
The plugin's agent ordering need not match this client-specific filtered order.

A bounded loop could send Next agent and inspect fresh per-server snapshots,
using machine + pane + occupant identity rather than a display name. This is
best effort: selected profile is persisted before endpoint activation finishes,
and server snapshots do not identify the exact view of an individual client.
Polling selection plus focused pane therefore cannot certify visible success.
Do not pre-focus the destination through the API and then treat that state as
proof that native cycling reached it. Such a loop was not implemented.

A more direct candidate is native Go to search. Official 0.9.5
[aggregate_navigation.rs](https://github.com/herdrdev/herdr/blob/v0.9.5/src/client/shell/aggregate_navigation.rs#L278-L419)
explicitly matches `pane.pane_id` as well as row labels and other searchable
fields, and stores endpoint + pane in each destination row. Search is substring
matching: IDs can collide between servers or match longer IDs; names can repeat
or be hidden by pane labels. A unique searchable name/ID could let keyboard
automation open Go to, filter and choose the target without cycling every agent.
This requires checking the actual picker result and timing/keyboard focus; it
is a source-confirmed possibility, not a tested implementation or an acknowledged
machine-and-pane API. No agents were renamed, focus sent, or config changed.

2026-11-03. The local Git repository was connected to the private
`robertohuertasm/herdr-tui-logi-plugin` GitHub repository before the first release.

## Confirmed from source and the installed schema

- Herdr 0.9.5 ships socket protocol 22. The plugin reference uses one
  newline-delimited JSON request per Unix socket connection, polls
  `session.snapshot`, and focuses a pane with `pane.focus`. It raises the
  terminal separately. [Source](https://raw.githubusercontent.com/niklasberglund/herdr-logi-plugin/main/src/Herdr.Core/HerdrClient.cs).
- `session.snapshot` includes agents, panes, layouts, tabs, workspaces and the
  focused IDs. Agent fields include `name`, `display_agent`, `agent_status`,
  `workspace_id`, `tab_id`, `pane_id`, `terminal_id`, and `state_change_seq`.
  The status enum is idle, working, blocked, done, unknown. The schema defines
  `state_change_seq`, but does not say it measures all agent activity. Its
  default is zero, so clients must tolerate it missing.
- The reference plugin orders recent agents by locally observed status changes,
  retaining workspace order at startup. It was written for protocol 20 and
  does not use `state_change_seq`. [Source](https://raw.githubusercontent.com/niklasberglund/herdr-logi-plugin/main/src/Herdr.Core/RecencyTracker.cs).
- Protocol 22 declares `agent.list`, `agent.focus` (`target`), `pane.focus`
  and `pane.close` (`pane_id`); `pane.zoom` (`pane_id`, optional `mode`,
  default toggle); and `pane.split` (`direction`: right/down, optional
  `target_pane_id`, `focus` default false).
- It also declares `tab.create`, `tab.focus` (`tab_id`), `workspace.create`,
  and `workspace.focus` (`workspace_id`). Both create operations default
  `focus` to false. Layout snapshots include their tab ID, focused pane and
  panes, enabling a candidate implementation of cycling within one tab.
- The existing VS Code plugin uses `PluginDynamicFolderNavigation.None`,
  omits a custom Back entry at the root, resets its internal level on activation,
  and handles long press separately from release. This is an established local
  example; capacity and paging have not been measured here.
- Both local VS Code and Cursor plugins define a tag-triggered GitHub Actions
  release workflow: verify the tag matches the manifest version, run core
  tests, build Release, pack and verify a `.lplug4` with LogiPluginTool
  6.4.2.3599, then attach it to a GitHub Release. Both use .NET 10, a manifest
  as the version source, and a static `docs/` site for GitHub Pages.

## Hypotheses and limitations

- Sorting descending by `state_change_seq` could offer a server-provided order
  of recent state transitions, including changes missed between polls. This is
  not a timestamp or proof of arbitrary output activity; ordering, resets,
  ties, and older-server compatibility need verification.
- Previous/next panes, tabs and workspaces may be implemented by selecting from
  a fresh snapshot and focusing an ID. Correct order and wraparound semantics
  require tests; pane cycling must remain within the current tab.
- Schema availability does not establish safety of `pane.close`, nor that a
  selected pane still contains the same agent at confirmation time. Validate
  identity immediately before a confirmed close.

## Pending

- With automatic approval, GitHub and a live read-only Herdr snapshot were accessible; the snapshot
  included `state_change_seq`. Verify focus with a running Herdr instance.
- The current build loads in Logi Plugin Service, and its `.lplug4` passes
  Logitech verification. Eight socket-simulated tests cover ranking, focus
  requests, pane cycling, and refusing a close after terminal replacement.
  Physical Keypad tests and live navigation/focus/close remain outstanding.
- Follow the other plugins' project structure and release workflow, using a
  distinct plugin identifier. Prepare a GitHub Pages site and release package;
  do not publish or tag until the repository and release are ready.
- Update the agent-monitoring labs status and prior-art notes when that sibling
  repository is writable. Its existing worktree already contains unrelated
  changes and must be preserved.
