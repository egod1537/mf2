# 01. 코드 감사 기준선 / 실제 근거

> 근거 저장소 `egod1537/mf2`, 브랜치 `impl`, HEAD `7c511d0148708575a0bc555273446a73bd182371`. 아래는 **정적 코드/파일 확인 사실**과 **검증할 리스크**를 구분한다. 2026-11-07 최신으로 확인한 커밋이며 Codex 실행 시 HEAD가 달라질 수 있다.

## 실제 존재하는 구성 (확인)

| 구역 | 확인한 구현 | 중요한 특성 |
|---|---|---|
| Bootstrap | `Assets/Game/Lifecycle/ApplicationLifetimeScope.cs`, `Networking/NetworkBootstrap.cs`, `SessionController.cs` | VContainer root, `DontDestroyOnLoad`, NetworkManager, 전역 `Active`/`Instance` 경로 일부 공존 |
| Server Session | `ServerSessionLifetimeScope.cs`, `ServerSessionCoordinator.cs`, `ServerSessionRunner.cs` | Coordinator가 ChildScope를 생성, Session을 시작/복구/종료; FishNet `OnPostTick`으로 서버 Step |
| Domain | `GameSession.cs`, `SimulationWorld.cs`, `PlayerRegistry.cs`, `SessionCommands.cs`, `PlayerModel.cs` | 도메인은 일반 C# 타입; `SimulationWorld`가 `WorldCore` 소유; Session은 Tick, archive, Player, command 관리 |
| Client Replica | `ClientPresentationLifetimeScope.cs`, `ClientPresentationCoordinator.cs`, `ClientStateCoordinator.cs` | 서버 Session과 다른 Scope/WorldCore; SessionId/Generation/Tick 추적 |
| Scene/World | `SceneLifetimeScope.cs`, `WorldViewBridge.cs`, `WorldHost.cs` | Scene scope는 ClientPresentation child; `WorldHost`는 Editor 작성용 World 소유 또는 runtime borrowed World로 동작 |
| Player | `Entities/Player/BlockPlayerController.cs`, `BlockPlayerInputSource.cs`, `BlockPlayerView.cs`, `EntityViewBinder.cs`, `PlayerPresentationBindingGate.cs`, `PlayerSpawnContext.cs` | Prediction은 Controller, Input/View/Binder는 분리; 동적 Spawn/데이터/주입 시점 복잡 |
| Net | `GameCommandNetworkAdapter.cs`, `PlayerReplicationAdapter.cs`, `WorldReplicationAdapter.cs`, `PlayerSpawner.cs` | Command validation & identity map, world/player snapshot·delta broadcast, collision readiness & spawn gate |
| Physics | `UnityPhysicsAdapter.cs`, `PlayerNetworkObjectMap.cs`, `ServerWorldCollisionBridge.cs` | `Rigidbody`/Collider ↔ PlayerId, Dedicated는 별도 collider projection, Host는 client projection 활용 |
| Terrain | `PerlinTerrainGenerator.cs`, `TerrainConfig.cs`, `SpawnLocator.cs`, `WorldCore.cs`, `ChunkReadiness.cs` | Seed형 생성, NativeContainer 기반 Chunk Store, 버전 기반 readiness |
| Persistence | `SessionJsonStorage.cs`, `SessionArchiveSnapshot.cs`, `WorldStorageSession.cs`, `RegionJsonWorldStorage.cs` | 세션 JSON 메타, 세대/체크섬, 월드 region, 복원 경로 |
| Sound | `FMOD/MF2Audio/`, `Assets/Tests/FMOD/FmodPlaybackSmokeTest.cs`, `Assets/Resources/FMOD/` | 테스트 오디오 소스/Bank/설정 확인. 앱 전역 IAudioService 등록은 위 Scope에서 확인되지 않음 |

## 우선 감사할 결합점 — 의심을 **검증**할 것

### A1. 세 개의 수명제어 경로가 함께 존재
- `ApplicationLifetimeScope`가 NetworkManager 등을 등록하고 `Active` 보유.
- `NetworkBootstrap.Instance`, `SessionController.Instance` 기반 경로가 아직 존재; UI/세션 코드가 이 경로에 직접 의존하는지 전역 검색.
- Server session child scope를 Coordinator가 수동 생성/Dispose, client presentation scope는 별도 Coordinator가 생성.
- **위험 가설:** 애플리케이션 종료, Scene 변경, Server 중지, Restore에서 비슷한 종료 작업이 두 번 실행되거나 늦게 실행될 수 있음. **검증:** 이벤트 구독/해제 순서, 중복 OnDestroy, 아이템 강제 Destroy와 DI Dispose 쌍을 실행 로그/테스트로 증명.

### A2. Spawn/주입 시점과 바인딩 복잡성
- `PlayerSpawner.SpawnFor`는 `Instantiate(playerPrefab)` → `BindSpawn(PlayerSpawnContext)` → 물리/네트워크 Map 등록 → FishNet `ServerManager.Spawn()` 순서.
- `BlockPlayerController`가 현재 `TickNetworkBehaviour`이며 자체 SyncVar, Prediction, BindSpawn 처리.
- 클라이언트측은 `PlayerReplicationAdapter`의 spawned callbacks와 `PlayerPresentationBindingGate`를 이용하여 읽기 모델을 연결함.
- **검증:** 주입이 필요한 MonoBehaviour가 있다면 *서버 및 클라이언트* Spawn, Host, pool 재사용 시 모두 주입되는지. 없으면 불필요한 컨테이너 주입 프레임워크를 만들지 말 것.
- **잠재 리팩터링:** identity와 per-spawn context는 명시적인 Bind/Unbind, 서비스 의존은 VContainer 또는 상위 Scene Scope를 통해 주입; 모든 Prefab에 Scope 강제하지 않음.

