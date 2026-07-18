# v1.3.0

Wave Link channels that become unavailable can retain EQ and VST configuration that the interface no longer allows users to access. Version 1.3 adds a guided recovery workflow that transfers that stored effect chain to an already-created replacement channel.

- Added interactive effects transfer with explicit source and destination selection.
- Copies only the ordered `AudioPluginConfigurations` effect chain; device identity, routing, application assignments, volume, and mute settings remain unchanged.
- Displays stored effect names and warns before replacing an existing destination chain.
- Creates an exact safety backup before atomically replacing settings.
- Added detailed unavailable-channel recovery instructions to the README.
