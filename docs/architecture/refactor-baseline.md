# Refactor baseline

## Checkout and environment

Audit date: 2026-11-07 (Asia/Seoul).

| Item | Observed value |
|---|---|
| Branch | `impl` |
| Requested reference | `7c511d0148708575a0bc555273446a73bd182371` |
| Actual starting HEAD | `53f47a14dec54627a8acdd1147180296613e42a0` |
| Starting worktree | clean |
| Unity | `6000.3.28f1 (8f082d2fe476)` |
| FishNet | Git package pinned to `#4.7.3` |
| VContainer | Git package pinned to `#1.19.0` |
| Pipeline | `0.10.0-exp.1`, Editor port 7800 |
| OS | Windows, Editor target Windows/Mac/Linux |

The requested reference was not checked out because the active `impl` branch had advanced. The
clean actual HEAD is the rollback point for this pass. No third-party package, imported texture,
FMOD bank, ProPixelizer source, or SceneToolbar source was edited.

## Sources and evidence classes

### MF2 sources

- Repository: <https://github.com/egod1537/mf2>
- Audit branch: <https://github.com/egod1537/mf2/tree/impl>
- Requested audit commit:
  <https://github.com/egod1537/mf2/commit/7c511d0148708575a0bc555273446a73bd182371>
- Architecture commit:
  <https://github.com/egod1537/mf2/commit/71ca0c572517e8662dcbc460e104d1a824d8481f>

Both commit objects were inspected from the local Git repository. Commit `71ca0c5` records
`Unity EditMode 203/203 passed; API-driven host vertical slice passed` in its commit message. That
sentence is historical provenance only; it is not counted as an execution performed in this pass.
Commit `7c511d0` adds the later FMOD/SceneToolbar and related project changes but was also not the
actual starting checkout.

### Caveworks reference boundary

- Analysis repository: <https://github.com/egod1537/caveworks>
- Report commit:
  <https://github.com/egod1537/caveworks/commit/1e77cee9f7daf1e832fdc911e72c661d6cc4d53a>

The report commit was inspected from the local `caveworks-publish` Git object. It contains reports,
reproducibility scripts, and derived metadata inventories. Its README/NOTICE explicitly exclude
decompiled game source, binaries, Unity assets, and third-party tool binaries. MF2 therefore uses
only the reported architectural ideas (read/write model separation, proxy seams, intentional
scopes, chunk lifecycle, snapshot/ACK cautions). No Caveworks implementation, asset, inferred
license grant, or unverified runtime behavior is copied or asserted here.

### Evidence labels used in this audit

| Label | Meaning |
|---|---|
| Static/local source | Checked directly in the current checkout or pinned package source |
| Historical record | Commit message or prior report; not re-executed evidence |
| Executed in this pass | Unity command result captured against the final working tree |
| NOT RUN | No success is inferred from source, an older commit, or another environment |

## Assembly direction

The application assembly graph observed from the checked-in asmdefs is:

```text
MF2.World.Runtime
  -> Unity.Burst, Unity.Collections, Unity.Mathematics

MF2.Game.Domain
  -> MF2.World.Runtime, Unity.Collections, Unity.Mathematics

MF2.Entities.Player
  -> MF2.Game.Domain, FishNet.Runtime, Unity.InputSystem, Unity.Cinemachine

MF2.Game
  -> MF2.Game.Domain, MF2.Entities.Player, MF2.World.Runtime
  -> FishNet.Runtime, VContainer, Unity.Mathematics, Unity.InputSystem, UnityEngine.UI

MF2.Game.Editor / MF2.Game.Tests
  -> adapter/runtime assemblies above; Editor or test only
```

`MF2.Game.Domain` has no FishNet, VContainer, uGUI, UnityEditor, FMOD, GameObject, Rigidbody, or
scene reference. The existing architecture boundary tests scan both the asmdef and source.

## Serialized asset inventory

- `Title.unity` contains the persistent `NetworkBootstrap`, FishNet `NetworkManager`, application
  scope, coordinators, adapters, block catalog, and spawned-player prefab reference.
- `Outgame.unity` is presentation/UI. `Test.unity` and `Ingame.unity` contain scene presentation
  scope, `WorldHost`/`WorldViewBridge`, spawn point, HUD, camera, and world-command debug input.
- No Player NetworkObject is serialized into a game scene. `PlayerSpawner` creates it after
  authenticated scene presence and the collision/readiness barrier.
- `MinecraftBlockMap` remains an authored reference scene and is not rebuilt by the multiplayer
  slice builder.
- `MF2 > Build Multiplayer Test Slice` was executed during this pass. It regenerated the four
  scenes and both player prefabs through the supported Editor path. Their `.meta` files and GUIDs
  did not change.

## Creation, ownership, and release

