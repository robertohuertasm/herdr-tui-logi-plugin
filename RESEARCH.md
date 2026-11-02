# Herdr provider: initial investigation

2026-11-03. The local Git repository was connected to the private
`robertohuertasm/herdr-logi-plugin` GitHub repository before the first release.

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
