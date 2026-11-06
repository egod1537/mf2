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

`Prefabs/Blocks` contains 401 ready-to-place, one-unit cube prefabs generated
from Minecraft 26.3 blockstate and model JSON. All prefabs share the single
`Materials/MinecraftBlockAtlas.mat` material and the 512x512
`Atlas/minecraft_blocks_atlas.png` texture. Each prefab uses its own UV-mapped
mesh under `Models/Blocks` and includes a one-unit `BoxCollider`.

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
