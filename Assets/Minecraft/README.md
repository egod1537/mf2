# Minecraft texture source

The files under `Textures/Block` were extracted from this machine's locally
installed Minecraft Java Edition `26.3` client JAR on 2026-11-06.

These textures are Mojang/Microsoft assets. Keep them separate from original
project art, and review the applicable Minecraft usage guidelines before
redistributing or publishing a build that contains them.

Unity import intent:

- Texture type: Default
- Filter mode: Point
- Compression: None
- Mip maps: Disabled
- Wrap mode: Repeat
- Alpha transparency: Enabled

## Generated Unity blocks

`Assets/Entities/Blocks/<block-name>` contains each ready-to-place,
one-unit cube prefab together with its dedicated UV-mapped mesh and copies of
every original source texture used by its six faces. A block such as grass can
therefore contain several PNG files, while shared Minecraft textures are copied
into each block folder that uses them. The 401 block folders are generated from
Minecraft 26.3 blockstate and model JSON. All prefabs still render through the
single `Materials/MinecraftBlockAtlas.mat` material and the 512x512
`Atlas/minecraft_blocks_atlas.png` texture, which remain under
`Assets/Minecraft`. Each prefab includes a one-unit `BoxCollider`.

The atlas contains 441 padded 16x16 entries. It uses Point filtering, no mip
maps and no texture compression. Biome tint variants are baked into separate
atlas entries so they can still use the same material.

Only blockstates whose referenced models are all complete 16x16x16 cubes are
generated. Slabs, stairs, doors, rails, plants, walls and other custom geometry
are deliberately excluded. Animated texture strips currently display their
first frame. Translucent blocks such as glass use alpha clipping to preserve the
single-material setup; use a separate translucent render material if accurate
partial transparency is required.

To rebuild the generated assets in the Unity Editor, use:

`Tools > Minecraft > Generate Cube Block Prefabs`

## Editor texture-pack switcher

Open **Tools > Minecraft > Texture Pack Switcher** to bake either of these
sources into the existing atlas:

- **Minecraft Default**: the checked-in Minecraft 26.3 block textures.
- **Rodrigo**: Rodrigo's Pack 1.16 block textures from its official Modrinth
  download. Textures that do not exist in the older 1.16 pack fall back to the
  matching Minecraft 26.3 texture.

Switching is Editor-only. It rebuilds the pixels in
`Atlas/minecraft_blocks_atlas.png`, updates the shared material and derived
World render manifest, and refreshes active scene world hosts. It does not
change block IDs, mesh UV layout, prefab GUIDs, or add a runtime pack selector.
The selected source is stored as a per-user Editor preference.

The Rodrigo ZIP is downloaded to
`LocalTexturePacks/Rodrigo/RodrigosPack-1.16.zip`, verified against the pinned
official SHA-512, and excluded from Git. Rodrigo's Pack is marked All Rights
Reserved by its author; do not commit, redistribute, or package that ZIP
without permission. Applying it changes the checked-in atlas-derived assets in
the local working tree, so review those changes before committing.
