<!--
 readme编写规范：
  1. 所有readme都需要保留本说明且不可做任何翻译修改。主readme(中文版)是本说明的唯一源头(Source of Truth)，禁止将非中文版本中被修改的说明同步回主readme。非中文版的此说明需始终保持与主readme单向同步更新。
  2. 非中文版本的readme必须保留主readme(中文版)的所有视觉元素、HTML 标签、图标路径和排版格式，非中文版本的readme是中文版的完整对应精准同步翻译版，翻译不可笼统，不可修改原意，不可增减内容。
  3. 针对目标语言环境进行功能描述的本土化。
  4. 非中文版本的readme需要合理修改指向资源的路径，确保资源能够正确显示。
  5. 非中文版本的readme都需要保留并置顶本说明
  6. 非中文版本的readme都需要置顶以下元素，可以替换为相应的翻译版本但必须保留原意：
	    <div align="right">
	    <a href="../readme.md">For the latest updates, please refer to the Chinese README.</a>
	    </div>
  7. 任何关于项目内容的更新，必须首先在主readme(中文版)中完成。在主版确认无误后，再根据本规范同步至其他语言版本。
  8. 非中文版本的readme顶部的语言切换器仅保留指向主readme(中文版)的链接，不互相跳转。非中文版本的入口仅在主readme中统一显示。
-->
<div align="right">
  <a href="../../readme.md">For the latest updates, please refer to the Chinese README.</a>
</div>
<div align="center">

<img src="../../ico.ico" width="64" height="64" alt="Eric Game Launcher icon" />
<h1>Eric Game Launcher</h1>
<p><strong>Bring Game Launching Back to Purity and Speed</strong></p>
<p>
<a href="https://github.com/EricZhang233/EricGameLauncher/releases/latest"><img alt="version" src="https://img.shields.io/github/v/release/EricZhang233/EricGameLauncher?label=version&color=512BD4" /></a>
<a href="https://github.com/EricZhang233/EricGameLauncher/releases/latest"><img alt="downloads" src="https://img.shields.io/github/downloads/EricZhang233/EricGameLauncher/total?label=downloads&color=0078D4" /></a>
<a href="https://github.com/EricZhang233/EricGameLauncher/stargazers"><img alt="stars" src="https://img.shields.io/github/stars/EricZhang233/EricGameLauncher?label=stars&color=E3B341" /></a>
</p>
<p>
<a href="https://dotnet.microsoft.com/"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet" /></a>
<a href="https://github.com/microsoft/microsoft-ui-xaml"><img alt="WinUI 3" src="https://img.shields.io/badge/UI-WinUI%203-0078D4?logo=windows" /></a>
<a href="https://www.microsoft.com/windows"><img alt="Windows 11" src="https://img.shields.io/badge/Windows-11%2024H2%2B-0078D4?logo=microsoft" /></a>
<a href="https://www.gnu.org/licenses/gpl-3.0.html"><img alt="License: GPL v3" src="https://img.shields.io/badge/License-GPLv3-blue.svg?logo=gnu" /></a>
</p>
<p>
<a href="#highlights">Highlights</a> • <a href="#quickstart">Quick Start</a> • <a href="#installation">Installation Guide</a> • <a href="#features">Features</a> • <a href="#cli">CLI</a> • <a href="#settings">Settings</a> • <a href="#architecture">Architecture</a> • <a href="#faq">FAQ</a> • <a href="#thanks">Thanks</a> • <a href="../../readme.md">中文</a>
</p>

<img src="banner-en-rounded.png" width="80%" alt="Banner" />

</div>

---

<a name="highlights"></a>

## Highlights

