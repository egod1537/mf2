# Native World Core, Chunk Meshing, Editor Paint, and Region JSON — Phases 1–4

`MF2.World.Runtime` is a headless-testable, `MonoBehaviour`-free sparse block world. The public
mutation boundary is `WorldCore.Enqueue(...)` followed by `WorldCore.Flush()`.

## Coordinates and storage

- One block is one world unit. Chunks are `16 x 16 x 16` (4,096 blocks).
- Signed world coordinates use mathematical floor division. World `-1` maps to chunk `-1`,
  local `15`.
- The only linearization is X-major: `index = x + 16 * (y + 16 * z)`. X changes fastest,
  followed by Y and then Z.
- `Block` is exactly 8 unmanaged bytes: `{ uint Id, uint State }`. ID zero is Air and Air state
  must be zero.
- Chunk block bytes live in one flat `NativeList<Block>`. Metadata, coordinate lookup, free slots,
  and leases are separate native containers; no NativeContainer is nested in another.

## Ownership, jobs, and versions

`WorldCore` owns every native allocation and must be disposed. Pool growth and lookup capacity
changes happen on the main thread during `Flush`. A `ChunkLease` exposes read-only backing storage,
a start index, and one fixed 4,096-block range. This keeps the command buffer as the sole mutation
path. Schedule jobs against that read-only storage and range, register their `JobHandle` with
`RegisterDataDependency`, complete dependencies, and release the lease before mutation or unload.
Unload is rejected while a lease exists. Lease tokens make a second release of a copied lease fail
without decrementing the count twice.

Every coalesced dirty chunk receives a monotonic `ulong` version. Capture the handle/version when
scheduling mesh or save work and call `IsChunkVersionCurrent` before accepting its result. A
recycled pool slot changes generation, so stale handles also fail this check. Dirty and change
arrays are views valid only until the next `Flush` or `Dispose`.

## Commands and history

Commands execute in insertion order. Fill bounds are inclusive and expand X-fastest, with a
configurable per-command bound (default 262,144 cells). Within one flush, repeated writes to a
cell produce one `BlockChange` containing its pre-batch original value and last-written final
value. Net no-ops remain history-visible but do not dirty or version a chunk. Dirty chunks and
loaded axis-neighbors touched at a chunk boundary are deduplicated.

## Stable block ID scheme v1

The ID scheme version persisted by `BlockRegistryManifest` is `1`:

1. A name must already be canonical lowercase ASCII `namespace:path` text. Namespace characters
   are `[a-z0-9_.-]`; path also permits `/`. There is exactly one colon and no whitespace.
   Because non-ASCII and uppercase input are rejected, normalization is frozen as identity.
2. Hash the exact UTF-8 bytes (identical to ASCII bytes for valid names) with standard xxHash32,
   seed `0`, and little-endian 32-bit lanes.
3. ID `0` is reserved for Air. `minecraft:air`, duplicate names, hash-to-zero results, and any
   collision across canonical names are registry build errors.
4. Stable IDs are not dense indices. `BlockRegistry` builds a separate unmanaged
   `stable ID -> dense index` table.

`Assets/World/Registry/BlockRegistryManifest.asset` is the canonical authoring/bootstrap index.
It stores prefab references outside the unmanaged runtime table. Rebuild it explicitly with
**Tools > Minecraft > Rebuild Block Registry Manifest**; normal startup does not rescan the
AssetDatabase.

## Allocation profile

`RepeatedCommandApplication_AfterWarmup_HasNoManagedGcAllocation` measures 1,000 alternating
set/flush operations after warming capacities. The measured hot window excludes world/registry
construction, native allocation or capacity growth, manifest/bootstrap string work, exception
paths, and Unity/NUnit runner overhead outside the window. The acceptance expectation is 0 managed
bytes in that window; it measured **0 bytes on Unity 6000.3.28f1**. Native pool growth is
intentionally a main-thread capacity event, not a managed-GC claim.

## Chunk mesh pipeline

`ChunkMeshingSnapshotBuilder` copies one immutable 4,096-block chunk plus six 16x16 neighbor
borders. Leases are held only during that copy. A Burst-compatible `IJob` then emits full-cube
vertices, normals, exact per-face UVs, and separate Opaque/Cutout/Transparent index lists. Unity
`Mesh` and `MeshData` APIs are used only by `ChunkMeshApplier` on the main thread. Installed direct
package dependencies were verified before enabling this path: Burst 1.8.31 and Collections 2.7.1.

The current occupancy rule is intentionally simple: every non-Air block occludes the adjacent
cube face, independent of render pass. An absent/unloaded neighbor chunk is Air. When persistence
later distinguishes an unloaded-but-known region from truly absent data, the streaming layer must
either provide its border or defer that mesh; this phase does not invent an “unknown” core state.

After an Editor command flush (or a fixed runtime simulation tick), update renderers explicitly:

```csharp
world.Flush();
renderPipeline.RebuildDirtyNow(world, renderTable, rendererPool);
```

The pipeline deduplicates the latest dirty version and checks both `ChunkHandle` generation and
chunk version before accepting job output. `ChunkRendererPool` keeps one GameObject and one Mesh
per active chunk and reuses them; it never creates per-block objects. Dispose the pool when its
owner is torn down.

`BlockGeometryKind.Variant` is reserved as the dispatch seam for later variant-driven/noncube
geometry. Phase 2 only emits `Cube`; it skips variant entries rather than guessing geometry.

## Minecraft atlas export and render passes

`Assets/World/Rendering/BlockRenderManifest.asset` is a derived registry built from the canonical
block registry and the existing generated prefab meshes. Rebuild it explicitly with
**Tools > Minecraft > Rebuild Block Render Manifest**. The exporter reads each mesh's 24 emitted
UVs verbatim in generator order (`north, south, east, west, up, down`) and does not reconstruct an
atlas index. This preserves face orientation and the generator's half-texel inset inside its
two-pixel padded atlas cells.

