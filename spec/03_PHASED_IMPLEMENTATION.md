# 03. 단계별 실행 플랜 / DoD Gates

## Phase 0 — 현 상태 보존, 기준선/재현 테스트 확정 (변경 최소)

**읽을 파일**: `AGENTS.md`, `README.md`, `Assets/Game/`, `Assets/World/Runtime/`, `Assets/Entities/Player/`, `Assets/Game/Editor/MultiplayerSliceBuilder.cs`, `.asmdef`, `ProjectSettings/EditorBuildSettings.asset`.

**작업**
1. 현 작업트리의 변경 내역/기준 SHA 기록, Codex 이전 작업과 충돌 확인.
2. 기존 테스트가 실제로 빌드/통과하는지 기록. Unity Editor 없으면 cmdline 시도 실패와 원인 명시. 테스트를 위한 가짜 stubs/무관한 테스트 삭제 금지.
3. 네트워크 모드별 구체적 소유권 DAG 및 lifecycle 그림 작성.
4. `GameSession`/ClientReplica/WorldCore/WorldHost/Pool/Collider/Audio의 생성·해제 경로를 표로 작성.
5. 확정 결함과 미재현 리스크를 나누고 P0 변경 후보에 우선순위 부여.

**Gate**: `docs/architecture/refactor-baseline.md` 작성, 기존 동작 목록 확인. 환경 문제는 기록, 추측 통과 금지.

## Phase 1 — DI/Scope 생명주기 정리 (P0)

**수정 후보**: `ApplicationLifetimeScope`, `ServerSessionLifetimeScope`, `ServerSessionCoordinator`, `ClientPresentationLifetimeScope`, `ClientPresentationCoordinator`, `SceneLifetimeScope`, `ServerSessionRunner`, `NetworkBootstrap`, `SessionController`.

**작업**
1. 각 Scope당 하나의 생성/파괴 owner 지정. `DisposeCore`/`Destroy(GameObject)`/MonoBehaviour `OnDestroy`에서 중복 해제 안전성 보장.
2. App infra, Session runtime, Client replica, Scene presentation 간 의존 방향 감시. sibling scope의 서비스 직접 Resolve 금지.
3. `Active`/`Instance` 경로 조사; 기존 public scene/UI 호환성을 유지하며 하나씩 facade 주입으로 치환. Unity bootstrap 및 editor API는 필요한 좁은 static shim 허용.
4. `ServerSessionRunner`의 `IInitializable`/FishNet TimeManager 준비 시점·구독 1회·재시작 재구독 검증.
5. Scope 구성 실패 시 rollback, SessionRestore 중 previous scope 보존, coroutine/event unsubscription의 idempotence를 테스트.

**Gate**: 반복 Host connect/disconnect 및 session restart 3회, Host 내 Domain과 Client replica 서로 다른 객체, Dispose 중 예외에도 정리 완료.

## Phase 2 — MVC/GameObject Binding/Spawn 정리 (P0)

**수정 후보**: `Assets/Entities/Player/BlockPlayerController.cs`, `BlockPlayerInputSource.cs`, `BlockPlayerView.cs`, `EntityViewBinder.cs`, `PlayerPresentationBindingGate.cs`, `PlayerSpawnContext.cs`, `Assets/Game/Networking/PlayerSpawner.cs`, `PlayerReplicationAdapter.cs`.

**작업**
1. `PlayerController`는 Prediction 및 FishNet callbacks를 유지. Input/View/Binder 책임 중복을 실제로 비교하고 유일한 state owner를 지정.
2. Model(struct)은 Registry 소유. Binder는 PlayerId·Reader로 Snapshot만 읽고 View 업데이트. Controller가 Domain의 PlayerModel을 임의로 복사하여 수정하지 못하도록 한다.
3. SpawnContext는 per-spawn identity+initial pose. FishNet이 Unity object를 생성하므로, **서비스 주입이 실제로 필요한 경우** 서버/클라이언트 생성 경로에 주입 어댑터. 주입 완료 전 서비스 사용 금지.
4. `Bind → Spawn → Unbind → Pool-return`이 중복 호출·실패·Host·Late join에서도 안전. 같은 PlayerId에 stale binding, destroyed view, Reader double-subscribe 방지.
5. Player-level Scope는 독립 Disposable이 실제 필요한 경우에만 생성. Player마다 임의 오버헤드 도입 금지.

**Gate**: Single player, Host+Client, Dedicated+2 Clients에서 자기 카메라/owner input, 타 플레이어 표현, spawn/despawn/rejoin, prediction, jump/landing 정상.

## Phase 3 — Physics/World readiness/WorldHost 책임 정리 (P0/P1)

**수정 후보**: `UnityPhysicsAdapter`, `PlayerNetworkObjectMap`, `ServerWorldCollisionBridge`, `WorldViewBridge`, `WorldHost`, `ChunkReadiness`, `ChunkRenderPipeline`, `PlayerSpawner`.

