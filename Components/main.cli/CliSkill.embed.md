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
- `recycle --list | --empty | --clean [--json]` — manage the recycle bin
- `scan [--steam | --epic | --xbox | --all] [--classify] [--invalid] [--delete-invalid] [--import] [--json]` — scan for installed games
- `search <query> [--json]` — search by title, path, pinyin, or pinyin initials
- `sort --list | --id <id> --move-up | --move-down | --swap-with <id>` — reorder items
- `settings --list | --get <key> | --set <key>=<value>` — view or modify settings
- `update --check [--channel <stable|latest>] [--json]` — check for updates
- `announcements --list | --read <id>` — view server announcements
- `install` / `uninstall` — create or remove desktop and start menu shortcuts
- `storage --status | --switch <system|portable>` — view or switch storage mode
- `skill` — print this integration guide
- `version` — show the version

## Settings keys

`launchMode` (single|double), `closeAfterLaunch` (true|false), `iconSize` (32-512), `updateChannel` (stable|latest), `githubToken`, `appIconPath`, `appTitle`, `lang` (Zh-CN|EN), `storageMode` (system|portable), `windowX`, `windowY`, `windowWidth`, `windowHeight`.

## Examples

```
EricGameLauncher.Cli.exe list --json
EricGameLauncher.Cli.exe launch --title "Counter-Strike 2"
EricGameLauncher.Cli.exe add --title "My Game" --path "C:\Games\game.exe" --admin
EricGameLauncher.Cli.exe search "cs"
EricGameLauncher.Cli.exe settings --set lang=EN
```

## Notes

- Item operations share the same data store as the GUI, so changes appear in both.
- Prefer `--id` over `--title` when the ID is known, because titles can collide.
