# Contributing to Aion DPS Meter

Thanks for helping! The meter is a passive packet reader for AION 2: it never touches the game process or its memory, and that stays so.

## Ways to help

- **Report a bug**: use the bug report form. A debug recording or the network log makes most problems much easier to solve.
- **Suggest something**: open a feature request and say what you were trying to do.
- **Translate**: texts live in `Client/assets/i18n/ui_strings.json`; fixes to a language you speak well are very welcome.
- **Game data**: wrong boss, skill or pet names? Names must come from the AION 2 client of that language, never from wikis. Tell us the language and what the game shows.
- **Code**: small, focused pull requests are easiest to review.

## Building

See [Building from source](README.md#building-from-source) (`dotnet build` in `Client`). The client is C# / WPF on .NET; run `dotnet run -- selftest` in `Client` before you open a pull request.

## Pull requests

- One topic per pull request, with a short description of what changed and why.
- Keep the style of the surrounding code (naming, comment density).
- Behaviour changes in the packet decoder need a recording or a self-check that shows the new result.
- Never include recordings or logs with real player names or account data.

## Licensing

The project is MIT licensed. By contributing you agree that your contribution is published under the same license. Please do not copy code from projects under other licenses (for example GPL meters); describing what you observed in the game's traffic is fine, pasted code is not.
