🇬🇧 **English** · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 [Polski](README.pl.md) · 🇹🇷 [Türkçe](README.tr.md) · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

Website with community boss leaderboards and character profiles: **https://aiondps.com**

A damage/heal meter for **Aion 2**. It reads the game's own network traffic on your machine through
the [Npcap](https://npcap.com) driver — passively: it never sends a packet and never touches the
game process or its memory. Nothing about your play leaves your machine unless you upload it (see
[Uploads](#uploads)); the other thing it sends is an update check, which asks GitHub whether a newer
release exists and can be switched off; see [Updates](#updates).

> Classic Aion (Chat.log based) is no longer part of the meter. The last version that supports it
> is kept on the branch [`aion1-included`](../../tree/aion1-included).

## Install

1. Install the [Npcap](https://npcap.com) driver (the meter needs it to see the game's traffic; it is
   not part of the installer).
2. Download `AionDpsMeter-win-Setup.exe` from the [latest release](../../releases/latest) and run it.
   It installs into your user profile and starts the meter — no administrator rights, no .NET needed.
3. Start the meter, then log in with your character. The meter reads your character, gear, skills and
   Daevanion boards from the game itself; the server you play on is detected automatically.

## Using it

Recording starts as soon as the meter is running. **Pause discards** rather than defers: events
during a pause are skipped for good, so resuming never replays a fight you sat out.

### Views

- **Dmg** — damage per player, with total and DPS, class icons and a sortable grid. The **Mob/Boss**
  filter switches the column between overall DPS and true per-target **iDPS**. Bosses are recognised
  from the game's own data and shown by name. **Double-click** a player for the skill breakdown.
- **Character** (the person icon) — opens a window with your own character: profile, gear with item
  levels and enchant, skills with levels, and the Daevanion boards. It keeps your last login, so it is
  never empty.

### Hide UI (overlay)

Turns the window into small click-through chips you can leave sitting on top of the game — one per
player, showing name, damage and DPS. Toggle it with **Ctrl+Alt+H**, from anywhere.

### Copy

**Copy** puts a one-line, chat-ready ranking on the clipboard (`Name 1.234.567 (890), …`); **Copy All**
gives you a Discord markdown table.

### Chat commands

`.ui` (overlay), `.pause` / `.resume`, `.dmg` (copy the ranking) and `.cleardmg` (clear the session).
The handler only accepts them from your own character. Aion 2's chat is not decoded yet, so for now
they do nothing.

## Uploads

- **Boss fights** are uploaded when you click upload (toolbar button or Session menu): the boss, the
  players who took part, damage, healing, damage taken and skills. Only bosses the game announced and
  the catalog knows are accepted.
- **Your own character profile** (name, class, level, gear, skills, Daevanion, legion, server) is
  uploaded automatically a few seconds after you log in, so you can be found on the website. Switch it
  off under **Settings**.
- Without an upload nothing leaves your machine.

## Updates

The meter updates itself. It asks GitHub for a newer release at startup and every five minutes while
it runs, downloads it in the background and swaps it in on the next start — no installer, no UAC
prompt. When an update is ready a green line appears in the status row; clicking it offers to restart
right away. **App → Check for updates** does the same on demand.

The check reads one URL and sends nothing but the request itself:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Turn it off under **Settings → Updates**; the menu item keeps working when it is off.

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
