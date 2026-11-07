# 04. VContainer, Unity, FishNet Lifecycle 통합

## 세 가지 종류의 생명주기

| 종류 | 생성 책임 | 종료/반환 책임 | 주의 |
|---|---|---|---|
| App/Session/Scene Scope | VContainer + 명시적 Coordinator | Scope owner | Scoped service는 별도 Destroy로 직접 파괴하지 않음 |
| NetworkObject | FishNet Scene/Spawn/Pool | FishNet Despawn/Pool | 클라이언트의 인스턴스는 서버와 다른 객체 |
| WorldCore/NativeContainer | SimulationWorld 또는 Editor WorldHost | 단일 runtime owner | borrowed scene view에서는 Core.Dispose 금지 |

## Lifecycle 시나리오 표

1. **Title 진입**: `NetworkBootstrap`/`ApplicationLifetimeScope` 구성 → Net infra/Facade 등록 → interactive process에서만 ClientPresentation scope. 초기화 중복 금지.
2. **Host 시작**: Net server/client 활성화 → Server Session Scope 단 한 개 생성 → GameSession.InitializeWorld/Start → ClientPresentation replica의 state 초기 동기화. Host가 GameSession.WorldCore를 클라이언트 View에 그대로 주입하지 않음.
3. **Ingame 씬 진입**: 서버가 FishNet Global Scene 로드 → SceneLifetimeScope는 ClientPresentation 아래 child → WorldHost가 replica borrowed World 바인딩 → collider 준비 → WorldReady ACK → PlayerSpawner.
4. **Spawn**: Player identity 결정/Domain register → prefabs 생성(또는 pool acquire) → 서비스 필요 시 DI가 네트워크 초기화 전에 완료 → per-spawn Bind → Rigidbody 및 Net object map register → FishNet Spawn → owner Input & View 준비.
5. **Despawn**: 입력/구독 정지 → Binder Unbind → map/physics registry Unregister → FishNet Despawn → pool return. 다른 세션에 있던 stale identity 사용 금지.
6. **Client disconnect**: Player unspawn, replica Clear/Dispose 정책을 의도에 맞게 호출, Scene 정리, Net infra는 연결 정책에 따라 유지. Event handler double-remove와 stale subscriptions 방지.
7. **Session Restore**: 새 SessionScope 후보 생성·restore 검증 → 새 generation 활성화 → old SessionStopping/Dispose → replica 데이터 이전 세대 폐기 → 재동기화/Collider/Spawn 준비.
8. **Dedicated**: client UI/camera/audio 필요 없음; physics collision projection은 서버에서. Scene presentation scope가 없어도 동작.

## DI 구성에서 절대 피할 것

- `MonoBehaviour`/`NetworkBehaviour`를 `builder.Register<T>(Lifetime.Scoped)`로 새로운 GameObject 없이 생성하려 시도.
- `Awake()`에서 `[Inject]` 주입된 값을 무조건 사용: 동적 Instantiate/scene object 주입 완료 시점 확인이 먼저.
- VContainer Scope의 자식 서비스가 상위 Scope에서 자동 Resolve될 거라고 가정.
- 모든 Entity가 독립 scope를 가져야 한다는 규칙. 자원 소유 없는 View에는 Scope 불필요.
- `OnDisable` / `OnDestroy` / `IDisposable` 세 군데서 같은 객체/NativeContainer에 중복 Dispose.

## 구현할 좁은 경계

- `SpawnContext`: SessionId, Generation(필요시), PlayerId, owner connection handle(ID는 service 내부 mapping), initial pose.
- `NetworkEntityBinding` 또는 기존 `PlayerPresentationBindingGate`: Reader/Identity/NetworkReady의 명시적 Bind/Unbind/Reset.
- `IEntityStateReader`: 권한 없는 View에서 state 조회.
- `IGameCommandSender`: local test or network transport. 서버 권한검사는 CommandAuthorizer에서만 최종 결정.
- EntryPoint lifecycle: 서버 Tick 구독이 켜진 시점/해제 시점이 한 경로에서만 발생하도록.

## 실패 경로에 대한 검증

- Scope.Build failure: 이후 이벤트가 남지 않으며 다른 Scope가 생성/Dispose된 상태로 남지 않는다.
- Spawn 중 collider registry 실패: 생성된 객체 Destroy/return, Identity mapping 및 Domain pending player 정리.
- Client가 world ready를 보내기 전 끊어짐: pending spawn 제거, stale ready 인정 금지.
- Host에서 두 논리 인스턴스가 서로 다른 `WorldCore` reference를 가진다.
- 복구 실패: old session에만 연결된 UI/physics/network object들은 기존 상태 유지.

기존 `ServerSessionCoordinator`와 `ClientPresentationCoordinator`가 이미 이 중 다수를 구현한다. **먼저 테스트로 정확성을 확인하고 필요한 부분만 교체**해야 한다.