*   🧩 **End-to-end execution**: Four configurable chains — main program, manager, substitute launch and launch alongside; EXE, shortcuts, URL protocols, web pages and Store apps all run directly.
*   🖱️ **Dynamic context menu**: Entries come and go per item, and a detected platform shows "Run Steam / Run Xbox"; each item takes up to 10 custom entries with their own title, command, arguments and elevation.
*   🗂️ **One library, every platform**: Steam, Epic Games, Xbox, Google Play Games, WeGame, standalone executables, web links and Store apps share a single grid in whatever order you choose.
*   🔍 **Flexible search**: Full Pinyin, Pinyin initials, Chinese, English, paths and URL protocols all work as keywords — `yxlm` reaches 英雄联盟, `C:\Program Files` matches by path, and `steam` sweeps up everything behind that protocol.
*   📥 **Scan and import**: Automatically detects games installed through Steam, Epic Games, Xbox, Google Play Games and WeGame, groups them into New / Existing / Invalid, and imports or cleans them up in bulk.
*   📦 **Install or extract**: The same EXE package supports installation or extraction. Installed copies have a Windows uninstall entry; extracted copies can manage their own shortcuts and use portable data storage in writable locations.
*   💻 **CLI & Skill**: The command line shares the desktop app's library, settings and core services, with built-in help and an integration guide for scripts and AI agents to manage and launch games.
*   🚀 **Quick Start**: A background host pre-reads configuration and warms up icons and the interface, so the next launch appears almost instantly. It is off by default. Choose More → Exit to end the current session.
*   🗑️ **Three-stage deletion**: Deleting in the main window only moves an item to the Recycle Bin; only deleting it there starts the 72-hour countdown, and it is purged on the next launch after that. A slip of the hand is recoverable.
*   🎒 **Configuration backup and migration**: Export settings, your library, recycled items and icons to a ZIP, then restore by replacing or adding without duplicates. Installed copies use system data; extracted copies in writable directories can switch between system and portable data storage.

<a name="quickstart"></a>

## Quick Start

