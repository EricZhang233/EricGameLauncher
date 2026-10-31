# EricGameLauncher CLI Integration Guide

## When to use

Use the CLI when the user wants to control their EricGameLauncher library from a terminal or automate launcher operations, such as launching, listing, searching, adding, editing, removing, sorting, scanning, settings, updates, announcements, shortcuts, or storage mode.

## Executable location

`EricGameLauncher.Cli.exe` is shipped in the same directory as `EricGameLauncher.exe`. Run it from that directory or use the full path.

## Golden rules

1. When unsure about a command, run `EricGameLauncher.Cli.exe -help` first.
2. Use `--json` whenever you need structured output to parse.
3. Exit code `0` means success, `1` means error. Do not ignore the exit code.
4. Running the CLI with no recognized command prints the help screen.
5. The `-debug` flag switches to local data and cache paths.
6. In JSON mode stdout contains JSON only; diagnostics and progress go to stderr. Mutation commands return a JSON success/exitCode result. Unknown commands return a nonzero exit code.
7. Item IDs remain stable when paths are edited and are preserved when an item moves to the recycle bin. Add, edit and scan import check only the active list for duplicate paths: an item may be re-added while an older copy stays in the recycle bin. Each recycled entry also carries a `recycleId` (shown by `list --recycle` and emitted in JSON); use `restore --recycle-id <recycleId>` when several recycled entries share one item ID.
8. A failed launch returns an error; it does not mean the game was started.
9. A corrupt configuration is preserved and writes fail explicitly. Restore its `.bak` or repair it before retrying. Concurrent item modifications are serialized across processes.

## Commands

`add` and `edit` accept `--isplatform true|false` and `--pin true|false` for all items. Pin is managed solely by application configuration and the property Pin toggle. Platform items default to pinned; explicit choices persist. All Jump List entries use Tasks in two sections: pinned platforms, then frequent nonplatform items. Fixed nonplatform items lead the frequent section in main panel order, followed without a separator by unpinned items ranked by successful launch count, ties in main panel order. A single system separator divides platforms and frequent items when both sections have entries. Unpinned platforms stay hidden. There are no custom destination categories, system pin synchronization, temporary pin candidates, or manual system pin prompts. Tasks cannot be pinned or removed through Windows. Screen space still limits visibility. Existing legacy system pins can be unpinned manually. JSON item output includes IsPlatform, Pin and LaunchCount.

`scan --google [--import] [--json]` reads the Google Play Games SQLite library, decrypting locally with the current Windows user's DPAPI key and AES-GCM. `scan --all` includes it. Packages prefixed with com.google or com.android are excluded. An unavailable library is an error, not evidence of an uninstall. GUI, CLI and Jump List main launches share successful launch counts; manager, alongside and custom actions do not count separately.

`scan --wegame [--import] [--classify] [--json]` reads the current user's WeGame SQLite installation manifest and decodes its two QQTEA-encrypted fields locally. It checks only immediate child directories of the recorded library roots, matches Rail game IDs against the installed list, and requires the game's WeGameLauncher/launcher.exe. Names come from directory names with the matching numeric suffix removed. The WeGame manager is preserved on import when found. Existing entries pointing to another executable inside the same game directory are treated as the same game. No installed-program enumeration or whole-disk scan is used; unsupported or damaged manifest data is an error. `scan --all` and GUI scanning include WeGame. Uninstall records remain a researched fallback, not an enabled scanner.

- `list [--recycle] [--json]` — list active items or the recycle bin (recycle entries include `recycleId`)
- `launch --id <id> | --title <title> | --path <path> [--admin] [--alt] [--alongside]` — launch a game or application

