# Runtime architecture

## Dependency and scope topology

```text
Persistent NetworkBootstrap GameObject
└── ApplicationLifetimeScope
    ├── NetworkManager / SessionController facade / network adapters
    ├── ServerSessionCoordinator
    │   └── ServerSessionLifetimeScope (server or Host only)
    │       ├── GameSession + PlayerRegistry + CommandProcessor + SessionClock
    │       ├── SimulationWorld -> authoritative WorldCore
    │       ├── UnityPhysicsAdapter / PlayerNetworkObjectMap
    │       └── ServerSessionRunner
    └── ClientPresentationCoordinator (interactive process only)
        └── ClientPresentationLifetimeScope
            ├── ClientStateCoordinator -> replica WorldCore
            └── SceneLifetimeScope
                └── UI, WorldViewBridge, WorldHost, player View/Binder
```

Server session and client presentation are sibling application children. A Host therefore has two
WorldCore instances: an authoritative server world and a client replica. Scene scopes inherit only
the client presentation branch and cannot resolve `GameSession`.

## Data ownership

| Data | Authoritative writer | Readers/projection |
|---|---|---|
| Session phase, tick, rules | server `GameSession` | replication snapshots, diagnostics |
| Player model/health | server `PlayerRegistry` and domain systems | player snapshot/delta -> client reader -> Binder/View |
| Static block/chunk data | server `SimulationWorld.WorldCore` | managed delta, RegionJSON, client replica |
| Client blocks/player state | `ClientStateCoordinator` applying server messages | WorldHost/View; never server authority |
| Predicted Player transform | Unity Physics + FishNet PredictionRigidbody | reconcile and visual hierarchy |
| Player identity mapping | transient FishNet adapter map | spawn/command/physics adapters |
| Mesh/collider objects | scene/dedicated projection | Unity renderer and physics |
| Archive metadata/world | persistence coordinator + RegionJSON | candidate session restore |

Every WorldCore has one disposer. A runtime WorldHost can own its EditMode authoring core or borrow
a client replica, never both. The `ownsWorld` flag is the enforced boundary.

## Authoritative tick

```text
FishNet inbound/prediction input
  -> FishNet TimeManager physics simulation
  -> OnPostTick
  -> ServerSessionRunner
  -> GameSession.Step
  -> validate/drain queued domain commands
  -> WorldCore.Enqueue / one WorldCore.Flush
  -> copy native Changes + DirtyChunks into managed TickDelta
  -> managed TickCompleted consumers (replication; save captures a completed boundary)
  -> FishNet state send/outbound
```

The domain never calls Unity physics simulation. Dynamic Rigidbody position and velocity remain
physics-owned. Domain ports expose `Unity.Mathematics` value DTOs only.

## Command trust boundary

```text
Input/UI -> IGameCommandSender
  -> reliable FishNet broadcast with client-local sequence
  -> authenticated NetworkConnection supplied by FishNet
  -> server connection-to-PlayerId lookup
  -> sequence/rate/payload/block/reach validation
  -> GameSession.SubmitCommand
  -> single authoritative mutation and replicated result
```

The network payload contains no trusted PlayerId, role, position, or connection ID. Reconnect and
session stop clear the monotonic sequence guard. During live restore, `TryRestoreConnection` may
rebuild only a transient connection map to a PlayerId already present in the candidate registry;
it rejects aliasing one PlayerId to two live connections.

## Replication ordering

- World and player snapshots establish a replica for `(SessionId, Generation)`.
- Deltas before the matching snapshot, cross-session data, old generations, and stale ticks or
  player revisions are rejected.
- Collision readiness is invalidated by replacement/delta and acknowledged only after the local
  synchronous mesh/collider rebuild returns and every loaded chunk matches the current
  `ChunkHandle` generation and data version.
- Player spawn requires authenticated scene presence, matching client WorldReady, and server-side
  collision readiness.
- Restore increments generation, broadcasts replacement snapshots, despawns old Player objects,
  and respawns them only after the new readiness barrier.

The current snapshot protocol is deliberately complete and sparse rather than streamed. This is a
known scale boundary, not an implicit promise of chunk streaming.

## Player MVC/GameObject binding

`PlayerModel` is a struct owned by `PlayerRegistry`. `EntityViewBinder` receives a stable PlayerId
and read-only reader, then projects immutable snapshots to `BlockPlayerView`. The Binder does not
write the model or Rigidbody.

`BlockPlayerController` remains a FishNet prediction adapter. Input, owner camera, facing pivot,
view, and binding gate are co-located in `Assets/Entities/Player/` according to `AGENTS.md`. Mouse
facing rotates only `FacingPivot`, not the physics root or owner camera. The capsule uses a
zero-friction PhysicsMaterial so horizontal wall contact cannot cancel gravity.

No entity LifetimeScope or forced VContainer prefab injection is present because the current
Player prefab has no injected service dependency. Per-spawn identity uses explicit
`PlayerSpawnContext`; binding reset and FishNet despawn/pool reset remain separate from scope
disposal.

## World and persistence

- Editor: WorldHost owns authoring WorldCore, serialized blocks, painting, mesh preview, and
  RegionJSON save/load.
- Client runtime: WorldHost borrows the client replica and owns only render/collider resources;
  its readiness query validates current per-chunk collider projections rather than asset presence.
- Dedicated runtime: `ServerWorldCollisionBridge` projects the authoritative core into
  collision-only chunk renderers.
- Server runtime: `SimulationWorld` is the only authoritative block owner.

Session storage keeps format version 2 metadata, checksums, generation directories, RegionJSON,
atomic pointer publication, and a previous-pointer backup. Restore fully validates and starts a
candidate scope before swapping. A failed read/build disposes only the candidate and preserves the
live session.

Logical persistence covers SessionId, generation, phase, tick/elapsed time, seed, Player models,
and blocks/chunks. It does not claim full Rigidbody or PhysX deterministic restoration.

## Audio and third-party boundaries

FMOD remains presentation-only. The existing Editor smoke component and bank/settings assets are
preserved; no Domain reference or global `AudioService.Instance` was introduced. Dedicated audio
integration is not claimed. ProPixelizer and SceneToolbar remain external/presentation assets and
were not modified internally.

