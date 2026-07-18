# Wave Link Settings Utility

An unofficial Windows utility that safely detects and repairs unavailable hardware inputs, cleans stale hidden channels, transfers effects, and backs up or restores Wave Link settings.

## What it can do

- **Free occupied channel slots:** find inputs whose `IsHiddenFromMixes` value is the JSON boolean `true` and remove stale entries.
- **Expose hidden inputs for inspection:** keep a hidden entry but set `IsHiddenFromMixes` to `false` so it becomes visible in Wave Link.
- **Recover effects from unavailable channels:** copy the ordered EQ/VST effect chain from an old channel to an already-created replacement without copying device identity, routing, volume, mute, or application assignments.
- **Detect and repair unavailable hardware IDs:** ask Windows Core Audio whether each configured hardware endpoint is active, disabled, unplugged, absent, or stale, suggest conservative replacements, and optionally relink stale identities.
- **Create exact backups:** save a byte-for-byte timestamped copy of the current `Settings.json` without changing it.
- **Restore safely:** validate and restore a managed backup while preserving the current settings as another exact backup.
- **Protect every modification:** close only the Wave Link GUI when necessary, validate temporary output, create a safety backup, atomically replace the settings file, and restart Wave Link by default.
- **Support guided and automated use:** use the returning interactive menu for every operation, or command-line options for cleanup, unhiding, endpoint detection and repair, backup, and restore.

The utility automatically discovers the packaged Wave Link settings file, requires no administrator access or separate .NET installation, and performs no network access or telemetry.

## The problem

Wave Link supports up to eight input channels. Sometimes an input remains in `Settings.json` with `IsHiddenFromMixes` set to `true` after it is no longer visible in the Wave Link interface. These stale hidden entries still occupy channel slots.

The result is confusing: Wave Link may refuse to add another input even though fewer than eight channels appear in the interface. Because the entry is hidden, there is no visible channel to inspect or remove. A Windows or device update can also assign a microphone or capture device a new Core Audio endpoint ID, leaving the original Wave Link channel marked **Unavailable** even though the hardware still works under its new identity. Wave Link also locks its settings file while running, making safe manual diagnosis and editing awkward.

This utility closes Wave Link, finds hidden entries, and lets you either remove them or unhide them for inspection. It checks configured hardware IDs against Windows Core Audio, suggests conservative active replacements, and can relink a stale identity while retaining the channel configuration. If a replacement channel already exists, it can instead copy the old channel's stored effect chain to it. Every settings replacement creates a backup, and Wave Link restarts afterward by default.

Wave Link Settings Utility—previously named WaveLinkHiddenInputCleaner—is open source, MIT-licensed, and unaffiliated with Elgato. Version 2 supports Windows 11 x64 and the current packaged Wave Link settings format. It requires neither administrator access nor a separately installed .NET runtime.

## Use

Download the release ZIP, extract it, and either double-click `WaveLinkSettingsUtility.exe` for an interactive menu or run it in Windows Terminal:

```powershell
.\WaveLinkSettingsUtility.exe
.\WaveLinkSettingsUtility.exe --version
.\WaveLinkSettingsUtility.exe --yes
.\WaveLinkSettingsUtility.exe --unhide
.\WaveLinkSettingsUtility.exe --backup
.\WaveLinkSettingsUtility.exe --detect-unavailable
.\WaveLinkSettingsUtility.exe --repair-unavailable
.\WaveLinkSettingsUtility.exe --repair-unavailable --yes
.\WaveLinkSettingsUtility.exe --restore .\Settings.json.backup-20260718-120000000
```

Options:

```text
--yes                  Skip confirmation
--unhide               Keep matching entries and set IsHiddenFromMixes to false
--backup               Create an exact timestamped settings backup
--detect-unavailable   Check hardware input endpoint IDs without changing settings
--repair-unavailable   Relink a stale ID when exactly one safe replacement is found
--restore <path>       Restore a managed backup beside the selected Settings.json
--settings-path <path> Select a settings file when multiple packages are found
--no-restart           Leave Wave Link closed when this utility stopped it
--help                  Show usage
--version               Show the build version
```

With no action option, interactive mode offers cleanup, effect transfer, backup, restore, endpoint detection and repair, or exit. After an operation or cancellation, the utility returns to the main menu; select **Exit** when finished. Cleanup retains the remove/unhide/cancel submenu. `--unhide` can be combined with `--yes` for unattended use. Effect transfer is interactive only.

