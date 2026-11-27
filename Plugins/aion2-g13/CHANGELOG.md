# Changelog

## 0.2.0 (not released)
- Works as a Logitech applet: the program registers with the Logitech LCD interface, shows the detected class and the last switch on the G13 display, and can be put into the software's Applets folder so it starts with it.
- With a stream link saved the program starts hidden in the tray; without one the window opens for the set-up.

## 0.1.0 (first version, not released)
- Reads the live stream of the DPS tool (the same one OBS uses) and finds the class of the own character.
- Per class (Gladiator, Templar, Ranger, Assassin, Spiritmaster, Sorcerer, Cleric, Chanter): do nothing, send a key combination (for example Ctrl+Alt+F13) or start a program. The G13 software reacts to the key combination and changes the profile.
- "Test" button per class, settings are saved, runs in the tray, switches only when the class changes.
- Build script for a small or a self-contained single .exe; the version shows in the window title.
