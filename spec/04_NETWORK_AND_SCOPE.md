# 04. P1 — 네트워크, 수명, DI 리팩터링 잔여사항

## 현재 구조 존중

ServerSessionLifetimeScope 아래 `GameSession/SimulationWorld`는 순수 C# 논리 상태, ClientPresentationLifetimeScope 아래 `ClientStateCoordinator`는 별도 복제 상태다. Host라도 두 객체를 공유시키지 않는다. Rigidbody/FishNet Prediction은 Player NetworkBehaviour의 권위 경계, WorldCore는 명시적 owner와 borrowed view의 수명 경계를 지킨다.

## Session 전환 이벤트 감사

`ServerSessionCoordinator.RestoreServerSession`은 candidate session/scope을 만들고 active를 candidate로 교체한 다음 기존 SessionStopping을 발행하고 신규 SessionStarted를 발행한다. 이때:

- `GameCommandNetworkAdapter.OnSessionStopping`의 `playerIdsByConnection.Clear()`가 이전 세션만 지우는지(이미 신규 mapping 들어온 경우 위험) 구독 순서를 테스트.
- `PlayerSpawner.OnSessionStopping`: 이전 플레이어에서 ID/NetworkConnection/Scene을 추출하여 `sessionRespawns`를 만들고 이전 NetworkObject를 despawn한다.
- `PlayerSpawner.OnSessionStarted`: 새로운 세션에 identity가 존재하는지 확인하고, 신규 world/collision readiness와 connection ACK가 맞는지 확인한 후 respawn한다.
- `WorldReplicationAdapter.OnSessionStarted`가 world snapshot 발행과 ready state 초기화에 관여한다. 각 이벤트 콜백 순서가 바뀌거나 예외가 나도 데이터가 손상되지 않도록 필요시 전이 절차 명확화.
- `PlayerSpawner.SpawnFor`의 `restoredMapping` fallback이 archived PlayerId를 중복으로 남기거나 무관한 새로운 PlayerId를 부여하지 않는지 테스트.

## Scope 수명/DI

- App/Session/Presentation/Scene Scope의 Resolve 방향은 상위 → 하위 생성이며, sibling scope 직접 Resolve는 금지.
- `ClientPresentationCoordinator`의 안전한 `PublishPresentationEvent`는 개선된 부분이다. 해제의 멱등성과 root Object 파괴 시 re-entrancy만 테스트하고 무조건 교체하지 않는다.
- `PlayerSpawner`, 각 ReplicationAdapter 등의 `OnEnable`/`Start`/`OnDisable`/`OnDestroy`에 있는 이벤트 register/unregister 대칭성, 중복 subscribe를 점검한다.
- `ApplicationLifetimeScope.Active`, `SessionController.Instance`, `NetworkBootstrap.Instance`는 실제 소비자를 확인하고 *외부 Bootstrap/Scene global lookup이 꼭 필요한지* 평가해 제거한다. 삭제로 단순히 DI 서비스로 접근할 수 없는 최초 Boot를 깨지 않는다.
- FishNet 생성 NetworkObject를 VContainer가 인스턴스화하지 않도록 하고, 실제 필요 시 서버·클라이언트 `InjectGameObject` 어댑터 및 inject-before-use 검증.
- 서버 physics/logic의 authoritative tick은 FishNet TimeManager의 기존 순서를 그대로 유지한다. Player `NetworkTransform`을 신설하지 않는다.

## UI, Audio, Editor 회귀 보존

최신 커밋의 Test/Ingame `ProPixelizerCamera`, `_PixelSize=5`, 단일 Chunk MaterialSet, OwnerCamera, 카메라 zoom을 보존. ProPixelizer 패키지 소스 변경 금지. FMOD 테스트 및 `AGENTS.md` Asset Co-location 규칙 유지. 이번 패키지는 occlusion dither 신규 기능 구현 명세가 아니다.

## 증빙

- 최소 scope 생성·Dispose DAG와 구독권 owner 표.
- Host session restore 전/후 도메인 SessionId, Generation, replica SessionId/Generation 및 PlayerId logging.
- 3회 반복 start/stop/restore 후 authority/replica/world/physics/SpawnGuard/Subscriptions 누수 없음.
