# Lifecycle and rollback

## Title and application startup

1. Title loads the generated persistent NetworkBootstrap root.
2. `ApplicationLifetimeScope` registers existing scene components and application services.
3. `NetworkBootstrap` survives global scene changes and configures FishNet to 30 Hz,
   `PhysicsMode.TimeManager`.
4. Interactive processes create the client-presentation child. Dedicated processes skip client
   presentation, UI, camera, listener, and scene-presentation scopes.

Duplicate bootstrap roots destroy the duplicate. `Awake` does not consume injected dynamic prefab
services; current player binding is explicit.

## Host

1. Outgame calls `SessionController.StartHost`.
2. FishNet server starts; the coordinator creates one server session scope and starts GameSession.
3. Local FishNet client starts while the authoritative and replica branches remain separate.
4. Server loads Test/Ingame globally.
5. The local client receives world/player snapshots; WorldViewBridge attaches the replica.
6. Mesh/collider rebuild completes; every loaded chunk's applied handle/version is verified before
   WorldReady is sent.
7. PlayerSpawner registers PlayerId, physics body, and NetworkObject, then FishNet spawns.

Repeated BeginServerSession calls return the same active session. Test↔Ingame scene replacement
does not create or dispose GameSession.

## Remote client

1. Join starts FishNet client only; no ServerSessionLifetimeScope exists.
2. The application-owned adapters write into ClientStateCoordinator.
3. A matching snapshot creates the replica; deltas cannot create it.
4. SceneLifetimeScope resolves the client reader, attaches the world view, builds colliders, and
   acknowledges readiness.
5. Spawn callbacks bind PlayerId and reader in either arrival order through the binding gate.

Disconnect clears bindings and replica state. A later connection starts with a fresh sequence and
must receive a new matching snapshot.

## Dedicated server

1. Bootstrap parses `-port`/`-mf2Port`, disables presentation/audio objects, and starts FishNet.
2. The first authenticated connection triggers the existing Test scene flow.
3. `ServerWorldCollisionBridge` creates collision-only chunk renderers from authoritative WorldCore
   and reports ready only when all loaded chunks have the current collider version.
4. Player spawn waits for both dedicated collision readiness and the connection's WorldReady ACK.

No client-presentation or scene-presentation scope is required on the server.

## Spawn and despawn

Spawn order:

```text
authenticated scene presence
 -> snapshot/collider WorldReady gates
 -> reserve connection spawn guard
 -> map connection to server-generated/restored PlayerId
 -> Instantiate prefab and BindSpawn context
 -> register Rigidbody and NetworkObject in session maps
 -> FishNet ServerManager.Spawn
```

Any failure reverses the completed registrations, releases the guard, removes newly created domain
identity, and destroys the unspawned instance. A restored domain identity is not deleted when only
its transient Unity spawn fails.

Despawn order releases the guard, removes runtime maps, optionally unregisters the domain player,
and then uses FishNet despawn or Unity destruction. Disconnect and scene-presence removal also
clear pending readiness state.

## Live restore

1. Persistence loads and validates pointer, manifest, checksums, RegionJSON, schema, and counters.
2. Coordinator creates a candidate session scope, restores world/players/clock, advances generation,
   starts it, and applies paused phase if needed.
3. Candidate references become active only after the candidate is complete.
4. `SessionStopping(old)` unsubscribes old tick replication, clears command mappings/readiness,
   and causes PlayerSpawner to capture `(connection, scene, PlayerId)` then despawn old objects.
5. Old session stops and its scope disposes exactly once.
6. `SessionStarted(candidate)` broadcasts fresh snapshots.
7. If a captured PlayerId exists in the candidate registry, the adapter rebuilds only the
   transient connection map. Otherwise normal registration creates a new identity.
8. Each active connection waits for the candidate generation's WorldReady ACK, then receives a new
   Player NetworkObject and new physics/map registrations.

An invalid archive fails before step 3, disposes only candidate resources, and leaves the previous
session and connected objects untouched. There is no automatic PlayerId/account reconciliation
for an unrelated archive whose player identities do not match current connections.

## Scene unload and application stop

- Scene unload: unsubscribe scene bridges, mark collision unavailable, detach borrowed WorldCore,
  dispose mesh/collider pools, and destroy scene views. Server session remains alive.
- Explicit disconnect: stop server session before FishNet shutdown, then return to Outgame.
- Scope GameObject `OnDisable`/`OnDestroy`: idempotent safety net, not the gameplay stop command.
- Editor assembly reload/quit: WorldHost lifecycle bridge disposes authoring hosts and native data.

## Failure rules

| Failure | Rollback |
|---|---|
| Server/session scope build | dispose partial child, clear coordinator fields, return NoSession |
| Candidate restore validation/build | dispose candidate only; preserve active session |
| Player spawn binding/registry failure | reverse maps/guard and destroy instance |
| Disconnect before WorldReady | remove pending spawn and stale ACK state |
| Scene view unload | detach/dispose view resources only; borrowed core survives |
| Replication consumer exception | log independently; never repeat WorldCore flush |
| Old generation/tick/revision | reject message without mutating replica |

