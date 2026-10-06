# Bundled WorldLogs routes

`world-routes.json` contains 1,432 maps derived from the owner's existing WorldLogs archive for this release. It is embedded in Core and available offline, including in clean source builds. The dataset contains observed map topology, not collision grids or guaranteed character access.

Only `Number`, `Name`, `DisplayName`, `Size`, `Portals`, and `WorldMaps` are retained. Original descriptions, walk spots and flags are omitted. No accounts, credentials, packet payloads, NPC records, game binaries or artwork are included. Map labels and route observations describe Dark Ages; game rights remain with their owners.

Dataset SHA-256: `084aad4b6903894fe13ef0b557fe9596ed354fa68346c0cc126bb559afa63a2f`. Includes 6,454 raw portal observations before the existing loader's boundary filtering. Map records are sorted by numeric ID. Update the bundle only from an explicitly supplied route collection; retain the field allowlist and verify routes through existing synthetic tests.