| Resource | Creator | Owner | Release boundary |
|---|---|---|---|
| Application scope | persistent Title bootstrap | `NetworkBootstrap` GameObject | process/bootstrap destruction |
| Server session scope | `ServerSessionCoordinator` | coordinator | explicit stop or successful replacement |
| Authoritative `GameSession` | server session container | server session scope | scope disposal; `Stop` is state transition only |
| Authoritative `WorldCore` | `SimulationWorld` | `SimulationWorld` | reverse-order scope disposal |
| Client presentation scope | `ClientPresentationCoordinator` | coordinator | client presentation end/application end |
| Client replica `WorldCore` | `ClientStateCoordinator` | client presentation scope | clear/replacement/scope disposal |
| Scene scope and views | loaded scene | scene | scene unload |
| Runtime `WorldHost` world reference | `WorldViewBridge.AttachRuntimeWorld` | borrowed only | detach; never disposes borrowed core |
| Editor `WorldHost` core | `WorldHost.EnsureInitialized` | Editor `WorldHost` | disable/destroy/reload bridge |
| Player NetworkObject | `PlayerSpawner`/FishNet | FishNet server spawn lifecycle | despawn/pool/destroy |
| Rigidbody and NetworkObject maps | server session scope | session-scoped adapters | unregister or scope disposal |
| Chunk renderer/collider pool | scene view or dedicated collision bridge | creating bridge/host | detach, scene unload, session stop |
| Session archive files | `SessionJsonStorage` | persistence destination | versioned snapshots plus pointer/backup policy |

VContainer 1.19.0's installed source was checked: `DisposeCore()` clears the container, so the
coordinator's explicit `DisposeCore()` followed by Unity GameObject destruction is idempotent when
`OnDestroy` observes the already-cleared scope. `GameSession.Dispose()` does not dispose
`SimulationWorld`; the container owns both and `SimulationWorld` is the sole WorldCore disposer.

## Confirmed facts, fixed defects, and remaining risks

### Confirmed and preserved

- FishNet TimeManager remains at 30 Hz with `PhysicsMode.TimeManager`.
- `ServerSessionRunner` steps the domain from `OnPostTick`; project code does not call a second
  runtime `PhysicsScene.Simulate`.
- `SimulationWorld.FlushAndCapture` copies `WorldCore.Changes` and `DirtyChunks` to managed DTOs
  immediately after the single flush.
- World snapshots/deltas and player snapshots/deltas carry SessionId, generation, and monotonic
  tick/revision gates. WorldReady also checks the active session generation/tick.
- RegionJSON, session format version 2, checksum, temporary file, pointer, and backup behavior are
  covered by existing tests.

### Defects found and fixed in this pass

1. The builder contract test failed because the checked-in generated Player prefab still used the
   non-ProPixelizer torso material. Running the supported builder regenerated Player/scene output;
   the builder regression now passes.
2. The airborne wall-slide regression used a local-physics-scene API that Unity only permits in
   Play Mode. The EditMode test now uses a disposable Preview Scene physics world and destroys its
   temporary objects, proving the frictionless capsule continues to fall while pressed into a wall
   without modifying project Physics settings.
3. Live session restore cleared the connection-to-PlayerId map but did not recreate active Player
   NetworkObjects in the replacement session registries. The restore handoff now despawns the old
   object, retains a restored PlayerId only when it exists in the candidate registry, waits for the
   new WorldReady barrier, and respawns/re-registers the object.
4. Client presentation event dispatch used direct multicast invocation, so an early subscriber
   exception could skip later cleanup. Dispatch now isolates subscribers and destruction ends the
   child presentation scope idempotently.
5. Runtime world readiness used scene asset/pool existence as a proxy for collider completion.
   It now verifies every loaded chunk's current handle generation and version against the applied
   collider projection before sending WorldReady or allowing dedicated spawn, and resets the
   readiness token when a pooled renderer is rebound to a recycled handle generation.

### Risks not represented as passes

- Full world snapshots materialize and broadcast one sparse block array. Large-world transport and
  allocation limits were not benchmarked in this pass.
- Collision build is synchronous and readiness now verifies the accepted handle/version after
  rebuild, but no injected native MeshCollider-cook failure scenario was run.
- `GameSession.Restore` intentionally sets `Terrain = null`; restored sessions fall back to authored
  spawn points unless a future archive version persists generator configuration.
- Archives do not contain Rigidbody velocities, rotations, PhysX contacts, or prediction history.
- Compatibility statics (`ApplicationLifetimeScope.Active`, `NetworkBootstrap.Instance`,
  `SessionController.Instance`) remain for generated scene/UI entry points.

## Test baseline

Commands were run through the connected Unity Editor, not inferred from source:

```text
unity command --port 7800 script recompile
unity command --port 7800 test run --mode editor --timeout 900
unity command --port 7800 test list --mode playmode
```

Chronology:

| Run | Result |
|---|---|
| Initial unfiltered EditMode | 208/210 passed; builder artifact and physics-test harness failed |
| After baseline repairs | 210/210 passed |
| After restore lifecycle fix and new test | 211/211 passed, 0 failed/skipped/inconclusive |
| After scope-event isolation and collider readiness integration | 213/213 passed, 0 failed/skipped/inconclusive |
| PlayMode inventory | 0 registered tests |

Unity wrote the Test Framework XML to
`C:/Users/egod1/AppData/LocalLow/DefaultCompany/mf2/TestResults.xml`. The Editor finished with
`compilationFailed=false`. Its sole current console error is the pre-existing optional Unterm
native bridge load failure (`unterm.dll`); no MF2/FishNet error remained after the Host smoke.

