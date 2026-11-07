# 02. 목표 아키텍처 — 논리 계층, Scope, Data Flow

## 핵심 설계

- Application은 전역 인프라를 보유. 네트워크 연결과 실제 게임 세션은 별개.
- ServerSessionScope가 **유일한 authoritative GameSession**과 Domain World를 소유.
- ClientPresentationScope가 **별도 replica state**를 소유. Host라도 서버 Domain World를 Client presentation에 직접 주입하지 않는다.
- SceneScope는 **씬에서 살아있는 View, Controller, Binder** 의존성을 처리. Scene 언로드 ≠ 반드시 Session 종료.
- `gameojbect`(프로젝트 용어)는 GameSession 결과를 Unity 게임 객체에 바인딩하는 **Controller/View/Binder + 필요한 NetworkAdapter/PhysicsComponent** 계층.
- Entity별 Scope는 그 Entity가 독립된 Disposable 서비스/컨텍스트를 소유하는 경우에만 추가한다. 단순 블록이나 Chunk에 Scope 1개씩 만들지 않는다.

## Scope 구조 (실제 주입 관계)

```text
ApplicationLifetimeScope
├── Global options, NetworkManager, NetworkSession facade
├── ServerSessionCoordinator ─── creates ServerSessionLifetimeScope
│                                  └── Domain: GameSession/PlayerRegistry/SimulationWorld
├── ClientPresentationCoordinator ─── creates ClientPresentationLifetimeScope
│                                       ├── ClientStateCoordinator (one per client)
│                                       └── SceneLifetimeScope
│                                           ├── InGame WorldViewBinder
│                                           ├── Game UI, player view/binder
│                                           └── optional per-entity LifetimeScope
└── Client audio presentation (when enabled, not dedicated server)

FishNet NetworkObject lifecycle (external to container)
  server/client create or pool-acquire -> dependencies ready -> bind identity
  -> spawn/enable -> unspawn/unbind -> pool return/destroy
```

> 형제 scope끼리 DI resolve하지 않는다. Application 스코프가 ChildScope 서비스를 직접 resolve하려고 하면 구조가 깨진다. 전역 Net Adapter에 client replica가 필요하면 이미 도입된 Coordinator/event bridge를 사용하거나, scope 소유권과 생성/해제를 분명히 한 Port를 쓴다.

## 도메인 모델과 MVC

```text
Network/Local user input
  -> MonoBehaviour Controller (input capture/validation of UI-level state)
  -> Command Sender/Proxy
  -> Server-side authorization / GameSession command ingress
  -> pure C# Game Systems -> Model(struct) in Registry, WorldCore
  -> Tick snapshot / delta
  -> FishNet ReplicationAdapter
  -> ClientStateCoordinator (read-only replica)
  -> EntityViewBinder(EntityId / PlayerId) -> View
```

### Domain에 포함

- `PlayerModel` 등 값 타입, Entity/Player Registry, 명령·검증, 게임 시스템, Tick·Snapshot, Terrain generation state.
- `IPhysicsQuery` 등 **Unity 타입이 아닌** 계약 (현재 `Unity.Mathematics`는 이미 사용 중이며 허용).

### Unity/GameObject에 포함

- `BlockPlayerController : TickNetworkBehaviour`는 FishNet Prediction entrypoint이며, 이름만 Controller여도 Domain System이 아니다.
- `BlockPlayerInputSource`, `BlockPlayerView`, `EntityViewBinder`, `PlayerPresentationBindingGate`, Rigidbody/Collider.
- 오브젝트의 정체성은 `PlayerId`와 세션 식별자/Generation. struct를 Controller에서 장기간 복사/변경하지 않는다.

### Physics 데이터 원본

| 종류 | 데이터의 유일한 변경 권한 | 복제/표현 |
|---|---|---|
| Dynamic Rigidbody | Physics + FishNet Prediction path | 몸체 상태 스냅샷은 읽기; 프레젠테이션 보간 |
| Player health/inventory | 서버 Domain Registry/System | State replication → View |
| Static blocks | 서버 SimulationWorld/WorldCore | world snapshots/deltas → Chunk Render/Collider |
| Client WorldCore | ClientStateCoordinator replica | 서버 명령으로만 갱신, 권위로 승격되지 않음 |

**무조건 모든 Entity의 Position을 WorldState로 이동하지 않는다.** 필요하면 `PhysicsBodySnapshot`으로 읽되 정식 single-writer를 보존한다.

## 설계 원칙

1. **Single Writer / Multiple Readers**: authoritative state 변경 입구는 Command/Systems. Server/Client 양쪽에 독립 작성 모델을 노출하지 않는다.
2. **One Owner / One Dispose**: WorldCore/NativeContainer, Mesh, Collider, ChildScope, subscriptions 각각 소유자 식별.
3. **Data-Driven Bindings**: MonoBehaviour는 Identity/Reader를 bind; 데이터 구조를 소유하지 않는다.
4. **Explicit lifecycle**: `Initialize → Start → Pause → Stop → Dispose`, `Spawn → Bind → Unbind → Despawn`, `Snapshot → Restore`가 테스트로 확인 가능.
5. **Bridge, not leak**: FishNet·FMOD·Unity 구현체는 Domain asmdef에서 역으로 참조되지 않는다.
6. **Do not over-generalize**: `ReadModel`/`WriteModel`/Proxy/Scope를 모두 객체마다 억지로 구현하지 않는다. 현재 기능과 테스트를 개선하는 최소 추상화만 도입.
7. **Release order is defined**: 이벤트 발행/수신 중에 World가 Dispose되지 않도록 stable boundary 보장.

## Caveworks 패턴 적용 범위

`caveworks` 저장소는 **소스 코드가 아니라 분석 보고서/타입 인벤토리**다. 설계 아이디어만 참고한다.
- 채택: Read/Write 접근권한 분리, ControllerProxy의 Command transport seam, SpawnContext, 의도적인 EntityScope, Chunk data/collision/render lifecycle 분리, ModelState/Repository 경계.
- 제외: Caveworks 구현체 복사·디컴파일 코드/에셋, 전체 FMOD 의존성 복제, SDF/Marching Cubes 이식, Game.Domain의 혼합 의존성, 전역 MULTIPLAY 플래그 분기.
- 네트워크: Caveworks 분석의 동기화 공백 가능성은 결론이 아니라 경고다. MF2 설계는 **server validates → mutates once → replicates to all interested clients**를 명시적으로 테스트.
