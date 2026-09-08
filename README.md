**English** · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md)

# Aion DPS Meter

A DPS and loot meter for **AION 4.6 (OriginAion)** that works entirely from the game's own
`Chat.log` file.

It reads a text file the client writes on its own. It does not capture network traffic, and it
does not read from or write to the game process. Nothing is sent anywhere — everything stays on
your machine.

## Install

1. Download `AionDpsMeter.msi` from the [latest release](../../releases/latest) and run it.
   Everything is bundled; you do not need .NET installed.
2. Start **Aion DPS Meter** from the Start menu (the installer offers a desktop shortcut too).
3. Open **Settings → App Settings** and pick your **Aion installation folder** — the root folder,
   the one containing `bin64\game.dll`. The dialog tells you right away whether it found a valid
   install and whether a `Chat.log` already exists there.

### Prerequisite: the client's chat log must be on

Aion only writes `Chat.log` when the client-internal `g_chatlog` option is enabled. That switch
lives in the game client, not in this tool — enable it however you normally would (for example
with [ShugoConsole](https://github.com/grenadium/ShugoConsole)). **Aion DPS Meter never touches
the game process for this**; if the file is not being written, the meter has nothing to read.

## Using it

Recording starts as soon as the meter is running and a valid Aion folder is set.

**It never reads your chat history.** On start it jumps to the *current* end of `Chat.log` and only
ever consumes lines written from that moment on — like a tape recorder switched on just now, not
an archive scanner. **Pause discards** rather than defers: lines written while paused are skipped
for good, so resuming never replays a fight you deliberately sat out. Your past private
conversations, guild chat and whispers are never looked at.

### Views

- **Dmg** — damage per player, with total and DPS, class icons, and a sortable grid. The
  **Mob/Boss** filter switches the column between overall DPS and true per-target **iDPS** (damage
  to one target divided by the group's shared engagement time with it).
- **Loot** — what dropped for whom: person, item, quantity and rarity grade. Relics also count
  toward each person's Abyss Points.

### Hide UI (overlay)

Turns the window into small click-through chips you can leave sitting on top of the game — one
per player, showing name, damage and DPS. Toggle it with **Ctrl+Alt+H**, from anywhere, so it is
never a one-way trip.

### Copy

**Copy** puts a one-line, chat-ready ranking on the clipboard (`Name 1.234.567 (890), …`). In the
Loot view it produces an Aion-chat loot summary instead, and **Copy All** gives you a Discord
markdown table.

### In-game commands

Type these as normal chat lines to drive the meter without leaving the game:

| Command | Effect |
|---|---|
| `.ui` | toggle the Hide-UI overlay |
| `.pause` / `.resume` | stop / continue recording |
| `.dmg` | copy the damage ranking to the clipboard |
| `.cleardmg` | clear the current session |
| `.loot` | copy the loot summary to the clipboard |

Only characters you registered in the settings can issue them, so a `.cleardmg` typed by a
stranger in a channel you are not even reading cannot wipe your session.

## Client languages

Chat lines are recognised in **English, German, French, Spanish and Russian**. Two players in one
group can run clients in different languages and both get counted correctly — the meter matches
each line against every language's sentence shapes, not just your own client's.

## How accurate is it?

Validated against three `Chat.log` files of the *same* Sauro Supply Base run, recorded on three
different PCs (one of them a German client), with the bosses' real HP as the reference. Damage
dealt to each boss lands within **0,02 % – 2,8 %** of that boss's actual HP:

| Boss | Real HP | Measured | Deviation |
|---|---|---|---|
| Guard Captain Ahuradim | 1.736.993 | 1.737.299 | +0,02 % |
| Derakanak the Reaver | 1.343.657 | 1.347.258 | +0,27 % |
| Archmagus Sayahum | 1.377.644 | 1.370.734 | −0,50 % |
| Commander Ranodim | 489.332 | 503.244 | +2,84 % |

The remainder is overkill on the killing blow, which no log-based meter can see. One player's
total came out identical to the unit on all three machines.

Two known, harmless outliers: a boss whose shield absorbs damage still has that damage logged
(so it reads over its HP), and several mobs sharing one name are summed together.

## Building from source

```
dotnet build
dotnet run -- selftest                    # parser and DPS-maths self-checks
dotnet run -- chatlog <path-to-Chat.log>  # parse a file and print the summary
```

Windows only (WPF). The self-check suite runs verbatim lines from real logs in every supported
language and is the fastest way to see whether a parser change broke anything.

## A note on server rules

This tool only reads a log file the game itself produces. Even so, private servers set their own
rules about third-party tools and addons — worth a look at OriginAion's before using it.
