# Player MVC / Presentation Binding 경계

## 책임 변경

| 계층 | 타입 | 변경 전 | 변경 후 |
|---|---|---|---|
| Model | `PlayerModel` / `PlayerRegistry` | Controller가 가진 class 모델에 transform 상태를 되씀 | Session Registry만 immutable struct를 소유하고 reader는 snapshot 복사만 반환 |
| System | `HealthSystem`, `CommandProcessor` | 규칙 경계가 불명확 | Unity 참조 없는 Domain System만 Registry 변경 경로를 사용 |
| GameObject Controller / NetworkAdapter | `BlockPlayerController` | 입력·Prediction·authoritative 모델 갱신·View를 함께 처리 | owner 입력과 FishNet Prediction 순서를 유지하고 domain `PlayerId` 동기화/명령 접착만 담당 |
| Input | `BlockPlayerInputSource` | Controller 내부 키보드 조회 | Input System 값을 tick 입력 DTO로 정규화 |
| View | `BlockPlayerView` | Controller가 카메라와 리그를 직접 조작 | owner 카메라, visual child, Rigidbody 기반 시각 애니메이션만 담당 |
| Binder | `EntityViewBinder` | Controller 자체를 매 frame polling | `PlayerId` + client `IEntityStateReader`를 event/dirty 기반으로 읽어 View에 즉시 투영 |
| Readiness | `PlayerPresentationBindingGate` | Spawn/DI callback 순서를 암묵적으로 가정 | replica dependency와 client NetworkReady가 모두 준비된 뒤 한 번만 Bind하고 pool reuse 때 둘 다 제거 |
| Replication | `PlayerReplicationAdapter` | 서버 Controller에 `PlayerModel` 참조를 연결 | Tick 완료 snapshot/delta를 client replica에 적용하고 client-spawned prefab에 explicit presentation context를 전달 |

## 소유권과 식별자

- `PlayerModel`은 readonly struct이며 Registry/replica 밖으로 나가는 값은 복사본이다.
- `PlayerId`는 session-scoped GUID다. FishNet `ObjectId`, connection id, Unity instance id와
  구분하며 같은 session에서 재사용하지 않는다.
- `BlockPlayerController`의 FishNet `SyncVar<Guid>`는 이 식별자만 전송한다. 서버
  `PlayerRegistry` 또는 `GameSession` 참조를 보유하지 않는다.
- Host의 `ServerSessionLifetimeScope`와 `ClientPresentationLifetimeScope`는
  `ApplicationLifetimeScope` 아래 형제다. View는 client scope의 `ClientStateCoordinator`를
  통해서만 바인딩하며 authority `GameSession`을 resolve할 수 없다. Dedicated server는
  client/scene presentation scope를 생성하지 않는다.

## Bind / Unbind

서버 GameObject 생성 시에는 장기 서비스 주입과 spawn별 데이터를 분리한다.
`PlayerSpawnContext`는 `SessionId`, `PlayerId`, owner connection 숫자, 초기 pose만 가진
immutable DTO이며 FishNet `NetworkConnection`, resolver 또는 GameObject를 담지 않는다.
`PlayerSpawner`가 prefab을 생성한 뒤 network spawn 전에 한 번 `BindSpawn`하고, 재사용되는
presentation subscription은 아래 gate의 Bind/Unbind 수명으로 별도 관리한다.

1. `PlayerReplicationAdapter`가 client-side FishNet spawn을 관찰한다.
2. Adapter가 client scope의 reader를 gate에 전달하고 Controller가 FishNet client callback과
   동기화된 `PlayerId`를 gate에 전달한다. 두 순서와 무관하게 모두 준비된 뒤에만
   `EntityViewBinder.Bind(PlayerId, IEntityStateReader)`를 호출한다.
3. Binder는 먼저 change event를 구독하고 즉시 전체 snapshot을 한 번 읽어 late join과
   respawn을 처리한다.
4. 같은 reader/id 재호출은 구독을 중복하지 않으며 revision이 같은 View 적용도 생략한다.
5. remove, despawn, disable, destroy 및 pool 반환에서는 중복 안전한 `Unbind()`가 구독과
   ID를 지우고 `ResetView()`로 placeholder 상태를 복구한다. 다음 spawn은 이전 reader와
   ID를 재사용하지 않는다.

Player delta는 `GameSession.Step`이 command 처리와 world flush를 끝낸 뒤 만들어진
`SessionTickResult`에서 전송된다. Binder는 FishNet DTO/RPC/SyncType을 모르며 View는
Registry, network, singleton을 조회하지 않는다.

오프라인 Player는 Domain replica가 없으므로 Binder를 만들지 않고 공통 Input/View만
재사용한다. World는 블록별 Binder를 만들지 않으며 기존 `WorldViewBridge`와 chunk dirty
pipeline을 유지한다.

## Physics / NetworkObject 경계

- Dynamic Player의 position, rotation, velocity 원본은 서버 Unity `Rigidbody`와 기존
  FishNet prediction/reconcile 경로다. `PlayerModel`에는 transform truth를 추가하지 않는다.
- `PredictionRigidbody.Simulate()`는 pending force 적용이며 물리 세계를 advance하지 않는다.
  실제 `Physics.Simulate()`는 `PhysicsMode.TimeManager`인 FishNet `TimeManager`가 tick당 한
  번 수행한다. MF2 adapter는 `PhysicsScene.Simulate`를 호출하지 않는다.
- `IPhysicsQuery`/`IPhysicsCommands`는 `float3`, `quaternion`, value snapshot만 노출한다.
  SessionScope의 `UnityPhysicsAdapter`가 `PlayerId`와 `Rigidbody`/Collider를 매핑하고,
  `ServerSessionRunner`의 post-physics `GameSession.Step`에서 읽을 수 있다.
- `PlayerNetworkObjectMap`은 서버의 stable `PlayerId`와 현재 FishNet `NetworkObject`를
  연결한다. Spawn 1이 Unregister되기 전에는 같은 ID를 Spawn 2에 연결할 수 없으며,
  despawn/pool 반환 시 physics body와 network object 매핑을 먼저 지운다. Network scene
  교체로 GameObject만 재생성할 때는 connection→`PlayerId`와 Domain 모델을 유지하고 새
  NetworkObject를 같은 ID로 등록한다. 실제 연결 종료나 Session 종료에서만 Domain ID를
  제거한다.
- `GameCommandNetworkAdapter`는 client가 보낸 PlayerId를 신뢰하지 않는다. 인증된 FishNet
  connection을 서버 map으로 변환한 뒤 `GameSession.SubmitCommand`를 호출한다.

## Terrain collision readiness

- 서버 `GameSession` 시작 전 `IWorldInitializer`가 유한 terrain을 `WorldCommand`와
  `Flush` 경로로 한 번만 생성한다. `WorldHost`는 이 authoritative world를 소유하지 않는다.
- 클라이언트 replica의 chunk mesh가 `MeshCollider`에 적용되면 session/generation/tick을
  포함한 ready ACK를 보낸다. 새 world delta를 받으면 readiness가 다시 invalidated된다.
- `PlayerSpawner`는 client collision ACK 전에는 spawn을 pending으로 유지한다. Dedicated는
  같은 versioned chunk mesh pipeline으로 별도 collision-only projection을 준비한 뒤 spawn한다.
- Host의 Binder/View는 계속 client replica만 읽는다. 같은 프로세스에 있는 server
  `SimulationWorld`를 presentation source로 resolve하지 않는다.