For stored items, `--alongside` runs the configured alongside command for this launch even when its saved switch is off. Without the flag, the saved switch applies. It never changes that switch or starts the command twice. An empty alongside command does nothing. Alongside failures return a launch error; a successful main launch still counts once. `--path` launches only the supplied command.
- `platform --id <id> | --title <title>` — launch the platform manager (Steam, Epic, Xbox)
- `add --title <title> --path <path> [--admin] [--icon <path>] [--platform <name>] [--mgr <path>] [--alt <path>] [--alongside <path>]` — add a new item
- `edit --id <id> [options]` — edit an item; run `edit --help` for all supported fields
- `remove --id <id> | --title <title> [--permanent]` — remove an item (to recycle bin by default)
- `restore --id <id> | --recycle-id <recycleId> | --title <title> | --all` — restore items from the recycle bin; restore fails if the main list already holds the same path or ID
- `recycle --list | --mark <id> | --empty | --purge | --clean [--json]` — manage the recycle bin (for `--mark`, an item ID or recycle ID is accepted)
- `scan [--steam | --epic | --xbox | --google | --wegame | --all] [--classify] [--invalid] [--delete-invalid] [--import] [--json]` — scan for installed games
- `search <query> [--json]` — search by title, path, pinyin, or pinyin initials
- `sort --list | --id <id> --move-up | --move-down | --swap-with <id>` — reorder items

Update installation and repair verify the downloaded ZIP against the GitHub asset SHA256 digest before proceeding. A missing or invalid digest, a hash mismatch, or an updater failure returns an error without reporting the update as ready.
- `settings --list | --get <key> | --set <key>=<value>` — view or modify settings
- `update --check [--channel <stable|latest>] [--json] | --install | --repair [--channel <stable|latest>] | --history [--json]` — check, install, or force-reinstall updates, or print the full update history (oldest first, requires network access to GitHub)
- `announcements --list | --read <id>` — view server announcements
- `install` / `uninstall` — create or remove desktop and start menu shortcuts
- `storage --status | --switch <system|portable>` — view or switch storage mode
- `exit` — stop the app and its background host (the quick start setting is kept)
- `skill` — print this integration guide
- `version` — show the version

## Settings keys

LaunchCount is runtime data hydrated from an independent ID-to-count JSON cache under the application cache directory, scoped by configuration directory. It is never serialized into items.yaml. Successful launches update only this cache; clearing the cache resets counts without changing project configuration. No historical count migration is performed.

`launchMode` (single|double), `closeAfterLaunch` (true|false), `quickStart` (true|false), `iconSize` (32-512), `updateChannel` (stable|latest), `githubToken`, `appIconPath`, `appTitle`, `lang` (Zh-CN|EN), `storageMode` (system|portable), `windowX`, `windowY`, `windowWidth`, `windowHeight`.

`quickStart` makes the launcher run as a background host registered for auto start at sign-in; it is disabled by default. The host preloads configuration data, validates and rebuilds icons, completes one warm UI pass and then releases the interface. The main window keeps normal window semantics — closing it closes the window — while the host stays resident with the warmed resources, so the next launch shows the window within a fraction of a second. Use `settings --set quickStart=true|false` to control it; `settings --list` also reports `quickStartRegistered`, `quickStartCommand` and `quickStartHostRunning`.

Startup animations are enabled by default and follow the Windows animation setting, independently of Quick Start. Disabling system animations switches directly between the splash and main view.

## Examples

```
EricGameLauncher.Cli.exe list --json
EricGameLauncher.Cli.exe launch --title "Counter-Strike 2"
EricGameLauncher.Cli.exe add --title "My Game" --path "C:\Games\game.exe" --admin
EricGameLauncher.Cli.exe search "cs"
EricGameLauncher.Cli.exe settings --set lang=EN
EricGameLauncher.Cli.exe settings --set quickStart=true
```

## Notes

- Item operations share the same data store as the GUI, so changes appear in both.
- Prefer `--id` over `--title` when the ID is known, because titles can collide.
- When stdout or stderr is redirected, both streams are written as UTF-8 without a BOM, so captured output keeps non-ASCII titles intact.
