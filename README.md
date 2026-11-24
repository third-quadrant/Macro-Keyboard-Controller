# Macro Keyboard Controller (enhanced)

An independent Windows controller for the **MINI KeyBoard** macro pad (6 keys + knob, VID `1189` / PID `8890`). It replaces the vendor application: no vendor EXE, DLL, or driver is needed at runtime.

The UI is in Korean.

## What it does

- Identifies each physical button separately: `KEY1`–`KEY6`, knob left / click / right.
- Binds one action per button: keyboard shortcut, text, program launch, media key, or mouse action.
- Applies changes immediately — no save button, no per-key "test" step.
- Picks programs from the installed Start-menu list instead of typing paths.
- Lights only the pressed key's LED, then turns it off after one second.
- Includes a wizard for rewriting the device's own key triggers, with a behaviour-level backup and restore path.

## How identification works

The device is reprogrammed so that `KEY1`–`KEY6` emit **F13–F18** (`0x68`–`0x6D`), which no normal keyboard sends. The app reads them with Raw Input and filters by device path, so other keyboards cannot trigger it. The knob keeps emitting Consumer Control usages (`0xEA` left, `0xE9` right, `0xE2` click).

F13–F18 are an internal detail; the normal UI only shows the physical button names.

## Requirements

- Windows 10/11
- .NET 8 Desktop Runtime for the framework-dependent build
- No NuGet packages

## Build and run

```powershell
.\build.ps1
.\run.ps1
```

or directly:

```powershell
dotnet build Fireworm.sln -c Release
dotnet run --project src/Fireworm.App/Fireworm.App.csproj
```

Self-contained publish:

```powershell
dotnet publish src/Fireworm.App/Fireworm.App.csproj -c Release -r win-x64 --self-contained true -o dist/Fireworm-standalone
```

Settings are stored in `%LOCALAPPDATA%\Fireworm\settings.json`; the activity log is written next to it.

## Tests

```powershell
dotnet run --project tests/Fireworm.Tests/Fireworm.Tests.csproj -c Release
```

## Device writes

Rewriting the device's triggers or LED modes writes to the device over its vendor HID interface (MI_01, Report ID 3, 65-byte reports). The app asks for confirmation, stores a behaviour-level backup in `device-backup.json`, and can restore the original `A`/`C` behaviour. Use at your own risk: the device cannot be fully read back, so recovery is based on the recorded behaviour, not a full configuration dump.

Per-key LED feedback uses uncommitted (volatile) writes, so it does not touch flash.

## Limitations

- F13–F18 still reach the foreground application; Raw Input only observes.
- The knob's original volume/mute behaviour cannot be removed with the known protocol, so knob actions can double up.
- No profiles, layers, macros/sequences, auto-start, or installer yet.
- Mouse actions are implemented but not exercised in automated tests.
- Hot-plug handling is implemented but not verified on hardware.

## Documentation

- `MVP_IMPLEMENTATION_REPORT.md` — implementation and verification report
- `docs/analysis/mini-keyboard-hid-protocol.md` — reverse-engineered vendor protocol notes
- `PHASE0_*`, `PHASE1_*`, `PHASE2_*` — device, input, and install-safety analysis

## License

MIT — see `LICENSE`.
