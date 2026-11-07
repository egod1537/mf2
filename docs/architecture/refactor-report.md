# Refactor audit report

## Overview

- Branch: `impl`
- Base SHA: `53f47a14dec54627a8acdd1147180296613e42a0`
- Code checkpoint SHA: `9695b83` (`fix: bound world deltas and session subscriptions`)
- Documentation SHA: this report is committed after the code checkpoint
- Completed in this pass: Phase 0 audit/gate, targeted lifecycle repair, builder regeneration,
  layered terrain/water rendering and collision, restore-safe spawning, bounded replication,
  versioned collision-readiness integration, repeated lifecycle coverage, and single-Editor Host
  runtime verification
- Reused from the current repository: domain/session scopes, replica scope, WorldHost ownership
  split, command/replication gates, persistence transaction, MVC binding, physics ports

## Changes

### Baseline and generated assets

- Regenerated Title/Outgame/Test/Ingame and Player prefabs through
  `MF2 > Build Multiplayer Test Slice` so builder output is the checked result.
- Preserved existing GUIDs and restored the expected ProPixelizer player material, camera/volume,
  scene scopes, network components, and player facing hierarchy.
- Test and Ingame now use one builder rendering path for the ProPixelizer chunk material set,
  orthographic camera configuration, and Minecraft map Volume profile. Test keeps its four spawn
  points, expanded ground, and world-command debug input.
- Reworked the airborne wall-slide regression to run valid EditMode script physics and clean up its
  temporary objects.

### Restore lifecycle

- `GameCommandNetworkAdapter` can now rebuild a transient connection mapping only for a PlayerId
  already present in a restored candidate session. It rejects duplicate live aliases and can
  release the map without deleting restored model state.
- `PlayerSpawner` now observes session replacement. It captures live connection/scene/PlayerId,
  despawns old objects, waits for replacement WorldReady, then respawns and registers the new
  Rigidbody/NetworkObject against the new session scope.
- Added a regression proving restored map release does not duplicate or remove Registry-owned
  PlayerModel state.

### Scope events and collision readiness

- `ClientPresentationCoordinator` now isolates each ready/stopping subscriber so one failing
  adapter cannot prevent later cleanup, and `OnDestroy` provides an idempotent presentation-end
  safety net.
- `ChunkRenderer` records `ChunkReadiness` for the exact applied handle generation and data
  version. `WorldViewBridge` and `ServerWorldCollisionBridge` release their readiness gates only
  when every currently loaded chunk has a current collider projection.
- Added regressions for subscriber failure cleanup and for rejecting an older collider version
  after a new WorldCore flush, including pooled renderer reuse after a handle-generation change.
- World/player replication adapters now remove their coordinator/session subscriptions on disable
  and resubscribe once on re-enable. World replication sends a fresh base after re-enable so missed
  authoritative ticks cannot leave a stale replica.

### Layered terrain, water, and bounded transfer

- Default Perlin generation writes deterministic stone/dirt/grass spans and fills low columns to
  water level 7 with registered `minecraft:water` blocks.
- Water uses the fourth render pass and a dedicated transparent material. The visual mesh and
  solid-only collider mesh have separate Unity ownership; liquid has no collision indices.
- Snapshot Begin/Part/End messages carry at most 48 blocks per reliable message. Normal deltas use
  the same cap; larger same-tick changes fall back to an atomic bounded snapshot.
- Restored sessions locate dry solid spawn columns from their stored authoritative snapshot when
  generator configuration is intentionally absent.

## Ownership result

- ServerSessionLifetimeScope is the sole authoritative GameSession/SimulationWorld owner.
- SimulationWorld is the sole authoritative WorldCore disposer.
- ClientStateCoordinator owns a separate replica WorldCore, including in Host mode.
- Runtime WorldHost borrows the client replica and owns only scene render/collision resources.
- FishNet owns spawned NetworkObject lifetime; adapters own transient identity/maps only.
- Restore recreates Unity runtime bindings rather than carrying old scope objects into the
  candidate.

## Verification

Environment: Unity 6000.3.28f1, FishNet 4.7.3, VContainer 1.19.0, Windows Editor.

