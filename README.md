# WaveLinkHiddenInputCleaner

An unofficial Windows utility that safely cleans stale hidden Wave Link inputs, transfers effects from unavailable channels, and backs up or restores settings.

## The problem

Wave Link supports up to eight input channels. Sometimes an input remains in `Settings.json` with `IsHiddenFromMixes` set to `true` after it is no longer visible in the Wave Link interface. These stale hidden entries still occupy channel slots.

The result is confusing: Wave Link may refuse to add another input even though fewer than eight channels appear in the interface. Because the entry is hidden, there is no visible channel to inspect or remove. Wave Link also locks its settings file while running, making safe manual diagnosis and editing awkward.

This utility closes Wave Link, finds those hidden entries, and lets you either remove them to free their slots or unhide them for inspection. It can also recover the stored effect chain from an unavailable channel by copying it to a replacement channel. It creates a backup before replacing the settings file and restarts Wave Link afterward by default.

WaveLinkHiddenInputCleaner is open source, MIT-licensed, and unaffiliated with Elgato. Version 1 supports Windows 11 x64 and the current packaged Wave Link settings format. It requires neither administrator access nor a separately installed .NET runtime.

## Use

Download the release ZIP, extract it, and either double-click `WaveLinkHiddenInputCleaner.exe` for an interactive menu or run it in Windows Terminal:

```powershell
.\WaveLinkHiddenInputCleaner.exe
.\WaveLinkHiddenInputCleaner.exe --yes
.\WaveLinkHiddenInputCleaner.exe --unhide
.\WaveLinkHiddenInputCleaner.exe --backup
.\WaveLinkHiddenInputCleaner.exe --restore .\Settings.json.backup-20260718-120000000
```

Options:

```text
--yes                  Skip confirmation
--unhide               Keep matching entries and set IsHiddenFromMixes to false
--backup               Create an exact timestamped settings backup
--restore <path>       Restore a managed backup beside the selected Settings.json
--settings-path <path> Select a settings file when multiple packages are found
--no-restart           Leave Wave Link closed when this utility stopped it
--help                  Show usage
--version               Show the build version
```

With no action option, interactive mode offers cleanup, effect transfer, backup, restore, or exit. Cleanup retains the remove/unhide/cancel submenu. `--unhide` can also be combined with `--yes` for unattended use. Effect transfer is interactive only.

The utility discovers `%LOCALAPPDATA%\Packages\Elgato.WaveLink_*\LocalState\Settings.json`. An override must point to `Settings.json` in one of those discovered package directories. Cleanup, effect transfer, backup, and restore close the `Elgato.WaveLink` GUI when it is running so the settings file can be accessed safely, then restart it unless `--no-restart` is used. Wave Link and Elgato audio services remain running.

Exit code `0` means success, no work, or cancellation; `1` means an operational failure; `2` means invalid arguments.

## Transfer effects from an unavailable channel

If a microphone or other input becomes **Unavailable**, its EQ and VST settings may still be stored on the old channel even though Wave Link no longer lets you open them. To transfer that effect chain safely:

1. Open Wave Link and create a new working channel for the affected microphone or device.
2. Leave the unavailable channel in place. Do not delete it yet; it contains the stored effects.
3. Start `WaveLinkHiddenInputCleaner.exe`. The utility will close Wave Link automatically when needed.
4. Select **Transfer effects**.
5. Select the unavailable or old channel as the source.
6. Select the newly created working channel as the destination.
7. Review the listed effects and the destination overwrite warning, then confirm.
8. Let Wave Link restart and verify the effects on the replacement channel.
9. Delete the unavailable channel only after confirming that the replacement works correctly.

The destination's existing effect chain is replaced, not merged. Only `AudioPluginConfigurations`—the ordered EQ/VST/effect configuration—is copied. Device identity, application assignments, routing, volume, mute, and per-mix settings remain those of the replacement channel. The replacement channel must already exist, and the source must contain at least one stored effect.

An exact backup is created automatically before the settings file is replaced. If the result is incorrect, use **Restore backup** from the main menu to return to the previous settings.

## Backups and restoration

Manual backups and the safety backup created before every replacement use this name beside the original:

```text
Settings.json.backup-YYYYMMDD-HHmmssfff
```

Backups are retained indefinitely. Interactive restore lists managed backups newest-first. Restore accepts only a managed `Settings.json.backup-*` file beside the selected package's settings, validates its JSON and expected Wave Link structure, stops only the GUI, atomically replaces the settings, and preserves the current file as another byte-identical timestamped backup—even if the current file is malformed.

Backups from a different Wave Link version may be incompatible: Wave Link may ignore them, fail to load them, or reset its settings. The utility displays this warning before restoration. It restarts Wave Link once after a restore attempt unless `--no-restart` is supplied.

If no exact boolean match exists—or the matches disappear before the post-shutdown read—the original is not rewritten. Temporary output is parsed before Windows atomically replaces the original.

## Unsigned binaries and checksums

Initial releases are unsigned, so Microsoft SmartScreen may display a warning. Download only from this repository’s Releases page and verify the separate SHA-256 file:

```powershell
Get-FileHash .\WaveLinkHiddenInputCleaner-v1.2.0-win-x64.zip -Algorithm SHA256
```

Compare the displayed hash with the release `.sha256` file.

After verifying the hash and **before extracting the ZIP**, right-click the ZIP, select **Properties**, check **Unblock**, and click **Apply**. If **Unblock** is not shown, no action is needed.

## Build and test

Install the .NET 10 SDK, then:

```powershell
dotnet test -c Release
dotnet publish src/WaveLinkHiddenInputCleaner -c Release -r win-x64 --self-contained true
```

The test suite uses synthetic JSON and process fakes; Wave Link need not be installed. No real `Settings.json` should ever be committed or attached to an issue.

## Privacy and security

The application performs no network access, telemetry, or upload. It reads only the selected local settings file and never logs its contents. See [SECURITY.md](SECURITY.md) for reporting and privacy guidance.
