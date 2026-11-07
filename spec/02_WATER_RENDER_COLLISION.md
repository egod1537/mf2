# 02. P0 — Water rendering / collision pipeline 계약

## 완료 목표

실제 지형의 Grass/Dirt/Stone은 고체로 렌더/충돌하며, Water는 **반투명 렌더되지만 고체 Rigidbody 충돌이 없는 정적 액체 블록**으로 동작한다. 기존 Chunk dirty-version/Handle, Burst Jobs, Native allocation/Dispose 계약 유지.

## 작업 위치

- `Assets/World/Runtime/Rendering/BlockRenderData.cs`
- `ChunkMeshBuildBuffers.cs`, `ChunkMeshBuildJob.cs`, `ChunkMeshBuilder.cs`, `ChunkMeshBuildRequest.cs`
- `ChunkMeshApplier.cs`, `ChunkRenderer.cs`, `ChunkRendererPool.cs`, `ChunkRenderPipeline.cs`
- `Assets/World/Runtime/Registry/BlockTraitsCatalog.cs`
- `Assets/World/Editor/Registry/BlockRenderManifestBuilder.cs`
- `Assets/World/Rendering/ChunkMaterialSet*.asset` 및 기존 Water material (필요 시)
- `Assets/World/Tests/Editor/ChunkMeshingTests.cs`

## 필수 계약

### 1. 렌더 패스 인덱스

```text
Submesh[0] = Opaque
Submesh[1] = Cutout
Submesh[2] = Transparent
Submesh[3] = Water
```

`ChunkMaterialSet.CreateSharedMaterialArray()` 반환 순서와 `ChunkMeshApplier.Apply` submesh 분할/offset/count를 정확히 동일하게 만든다. Index-buffer 범위는 오버랩 없이 전체 인덱스 길이 합계와 일치해야 한다. 물이 opaque index에 섞여 들어가지 않아야 한다.

### 2. 렌더와 Collider를 독립적으로 소유

`ChunkRenderer`는 **렌더 메시**와 **고체 전용 Collider 메시**를 별도로 보유한다. `MeshCollider.sharedMesh`에 렌더 메시를 그대로 꽂지 않는다. Collider용 버텍스/삼각형 데이터는 생성기에서 `BlockTraitsCatalog.IsSolid` 혹은 일관된 런타임 traits table로 필터링한다.

중요: 단순히 Water SubMesh만 제외하고 Renderer의 정점 배치를 무작정 재활용할 경우 `submesh`와 vertex/index remapping을 검증해야 한다. 별도 collider geometry 생성 또는 안전한 filtered index-buffer 모델 중 현재 코드의 Job/Native allocation과 가장 적합한 방법을 선택한다. Collider는 물 안에서 이동할 때 고체 바닥/벽만 반영해야 한다.

리소스 소유자: chunk의 두 메시 수명과 Pool 반환/재사용 시 Clear 및 OnDestroy의 idempotence. `ChunkMeshBuildRequest.Dispose()`의 Job completion/NativeArray 해제 유지. Stale request 버전 거절 시 기존 collider 상태를 손상시키지 않는다.

### 3. 인접 면 culling

- Solid vs Solid: 외부 표면만 보여준다.
- Water vs Water: 내부 면 제거. 청크 경계에서도 동일.
- Water vs Air: 물의 노출면을 렌더한다.
- Solid vs Water: 물 아래 고체 표면이 보이도록 solid face를 렌더한다.
- Water vs Solid: 같은 평면의 중복 water face가 불필요하면 cull하되, 빈 공간에서 보이는 물 가장자리는 유지한다.
- Transparent/Cutout은 완전 고체와 의미가 다를 수 있으므로 `BlockRenderPass`와 `BlockTraitsKind`가 독립이라는 기존 의도를 유지한다.

### 4. Water 머티리얼 정합성

`BlockRenderManifestBuilder.CreateWaterEntry()`와 `WaterProPixelizer.mat`의 UV, texture binding, alpha, renderQueue, ZWrite, shader keyword가 실제 런타임에서 유효한지 검증한다. 기존 Sprite/Water 텍스처 사용 여부는 현재 아틀라스/머티리얼 정의를 확인해 결정하고, **실제 데이터가 없는 UV를 가정하지 않는다.** 외부 ProPixelizer 패키지 원본은 수정하지 않는다.

기본 Water는 정적(흐름·부력·수영·전파 시스템 없음). 물을 숨기거나 chunk unload할 때 Collider에 빈/이전 mesh가 남지 않는다.

### 5. Readiness

`AreAllLoadedChunkCollidersReady`, `ChunkRenderer.IsCollisionReadyFor`, `WorldHost.IsCollisionReadyFor`, `ServerWorldCollisionBridge.IsReadyFor`는 **Solid-only collider mesh**가 현재 chunk handle/version을 반영하고 PhysX 반영까지 완료한 뒤 true가 되도록 연결한다. 물만 있는 chunk의 empty collider mesh도 합법적 "collision ready" 상태와 구분 없이 실패하는 버그가 없게 테스트한다.

### 반드시 추가할 테스트

- Water-only block/chunk: 렌더 Water index만 생성, collider triangle 0.
- Stone+water adjacent: solid face 보임, 물은 Water submesh, 충돌은 stone에만 있음.
- Stone/Grass/Dirt only: 기존 surface triangle 수와 collider behavior 유지.
- 서로 다른 chunk에 걸친 water-water, water-stone 접면: 일관된 face count, crack/중복면 없음.
- Chunk version 변경/Pool reuse, water↔stone block edit: 렌더와 collider 둘 다 최신 버전으로 업데이트.
- Dedicated collision-only projection: water가 보이지 않아도 collider는 올바르고 spawn readiness가 완료된다.
- RegionJSON / snapshot → restore → RenderTable 재생성 후 Water stable ID, 고체 collision 동일.

**Gate:** 단위 테스트 + 씬에서 플레이어가 물 표면을 바닥처럼 밟지 않으며 물 아래 고체 바닥/벽에는 정상 충돌.
