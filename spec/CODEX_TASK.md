# Codex Task — MF2 최신 커밋 리팩터링 결함 보완

## 즉시 수행할 요청

`egod1537/mf2`의 현재 `impl` 작업 트리에서 리팩터링을 계속하라. 이 패키지의 문서를 순서대로 읽고, **새 기능 확장보다 기존 구현의 정확성 및 안정성 보완을 먼저 완료**하라.

**감사 스냅샷:** `53fd8bda2d7b14808e98ea414f8dbcb9522a1c3a`.

### 착수 조건

1. `git status`, `git log -5`, `AGENTS.md`, Unity/ProPixelizer/FishNet/asmdef 및 기존 테스트를 재확인. 사용자의 작업 중 변경을 손상시키지 말 것. HEAD가 다르면 diff를 먼저 분석하고 **이미 해결된 항목은 수행하지 말 것**.
2. `README.md`와 최상위 01~06 문서, `original_architecture_spec/`의 설계 목표를 읽되 최신 파일을 우선한다.
3. 문제를 아래처럼 분류: `CONFIRMED_IN_CODE`(정적 불일치), `RUNTIME_UNVERIFIED`(실행 확인 필요), `NOT_APPLICABLE`(현재 코드에서 해결됨).
4. Unity 환경에서 기준 EditMode 테스트 및 두 가지 네트워크 스모크 경로의 실행 가능성을 기록. 실행 불가하면 이유 기록.

### P0-1: 물 메시 파이프라인과 충돌 분리

- `BlockRenderPass.Water = 3`와 **4개 머티리얼**을 읽는 `ChunkMaterialSet`에 비해, `ChunkMeshBuildBuffers/Job/Applier`가 **3개 렌더 서브메시**만 산출하는 최신 코드의 불일치를 해결한다.
- 물이 opaque submesh에 섞이지 않도록 **Water 전용 index buffer/submesh**를 처리하고 렌더러의 머티리얼 순서를 일치시킨다.
- `ChunkRenderer.Apply`의 `meshCollider.sharedMesh = mesh` 경로를 **고체 전용 별도 충돌 메시**로 바꾼다. 물·비고체·투명 렌더를 고체 충돌로 묶지 않는다. 기존 solid block collision은 보존한다.
- `ChunkReadiness`, `AreAllLoadedChunkCollidersReady`, `WorldViewBridge`가 **실제로 물이 제외된 최신 충돌 메시 커밋 완료**에 따라 Ready가 되도록 한다.
- Solid-Liquid 및 Liquid-Air 면 가시성, 청크 경계와 메시 빌드 버전 가드를 검증한다. 과도한 메시 복사를 유발하지 않는 구현을 선호하되 정확성 우선.

### P0-2: 액체와 복원된 월드의 Spawn 안정성

- `SpawnLocator.TryLocate`의 `!IsAir` 기준이 물을 지지 가능한 바닥으로 보지 않게 고친다.
- Spawn 머리/캡슐 공간에 액체가 남아 있는 지형도 기본적으로 피한다.
- `GameSession.Restore` 이후 `Terrain == null`인 상태에서 `PlayerSpawner.SpawnFor`가 기존 SpawnPoint (`y = 0.1`)로 돌아가 지형 내부에 들어갈 수 있는 **복원 Spawn 리스크**를 해결한다.
- 저장된 PlayerId의 모델/transport mapping/NetworkObject/Physics Body는 동일한 세션 세대에서만 연결하고, 복원 실패 시 이전 실행 세션과 저장 데이터를 훼손하지 않는다.

### P1: 네트워크·DI·수명 마무리

- SessionStopped/Started 이벤트 처리 순서, PlayerSpawner의 `sessionRespawns`, `pendingSpawns`, `ConnectionSpawnGuard`, `TryRestoreConnection`의 old/new generation 정합성을 실제 테스트한다.
- 기존 순수 C# `GameSession`, 서버 권위 WorldCore, 별도 클라이언트 replica, FishNet Prediction을 유지한다.
- `ApplicationLifetimeScope.Active`, `SessionController.Instance`, `NetworkBootstrap.Instance` 등의 실제 호출자를 찾아 불필요한 전역 접근만 **기능/테스트 보존 조건 아래** DI Facade로 정리한다. 정적 심볼을 기계적으로 전부 삭제하지 말 것.
- Scene Builder 재실행 후 `Test`/`Ingame`의 ProPixelizer 렌더 설정, 플레이어 카메라, FMOD smoke가 유지되는지 확인한다.

### 구현·검증 원칙

- 외부 `Assets/External/ProPixelizer/`, FishNet, VContainer, FMOD 소스를 직접 수정하지 않는다.
- 기존 WorldCore native container 소유권·Burst/Jobs·DirtyChunk delta 흐름을 보존한다.
- 기존 저장 포맷을 깨뜨리거나 게임 플레이 저장 데이터를 무단 재생성/삭제하지 않는다. 포맷 확장이 필요하면 버전/하위호환/이행 테스트를 제시한다.
- 플레이어 Physics/Predict/Replicate/Reconcile을 건드리지 않는다(수정 불가피하면 영향 분석 및 Host/Dedicated 회귀 증거 필수).
- 각 P0 완료 후 테스트 및 변화 증빙이 확보되지 않으면 P1으로 넘어가지 않는다.
- Unity 런타임을 직접 실행할 수 없으면 **실행한 척 하지 말고 미실행**으로 남긴다.

### 최종 보고

`docs/architecture/refactor-review-53fd8bd.md`를 작성한다.
`06_DELIVERABLES.md`의 체크리스트, 변경 파일 목록, 테스트 결과, 위험·잔여 작업을 포함한다.
