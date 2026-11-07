# 07. WorldCore, Terrain, WorldHost, Persistence

## 기본 소유권

| 자원 | Runtime authoritative owner | Runtime client owner | Editor owner |
|---|---|---|---|
| `WorldCore` | `SimulationWorld` (ServerSessionScope) | `ClientStateCoordinator` (ClientPresentationScope) | `WorldHost` if it created authoring world |
| `WorldHost` | 서버가 직접 Domain owner가 되지 않음 | Borrowed WorldCore projection | Authoring World + serialized scene data |
| `ChunkRenderPipeline`/`ChunkRendererPool` | Dedicated collider bridge의 projection | Scene/WorldHost의 View | WorldHost |
| `IWorldStorage` | Save/Restore orchestrator | 로컬 디스크 캐시가 없다면 없음 | `WorldStorageSession` / editor tools |

**중요**: `WorldHost`는 이미 `ownsWorld` flag를 구현. Runtime borrowed world에 대해 `DisposeWorld()`에서 `WorldCore.Dispose()`가 호출되지 않도록 유지. 리팩터링 명목으로 해당 소유권 분리를 제거하지 않는다.

## WorldCore 성능/일관성 보호

- `WorldCore.Enqueue`/`Flush` 단일 mutation boundary.
- ChunkLease가 살아있거나 Job dependency가 완결되지 않은 상태에서 chunk unload/overwrite/Dispose 방지.
- DirtyChunk generation/version 및 neighbor dirty marks 유지.
- 렌더 메시·콜라이더 재생성 중 stale version result가 새 버전을 overwrite하지 않도록 검사.
- `PerlinTerrainGenerator`의 seed/config와 동일 입력 동일 지형 signature 성질 유지.
- 예상 성능 회귀 방지: 매 블록 GameObject 생성, EntityScope 생성, NativeArray의 매 프레임 managed list 복사 금지.

## Collision readiness protocol

시각적 렌더 준비와 충돌 준비는 다른 상태다.

```text
WorldCore data version ready
    ├── Render view build -> visual-ready(version)
    └── Collider build  -> collision-ready(version)
             ↓
    WorldReady (session, generation, snapshot tick/version)
             ↓ server validates
        Player Spawn permitted
```

실제 코드의 `ChunkReadiness` 구조를 우선 재사용. 초기 수직 슬라이스는 player spawn 주변 전체 terrain chunk의 Collider만 보장해도 충분. 오브젝트 수가 커질 것을 가정해 글로벌 모든 청크 ready를 무조건 기다리게 만들지 않는다.

## WorldHost와 Editor Paint 보존

- `[ExecuteAlways]` 씬 작성용 WorldHost는 독립 tool 워크플로. SessionScope가 EditMode authoring world를 강제로 소유하면 안 됨.
- 현재 `WorldHost`의 runtime `AttachRuntimeWorld`, `DetachRuntimeWorld`, `RefreshRuntimeWorld`와 Editor `Enqueue`, `Flush`, `SaveWorld`, `LoadWorld`의 책임을 구분.
- `Assets/World/Editor/Tools/BlockPaintTool.cs`, WorldStorageMenu, ManifestBuilder, 관련 EditMode 테스트의 Scene data 호환 확인.
- Scene/Prefab GUID, 직렬화 프로퍼티 값, 기존 저장 포맷을 바꾸지 않는다. 필요한 migration은 별도 설계/검증 후 진행.

## 저장/복원

현재 `SessionArchiveSnapshot`에 포함된 핵심은 `SessionId`, `Generation`, `Phase`, `Tick`, `Elapsed`, `Seed`, `Players`, `WorldStorageSnapshot`. **물리 엔진 내부 상태나 모든 Rigidbody 속도가 포함된 완전한 물리 리플레이가 아님.**

요구하는 것은 **논리적으로 안전하고 테스트 가능한 재구성**:

1. 세션 Tick 완료 안정 경계에서 Snapshot 캡처. Command Queue, Native job, ChunkLease 유효성 확인.
2. 새로운 Candidate SessionScope에서 저장 파일/체크섬/모델 유효성 검증 후 Restore.
3. Candidate의 runtime World/Collision을 정상 준비하고 old session을 교체.
4. `PlayerId` ↔ 실제 연결/NetworkObject 매핑을 새 scope에 맞게 복구 또는 재생성. `PlayerSpawner`가 `Terrain=null`인 복원 Session에서도 안전한 지형 기반 Spawn 위치를 찾는지 확인.
5. Client replica와 세대 fencing, world collision readiness, late join snapshot을 재수행.
6. 에러 시 old session을 보존. 실패한 Candidate Scope native resources를 모두 Dispose.

### 데이터 포맷

- `SessionJsonStorage.FormatVersion=2`, `current.json`, `*.bak`, `*.tmp`, snapshot 세대, checksum 정책 유지.
- `RegionJsonWorldStorage`의 chunk/region Save/Load와 `WorldStorageWorldBuilder` 검증 체계를 유지.
- 신규 상태가 정말 필요하다면 구버전 리더와 migration/rollback 테스트를 추가. 물리 스냅샷은 지원 범위·낙관적 복구 정확도를 문서로 분리하고 무단 포맷 개편 금지.

## 검증 목록

- deterministic Perlin seed sample and world signature
- chunk negative coordinate/edge/removal/FillRegion, dirty neighbor, stale version
- collider update after world changes; no pre-ready spawn
- disposing borrowed client WorldHost does not dispose ClientStateCoordinator's WorldCore
- repeated load/release no NativeContainer/mesh leak
- Save+Restore and backup fallback, corrupted snapshots do not mutate live session
- Editor World painting/scene serialization after runtime test
