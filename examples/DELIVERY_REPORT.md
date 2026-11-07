# MF2 authoritative terrain vertical slice — delivery report

Date: 2026-11-07  
Unity: 6000.3.28f1  
Baseline branch / HEAD: `impl` / `1ec016f6c8fc5e339640bc4ccc8256e87c1f67da`

## Baseline audit

| Area | Observed baseline | Decision |
|---|---|---|
| Domain/session | `GameSession`, value `PlayerModel`, `PlayerRegistry`, command processor, managed snapshot/archive already existed | Reused; no second session model |
| Server/client state | Server `SimulationWorld` and client-only `ClientStateCoordinator` already used separate `WorldCore` instances | Preserved; host presentation never reads the authoritative core |
| Movement | `BlockPlayerController` already used FishNet Replicate/Reconcile and `PredictionRigidbody` | Preserved; no `NetworkTransform` and no replacement movement RPC |
| Physics tick | `NetworkBootstrap` configured FishNet `TimeManager` physics | Preserved; no direct `Physics.Simulate` call added |
| World | `WorldCore` command/flush, leases, versions, storage and chunk meshing already existed | Reused; no replacement world engine or storage format |
| Runtime view | `WorldHost` already supported a borrowed client replica without disposing it | Extended only with chunk mesh collision cooking |
| Missing P0 | No finite terrain generator, safe height spawn, chunk collider, collision readiness ACK, or dedicated collision projection | Implemented as the vertical slice below |

The working tree was already heavily modified and contained untracked architecture work. No stash,
reset, generated-scene rebuild, or destructive cleanup was performed.

## Implemented

- PASS — validated finite `TerrainConfig`: signed origin, bounds, height range, noise settings,
  octave limit, non-air block and total block budget.
- PASS — deterministic seeded `PerlinTerrainGenerator` using `Unity.Mathematics.noise`, column
  `FillRegion` commands and bounded multi-batch `FlushAndCapture` calls.
- PASS — `GameSession.InitializeWorld` allows exactly one pre-start initialization. Restore still
  consumes the saved world and never regenerates it.
- PASS — server session scope selects the initializer once and owns the authoritative `WorldCore`.
- PASS — `SpawnLocator` queries authoritative block data, requires two air cells, avoids the outer
  border where possible and gives deterministic distinct columns.
- PASS — immutable `PlayerSpawnContext` separates per-spawn identity/pose from injected services
  and FishNet connection objects.
- PASS — one `MeshCollider` per active chunk reuses the accepted versioned chunk mesh. Old build
  versions remain rejected by the existing mesh request/pipeline checks.
- PASS — client replica collision readiness is reset on each accepted world version, marked only
  after synchronous mesh/collider application, and acknowledged to the server with session,
  generation and tick identity.
- PASS — `PlayerSpawner` defers spawn until the client ACK and server collision projection are ready.
- PASS — dedicated servers create a renderer-less-in-practice hidden chunk projection (no cameras
  or UI; the shared chunk mesh pipeline supplies MeshColliders) and gate spawn on it.
- PASS — server and client state instances remain separate. Domain code gained no FishNet,
  `MonoBehaviour`, `GameObject`, Rigidbody or scene reference.
- PASS — the client replica is registered once in the presentation container and inherited by the
  scene child scope. The host therefore renders the same replicated instance that received the
  baseline, while the authoritative server world remains a different instance.
- PASS — host/join entry points resolve the transport when invoked instead of depending on the
  first-frame `Start()` order; an unavailable transport now fails without a null-reference exception.
- PASS — `WorldHost` editor ownership, RegionJSON storage, paint/save/load surface and borrowed-world
  disposal contract were preserved.

## Key design choices

1. Caveworks supplied naming and boundary ideas only. No unavailable method body, inheritance tree,
   VContainer registration or call graph was assumed.
2. Existing `IEntityStateReader` remains the read-model contract instead of introducing a parallel
   `IPlayerReadModel` hierarchy.
3. Terrain generation is a session initializer, not a `WorldHost.Start` side effect. This prevents
   editor repaint/domain reload generation and duplicate host ownership.
4. The client receives canonical full block snapshots and ordered deltas. Seed is bootstrap metadata,
   not the authoritative replication mechanism.
5. Collision and visual readiness remain separately representable. Spawn needs collision readiness;
   it does not wait for a separate visual-ready flag.
6. Player movement prediction and reconciliation remain unchanged.

## Files changed for this slice

New `.meta` files accompany new Unity assets and are omitted below for readability.

- Domain/terrain: `Assets/Game/Domain/Terrain/TerrainConfig.cs`,
  `PerlinTerrainGenerator.cs`, `SpawnLocator.cs`, and `Assets/Game/Domain/GameSession.cs`
- Session/presentation lifetime: `Assets/Game/Lifecycle/ServerSessionLifetimeScope.cs`,
  `ServerSessionCoordinator.cs`, `ClientPresentationLifetimeScope.cs`,
  `ClientStateCoordinator.cs`, and `WorldViewBridge.cs`
- Player lifecycle: `Assets/Entities/Player/PlayerSpawnContext.cs`,
  `BlockPlayerController.cs`, and `MVC.md`
- Networking: `Assets/Game/Networking/PlayerSpawner.cs`, `WorldReplicationAdapter.cs`,
  `ServerWorldCollisionBridge.cs`, `NetworkBootstrap.cs`, and `SessionController.cs`
- World collision/readiness: `Assets/World/Runtime/Rendering/ChunkReadiness.cs`,
  `ChunkRenderer.cs`, `ChunkRendererPool.cs`, and `Assets/World/Runtime/Core/WorldHost.cs`
