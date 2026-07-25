# Wreckfest 2 Tuning Editor

A small Windows desktop app for editing the tuning presets in your **Wreckfest 2** career
profile — adjust setups with sliders, and **export / import / duplicate** presets so you can
reuse a good tune across cars, back it up, or share it with a friend.

Everything it writes stays **within the game's own limits** — it never produces a value the game
itself couldn't. It's an unofficial community tool, not affiliated with Bugbear or THQ Nordic.

## What it does

- Browse every car in your profile and each car's tuning presets.
- Edit tuning values with sliders that snap to the game's own legal steps.
- **Create** a fresh preset (game defaults)
= **duplicate** existing presets
- **Import & Export** presets from a `.json` file
- **Writes safely** by backing up your profile before any
## Download & run

1. Grab `Wf2App.exe` from the [latest release](../../releases/latest).
2. Double-click it. **No .NET install needed** — it's a self-contained build.

Windows SmartScreen may warn about an unsigned app the first time (Expand → *More info* → *Run
anyway*). The app finds your profile automatically.

## Safety — please read once

- **Close Wreckfest 2 before saving.** Never write to the live profile while the game is running
  (it can overwrite your change from memory on its next save); the app warns you if it's running.
  Steam itself running is fine.
- **Your profile is backed up automatically** before each write, to
  `Documents\Wreckfest2 Backups\`. If anything ever looks wrong in-game, restore the most recent
  backup.
- This edits your real career profile. It only writes in-game-legal values, but as with any save
  editor, keep the backups until you're comfortable.

Want to share a save (e.g. for a bug report) without leaking your Steam/online ids? Run
`wf2 scrub <in.sgfi> <out.sgfi>` (from `Wf2Cli`) to anonymize it first.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build
dotnet test                     # Wf2Core.Tests

# run the GUI
dotnet run --project Wf2App

# build the standalone single-file exe
dotnet publish Wf2App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

## License

[MIT](LICENSE) © 2026 Jonathan Soszka.

## Contributing

Working on the code or the save format? See [CONTRIBUTING.md](CONTRIBUTING.md) — the
reverse-engineering reference, project status, documentation map, and build/test notes.