### A3. 권위 vs Replica, 저장/복구 경계
- `GameSession.Restore`는 world+players+clock+Id/Generation/Seed를 복구하고 `Terrain = null`로 설정.
- `SessionArchiveSnapshot`에는 `Id, Generation, Phase, TickIndex, ElapsedSeconds, Seed, Players, World`가 있고 **Rigidbody 속도/회전/물리 스냅샷은 이 파일에 직접 담겨 있지 않다**.
- `WorldReplicationAdapter`는 세션 시작/인증 시 `CaptureSnapshot()` 전체 블록 배열을 Broadcast, 변경 시 Delta Broadcast.
- `ClientStateCoordinator`는 SessionId, Generation, world/player tick을 추적하며 일반적인 구세대 메시지 차단 로직이 존재.
- **검증:** 복원 이후 새 연결의 SpawnLocator/terrain config, Rigidbody 재구성, world collision readiness, 재동기화 그리고 이전 네트워크 Entity 정리; 직렬화 대상 확장은 명시적 이유가 있을 때만.

### A4. Scene ↔ World 소유권
- `WorldHost`는 `[ExecuteAlways]`이며 EditMode 작성용 World와 Runtime borrowed World를 지원하고 `ownsWorld` flag로 Dispose 대상을 구분한다.
- `WorldViewBridge`는 client world 이벤트에 구독하고 borrowed world를 Attach/Detach한다. `ServerWorldCollisionBridge`는 Dedicated에서 collider view를 따로 구성한다.
- **검증:** WorldCore 교체/OnDisable/씬 언로드 중 Renderer/Collider, NativeArray/ChunkLease가 이미 Dispose된 Core를 보유하는 순간이 없는지. 에디터 Paint, Scene Save/Load는 유지.

### A5. World readiness와 authority handoff
- `WorldViewBridge`는 `host.AttachRuntimeWorld()`/`RefreshRuntimeWorld()` 후 assets가 있으면 `MarkCollisionReady()` 호출.
- `ServerWorldCollisionBridge`는 Dedicated에서 MeshCollider를 재구성한 뒤 readiness 이벤트; Host에서는 client projection 활용이라고 주석에 명시.
- `PlayerSpawner`는 server collision + 해당 연결 world readiness를 만족해야 Spawn을 진행.
- **검증:** `HasEditorAssets`나 `rendererPool != null` 조건이 실제 Collider 생성 성공을 충분히 대변하는지, dirty chunk remesh 후 바뀐 충돌에 대한 ACK/재스폰 조건이 적절한지.

### A6. 서버 Tick과 물리
- `NetworkBootstrap`이 `TimeManager.SetPhysicsMode(PhysicsMode.TimeManager)`를 사용.
- `ServerSessionRunner`는 OnPostTick에 Domain Step을 연결하고 `BlockPlayerController`는 FishNet PredictionRigidbody를 사용.
- **검증:** 의도한 순서: input/prediction physics → physics snapshot 필요시 domain → delta capture → replication. 다른 `FixedUpdate`나 수동 Physics.Simulate가 중복 실행되지 않는지.
- **주의:** 순서 자체는 FishNet 4.7.3 현 프로젝트에서 PlayMode/Host 검증해야 한다.

### A7. Audio/툴/어셈블리
- 최신 커밋 `7c511d0`에 `FMOD/MF2Audio` 테스트 프로젝트, smoke test, `Assets/Plugins/SceneToolbar` 추가.
- `MF2.Game.Domain` asmdef의 FishNet/Unity 컴파일 의존성 추가 여부를 현 로컬에서 검사.
- **검증:** FMOD 네이티브 패키지·Bank 경로·실제 API 존재, Dedicated Audio 비활성화. 리팩터링 Phase에서는 FMOD 통합 테스트를 망가뜨리지 않는 것이 우선.

## 위험 분류 / 우선순위 결정

| 분류 | 예시 | 처리 |
|---|---|---|
| **확정된 사실** | `WorldHost` borrowed/owned flag가 존재, `PlayerSpawner`가 Instantiate/Bind 후 Spawn | 변경 설계 시 기준으로 사용 |
| **검증 필요한 위험** | 구독 중복, 전환 시 누수, 대규모 snapshot 부하, collision ready false-positive | 재현 테스트부터; 미재현 시 유지 가능 |
| **의도적 설계 선택** | `GameSession`은 순수 C#, FishNet Prediction 유지 | 훼손 금지 |
| **향후 기능** | FMOD 범용 AudioService, chunk streaming, SDF | 이번 리팩터링의 필수 통과조건 아님 |

## 감사 결과에 추가할 필수 증거

Codex는 최소 다음을 문서화한다:
1. C# 어셈블리 참조 방향(가능하면 asmdef dependency DAG).
2. GameSession/ClientState/WorldHost/NetworkObject별 생성-수명-해제 소유자 표.
3. Scene/Host/Client/Dedicated 이벤트 타임라인.
4. 복제·동기화 DTO의 식별자/세대/순서/인증 및 제한 정책.
5. 실제 Editor/PlayMode 로그·테스트 결과와 재현 불가능 항목.
