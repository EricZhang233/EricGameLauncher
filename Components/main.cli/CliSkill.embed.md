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

## Commands

- `list [--recycle] [--json]` — list active items or the recycle bin
- `launch --id <id> | --title <title> | --path <path> [--admin] [--alt] [--alongside]` — launch a game or application
- `platform --id <id> | --title <title>` — launch the platform manager (Steam, Epic, Xbox)
- `add --title <title> --path <path> [--admin] [--icon <path>] [--platform <name>] [--mgr <path>] [--alt <path>] [--alongside <path>]` — add a new item
- `edit --id <id> [options]` — edit an item; run `edit --help` for all supported fields
- `remove --id <id> | --title <title> [--permanent]` — remove an item (to recycle bin by default)
- `restore --id <id> | --title <title> | --all` — restore items from the recycle bin
- `recycle --list | --mark <id> | --empty | --purge | --clean [--json]` — manage the recycle bin
- `scan [--steam | --epic | --xbox | --all] [--classify] [--invalid] [--delete-invalid] [--import] [--json]` — scan for installed games
- `search <query> [--json]` — search by title, path, pinyin, or pinyin initials
- `sort --list | --id <id> --move-up | --move-down | --swap-with <id>` — reorder items
- `settings --list | --get <key> | --set <key>=<value>` — view or modify settings
- `update --check [--channel <stable|latest>] [--json] | --install | --repair [--channel <stable|latest>] | --history [--json]` — check, install, or force-reinstall updates, or print the full update history (oldest first, requires network access to GitHub)
- `announcements --list | --read <id>` — view server announcements
- `install` / `uninstall` — create or remove desktop and start menu shortcuts
- `storage --status | --switch <system|portable>` — view or switch storage mode
- `exit` — stop the app and its background host (the quick start setting is kept)
- `skill` — print this integration guide
- `version` — show the version

## Settings keys

`launchMode` (single|double), `closeAfterLaunch` (true|false), `quickStart` (true|false), `splashFadeIn` (true|false), `splashFadeOut` (true|false), `iconSize` (32-512), `updateChannel` (stable|latest), `githubToken`, `appIconPath`, `appTitle`, `lang` (Zh-CN|EN), `storageMode` (system|portable), `windowX`, `windowY`, `windowWidth`, `windowHeight`.

`quickStart` makes the launcher run as a background host registered for auto start at sign-in; it is disabled by default. The host preloads configuration data, validates and rebuilds icons, completes one warm UI pass and then releases the interface. The main window keeps normal window semantics — closing it closes the window — while the host stays resident with the warmed resources, so the next launch shows the window within a fraction of a second. Use `settings --set quickStart=true|false` to control it; `settings --list` also reports `quickStartRegistered`, `quickStartCommand` and `quickStartHostRunning`.

`splashFadeIn` and `splashFadeOut` control the splash animations but apply only while `quickStart` is enabled; with quick start off the splash always animates.

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
