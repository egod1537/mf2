# 01. 원격 최신 코드에서 확인한 정적 불일치

**기준 HEAD** `53fd8bda2d7b14808e98ea414f8dbcb9522a1c3a`.

## 확정된 코드 구조 사실 (CONFIRMED_IN_CODE)

| 항목 | 최신 코드 근거 | 영향 |
|---|---|---|
| Water pass 번호 | `Assets/World/Runtime/Rendering/BlockRenderData.cs`: `BlockRenderPass.Water = 3` | 네 번째 렌더 패스 도입됨 |
| 4 머티리얼 | `Assets/World/Runtime/Rendering/ChunkMaterialSet.cs`: `CreateSharedMaterialArray() => {opaque,cutout,transparent,water}` | 렌더 메시 4 submesh 필요 |
| 3 index list | `ChunkMeshBuildBuffers.cs`: `OpaqueIndices`, `CutoutIndices`, `TransparentIndices`만 있음 | Water 전용 index 없음 |
| Water 분기 없음 | `ChunkMeshBuildJob.cs`: `AddIndex`에서 `Water` case 없음, default는 `OpaqueIndices` | Water geometry가 opaque로 분류될 수 있음 |
| 3 submesh | `ChunkMeshApplier.cs`: `meshData.subMeshCount = 3` | Water material slot과 geometry 불일치 |
| 렌더 메시를 Collider에 사용 | `ChunkRenderer.cs`: `meshCollider.sharedMesh = mesh` | 물 메시가 포함되면 고체와 동일 충돌로 취급 |
| 액체 판정 도입 | `Assets/World/Runtime/Registry/BlockTraitsCatalog.cs`: `Water = Liquid` | traits와 실제 충돌 파이프라인 동기화 필요 |
| Spawn에서 non-air로 검사 | `Assets/Game/Domain/Terrain/SpawnLocator.cs`: `world.GetBlockOrAir(...).IsAir` | 물 표면이 Spawn 지지 후보가 될 수 있음 |
| 복원 후 Terrain 없음 | `Assets/Game/Domain/GameSession.cs`: `Restore`에서 `Terrain = null` | Spawn 안전 탐색에서 terrain config 의존 경로 실패 |
| Terrain이 없을 때 SpawnPoint fallback | `Assets/Game/Networking/PlayerSpawner.cs`: Terrain이 있을 때만 `SpawnLocator` 사용 | 복원 후 낮은 기본 SpawnPoint로 생성될 위험 |

## 확인되지만 "버그 확정"이 아닌 항목 (RUNTIME_UNVERIFIED)

- Host/Client에서 복원 직후 SessionStopping 이벤트가 기존 연결 mapping을 해제하는 동안 신규 복원 PlayerId가 매핑되는 순서가 안전한지.
- SessionStarted 방송, WorldReady ACK, Collider ready 전이 후 같은 기존 연결이 재스폰되는지.
- `SceneLifetimeScope`와 `ClientPresentationCoordinator`의 중복 OnDestroy/Dispose 중 리스너 잔존/무해성.
- Water 머티리얼이 실제 ProPixelizer/URP 투명 패스로 보이는지, 별도 texture/alpha/Depth가 잘 동작하는지.
- 저장 복원 시 기존 월드의 지형 데이터와 플레이어 상태를 얼마나 정확하게 복원할 수 있는지.

## 이미 구현되어 보존할 것

- `TerrainConfig.CreateDefault()`의 기본 지형: Grass 1, Dirt 3, Stone, WaterLevel 7.
- 생성기의 `SimulationWorld`/`WorldCommand`·Flush 처리와 Seed 서명.
- Test/Ingame 공통 ProPixelizer 재질, Game Camera `ProPixelizerCamera` 설정과 빌더 테스트.
- 서버 전용 `GameSession` + `SimulationWorld`와 클라이언트 `ClientStateCoordinator` replica 분리.
- FishNet 예측 이동/점프, authoritative command envelope와 리플리케이션.
- `ClientPresentationCoordinator` 구독자 예외 격리, `WorldViewBridge.IsCollisionReadyFor()`의 최신 비교 방식.
- 세션 복원 후 재연결을 위한 `TryRestoreConnection`, `ReleaseConnectionMapping`, `sessionRespawns` 추가 구현.

## 비교 커밋

- Latest: https://github.com/egod1537/mf2/commit/53fd8bda2d7b14808e98ea414f8dbcb9522a1c3a
- Before: https://github.com/egod1537/mf2/commit/53f47a14dec54627a8acdd1147180296613e42a0

기준 시점의 Unity 테스트/플레이는 이 문서 작성자가 실행하지 않았으며, 정적 코드 증거와 실기기 재현 여부를 구분한다.
