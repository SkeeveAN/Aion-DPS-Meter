🇬🇧 **English** · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)

# Aion DPS Meter

**The free DPS meter for AION 2** — live damage and healing for your whole group, a click-through overlay, boss leaderboards, a pet and gathering map, an event timetable and world boss timers. For Windows, open source, and it never reads game memory.

<p align="center">
  <a href="../../releases/latest"><b>⬇ Download for Windows</b></a> · <a href="https://aiondps.com">Website</a> · <a href="https://aiondps.com/features">All features</a> · <a href="https://aiondps.com/instances">Boss leaderboards</a>
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/SkeeveAN/Aion-DPS-Meter?label=release" alt="release"></a>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/SkeeveAN/Aion-DPS-Meter/total?label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" alt="Windows">
</p>

<p align="center">
  <img src="Web-Frontend/images/features/meter@2x.webp" width="440" alt="The meter window: every player with class icon, DPS, total damage and a share bar">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="320" alt="The click-through overlay as small chips on top of the game">
</p>

> The names, players and numbers in the pictures below are demo data.

## Why use it

- Live DPS, iDPS and HPS for every player in your party or raid
- A click-through overlay that never takes a click
- Skill breakdown with crit rates and a DPS curve
- Boss leaderboards and public character profiles on aiondps.com (optional upload, off by default)
- Interactive map, event timetable, world boss timers and a network overlay
- Safe: passive packet capture, no memory reading, no injection
- Free, open source (MIT) and updates itself

Works with the global release of AION 2 — every region NCSOFT launched on 5 October 2026. Korea and Taiwan run a few versions ahead, so skill recognition may be incomplete there.