**작업**
1. Rigidbody가 authority인 항목과 Domain이 authority인 항목을 표로 고정. Domain은 Physics contract로 읽기/명령.
2. `PhysicsMode.TimeManager`와 Player prediction을 유지하고 단일 physics tick policy 문서화. 불필요한 `PhysicsScene.Simulate` 추가 금지.
3. collision readiness가 실제 collider mesh build success를 증명하는지 테스트. `HasEditorAssets` 같은 구성요건과 collider actually ready를 혼동하지 않도록 신호 명확화.
4. WorldCore borrowed/owned 관계 및 ChunkRendererPool teardown의 idempotence/Job dependency completion 검증.
5. Host/Client/Dedicated에서 render-only와 collision-only projection의 중복 여부 및 필요성 확인 후 최소 정리.

**Gate**: 지형 생성·메시·콜라이더 완료 전 Spawn 금지, 이동/점프/착지, 월드 변경 시 dirty-chunk 재구축, NativeContainer/mesh double-dispose 없음, Editor painting 회귀 없음.

## Phase 4 — 명령·네트워크 복제 신뢰 경계 (P1)

**수정 후보**: `GameCommandNetworkAdapter`, `WorldReplicationAdapter`, `PlayerReplicationAdapter`, `ConnectionCommandGuard`, `ServerWorldCommandAuthorizer`, `ClientStateCoordinator`, `SessionController`.

**작업**
1. ingress에서 인증된 연결 ↔ PlayerId 매핑, Replay/Sequence, command size/extent/block-id/command-rate cap 검증. `RequireOwnership=false` 성격의 방송/호출은 접근 통제 확인.
2. SessionId+Generation+Tick/Player Revision gating의 실제 처리 순서 테스트. 재연결·서버 세션 교체 시 이전 Packet/ACK 무시.
3. 서버 변경은 한 번만 발생; Host local view 중복 적용 금지; Host + 2 Clients에서 서로 동일한 최종 지형/플레이어 상태.
4. 전체 World snapshot을 매번 큰 단일 Broadcast로 보내는 현재 방식의 규모·할당·송수신 제한 측정. **P1에서 필수 스트리밍 새로 개발 금지**. 문제가 재현될 때 간단한 chunk-limited batching 또는 후속 설계안만 제시.
5. 전역/씬 이벤트 등록 순서, World ready/Spawn 순서, Player snapshot와 World snapshot 순서 뒤집힘 내성 확보.

**Gate**: Late join, disconnect/reconnect, stale/out-of-order delta, invalid block id/oversized fill, unauthorized command 테스트 통과. 외부 transport가 없으면 domain/adapter 단위 테스트라도 실행하고 미실행 정확히 기록.

## Phase 5 — Persistence/Restore 안정성 (P1)

**수정 후보**: `GameSession`, `SessionArchiveSnapshot`, `SessionJsonStorage`, `SessionPersistenceCoordinator`, `ServerSessionCoordinator`, `WorldStorage*`, `PlayerSpawner`.

**작업**
1. archive의 현재 범위가 logical world/player/tick/seed임을 문서화. `Rigidbody`/PhysicsContact 완전 재현과 혼동 금지.
2. 복원은 새 후보 world/session 준비와 검증 성공 후 old session 교체; 저장 중 command flush와 NativeContainer lease 안정성 보장.
3. `GameSession.Restore`가 `Terrain=null`로 설정하는 점과 `PlayerSpawner`의 TerrainConfig 기반 Spawn 위치 선정을 연계 검증. 저장된 generator settings가 꼭 필요하면 format version·migration 계획 수립.
4. Session 복원 시 Generation 증가에 따라 client replica 완전 교체, Collider 재생성, physics body re-register; 재연결 시 stale ACK/identity map 정리.
5. `SessionJsonStorage`의 pointer, backup, checksum, temp file behavior를 기존 테스트로 보존. 데이터 손실 방지.

**Gate**: 정상 저장→재시작→불러오기, 잘못된 저장/중간 실패 rollback, 기존/새 클라이언트 일관성. 기존 region format 파괴 없음.

## Phase 6 — FMOD/asmdef/코드 청소 (P2)

**작업**
1. asmdef 의존 DAG에서 순환/상위 계층의 Unity/FishNet 역참조 금지. 불필요한 dll/ref만 제거, 확정된 사유 없는 대규모 파일 재배치는 보류.
2. FMOD smoke 테스트를 유지. 실제 FMOD runtime integration이 설치·검증 가능하면 `IAudioService` 최소 인터페이스를 Application/Presentation 경계에 도입(서버 오디오 noop/무등록). 기존 출력이 없는데 새 Audio 구현을 필수 개발하지 않음.
3. 코드 중복/빈 어댑터, 불필요한 static fallback/폴링 제거는 동작 동일성 테스트 후에만.
4. README에 실행/씬 흐름, 책임/소유권 표, Host/Client/Dedicated 검증 결과 반영.

**Gate**: 변경된 모든 asmdef 컴파일, Headless 서버 오디오 에러 없음, FMOD smoke 정상 또는 환경 제한 명확히 기록, 다른 Phase 회귀 전부 유지.

## Stop / Rollback 규칙

- 기존 동작이 망가지면 다음 Phase 진행 금지, 해당 Phase의 변경 범위에서 원인을 찾아 수정한다.
- Saved Scene/Prefab GUID가 깨지거나 에디터 Paint 기능이 소실되면 실패.
- 알 수 없는 라이브러리 API를 상상해서 쓰지 말고 설치 버전에서 확인한다.
- 실제 테스트가 불가능하면 마지막으로 확실히 통과한 게이트까지만 구현·보고하며 미완료를 명시한다.
