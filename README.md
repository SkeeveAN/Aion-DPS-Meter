🇬🇧 **English** · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)

# Aion DPS – Daevas Power Suite

**The free, open-source companion app for AION 2** — live damage and healing for your whole group, a click-through overlay, boss leaderboards, a pet and gathering map, an event timetable and world boss timers. For Windows, open source, and it never reads game memory.

<p align="center">
  <a href="../../releases/latest"><b>⬇ Download for Windows</b></a> · <a href="https://aiondps.com">Website</a> · <a href="https://aiondps.com/features">All features</a> · <a href="https://aiondps.com/aion-2-dps-meter">Comparison with other meters</a> · <a href="https://aiondps.com/instances">Boss leaderboards</a>
  <br><a href="https://aiondps.com">aiondps.com</a> · <a href="https://daevasuite.com">daevasuite.com</a> · <a href="https://daevaspowersuite.com">daevaspowersuite.com</a>
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/SkeeveAN/Aion-DPS-Meter?label=release" alt="release"></a>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/SkeeveAN/Aion-DPS-Meter/total?label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT"></a>
  <a href="../../actions/workflows/ci.yml"><img src="https://github.com/SkeeveAN/Aion-DPS-Meter/actions/workflows/ci.yml/badge.svg" alt="tests"></a>
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
- Boss casts announced by voice, a loot log, an attributes tool, a crafting calculator and a daily checklist
- Passive: packet capture only, no memory reading, no injection, nothing sent to the game
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
options. The compact panel can optionally let mouse clicks through to the game. The overlay only draws while the game is in front. Toggle it from anywhere with a hotkey.

Optional (**Settings → Interface → Hide during game menus**, off by default): the meter and its overlays step aside while one of the game's menus is open (map, inventory, legion, shop, Esc ...) and come back when you close it. It works by the mouse cursor: in the control mode that hides the cursor while you play, the game shows it when a menu opens; holding the show-cursor key (Alt by default) keeps the meter up so you can click it, and **Ctrl+Alt+J** shows the overlays for the menu that is open. In the classic control mode the cursor is always shown, so menus cannot be told apart; the meter notices that while you fight and simply stays up.

### Skill breakdown for every player

**Double-click** a player: every skill with hits, crit rate, total, min, max and average, split by target,
plus a DPS curve over the whole fight. Crit rates are read straight from the server's hit data, so they are exact. The table also shows how a skill's hits landed: **Back**, **Front**, **Perfect**, **Double** and **Multi** (several strikes in one hit) as the share of its direct hits, read from the hit data. Double (about twice the damage), Perfect (a small bonus) and Parry were checked against the damage they do; Back and Front come from a direction byte that has not been checked against the game yet, and the window says so.

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="Skill breakdown of one player with a damage-over-time curve">
</p>

### Rotation, effects and stagger

The same window has two more tabs. **Rotation** draws every skill the player used on a timeline at the moment they used it: support skills in the lane on top, damage skills below, a skill woven in right after another one row lower, so a run of weaves reads as a staircase. Behind it runs the player's DPS (the average of the last five seconds) and the moments a monster was staggered. Drag to move along the fight, **Ctrl + wheel** zooms, hover an icon for the use (when, which use of how many, the time since the one before, the DPS then). Below, the **opener** (the first skills in order) and *rotation in short*: uses, per minute, the time between two uses, the first use and the share of the damage. **Buffs** lists every effect that was on the player with its uptime, the part of it that came from the player themself, how many times it was put on, and who gave it - an effect two players kept up at once counts once.

Sessions that are saved to a file with the rotation data (see Sessions) show it again when loaded.

<p align="center">
  <img src="Web-Frontend/images/features/rotation@2x.webp" width="640" alt="The Rotation tab of the player window: skills on a timeline with the DPS behind them, the opener and the rotation in short">
</p>

### Replay of a boss fight (early test)

In the player window of a boss fight, **Replay** plays the fight back from above: your group as class-coloured dots with the last seconds of their walk, the boss with the way it faces and its health, the areas of the skills it is winding up (amber while it winds up, red as it lands - dashed while the size is still a guess), a red ring on whoever it hit, the dead crossed out. Under the map a timeline shows the boss's big attacks (amber), the hits on the group (red), deaths, staggers (shaded) and the boss's health; click or drag on it to go to that moment. Play and pause (Space), speeds from half to four times, ← and → move five seconds, drag the map, wheel to zoom, double-click to fit it again.

The positions are the game's own readings: it sends them with movements and skills, not on a regular beat, so a walk is drawn straight between two of them. The game never says how big an attack's area is: the meter learns each skill's size from who it hit and remembers it between fights. Only your group and the bosses are shown, never other players. A fight keeps its replay in the local history; fights recorded by an older version have none.

<p align="center">
  <img src="Web-Frontend/images/features/replay@2x.webp" width="640" alt="The replay of a boss fight: the group around the boss, an area being wound up, the timeline with the boss's health">
