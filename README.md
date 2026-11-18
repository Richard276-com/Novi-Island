# Dynamic Island for Windows

A living notch for the top of your Windows screen. It sits quietly as a small pill (or a notch hanging from the top edge), reacts to what's happening on your PC, and opens into a set of pages: music, AI chat, timer, to-dos, a file shelf, system stats, mini games and more.

Native WPF, a single `.exe`, no runtime to install (it uses the .NET Framework that ships with Windows).

---

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Install](#install)
- [Using it](#using-it)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Setting up the extras](#setting-up-the-extras)
- [Settings](#settings)
- [Building from source](#building-from-source)
- [Command line (IPC)](#command-line-ipc)
- [Project layout](#project-layout)
- [Troubleshooting](#troubleshooting)
- [Where your data lives](#where-your-data-lives)
- [Contributing](#contributing)
- [Credits](#credits)
- [License](#license)

---

## Features

### On the closed island
- **Live activities**: the playing song (artwork, animated waveform), a running timer, or both at once. When nothing is going on, an idle clock and date.
- **System events**: volume and brightness (drag the bar to change them), charging and battery levels, Wi-Fi/Ethernet, Bluetooth devices, displays, microphone mute, clipboard copies and track changes.
- **Notifications from other apps**: WhatsApp, Discord, browsers, mail… anything that shows a Windows notification. You get the app icon, the sender and two lines of the message. Several messages from the same app are grouped on one card with a counter. Click a card to bring the app forward.
- **Do not disturb**: holds app notifications and gives you a summary when you turn it off.
- **Smart visibility**: tucks into a tiny tab over fullscreen video and games, or when a window sits under it.

### Pages (hover or click the island to open)
| Page | What it does |
|---|---|
| **Home** | Greeting, clock, battery, a "Now" card (running timer, playing song or next to-do, with live progress) and up to 5 shortcuts you choose. |
| **Media** | Artwork, title, artist, the source app, a seek bar that previews the time while dragging, play/pause/skip, blurred ambient artwork. Works with any app that shows media controls in Windows. |
| **AI chat** | Claude, Gemini or GPT. Sign in through the official CLIs in your browser, or use an API key. Markdown replies (tables, code blocks with copy, task lists…), the model name under every answer. |
| **Timer** | Presets, ± and scroll to adjust, "ends at" time, pause/resume, +1 min, a ring countdown. |
| **To-do** | Add, edit in place, tick off (done items glide to the bottom), progress bar. |
| **Shelf** | Drop files on the island to park them; drag them out later. Choose Copy or Move, see thumbnails, type badges and sizes. |
| **System** | CPU, memory and disk gauges that change colour under load, a CPU history chart, uptime and process count. |
| **Games** | Blocks (falling-block puzzle), Sudoku (unique-solution puzzles, three levels), Mines and Merge (slide tiles, equal numbers merge; one-step undo). Best scores are kept. |

You choose which pages appear and in what order (Settings → Pages). A hidden page still opens if you ask for it directly (Home shortcut or hotkey).

### Launcher
Press **Alt+Space**: search installed apps, calculate (`12*7`), open websites, search the web with `?term`.

### Wallpaper picker
- **Local**: your wallpaper folder, with the current one marked. Shuffle, or switch the folder from the picker.
- **Online**: Wallhaven, Konachan (safe only), Unsplash and Bing's images of the day. Click to download and apply.
- **Wallhaven filters**: General / Anime / People and SFW / Sketchy / NSFW chips. A search shows the best matches from everything, not just this month's top list.
- Fit modes: Fill, Fit, Center, Span.

### Also
- **Caffeine**: keeps the PC and screen awake while it's on.
- **Themes**: a dozen palettes, a theme from your wallpaper, or your own colours. Bundled fonts: Inter and JetBrainsMono Nerd Font.
- **Smooth motion**: spring animations everywhere, adjustable speed (or off).
- **Setup wizard** on first start, and a hidden easter egg somewhere in Settings.

---

## Requirements

- Windows 10 (1809 or newer) or Windows 11.
- .NET Framework 4.8 (already part of Windows 10/11).
- Optional, for AI sign-in without API keys: the official CLIs (`claude`, `agy`, `codex`). The app can install them for you.

---

## Install

1. Grab the latest zip from the [Releases](../../releases) page (or [build it yourself](#building-from-source)) and extract it. Keep `DynamicIsland.exe` and the `fonts` folder side by side.
2. Run `DynamicIsland.exe`.
3. The **setup wizard** opens. Its *Install* step copies the app to `%LOCALAPPDATA%\Programs\DynamicIsland` (no admin needed), adds a Start menu shortcut, an optional desktop shortcut, *Start with Windows*, and an entry in **Apps & features**.
4. Pick your name, look, pages, and whether you want notifications and AI. Press **Finish**.

You can run the wizard again from the tray icon menu (**Setup wizard…**) or Settings → About.

### Uninstall
Windows Settings → **Apps → Installed apps → Dynamic Island → Uninstall**, or run `DynamicIsland.exe --uninstall`. You'll be asked whether to keep your settings, to-dos and chats.

---

## Using it

| Do this | To |
|---|---|
| Hover or click the island | Open it |
| Swipe on the touchpad, scroll over the page dots, or press ← → | Change page |
| Hover the page dots | See every page as an icon dock; click one to jump there |
| Right-click the island (or the tray icon) | Menu: Settings, Wallpapers, Do not disturb, Setup wizard, Quit |
| Scroll over the closed island | Change the volume |
| Middle-click the island | Play / pause |
| Drop files on the island | Add them to the Shelf |
| Click an event or notification | Act on it (open the app, the page…) |
| Esc | Close the island |

---

## Keyboard shortcuts

All of them can be changed in Settings → Shortcuts.

| Shortcut | Action |
|---|---|
| `Ctrl+Alt+I` | Open / close the island |
| `Alt+Space` | Launcher |
| `Ctrl+Alt+C` | AI chat |
| `Ctrl+Alt+T` | To-do |
| `Ctrl+Alt+Y` | Timer |
| `Ctrl+Alt+P` | Pause / resume the timer |
| `Ctrl+Alt+M` | System page |
| `Ctrl+Alt+H` | Shelf |
| `Ctrl+Alt+W` | Wallpaper picker |
| `Ctrl+Alt+D` | Dismiss the current event |
| `Ctrl+Alt+S` | Settings |

Inside the open island: `1`–`8` jump to a page, `Space` plays/pauses (or pauses the timer). Games have their own keys (Blocks: ← → ↑ ↓ Space P; Sudoku: 1–9, arrows, Backspace).

---

## Setting up the extras

### Notifications from other apps
The island reads the Windows notification centre. Windows has to allow it once:
**Windows Settings → Privacy & security → Notifications → Let apps access your notifications** (on). An app only shows up on the island if its own Windows notifications are on.
In Settings → Notifications → *Other apps* you can hide the message text, set how long cards stay, and mute apps.

### AI chat
Settings → AI chat:
- **Browser sign-in** (recommended): uses your own Claude, Google or ChatGPT account through the official CLI. If the CLI is missing, *Sign in* installs it first (for Gemini that's just the standalone `agy` CLI, not the full Antigravity app).
- **API key**: paste a key instead. Keys are encrypted for your Windows user (DPAPI).

### Wallhaven NSFW
Wallhaven only returns NSFW results to a signed-in account:
1. On wallhaven.cc → Settings → **Browsing**, allow NSFW.
2. Copy your API key from Settings → **Account**.
3. Paste it in Settings → Launcher & Wallpapers → *Wallhaven API key*, then turn on the **NSFW** chip in the picker.
NSFW thumbnails are blurred until you hover them (can be turned off).

---

## Settings

Open from the island's right-click menu, the tray icon, the Home shortcut, or `Ctrl+Alt+S`. Use the search box (`Ctrl+F`) to find any option.

| Group | Sections |
|---|---|
| Personal | General (name, greeting, start with Windows) |
| Island | Appearance · Size & position · Behavior · Motion · Notifications |
| Pages | Pages (order, visibility, Home shortcuts, where it opens) · Timer, To-do & Shelf · AI chat · Launcher & Wallpapers |
| System | Shortcuts · About |

---

## Building from source

No Visual Studio or .NET SDK needed: `build.bat` uses the C# compiler that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).

```bat
cd DynamicIsland
build.bat
```

This produces `DynamicIsland.exe` next to the sources. Close the running app first (the exe can't be replaced while it runs).

Notes for contributors:
- The compiler is C# 5: no string interpolation (`$"…"`), no `?.`, no `=>` members, no `nameof`.
- Debug logging: set `DI_DEBUG=1` before starting; logs go to `%APPDATA%\DynamicIsland\error.log`.

### Releasing

1. Bump `AppInfo.Version` in `Updater.cs` (e.g. `"1.0.2"`).
2. Run `release.bat`. It closes a running island, rebuilds, checks that the exe reports the new version and writes `dist\DynamicIsland-v<version>.zip`.
3. On GitHub, create a release tagged `v<version>` and attach that zip. Installed copies pick it up from Settings → About (or at start).

---

## Command line (IPC)

A running island accepts commands from a second instance:

```bat
DynamicIsland.exe --ipc <command> [args]
```

Handy ones:

| Command | Does |
|---|---|
| `toggle` · `expand [page]` · `collapse` | Open / close |
| `page <home\|media\|ai\|timer\|todo\|shelf\|system\|games>` | Show a page |
| `launcher [text]` · `wallpaper [local\|online]` · `wallsearch <words>` | Launcher / wallpaper picker |
| `settings [section]` · `settings-search <words>` | Settings |
| `timer <seconds\|toggle\|cancel>` · `todo <text>` · `shelf <path…\|clear>` | Tools |
| `ai send <text>` · `ai new` | AI chat |
| `caffeine [on\|off]` · `theme <palette>` · `dismiss` | Misc |
| `simulate <volume\|brightness\|charging\|track\|urgent\|drop>` | Show a sample event |
| `setup [step]` | Open the setup wizard |
| `update [check\|install]` · `quit` | Check GitHub for a newer release (result in `error.log`) or update and restart · Quit |

Other flags: `--silent` (start without the hello event), `--uninstall`.

---

## Project layout

| File | Contents |
|---|---|
| `Island.cs` | The island window: shape and spring animation, event queue, hover/click handling, media session, tray, IPC, entry point |
| `Pages.cs` | The open island: page carousel and dock, Home, Media, Timer, To-do, Shelf, System, AI chat UI, Markdown renderer, Launcher, Wallpaper picker |
| `Games.cs` | Blocks, Sudoku, Mines, Merge |
| `Notify.cs` | Notifications from other apps, Do not disturb |
| `Settings.cs` | Settings model, saving, and the Settings window |
| `Wizard.cs` · `Installer.cs` | First-run setup wizard; per-user install and uninstall |
| `Ai.cs` | AI providers (CLIs and APIs), models, usage limits |
| `Launcher.cs` | Online wallpaper providers, app index, wallpaper service |
| `Services.cs` | Audio, brightness, battery, Bluetooth, network, system stats, hotkeys |
| `Core.cs` | Shared helpers: theme palettes, animation, controls (toggles, sliders, rings…), Win32 interop |
| `EasterEgg.cs` | 🤫 |
| `fonts/` | Bundled Inter and JetBrainsMono Nerd Font |

---

## Troubleshooting

| Problem | Try |
|---|---|
| The island shows as a black box | Settings → Motion → Renderer → **Compatible**, then restart. |
| App notifications don't appear | Allow notification access (see [above](#notifications-from-other-apps)), check that the app's own notifications are on in Windows, and that it isn't muted or Do not disturb isn't on. |
| A hotkey doesn't work | Another app owns it: Settings → Shortcuts shows it in red; pick another combination. |
| AI says "not signed in" | Settings → AI chat → Sign in, or *Check status again* after signing in. |
| Wallhaven NSFW shows nothing | Add the API key and allow NSFW in your Wallhaven account. |
| Something else | Set `DI_DEBUG=1`, reproduce, and look at `%APPDATA%\DynamicIsland\error.log`. |

---

## Where your data lives

`%APPDATA%\DynamicIsland\`
- `settings.json`: all settings (API keys are DPAPI-encrypted).
- `todos.json`: your to-dos.
- `ai-chat.json`: the current AI conversation.
- `error.log`: errors (and debug logs when `DI_DEBUG=1`).

Downloaded wallpapers go to your wallpaper folder (default `Pictures\Wallpapers`).

---

## Contributing

Issues and pull requests are welcome. Stick to the C# 5 rules listed under [Building from source](#building-from-source), run `build.bat` to make sure it still compiles, and say what you tested in the PR.

---

## Credits

- **Inter** by Rasmus Andersson, and **JetBrains Mono** (Nerd Font build) by JetBrains: both under the SIL Open Font License, see `fonts/`.
- Wallpapers come from [Wallhaven](https://wallhaven.cc), [Konachan](https://konachan.net), [Lorem Picsum](https://picsum.photos) (Unsplash photos) and Bing. Each image belongs to its author; respect their licences.
- AI chat talks to Anthropic, Google and OpenAI through their official CLIs or APIs with your own account.

---

## License

[MIT](LICENSE). The bundled fonts keep their own SIL Open Font License (see `fonts/`).
