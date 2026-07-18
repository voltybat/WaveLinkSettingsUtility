# v2.1.0

Wave Link Settings Utility can now diagnose and repair hardware channels that become **Unavailable** after Windows assigns the device a new Core Audio endpoint ID.

- Classifies configured hardware endpoints as active, disabled, unplugged, not present, stale, or unknown.
- Suggests active replacement endpoints only for exact device-name matches or names differing by Windows' numeric device prefix.
- Reports multiple matching endpoints as ambiguous instead of guessing.
- Relinks stale identities throughout channel keys, device settings, input ordering, icons, and VST state references while preserving effects and mixer settings.
- Refuses to relink disabled or unplugged devices, existing replacement channels, and conflicting settings references.
- Creates an exact safety backup before every repair and atomically replaces validated settings.
- Adds interactive **Detect and repair**, `--detect-unavailable`, and `--repair-unavailable` workflows.
- Verified the recovery path against a deliberately broken live Shure MV7+ channel.

## v2.0.0

WaveLinkHiddenInputCleaner is now **Wave Link Settings Utility**, reflecting its broader cleanup, effect recovery, backup, and restore capabilities.

- Renamed the repository, solution, projects, executable, namespaces, workflows, and release assets to `WaveLinkSettingsUtility`.
- Displays the exact assembly version at the top of the interactive menu and through `--version`.
- Returns to the main menu after each interactive operation instead of closing the utility.
- Retains the existing settings cleanup, effect transfer, backup, and restore behavior.