| Check | Result |
|---|---|
| Unity recompile | PASS, `compilationFailed=false`, 0 compiler errors |
| Full EditMode baseline at `f3aee84` | PASS 231/231, 0 failed/skipped/inconclusive |
| Full EditMode after final code | PASS 235/235, 0 failed/skipped/inconclusive |
| PlayMode test inventory | 0 tests; NOT RUN |
| Builder menu | PASS; menu completed, wiring tests pass, GUIDs stable |
| Single-Editor Host start/Test spawn | PASS; current collider versions ready, one Player object |
| World snapshot/client match | PASS; 37,775 blocks, 1,314 water, 0 mismatches |
| Render/collider separation | PASS; 79,494 visual, 6,102 water, 73,392 collider indices |
| 49-block large change | PASS 49/49 on server/client via bounded snapshot fallback |
| Adapter disable/re-enable | PASS; stale client resynchronized and collision-ready |
| Test movement/jump | PASS; moved with W, Y 6.500→7.841→6.501 |
| Test→Ingame | PASS; same SessionId/generation, one player/camera, collision ready |
| Live Host restore | PASS; same SessionId, generation 1→2, same PlayerId |
| Restore object replacement | PASS; instance `-35522` despawned, `-36010` spawned |
| Disconnect and exit Play Mode | PASS; Editor returned stopped/ready |
| Remote external client | NOT RUN; no second process/MPPM package in this pass |
| Dedicated + 2 clients | NOT RUN; no dedicated/client builds launched |
| FMOD audible smoke | NOT RUN; component/assets preserved but no audible runtime scene executed |
| Performance comparison | NOT RUN; no stable pre-change profiler capture |

Test result XML: `C:/Users/egod1/AppData/LocalLow/DefaultCompany/mf2/TestResults.xml`.

During the Host restore smoke:

```text
before: session=459b9fbf-38d1-4d96-bca9-734ffbe2dc9b
        generation=1, player=5632322d-3588-4579-93a9-55f824ce14a2
        object=-35522, mappings/domainPlayers/objects=1/1/1
after:  same session and player, generation=2, object=-36010
        mappings/domainPlayers/objects=1/1/1
```

The only current Unity console error is the pre-existing optional `dev.tnayuki.unterm` native
bridge load failure. It is outside MF2 and was not modified. Earlier compile errors visible in the
retained console history were intermediate edits; the final compilation and suite are green.

### Evidence scope

- **Executed now:** final Unity compilation, 235 EditMode tests, empty PlayMode inventory query,
  builder menu execution, single-Editor Host/Test/Ingame, bounded large-change replication,
  disable/re-enable resynchronization, movement/jump, capture, disconnect, and Play exit.
- **Static verification:** asmdef direction, installed FishNet/VContainer package source, ownership
  and subscription paths, scene/prefab references, and persistence format code.
- **Historical only:** architecture commit `71ca0c5` reporting 203/203 and an API-driven Host
  vertical slice. Those numbers are not merged with the current test count.
- **Reference only:** Caveworks report commit `1e77cee9`; it supplies analysis patterns and risk
  prompts, not source code or MF2 runtime evidence.
- **NOT RUN:** external remote client, Dedicated plus two clients, audible FMOD smoke, and profiler
  baseline. These remain NOT RUN even where static code or an older commit suggests they work.

## Existing behavior regression status

- Prediction/Reconcile code and TimeManager physics mode: source preserved; automated structural
  and friction/physics tests pass. Remote movement observation is NOT RUN.
- Editor WorldHost/BlockPaintTool/RegionJSON: existing EditMode regression tests pass.
- Global scene/Outgame UI and builder wiring: generated wiring tests pass; Test→Ingame runtime
  transition passed; external ParrelSync flow is NOT RUN.
- Save/load: archive, corruption, backup, candidate swap tests pass. Live logical restore smoke
  passed; Rigidbody velocity/contact restoration remains unsupported.
- ProPixelizer: Test/Ingame share the same camera, post-processing, and chunk material setup;
  Outgame camera/post-processing and Player material are preserved by builder tests.
- Collision readiness: current handle/version projection regression passes; an injected native
  MeshCollider-cook failure was not run.
- SceneToolbar and Fluent IMGUI: their existing EditMode tests pass.

## Deferred work and limitations

1. Add automated PlayMode and multi-process Host/remote/Dedicated scenarios. The current project
   has zero registered PlayMode tests and no Multiplayer Play Mode package.
2. Add chunk streaming/interest management only when worlds exceed the current bounded complete
   snapshot ceiling; the present protocol is safe but still materializes a complete sparse world.
3. Persist terrain generator configuration only if future restored gameplay needs regeneration;
   current spawn recovery reads the authoritative stored snapshot and does not regenerate.
4. Define account/authentication-based ownership before mapping current connections to player IDs
   from an unrelated save. This pass only preserves an identity already present in the candidate.
5. Physics archive support would need explicit body DTOs and proxy reconstruction; PhysX contact
   cache/prediction history are not promised.
6. Run FMOD smoke on supported audio hardware and verify headless initialization policy before
   introducing an application audio port.

Unity Pipeline auto-tick was enabled at a 16 ms interval for this Editor session while running the
documented compile/test/play-mode commands. This is an Editor-session tool setting, not a project
asset change.