- Builder wiring: `Assets/Game/Editor/MultiplayerSliceBuilder.cs` (builder itself was not run)
- Tests: `TerrainConfigTests.cs`, `PerlinTerrainGeneratorTests.cs`,
  `WorldCoreGenerationTests.cs`, `SpawnLocatorTests.cs`, `ChunkReadinessTests.cs`,
  `WorldReplicaReadinessTests.cs`, and `LifetimeScopeTopologyTests.cs` under
  `Assets/Game/Tests/Editor/`
- Compatibility-only test edit: four NUnit boolean assertions in
  `ArchitectureBoundaryTests.cs` were changed to the installed NUnit-compatible overload without
  changing their meaning.
- Delivery documentation: `examples/DELIVERY_REPORT.md`

## Tests

| Test / command | Result | Evidence |
|---|---|---|
| Baseline `MF2.Game.Tests` EditMode | PASS | 67/67 before this slice |
| Post-change `MF2.Game.Tests` EditMode | PASS | 92/92 |
| Full project EditMode | PASS | 203/203, 0 failed/skipped/inconclusive; final run after all fixes |
| Script compilation | PASS | `script recompile_status`: `failed=false`, `compilationFailed=false` |
| Terrain validation/determinism/origins | PASS | `TerrainConfigTests`, `PerlinTerrainGeneratorTests` |
| WorldCore command/flush generation | PASS | `WorldCoreGenerationTests` |
| Safe distinct spawn locations | PASS | `SpawnLocatorTests` |
| Collision/version readiness | PASS | `ChunkReadinessTests`, `WorldReplicaReadinessTests` |
| Existing command/read-model/scope/persistence tests | PASS | Included in full 203-test run |
| Host Play Mode vertical slice | PASS | FishNet host started; Test scene produced one grounded player at `(-30.5, 7.5, -30.5)`, a ready client replica, and 16 non-empty chunk meshes with 16 chunk `MeshCollider`s (48,952 vertices total) |
| Host terrain bootstrap | PASS | One authoritative generation: seed `12345`, 36,461 blocks, signature `4011A6AC7D4F9288` |
| Title/Outgame button navigation | NOT RUN | Host and scene transition were invoked through the Editor pipeline API, not human UI clicks |
| Remote client / late join Play Mode | NOT RUN | Requires a second reachable player instance |
| Dedicated `-batchmode -nographics` | NOT RUN | Requires a dedicated build/process and remote client |
| Manual WASD/sprint/jump/seam/landing | NOT RUN | Human input scenario not executed |
| `MF2 > Build Multiplayer Test Slice` rerun | NOT RUN | Builder source/tests updated; generator was not run because it would overwrite dirty user scenes |
| Global Unity Console clean | FAIL | The pre-existing `dev.tnayuki.unterm` package still logs a missing `unterm.dll` Editor error on reload. No MF2 terrain/session/runtime exception was observed. A separate Pipeline `scene open` error was caused by the diagnostic command being intentionally rejected during Play Mode. |

Commands used for the reported automated verdicts:

```text
unity command --project-path C:\Users\egod1\proj\mf2 test run --mode editor --filter MF2.Game.Tests --filter-type assembly --timeout 600
unity command --project-path C:\Users\egod1\proj\mf2 test run --mode editor --timeout 600
```

## Manual QA still required

1. Exercise the actual Title → Outgame → Host → Test/Ingame UI path. The equivalent API-driven host
   and network-scene path passed, but the buttons themselves were not clicked manually.
2. Walk and jump over X/Z chunk boundaries. Confirm visible faces and collision have no seam.
3. Join a remote client after generation. Confirm its world collision ACK precedes spawn and the host
   and remote see the same terrain.
4. Run a dedicated server with `-batchmode -nographics -port 7770`, connect 1–2 clients, and confirm
   the dedicated collision-ready log plus grounded movement.
5. Disconnect/reconnect and repeat scene entry. Confirm no duplicate player, collider, subscription
   or disposed native-container exception.
6. Open an authoring `WorldHost` scene and exercise paint/save/load; do not run the slice builder
   without first reviewing dirty scene/prefab diffs.

## Known limitations / P1 follow-up

- The initial finite world uses a full block snapshot. It is correct for this slice but is not the
  final bounded AOI/segment/ACK protocol for large worlds.
- Terrain configuration fields are not yet added to `SessionArchiveSnapshot`; restore is authoritative
  from saved block data and retains the seed, while config-schema migration remains P1.
- Host uses the local client chunk projection for collision after its readiness ACK. Dedicated uses
  a separate authoritative collision projection. Both are version gated, but multi-process behaviour
  still needs the manual scenarios above.
- The default 64×64 footprint follows the supplied illustrative four-by-four chunk bounds. Startup
  cost and transport payload should be profiled before increasing it.
- The project-wide Console is not globally clean because the unrelated `dev.tnayuki.unterm` package
  cannot load its Windows native `unterm.dll`. This slice compiled and ran without an MF2 exception,
  but the package installation/native import must be repaired separately to satisfy a literal
  zero-error Console acceptance check.
- Infinite streaming, compression, AOI eviction and advanced save migration remain out of scope.

## Pipeline documentation consulted

- `Documentation~/commands/scripts.md`
- `Documentation~/commands/build-and-compilation.md`
- `Documentation~/commands/editor-lifecycle-and-observability.md`
- `Documentation~/commands/runtime.md`

Auto-tick was left enabled at 16 ms for the already-running Editor so background compile/test work
continues while it is unfocused.