</p>

### Your whole character at a glance

The **Character** window (person icon) shows what the game sends about you: gear with item level and
enchant (including the Amplify stage above +15), arcana, species knowledge, all skills with their levels
and the full Daevanion boards. It keeps your last login, so it is never empty. Hover a piece for its
details (stones, rolled stats, god stone).

The meter also notices which of your **skill presets** is active and keeps every preset it has seen: skill levels, stigmas and stones. The skills tab shows each preset with the stones of its skills, and a skill's tooltip lists the stones and the effects a stigma unlocks, in the game's own words.

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="The Character window with equipment, item levels and enchant">
</p>

### Interactive map with pets and gathering

A map overlay shows your position, the spawn points of the pets you are after and the gathering spots you
switched on — herbs, ores, wood, gems and more — without alt-tabbing to a website. Only what your own
faction can use is listed, and the last known position stays on the map while the game is closed. The pet
settings list every region of Verteron, Altgard and the Abyss with its pets and your own pet level, plus a
**Rare** tab for pets with few spawn points. Edge arrows point to the nearest pet or gatherable. Inside a dungeon or party instance the map switches to that instance (25 maps from the game's own pictures, for the instances that have pet monsters) and shows its pet monsters.

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="The interactive map with pet spawn points and the pet list">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="Gathering settings: switch on what appears on the map">
</p>

### Event timetable with reminders

An overlay lists the next events — Battlefield, the Abyss sieges (Artifact Siege), the Shugo Festival,
the Beritra Air Raid, the Spacetime Rift and more. Pick a sound and volume per event and get reminded a
few minutes before it starts. One switch turns all reminders on or off. The times follow the game server's UTC clock, so they are the same in every region and in summer and winter time, and are shown in your PC's time. The Abyss executor bosses (Mon, Thu, Sat) and Guardian Lord Nahma (Fri, Sun) are listed too, and there are ten notification sounds to choose from.

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

### Stat calculator (beta)

See what a change of stats does to your damage before you swap a piece. It starts from your own stats as the game sends them; you say which side you mostly hit from (front, back, don't know) and your crit rate, or take both from your last fight. Type a change into any stat and read the damage with it against the damage now, plus how much of each stat gives one more percent. The expert mode adds buffs and the boss's resistance and is still in beta. The result is an estimate, because the game's damage formula is not published: it says "3.8 % more", never "this much damage". **Start recording** writes down your stats and the hits of a fight for up to 10 minutes; **Send** uploads it after showing exactly what goes out - no name, no legion, no IP address, only a random identifier of your installation - so that the calculator can be made better. The idea comes from the Stat Efficiency Calculator of [RATmeter](https://github.com/Kuroukihime/AIon2-Dps-Meter); layout, code and formula are our own.

### Network report

Under **Tools → Network**: **Save report** writes the last 15 minutes of your connection (ping, jitter, packet loss, the answer times of your router, the internet and the game server, pauses and what caused them, the kind of adapter) as a formatted JSON file named `aiondps_VERSION_DATE_TIME.json` where you choose; **Send report** sends the same report to the developer. It holds no IP address of yours and no names (not of the PC, the Windows account, an adapter, a character or a legion); before sending, a box lists what is in it and what is not. The idea of a saved diagnostic report comes from the environment diagnostics of the Aletheia meter.

### Combat overlay

One transparent frame with up to three elements, each of which you place and size on its own:

- **Boss cast bar:** the moment a boss starts a skill, its name, a bar that runs down with the time left, and whom it is aimed at (you in red). A skill that has hit several players is marked **AREA**; **LETHAL** means the highest hit you took from it so far is at least your current health. The meter learns each skill (area or not, when its damage lands) and remembers it between sessions.
- **Target of the target:** one block that moves, resizes and scales as a whole, with parts you can also place on their own inside it. If the target you selected with Tab is itself aiming at someone, you see who: a boss with its picture and name, a player with the class emblem and name. The **healer bar** (optional) adds that target's health bar and its debuffs (or buffs) to the block.

Unlock the overlay in **Settings → Boss fight** to arrange it: drag an element's body to move it, pull its edges to resize it, pull its corner to scale it; the grip at the bottom right of the whole frame scales all elements together. Locked, it is click-through and only shows while the game is in front. The chosen Windows voice can also **announce boss casts** by voice (with or without the skill name, at you, at everyone, lethal), even with the overlay switched off while Aion is in front; the settings have example sentences to listen to. **Ctrl+Alt+C** switches it on and off, and so does the *all overlays* hotkey.

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="The boss fight overlay: two boss casts with a time bar and the target of the target block with picture, name, health bar and debuffs">
</p>

### Loot, attributes and the daily checklist

The **Tools** window lists these on the left. The **Loot** tool shows what you pick up from the ground, by session and map, as a page and as an overlay (**Ctrl+Alt+L**). The **Attributes** tool shows the attribute ring with your points, what each attribute and Lord value does (0.1 % per point), all bonuses added up, and a stat tab that explains every stat with its share from the ring. The **checklist** keeps your daily and weekly tasks, reads the mandatory daily quests and the weekly Odyle energy purchase from the game, and leaves the rest to tick by hand. Every tool remembers its own width and height.

### Crafting calculator

Mark the recipes you want to craft and how often (for the whole account, so your twinks see them too, or for one character), and the **Crafting calculator** lists, recipe by recipe, which materials you still lack. Intermediates are broken down into what you really have to gather: three smithing hammers need 18 ingots, which means 36 ore, and ingots you already own count first. The stock is read from the game, from the inventory, the character storage and the account storage, and a material that lies in a storage says so. Keep the page open on a second screen or show the same list as an overlay in the game (**Ctrl+Alt+R**). The recipes come from the game's own tables; the order of your list decides which recipe gets the stock first. Log in once so the stock is read; what you move between the inventory and a storage shows up after the next sort.

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

### The meter in your stream (OBS, Streamlabs)

Show the meter live over your game without filming its window: switch on **Settings → Streaming → Stream overlay for OBS**, then use **Copy link for OBS** (or the tray icon's *Copy stream overlay link*), add a **Browser** source in OBS or Streamlabs, paste the link and set the source's size. The page follows the meter live: the current view (damage, heal, damage taken, all in one), the players with class icon and bars, and the boss's health. Add `?scale=1.5` or `?rows=5` to the link to enlarge it or show fewer rows.

It also works with **two PCs** (the game on one, OBS on the other): copy the link to the streaming PC, it holds the game PC's address in your network. Only your own network can reach it, never the internet; the link carries a random key (**New key** makes the old link stop working), only `GET` is served, the page loads nothing from anywhere else, and nothing is sent to anyone. Windows may ask once to allow the meter in the firewall: allow it for private networks. Player names are **off by default** - everyone is shown by their class, like in the uploads - and can be switched on in the same settings card.

### Sessions

Every map and instance is recorded as its own session, so you can look at an earlier run again. A plausibility check compares the damage with the boss's lost hit points.

<p align="center">
  <img src="Web-Frontend/images/features/sessions@2x.webp" width="760" alt="Session manager and combat analyzer in one window">
</p>

The session manager and the **combat analyzer** share one window with a splitter you can move: pick a session on the left, and on the right the player details open (skills, rotation, effects, the replay map), limited to that session. Above the player list you switch between **damage, healing and damage taken**; the list shows the deaths and how long each player was dead. The meter itself keeps recording in the background. **Save to file** can take the rotation, effects and replay data with it (a checkbox that is remembered; the file gets several times bigger, about 0.5 to 5 MB for a five-minute boss fight), so a loaded file shows them too. The window remembers its place and the splitter.

## Using it

Recording starts as soon as the meter is running. **Pause discards** rather than defers: events
during a pause are skipped for good, so resuming never replays a fight you sat out.

- **Copy** puts a one-line, chat-ready ranking on the clipboard (`Name 1.234.567 (890), …`); **Copy All**
  gives you a Discord markdown table.
- Appearance is set under **Settings → Interface**: theme (dark/light), font size, language, overlay look
  and opacity, start with Windows.
- Your settings survive a crash: the settings file is written in one go, and a copy of the last start is kept next to it. If the file is ever damaged, that copy is restored (the damaged file is kept beside it). **Settings → General → Reset all settings to defaults** asks twice and can be undone for 30 seconds; your upload and privacy choices, the update check and your character stay as they are.

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
| **Ctrl+Alt+L** | Loot overlay |
| **Ctrl+Alt+R** | Crafting overlay |
| **Ctrl+Alt+J** | Show the overlays during the open game menu |
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

NCSOFT has not approved this tool. Its operating policy sanctions "unauthorized programs": programs or devices that bypass technical protection or impair normal operation, for example programs that enable actions regular players cannot perform, and the creation or distribution of such programs or changes to client or server software. Bots are listed separately, and a notice on the account trust status mentions "suspicious software" without defining it. NCSOFT names behaviors, not individual programs; damage meters and the passive reading of network traffic are neither listed nor prohibited by name, but the list is not exhaustive. The meter plays nothing for you, automates nothing, raises no damage or speed, reads no game memory, changes neither client nor server and sends no packet to the game; that it falls outside the definition is our reading, not a statement from NCSOFT. Check [the current rules](https://aion2.plaync.com/de-de/board/rules/list) and use it at your own responsibility.

## Disclaimer and license

Aion 2 and all related names, texts and artwork are the property of NCSOFT. This project is not affiliated with, endorsed by or connected to NCSOFT; game content stays with its owner. The source code is released under the [MIT License](LICENSE) — the license covers the code only, not any game content.