The exporter reads but does not rewrite the original atlas, material, mesh, or prefab assets, so
their GUIDs stay stable. It writes only these derived assets under `Assets/World/Rendering/`:

- `BlockRenderManifest.asset`: stable ID, cube/variant dispatch kind, six exact four-corner UV
  records, and per-face render pass.
- `ChunkMaterialSet.asset`: fixed Opaque, Cutout, Transparent material order.
- Three derived materials referencing the same Minecraft atlas.

Pass classification reflects the emitted atlas pixels: fully opaque tiles are Opaque, binary
alpha tiles are Cutout, and tiles containing partial alpha are Transparent. The source generator's
single cutout material is not treated as a stable block/pass registry.

Transparent faces use one chunk submesh/material and Unity renderer sorting. There is no
per-triangle transparent sorting in this cut; intersecting or deeply layered transparent blocks
can therefore sort imperfectly. The stable three-submesh format allows later sorting or geometry
policy changes without changing block IDs or atlas UV records.

## Editor paint vertical slice

Open **Tools > World > Block Palette**. The palette uses standard `EditorGUILayout`/`GUILayout`
controls and has no assembly dependency on the optional `MF2.EditorUI` Fluent IMGUI helper. This
keeps the editor vertical slice compile-safe when that helper is absent.

1. Create or select a `WorldHost` in the active scene. The palette's **Create World Host** action
   assigns the canonical registry, render manifest, and shared material set.
2. Search canonical names and click a block, then activate **Block Paint Tool**.
3. Paint hits place against the hit voxel face. With no voxel hit, Paint intersects the configured
   XZ, XY, or YZ integer work plane. Erase and Pick require an existing voxel hit.

The tool never claims Alt navigation, right/middle mouse input, or scroll. It takes IMGUI
`hotControl` only after a valid left-button Paint/Erase stroke begins and releases it on mouse-up,
navigation, window exit, or tool deactivation. A drag collects commands and flushes once, so it is
one command batch; a 3D grid interpolation fills cells skipped by sparse SceneView mouse events.
The wire ghost is preview-only. Invalid targets display a SceneView reason instead of writing.

`WorldHost` serializes only managed `{x,y,z,id,state}` records and asset references into the scene.
It reconstructs native world/render ownership on enable and deterministically disposes it on scene
disable, destruction, Editor quit, and before assembly reload. Hidden renderer GameObjects and
Meshes use `DontSave` flags and never enter Scene YAML. Block edits intentionally do not use Unity
Undo yet; the custom history plus Unity Undo adapter remains the next-stage boundary.

Categories, favorites/recent, area/sphere tools, packed-state controls, thumbnails, and custom
ShortcutManager bindings are deliberately not part of this vertical slice.

## Human-readable region JSON storage

`IWorldStorage` is a managed seam between native world ownership and persistence. The JSON layer
receives only sparse `StoredChunk`/`StoredBlock` snapshots. It never receives or serializes Unity
objects, NativeContainers, renderer state, meshes, dense registry indices, or chunk pool memory.

A region contains `16 x 16 x 16` chunks (`256 x 256 x 256` blocks). Chunk-to-region conversion
uses floor division: chunk `-1` is region `-1`, local chunk `15`. Files are named
`r.<x>.<y>.<z>.json` and contain, in fixed property order:

- `formatVersion: 1`
- `hashScheme: "xxhash32-utf8-seed0-v1"`
- `region: [x,y,z]`
- deterministic X/Y/Z-sorted local chunks and ascending block indices

Only non-Air `{index,id,state}` entries are written. Numeric stable IDs and packed state are
authoritative; optional debug names are accepted only when their canonical hash matches the ID.
The loader rejects mismatched region coordinates, versions/schemes, invalid local coordinates or
indices, duplicate chunks/indices, Air entries, unknown IDs when a registry validator is supplied,
and invalid packed states when a later schema validator supplies that rule. All files are parsed
and validated into a managed snapshot before `WorldStorageWorldBuilder` creates a replacement
world, so corrupt input does not partially mutate the live world.

`WorldStorageSession` copies block data under a chunk lease before JSON serialization and tracks
data-dirty handle/version pairs across flushes. It acknowledges a chunk only after its region write
succeeds and that exact handle/version remains current. Failed saves and chunks changed during a
save stay dirty. Empty replacement chunks remove old entries; an empty merged region removes its
file. Saving a subset always merges with the valid existing region, preserving unloaded chunks.

Manual Editor entry points are **Tools > World > Save Active World...** and
**Tools > World > Load Active World...**. They run only from explicit menu actions, never from an
EditorWindow Repaint. Code can call `WorldHost.SaveWorld(directory)` and
`WorldHost.LoadWorld(directory)` directly at an explicit save/load boundary.

### Crash and writer guarantees

Each complete region is written to same-directory `*.json.tmp`, flushed through the file stream,
then committed. First saves use a same-filesystem rename. Existing files use `File.Replace` where
the platform supports it. The fallback first copies the last valid file to `*.json.bak`, performs
the replacement, and restores the backup on a handled failure; it cannot promise atomic replacement
against process or machine loss on filesystems without atomic replace. A process-local lock plus a
path-keyed named mutex prevents concurrent writers for the same region.

Startup scans report leftover `*.tmp` and `*.bak` as `RecoveryArtifacts`. A valid primary file
remains the only automatic load source even when a temporary file exists. An orphaned backup causes
a loud recovery error. Temporary and backup files are never promoted, overwritten, or interpreted
as truth automatically; choosing recovery is an explicit higher-level operation.
