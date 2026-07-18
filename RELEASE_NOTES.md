# v1.2.0

Wave Link supports up to eight input channels, and hidden entries still consume those slots. Stale hidden entries can therefore prevent new channels from being added even when fewer than eight are visible. Removing an entry frees its slot; unhiding keeps the entry and exposes its occupied slot for inspection.

- Added an interactive top-level menu for cleanup, backup, restore, and exit.
- Added `--backup` and `--restore <backup-path>` automation options.
- Cleanup, backup, and restore close Wave Link when it is running so the settings file can be accessed safely, then restart it by default.
- Manual backups preserve the exact settings bytes.
- Restore accepts only managed backups for the selected package, validates them, preserves the current settings, atomically replaces the file, and safely controls only the Wave Link GUI.
- Restore warns that settings backups from different Wave Link versions may be incompatible.