Endpoint detection does not modify settings. Because Wave Link locks `Settings.json`, the utility closes and restarts its GUI while reading it. Detection checks only channels marked `HardwareInputDevice`; Wave Link software inputs are excluded. For non-active IDs, it enumerates active Windows capture endpoints and suggests exact friendly-name matches (including names that differ only by Windows' numeric device prefix). Multiple matches are reported as ambiguous and require manual selection.

Interactive detection offers to relink a stale or absent ID when exactly one high-confidence active replacement exists. The `--repair-unavailable` option provides the same repair flow; add `--yes` to accept safe unique matches automatically. Repair updates the channel key, nested device ID, ordered-input references, icons, and VST state keys while preserving the channel's effects and mixer settings. It collapses pre-existing duplicate references only when their values are identical and aborts on conflicts. Disabled and unplugged endpoints are reported but never automatically relinked. Every repair creates an exact managed backup before atomic replacement.

The utility discovers `%LOCALAPPDATA%\Packages\Elgato.WaveLink_*\LocalState\Settings.json`. An override must point to `Settings.json` in one of those discovered package directories. Operations that read or replace settings close the `Elgato.WaveLink` GUI so its locked file can be accessed safely, then restart it unless `--no-restart` is used. Wave Link and Elgato audio services remain running.

Exit code `0` means success, no work, or cancellation; `1` means an operational failure; `2` means invalid arguments.

## Detect and repair an unavailable hardware input

Use this workflow when an existing microphone, capture card, or other hardware channel says **Unavailable**, but Windows recognizes the device:

1. Leave the unavailable channel in Wave Link; it contains the settings to preserve.
2. Start `WaveLinkSettingsUtility.exe` and select **Detect and repair**, or run `--detect-unavailable` for a read-only diagnosis.
3. Review the stored endpoint state and suggested active replacement.
4. If exactly one high-confidence replacement is shown, confirm the relink. From the command line, use `--repair-unavailable`; add `--yes` only when automatic confirmation is desired.
5. Let Wave Link restart, then verify audio, effects, volume, mute, and routing.
6. If anything is wrong, restore the automatically created backup from the main menu.

The utility automatically repairs only endpoints classified as stale or not present. It does not relink disabled or unplugged devices because those conditions may be temporary. Replacement matching accepts an exact friendly name or the same name after removing a Windows numeric prefix such as `2-`. Similar names are rejected, and multiple matching endpoints are reported as ambiguous.

Relinking migrates the hardware channel's identity throughout the supported settings structure, including its `InputSettings` key, nested `DeviceSettings.DeviceId`, ordered-input references, icon mappings, and VST window-state keys. Existing effects and mixer settings remain attached to the channel. Identical historical references are collapsed safely; conflicting keys abort the repair without replacing `Settings.json`.

If Wave Link already contains a separate working channel for the replacement ID, identity repair stops instead of merging the two channels. Use **Transfer effects** to copy the old effect chain to that existing replacement.

## Transfer effects from an unavailable channel

If a microphone or other input becomes **Unavailable**, its EQ and VST settings may still be stored on the old channel even though Wave Link no longer lets you open them. To transfer that effect chain safely:

1. Open Wave Link and create a new working channel for the affected microphone or device.
2. Leave the unavailable channel in place. Do not delete it yet; it contains the stored effects.
3. Start `WaveLinkSettingsUtility.exe`. The utility will close Wave Link automatically when needed.
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
Get-FileHash .\WaveLinkSettingsUtility-v2.0.0-win-x64.zip -Algorithm SHA256
```

Compare the displayed hash with the release `.sha256` file.

After verifying the hash and **before extracting the ZIP**, right-click the ZIP, select **Properties**, check **Unblock**, and click **Apply**. If **Unblock** is not shown, no action is needed.

## Build and test

Install the .NET 10 SDK, then:

```powershell
dotnet test -c Release
dotnet publish src/WaveLinkSettingsUtility -c Release -r win-x64 --self-contained true
```

The test suite uses synthetic JSON and process fakes; Wave Link need not be installed. No real `Settings.json` should ever be committed or attached to an issue.

## Privacy and security

The application performs no network access, telemetry, or upload. It reads only the selected local settings file and never logs its contents. See [SECURITY.md](SECURITY.md) for reporting and privacy guidance.
