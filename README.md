# WaveLinkHiddenInputCleaner

An unofficial Windows utility that safely removes stale Wave Link input entries whose `IsHiddenFromMixes` value is the JSON boolean `true`.

WaveLinkHiddenInputCleaner is open source, MIT-licensed, and unaffiliated with Elgato. Version 1 supports Windows 11 x64 and the current packaged Wave Link settings format. It requires neither administrator access nor a separately installed .NET runtime.

## Use

Download the release ZIP, extract it, and either double-click `WaveLinkHiddenInputCleaner.exe` for interactive cleanup or run it in Windows Terminal:

```powershell
.\WaveLinkHiddenInputCleaner.exe
.\WaveLinkHiddenInputCleaner.exe --dry-run
.\WaveLinkHiddenInputCleaner.exe --yes
```

Options:

```text
--yes                  Skip confirmation
--dry-run              Report matches without stopping Wave Link or changing files
--settings-path <path> Select a settings file when multiple packages are found
--no-restart           Leave Wave Link closed when this utility stopped it
--help                  Show usage
--version               Show the build version
```

The utility discovers `%LOCALAPPDATA%\Packages\Elgato.WaveLink_*\LocalState\Settings.json`. An override must point to `Settings.json` in one of those discovered package directories. It only stops the `Elgato.WaveLink` GUI process; Wave Link and Elgato audio services remain running.

Exit code `0` means success, no work, dry run, or cancellation; `1` means an operational failure; `2` means invalid arguments.

## Backups and restoration

Before replacing changed settings, the utility creates a byte-identical backup beside the original:

```text
Settings.json.backup-YYYYMMDD-HHmmssfff
```

Backups are retained indefinitely. To restore one, close Wave Link, rename the current `Settings.json` for safekeeping, copy the chosen backup to `Settings.json`, and reopen Wave Link.

If no exact boolean match exists—or the matches disappear before the post-shutdown read—the original is not rewritten. Temporary output is parsed before Windows atomically replaces the original.

## Unsigned binaries and checksums

Initial releases are unsigned, so Microsoft SmartScreen may display a warning. Download only from this repository’s Releases page and verify the separate SHA-256 file:

```powershell
Get-FileHash .\WaveLinkHiddenInputCleaner-v1.0.0-win-x64.zip -Algorithm SHA256
```

Compare the displayed hash with the release `.sha256` file.

## Build and test

Install the .NET 10 SDK, then:

```powershell
dotnet test -c Release
dotnet publish src/WaveLinkHiddenInputCleaner -c Release -r win-x64 --self-contained true
```

The test suite uses synthetic JSON and process fakes; Wave Link need not be installed. No real `Settings.json` should ever be committed or attached to an issue.

## Privacy and security

The application performs no network access, telemetry, or upload. It reads only the selected local settings file and never logs its contents. See [SECURITY.md](SECURITY.md) for reporting and privacy guidance.