A damage/heal meter and toolkit for **Aion 2**. It reads the game's own network traffic on your machine
through the [Npcap](https://npcap.com) driver — passively: it never sends a packet and never touches the
game process or its memory. Nothing about your play leaves your machine unless you upload it (see
[Uploads](#uploads)); the other thing it sends is an update check, which asks GitHub whether a newer
release exists and can be switched off; see [Updates](#updates).


## Contents

[Install](#install) · [What it does](#what-it-does) · [Using it](#using-it) · [Hotkeys](#hotkeys) ·
[Uploads](#uploads) · [Updates](#updates) · [Languages](#languages) · [Building from source](#building-from-source)

## Install

1. Install the [Npcap](https://npcap.com) driver (the meter needs it to see the game's traffic; it is
   not part of the installer).
2. Download `AionDpsMeter-win-Setup.exe` from the [latest release](../../releases/latest) and run it.
   It installs into your user profile and starts the meter — no administrator rights, no .NET needed.
3. Start the meter, then log in with your character. The meter reads your character, gear, skills and
   Daevanion boards from the game itself; the server you play on is detected automatically.

## What it does

### Live damage and healing for everyone

Every player in your group or raid with name, total damage, DPS and a share bar; faction and class are
shown as icons. Filter by class, group or target. The **Mob/Boss** filter switches between overall DPS and
true per-target **iDPS**; bosses are recognised from the game's own data and shown by name. A **PvE/PvP**
switch separates the two, and a wipe on a boss gets an entry of its own (a skull marks an attempt after
which the boss went back to full health).

### Five views, one click apart

**Damage**, **Heal**, **Damage taken** and two **All in One** views. Switch with the hotkey
(default **Ctrl+Alt+M**) or by clicking the mode label in the overlay. *All in One* shows damage taken,
healing and total side by side, *All in One Compact* keeps the familiar DPS and total columns and puts
healing and damage taken in a small second line under the name.

<p align="center">
  <img src="Web-Frontend/images/features/mode-all@2x.webp" width="460" alt="All in One view: damage taken, healing and total damage side by side">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="380" alt="The click-through overlay as small chips on top of the game">
</p>

### A click-through overlay

**Hide UI** turns the window into small chips (or a compact panel) that sit on top of Aion 2 without
taking a single click. Opacity, damage-share bars, the boss's health and damage taken per player are
options. The overlay only draws while the game is in front. Toggle it from anywhere with a hotkey.

### Skill breakdown for every player

**Double-click** a player: every skill with hits, crit rate, total, min, max and average, split by target,
plus a DPS curve over the whole fight. Crit rates are read straight from the server's hit data, so they are exact.

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="Skill breakdown of one player with a damage-over-time curve">
</p>

### Your whole character at a glance

The **Character** window (person icon) shows what the game sends about you: gear with item level and
enchant (including the Amplify stage above +15), arcana, species knowledge, all skills with their levels
and the full Daevanion boards. It keeps your last login, so it is never empty. Hover a piece for its
details (stones, rolled stats, god stone).

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="The Character window with equipment, item levels and enchant">
</p>

### Interactive map with pets and gathering

A map overlay shows your position, the spawn points of the pets you are after and the gathering spots you
switched on — herbs, ores, wood, gems and more — without alt-tabbing to a website. Only what your own
faction can use is listed, and the last known position stays on the map while the game is closed. The pet
settings list every region of Verteron, Altgard and the Abyss with its pets and your own pet level, plus a
**Rare** tab for pets with few spawn points. Edge arrows point to the nearest pet or gatherable.

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="The interactive map with pet spawn points and the pet list">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="Gathering settings: switch on what appears on the map">
</p>

### Event timetable with reminders

An overlay lists the next events — Battlefield, the Abyss sieges (Artifact Siege), the Shugo Festival,
the Beritra Air Raid, the Spacetime Rift and more. Pick a sound and volume per event and get reminded a
few minutes before it starts. One switch turns all reminders on or off.

<p align="center">
  <img src="Web-Frontend/images/features/timetable@2x.webp" width="380" alt="Timetable settings with a sound and volume per event">
</p>

### World bosses on your overlay

Switch on a field boss to see its timer on the overlay and hear its sound the moment it is up. The times
come straight from the game; the server sends them for the map you are on, the other maps show theirs
once you have been there. Sound and volume are set per boss, grouped by region.

<p align="center">
  <img src="Web-Frontend/images/features/worldboss@2x.webp" width="380" alt="World boss settings: every field boss by map with its respawn time, a switch, sound and volume">
</p>

### Network overlay

Ping, jitter, traffic, packet loss and how long you have been online, measured from the traffic your PC
already receives. Values turn from green to red as the connection gets worse. Choose the lines you want,
lock it in place, switch it on and off with a hotkey.

<p align="center">
  <img src="Web-Frontend/images/features/network-overlay@2x.webp" width="340" alt="The network overlay: ping, jitter, traffic and packet loss">
</p>

### Combat overlay

One transparent frame with up to three elements, each of which you place and size on its own:

- **Boss cast bar:** the moment a boss starts a skill, its name, a bar that runs down with the time left, and whom it is aimed at (you in red). A skill that has hit several players is marked **AREA**; **LETHAL** means the highest hit you took from it so far is at least your current health. The meter learns each skill (area or not, when its damage lands) and remembers it between sessions.
- **Target of the target:** if the target you selected with Tab is itself aiming at someone, you see who: a boss with its picture and name, a player with the class emblem and name.
- **Healer target** (optional): the selected target's health bar and the debuffs (or buffs) on it.

Unlock the overlay in **Settings → Combat overlay** to arrange it: drag an element's body to move it, pull its edges to resize it, pull its corner to scale it; the grip at the bottom right of the whole frame scales all elements together. Locked, it is click-through and only shows while the game is in front. **Ctrl+Alt+C** switches it on and off, and so does the *all overlays* hotkey.

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="The combat overlay: two boss casts with a time bar, the target of the target and the healer's target with health and debuffs">
</p>

### Announcements and Twitch Drops

A megaphone button opens the official NCSoft announcements on aiondps.com — maintenance, outages, new
servers, server transfers, compensation — newest first, with a coloured label per kind and a filter. While
an AION 2 Twitch Drops campaign is running, a Twitch button opens a page with its rewards, the hours you
have to watch, a countdown and the streams that have Drops enabled right now. A *New* mark shows when
something is newer than your last visit.

<p align="center">
  <img src="Web-Frontend/images/features/announcements@2x.webp" width="430" alt="Official AION 2 announcements with kind filter">
  <img src="Web-Frontend/images/features/twitchdrops@2x.webp" width="430" alt="Twitch Drops campaigns with rewards and live streams">
</p>

### Fight history

Fights are recorded locally so you can look at an earlier boss again (switch it off under
**Settings → Interface**). The meter resets after a configurable time out of combat.

## Using it

Recording starts as soon as the meter is running. **Pause discards** rather than defers: events
during a pause are skipped for good, so resuming never replays a fight you sat out.

- **Copy** puts a one-line, chat-ready ranking on the clipboard (`Name 1.234.567 (890), …`); **Copy All**
  gives you a Discord markdown table.
- Appearance is set under **Settings → Interface**: theme (dark/light), font size, language, overlay look
  and opacity, start with Windows.

<p align="center">
  <img src="Web-Frontend/images/features/settings@2x.webp" width="340" alt="Settings: interface, overlay and behavior">
  <img src="Web-Frontend/images/features/hotkeys@2x.webp" width="340" alt="Settings: hotkeys">
</p>

## Hotkeys

System-wide, also while the game has the focus. Click a field in **Settings → Hotkeys**, press the
combination you want (it needs Ctrl, Alt, Shift or Win); **×** removes it. They replace chat commands,
which cannot work because Aion 2's chat is not readable from the network stream.

| Default | Action |
|---|---|
| **Ctrl+Alt+H** | Overlay (hide the window, show chips) |
| **Ctrl+Alt+P** | Pause / resume |
| **Ctrl+Alt+D** | Copy the damage ranking |
| **Ctrl+Alt+Shift+X** | Clear the session |
| **Ctrl+Alt+U** | Upload boss damage |
| **Ctrl+Alt+M** | Next mode (five views) |
| **Ctrl+Alt+T** | Timetable overlay |
| **Ctrl+Alt+K** | Interactive map on / off |
| **Ctrl+Alt+E** / **Ctrl+Alt+G** | Edge arrows for pets / gatherables |
| **Ctrl+Alt+B** | World boss overlay |
| **Ctrl+Alt+N** | Network overlay |
| **Ctrl+Alt+C** | Combat overlay |
| **Ctrl+Alt+O** | Hotkey overlay (a movable list of the hotkeys) |
| **Ctrl+Alt+A** | All overlays on / off |

## Uploads

- **Boss fights** are uploaded when you click upload (toolbar button, Session menu or **Ctrl+Alt+U**): the
  boss, the players who took part, damage, healing, damage taken and skills. Only bosses the game
  announced and the catalog knows are accepted. Automatic upload is available and **off by default**.
- **Your own character profile** (name, class, level, gear, skills, Daevanion, legion, server, faction) can
  be uploaded automatically a few seconds after you log in, so you can be found on the website. This is
  **off by default**; switch it on under **Settings**.
- Without an upload nothing leaves your machine.

## Updates

The meter updates itself. It asks GitHub for a newer release at startup and every five minutes while
it runs, downloads it in the background and swaps it in on the next start — no installer, no UAC
prompt. When an update is ready a green line appears in the status row (and a badge on the transparent
overlay); clicking it offers to restart right away. **App → Check for updates** does the same on demand.
An old version keeps uploading for 72 hours after its successor came out, then must update.

The check reads one URL and sends nothing but the request itself:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Turn it off under **Settings → Updates**; the menu item keeps working when it is off.

## Languages

English, German, French, Spanish, Russian, Chinese (simplified and traditional), Portuguese (Brazil),
Japanese and Korean — set under **Settings → Interface → Language**. This README is also available in
[Deutsch](README.de.md), [Español](README.es.md), [Français](README.fr.md), [Русский](README.ru.md),
[中文](README.zh.md), [繁體中文](README.zh-TW.md), [Português (Brasil)](README.pt.md),
[日本語](README.ja.md) and [한국어](README.ko.md).

## Building from source

```
cd Client
dotnet build
dotnet run -- selftest                              # self-checks (protocol, capture, decoding, ...)
dotnet run -- aion2-record <out.jsonl>              # record the game's traffic ("stop" ends it)
dotnet run -- aion2-replay <file.jsonl>             # replay a recording through the real decoder
dotnet run -- aion2-upload-dryrun <file.jsonl>      # build the uploads of a recording, send nothing
```

Windows only (WPF). `Tools/aion2-dat` reads the game's text tables (names in eight languages); see its README.

## A note on server rules

The meter only passively observes the game's own network traffic. Even so, publishers set their own
rules about third-party tools — worth a look at the terms of the game before using it.

## Disclaimer and license

Aion 2 and all related names, texts and artwork are the property of NCSOFT. This project is not affiliated with, endorsed by or connected to NCSOFT; game content stays with its owner. The source code is released under the [MIT License](LICENSE) — the license covers the code only, not any game content.