*   Requires Windows 11 24H2 or later, with support for x64 and ARM64 PCs.
*   Before running, install the x64 versions of the [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [Windows App Runtime 1.8](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
*   ARM64 PCs use the same x64 application through Windows 11 x64 emulation. Choose the x64 versions of the runtime dependencies too. The installer does not install these dependencies automatically.

### Three steps

1. **Download**: Get the installer named `EricGameLauncher_Setup_<version>.exe` from [Releases](https://github.com/EricZhang233/EricGameLauncher/releases/latest).
2. **Deploy and run**: Open the Setup EXE, choose Install or Extract, and launch `EricGameLauncher.exe` when finished. To keep configuration with the application directory, enable portable data storage by following the [installation guide](#installation) below.
3. **Fill your library**: Click **More → Scan** in the top-right corner to detect installed games and import them in one click, or use **More → Add** to point at an exe, shortcut, web page or Store app.

---

<a name="installation"></a>

## Installation Guide

The same Setup EXE provides Install and Extract. Choose based on how you want to manage the application and its data. Running the application in either mode requires the x64 runtime dependencies listed above; the installer does not install them automatically.

### Choose a deployment type

| Type | Home page option | Data location |
| :--- | :--- | :--- |
| **Installed** | Install | Always uses the system data directory. |
| **Portable deployment** | Extract | Writable locations support portable data storage, with configuration in the program directory's `Data` folder. |

### Installed mode

1. Open the Setup EXE and choose Install on the home page.
2. Choose the installation directory. The default is `%LocalAppData%\Programs\EricSoft\EricGameLauncher`, or you can select another location. Paths with spaces or non-ASCII characters are supported.
3. Confirm the shortcut options: the Start menu shortcut is required; the desktop shortcut is optional and unchecked by default.
4. Review the confirmation page and start installation. When finished, launch the application from the result page, Start menu or desktop shortcut.

Installed mode stores settings, the library, recycled items and icons in `%ProgramData%\eric\EricGameLauncher` and does not offer a switch to portable data storage. The installer maintains shortcuts, so the application's shortcut management menu is hidden. Opening the Setup EXE again detects the existing installation and displays an upgrade confirmation page.

### Deploy a portable copy

1. Open the same Setup EXE and choose Extract on the home page.
2. Choose a writable program directory, such as `%USERPROFILE%\Apps\EricGameLauncher`. For portable data storage, use a location outside protected directories such as `Program Files` and `Windows`, and make sure the program directory and its `Data` subdirectory are writable.
3. Confirm the path and start extraction. When finished, launch the application from the result page, or open the destination directory and double-click `EricGameLauncher.exe`.
4. Open **Settings → Data** and check the current storage mode. If it shows System mode, use the adjacent switch button to select Portable mode and wait for configuration and icon migration to finish. Confirm that the current data location points to the program directory's `Data` folder.
5. If you want desktop or Start menu entries, create them through the application's Shortcuts menu.

**Extract deploys the application files; Portable mode determines where data is stored.** First use without existing configuration defaults to System mode. In locations that support portable data storage, existing copies retain their selected mode. If no mode has been selected but `Data` already contains configuration, the application reads it automatically. Complete the check in step 4 if you want configuration to travel with the directory. Extraction does not register an installation, create an uninstaller or create shortcuts automatically.

Once portable data storage is configured, close the launcher before moving or copying the whole program directory, keeping its `Data` folder intact. The destination computer still needs the runtime dependencies, and game paths must match its actual installation locations. Temporary caches and launch counts do not travel with the directory. You can also transfer data through [configuration backup and restore](#backup).

#### Advanced: rename the EXE to ZIP and extract it directly

Users familiar with file extensions and archives can extract the application files directly from the EXE without running the installer.

1. Make a copy of the downloaded `EricGameLauncher_Setup_<version>.exe`. Enable file name extensions in File Explorer, then change the copy's `.exe` extension to `.zip`, for example `EricGameLauncher_Setup_<version>.zip`.
2. Open the renamed file as a ZIP archive and extract all its contents to a writable program directory.
3. Run `EricGameLauncher.exe` in the extracted directory, then follow step 4 above to check and configure portable data storage. The required x64 runtime dependencies must still be installed.

The result is a set of application files for portable deployment, including the main program, command-line program and their dependencies. These are the same application files produced by Extract on the installer's home page, **without the installer or uninstaller**. Keep all extracted contents together instead of copying only the main EXE. This operation does not register an installation or create shortcuts automatically.

### Update, migrate and remove

*   **Update an installed copy**: Use the application's updater, or run the Setup EXE again and choose Upgrade. The registered installation directory is retained, and the Start menu shortcut and any corresponding desktop shortcut that currently exists are updated.
*   **Update a portable copy**: Use the application's updater. It remains an extracted deployment and continues reading data according to its current storage mode.
*   **Switch to installed mode**: First switch the current extracted copy to System data storage and confirm that migration has finished, then run the Setup EXE to install. You can also export a configuration backup and import it into the installed copy.
*   **Uninstall an installed copy**: Use **Windows Settings → Apps → Installed apps**, or run `Uninstall.exe` in the installation directory. The fixed system data directory is removed by default. Select Keep data to retain settings, items and icons; the success screen shows their location.
*   **Remove a portable copy**: Remove shortcuts created through the application, turn off Quick Start and exit, then delete that copy's program directory. Portable data is in its `Data` folder, so export a backup first if you want to keep it. System-mode data is stored in the system data directory and is not deleted with the program directory.

Uninstalling an installed copy handles only the program files it owns, corresponding shortcuts, installation record and chosen data cleanup scope. Game files, unrelated files in the installation directory and `Data` folders in other extracted copies are retained. Copies using the system data directory share that data, and the choice to keep or remove it applies to the shared directory.

---

<a name="features"></a>

## Features

<div align="center">
  <img src="readme.overview.gif" width="100%" alt="Main window: icon grid, hover hint, Pinyin search and icon size adjustment" />
</div>

### Every game in one grid

*   **No platform divide**: Steam, Epic Games, Xbox, Google Play Games, WeGame, standalone executables, web links and Store apps share one grid, ordered exactly how you like.
*   **Icons from small to huge**: Continuous scaling from a compact list to an immersive large grid, applied instantly and remembered next time.
*   **Multiple matching paths**: `yxlm` (initials), `yingxionglianmeng` (full Pinyin), `部落` (Chinese) and `pcl` (path) all find their target.
*   **Manual ordering**: Drag cards directly in the grid and release to save the order. Name tooltips pause during dragging.
*   **Hover to fill in the blanks**: Rest the pointer for a moment and the full title appears — no more guessing long names.
*   **Launch gesture**: Single or double click, switched whenever you like in Settings.

### End-to-end execution: more than a shortcut

Configuring a game means configuring a complete launch chain:

| Capability | Description |
| :--- | :--- |
| **Main program** | Native `EXE`, `LNK` shortcuts, URL protocols such as `steam://` / `epic://` / `starward://`, web links and `shell:AppsFolder\` Store apps can all serve as the launch target. |
| **Run as administrator** | Configure elevation separately for each execution target when it needs additional permissions. |
| **Manager** | Set a platform manager path and open it separately from the context menu, or use the detected platform entry. |
| **Substitute launch** | Let a custom command (for example `starward://`) fully take over the original executable's launch logic. |
| **Launch alongside** | Start a translator, timer, performance monitor or key mapper together with the game in one click. |
| **Custom context menu** | Up to 10 entries per item, each with its own title, command, arguments and elevation flag; deleting a middle entry shifts the rest up automatically. |
| **Arguments and environment variables** | Every execution target accepts full arguments; `%AppData%`, `%LocalAppData%` and friends expand automatically; the Properties panel provides separate path and argument fields. |

### Property panel: every detail is editable

*   **Name and icon**: Rename anything at any time, or upload your own image as a cover.
*   **Four execution targets, one column each**: Main program, manager, substitute launch and alongside execution are independent rows, each with a file picker and argument splitting.
*   **Visual custom menu editing**: Title, command and elevation flag line up in a column, up to 10 groups.
*   **Save takes effect immediately**: Changes are written to the local configuration and the interface refreshes on the spot. Mark clients as game platforms and choose whether to pin them to the taskbar Jump List.

<div align="center">
  <img src="readme.editor.panel.png" width="49%" alt="Property panel: display name, icon, four execution targets, elevation and custom context menu" />
  <img src="readme.scan.result.png" width="49%" alt="Scan results grouped into New, Existing and Invalid" />
  <br/>
  <sub>Left: property panel (every configurable field) — Right: scan results (New / Existing / Invalid)</sub>
</div>

### One-click multi-platform scan

| Platform | Automatic scanning support |
| :--- | :--- |
| Steam | Reads local libraries and installation manifests, including multiple library folders. |
| Epic Games | Reads local installation records and creates platform launch entries. |
| Xbox / Microsoft Store | Identifies games among installed Store apps. |
| Google Play Games | Reads the current user's local game library and filters out Google and Android internal components. Uses a matching Start menu shortcut's launch address when available. |
| WeGame | Uses the current user's installation records to discover games and launch targets. An entry already in the main library pointing to another executable in the same game directory counts as an existing item. |

Games from other platforms can also be added through **More → Add**, using an executable, shortcut or protocol URL registered by the corresponding client.

*   **Automatic discovery**: Natively detects games installed through Steam, Epic Games, Xbox (Microsoft Store / UWP), Google Play Games and WeGame, extracting paths and fetching icons.
*   **Grouped results**: Results are listed as **New / Existing / Invalid**, and anything already in your library is never imported twice.
*   **One-click import**: Select all or tick entries individually, then import them.
*   **Invalid cleanup**: Uninstalled or broken-path entries are gathered together for batch removal.

### Launch platforms and frequent games from the taskbar

Right-click the launcher's taskbar icon to start a pinned platform or frequent game. Pinned platforms follow the main window order. Frequent items show manual pins first, followed by other items ranked by successful launch count. Save pin changes in Properties to refresh the list in the background.

### Three-stage deletion: a slip of the hand is still recoverable

| Stage | Behavior |
| :--- | :--- |
| Stage one | Delete in the main window → the item only moves to the Recycle Bin; the game is still there and can be restored at any time. |
| Stage two | Delete in the Recycle Bin → a 72-hour countdown starts and the remaining time is shown in the interface. |
| Stage three | The countdown ends → the item is purged automatically on the next launch. |

The Recycle Bin also supports emptying in one click, which starts the same 72-hour retention period. Restoration reports a conflict if the main library already contains the same path or item ID.

<a name="backup"></a>

### Configuration backup and restore

Click the configuration import/export icon in **Settings → Data** to back up, restore and view results in one dialog. This is available in installed and extracted copies and in both data storage modes.

*   **Export a ZIP**: Choose a filename and location to back up settings, the main library, recycled items and referenced icons, including a custom application icon. Game files, temporary caches and launch counts are excluded. Missing icons are reported by count while the rest of the configuration can still be exported.
*   **Recognize a backup**: Before importing, the application checks the backup format, configuration contents and icon integrity. It displays the filename, backup format and configuration versions, the generating application version and creation time in local time. An application release package cannot be imported as a configuration backup.
*   **Choose what to restore**: Select Settings and Items and icons separately. Unselected contents are retained; recycled items are restored together with Items and icons.
*   **Replace or add without duplicates**: Replace substitutes the selected contents. Add without duplicates keeps existing setting values, fills in missing fields and skips items with duplicate launch targets. New items receive new IDs if their IDs conflict. The result shows the numbers added and skipped.
*   **Restore protection**: If another process changes the current configuration after preview, you must confirm again. Write failures trigger an attempt to restore the original configuration; interrupted restore records are handled on the next configuration access.
*   **Use on another computer**: Icons included in the backup are restored to the current data directory. GitHub tokens retain their original Windows user's encryption and may need to be entered again on another computer or account. Game launch paths must also match the actual installation locations on the destination computer.

### Updates and announcements

*   **Silent update checks**: GitHub Releases is queried on startup; a new version is indicated by the version number and update icon. Mandatory updates display an update dialog.
*   **Two channels**: **Official** receives stable releases, while **Dev** can receive prereleases.
*   **One-click download and install**: Download and install an available update. Installed copies also update their version registration and shortcuts; extracted copies update the application files in their directory. Updates preserve the current deployment type and data storage mode. You can also repair the current version when its release package is available. Manual checks show progress immediately in the update window.
*   **Update history**: A version list on the left and the full notes on the right, with jump-to-any-version navigation, sourced from GitHub release records.
*   **Announcement center**: Cloud announcements are fetched on startup, bodies support Markdown in Chinese and English, read state is persisted locally, and unread items carry a red dot.
*   **Update protection**: The downloaded package must match the GitHub asset SHA256 before the launcher exits and installation begins. Verification failures can be retried or dismissed. Installation failures trigger rollback; failed rollback preserves the recovery backup.
*   **Configuration protection**: Atomic replacement retains a `.bak` file. Corrupt configuration cannot be silently overwritten with an empty library. GUI and CLI item changes use transactions across processes.
*   **Deletion scope**: Deleting from the main library moves an entry to the recycle bin. Deleting it again or emptying the bin starts the 72-hour retention period. Game files remain in place.

<a name="settings"></a>

### Settings at a glance

*   **General**: Quick Start, launch gesture (single / double click), and exit after launching. Animations follow the Windows setting.
*   **Updates**: Update channel (Official / Dev) and a GitHub token, which raises the API rate limit and is encrypted with DPAPI before it is stored locally.
*   **Data**: View the data location, import or export configuration, and open the configuration and cache folders. Extracted copies in writable directories support system / portable data migration; installed copies and extracted copies in write-restricted locations use system data only.
*   **Title bar**: Icon size, Recycle Bin, shortcut management for extracted copies, privacy and permission notes, and update checks.

Hover over a setting’s icon or name for an explanation, or click to expand its help.

### Quick Start: warm up before you open it

*   **Background host**: After sign-in a resident host pre-reads configuration and items, validates and rebuilds icons and completes a full warm-up pass of the interface, then releases it — leaving no window on screen.
*   **Ready for the next launch**: The resident host keeps warmed resources ready for the next window. Startup time depends on the machine and library.
*   **System animation preference**: Startup animations follow Windows settings. Turning off Windows animations switches directly between visual states.
*   **To exit completely**: **More → Exit** ends the window and the background host right away without changing the Quick Start setting. On Windows 11 24H2 and later you can also right-click the taskbar entry and choose "End task".
*   **No background process wanted**: Turn the switch off and the auto-start entry is removed immediately.

---

<a name="cli"></a>

## Command line: share your library with scripts

`EricGameLauncher.Cli.exe` shares core services and data with the desktop app. Use it to launch games, manage your library, settings and recycle bin, and back up, preview and restore configuration from scripts.

Use the program's `-help` option for the complete commands and arguments, and `skill` for the integration guide for scripts and AI agents. Both documents are built into the program.

---

<a name="architecture"></a>

## Architecture

**Eric Game Launcher** shares Core services between its desktop interface and console, covering configuration, library management, launching, scanning and updates.

```mermaid
flowchart TB
    subgraph Shell["Shells · zero business logic"]
        GUI["EricGameLauncher.exe<br/>WinUI 3 rendering / animation / dialogs"]
        CLI["EricGameLauncher.Cli.exe<br/>argument parsing / output formatting"]
    end
    subgraph Core["Core · the single business layer"]
        SVC["Config and item services<br/>launch execution · scan and import<br/>updates and announcements · icon extraction"]
    end
    GUI --> SVC
    CLI --> SVC
    SVC --> DATA["settings.yaml · items.yaml · icon cache"]
    SVC --> OS["Windows registry / processes / icons / protocols"]
```

*   **Modern stack**: **Windows App SDK 1.8** + **WinUI 3** + **.NET 10**, using Mica / Acrylic materials and an adaptive grid.
*   **Single Core principle**: The desktop app and the CLI share one business layer, so features stay aligned and every operation is logged consistently.
*   **Human- and machine-readable configuration**: YAML (`YamlDotNet`) storage with `settings.yaml` and `items.yaml` kept separate, so frequent changes never rewrite everything.
*   **Native performance**: P/Invoke calls into system APIs handle icon extraction and caching, while window materials and the title bar are composed by the OS.
*   **Pinyin index**: `TinyPinyin` builds full-Pinyin and initial-letter indexes for titles in memory, to support title matching without rebuilding the indexes for each keystroke.
*   **Secure storage**: The GitHub token is encrypted with DPAPI before it touches disk — never stored in plain text.
*   **Standalone installation and extraction**: No MSIX packaging or certificate import is required. The native EXE installer supports installed and extracted deployments. The installer and uninstaller do not require .NET or WinUI; the application itself uses the system-installed x64 .NET Runtime and Windows App Runtime.
*   **Single instance and fast wake-up**: Win32 single-instance and message wake-up handling means launching again never opens a second window; it simply brings the existing one forward.

---

<a name="faq"></a>

## FAQ

<details>
<summary><b>Why is there an EricGameLauncher process in Task Manager after I sign in?</b></summary>

That is the Quick Start background host, which pre-reads configuration and warms up the interface so the next launch is almost instant. Quick Start is off by default. The launcher also checks GitHub for updates and announcements.

If you would rather not have it, turn off **Settings → Quick Start** and the auto-start entry is removed immediately; you can also end it at any time with **More → Exit**.
</details>

<details>
<summary><b>How do I exit completely instead of staying in the background?</b></summary>

Use **More → Exit** and both the window and the background host terminate, without changing the Quick Start setting. On Windows 11 24H2 and later you can also right-click the taskbar entry and choose "End task".
</details>

<details>
<summary><b>Does it need installing, and does it write to the registry?</b></summary>

You can install the application or simply extract and run it. Installation registers the current user's installation information and uninstall entry and creates the selected shortcuts. Extraction does not register these entries. Enabling Quick Start registers sign-in auto-start; extracted copies can also manage shortcuts through the application.

Game scanning reads local disks and installation records. Store app classification may query Microsoft using a package family name. Updates and announcements use GitHub.
</details>

<details>
<summary><b>Where is my data stored?</b></summary>

**System mode** stores data in `%ProgramData%\eric\EricGameLauncher`: `settings.yaml` holds settings, `items.yaml` holds the library and recycled items, and `ico` holds icons. Installed copies always use this location. Copies using the same system data directory share this data.

**Portable mode** stores data in the program directory's `Data` folder. Extracted copies can switch between system and portable modes when the program and data directories are writable and are outside protected locations such as `Program Files` and Windows directories. Switching migrates configuration and icons; write-restricted locations use system mode only.

Temporary caches and launch counts are stored in `%TEMP%\eric\ericgamelauncher`. To back up or transfer configuration, use [configuration import and export](#backup) in Settings instead of organizing the files manually.
</details>

<details>
<summary><b>Can I keep my library when uninstalling?</b></summary>

Yes. Select Keep data (settings, items and icons) on the uninstall confirmation page to retain the system data directory. It is unchecked by default, so uninstallation normally removes that directory. This choice applies to the fixed system data directory, which is shared with any other copies using it. Game files and portable data in other extracted copies are outside the cleanup scope.
</details>

<details>
<summary><b>Is Windows 10 supported?</b></summary>

No. Built on Windows App SDK 1.8 and WinUI 3, this project requires **Windows 11 24H2 (Build 26100)** or later.
</details>

<details>
<summary><b>Which release should I download on an ARM64 PC?</b></summary>

Download the same x64 package. It runs through Windows 11 x64 emulation. Install the x64 versions of both dependencies as well, including the .NET 10 Runtime and Windows App Runtime 1.8.
</details>

<details>
<summary><b>Can I add a game manually if the scan misses it?</b></summary>

Yes. Use **More → Add** to select an executable or shortcut, or enter a platform URL, website or Store app entry. Scanning depends on each platform's local installation records. If a game is missing, first check whether its platform client recognizes it. A missing scan result alone does not establish that a game has been uninstalled. Platform URLs require the corresponding client to be installed.
</details>

<details>
<summary><b>Why does an unpinned game still appear on the taskbar?</b></summary>

An unpinned game can still appear among frequent items based on its launch count. Unpinning a platform hides it from the platform group. Check **This is a game platform** and the pin state in Properties, save, and allow the taskbar list to refresh. Clearing the temporary cache resets the corresponding launch counts.
</details>

---

## Contribution & Support

We welcome contributions of any kind — bug reports, documentation improvements or code (PRs).

*   Found a problem or have an idea? Open an [Issue](https://github.com/EricZhang233/EricGameLauncher/issues) with reproduction steps and it will be handled faster.
*   Want to get involved in development? Use the repository’s .NET 10 SDK on Windows and build `EricGameLauncher.csproj`. Run `dotnet test` for the full local suite. GitHub workflows build release packages. See the [repository guidelines](../../agent.md), [test guide](../../Test/README.md) and [build guide](../BUILDING.md).

If you find this project helpful, please give it a heart-felt ⭐️ **Star**!

<a name="thanks"></a>

## Special Thanks / Links

*   [**Starward**](https://github.com/Scighost/Starward): A powerful Mihoyo game launcher. Our initial vision for this project was just a collection launcher for local games, with no plans to support distribution platforms. The reason we initially supported URL-scheme protocols was to support Starward's protocol for recording playtime in Mihoyo games. Later, we thought if we already support URL-schemes, why not support protocols from platforms like Steam/Epic Games? Thus, the current feature of supporting distribution platform launchers was born. Starward's innovation and contribution in the field of Mihoyo game launchers are indelible. We hope to pay tribute by supporting its protocol and provide players with more choices. With this opportunity, our launcher should support the vast majority of launcher URL-scheme protocols and parameterised execution.

## Friendly Links

*   [**TaskbarLyrics**](https://github.com/ANYNC/TaskbarLyrics): A useful taskbar lyrics tool and another project I help maintain.

---
<div align="center">
  Made with ❤️ by EricZhang233
</div>
