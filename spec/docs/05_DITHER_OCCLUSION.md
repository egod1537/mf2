# 05. T2 — 가림 블록 디더링 시 내부 면 결손 해결

## 5.1 실제 코드에서 확인된 원인 경로

1. `Assets/Game/Presentation/PlayerOcclusionDither.cs`: 로컬 owner camera↔target 여러 선분에서 voxel DDA를 실행, solid occluder 최대 기본 64개를 `WorldHost.SetDitheredOccluders()`로 보냄.
2. `Assets/World/Runtime/Core/WorldHost.cs`: `SetDitheredOccluders`는 `rendererPool.SetDitheredBlocks()`에 위임. `WorldCore`/collision은 바꾸지 않음.
3. `Assets/World/Runtime/Rendering/ChunkRendererPool.cs`: ProPixelizer 기반 occlusion dither material을 별도 submesh로 붙이며 활성 청크에 dither set 전달.
4. `Assets/World/Runtime/Rendering/ChunkRenderer.cs`: 이미 생성된 base mesh의 삼각형을 dither/일반 submesh로 재라우팅.
5. `Assets/World/Runtime/Rendering/ChunkMeshBuildJob.cs`: `ShouldRenderFace()`에서 인접 solid opaque 블록 사이 내부 면은 애초에 생성하지 않는다.

**관찰 가설:** Dither가 가려진 상단/전면 블록을 부분적으로 투과시키면, 바로 뒤 또는 아래 solid 블록의 해당 면은 culled 상태라 실제 물질이 없는 것처럼 바닥이 뚫려 보인다. 이 **가설은 사용자의 관찰과 코드에 부합하지만 반드시 실행 화면/프레임 디버거로 검증**한다. WorldCore에서 아래 블록 자체가 누락된 경우는 별도 월드 생성 버그다.

## 5.2 설계 목표

- 화면 가림 블록은 기존 ProPixelizer ordered dithering 유지.
- 투과 픽셀 뒤에 **실제 위치에 존재하는** 인접 블록의 면이 보이도록 한다.
- 월드 데이터·서버 권한·충돌 메시·물리/네트워크는 변경하지 않음.
- 임의 카메라 각도, 블록 세로/가로 중첩, 청크 경계, 물·투명 블록에서 오작동 없음.
- Dither가 꺼지면 원래 mesh/렌더 성능으로 정확히 복귀. 대량 월드에서도 무의미한 내부 면을 영구 생성하지 않는다.

## 5.3 구현 방향 — 권장 A안: 필요한 내부 면 전용 시각 오버레이

1. 기존 `ChunkMeshBuildJob` 기반 최적화 및 collider mesh를 기본으로 유지한다.
2. 현재 active dithered occluder set으로 **카메라에서 투과되어 보일 수 있는 인접 solid 면**을 찾는다. 존재하는 `WorldCore`/client Replica 값으로 판정, 없으면 생성하지 않는다.
3. 평소에는 보이지 않는 인접 면을 **오직 렌더 전용 geometry**로 임시 생성하거나, 필요한 precomputed boundary geometry를 별도 submesh에서 활성화한다.
4. 원본 블록의 texture/UV/atlas/face orientation/ambient 조명/색상/normal과 일치시킨다. `ChunkRenderer`의 기존 MeshCollider/CollisionIndices는 유지.
5. 별도 Mesh 또는 submesh를 청크 단위로 배치, 변한 occluder set을 diff하여 갱신. 매 LateUpdate 매번 전체 chunk 리메시 금지. 0 allocation에 가까운 재사용 풀/버퍼 사용.
6. 선택된 내부 면을 Dither 머티리얼로 그리지 말고 **해당 인접 solid 블록의 본래 머티리얼**로 렌더한다. Depth/ZWrite/transparent ordering 검증.
7. 청크 간 이웃 면, 경계/음수 좌표, unloaded chunk는 명시적으로 처리하고 stale overlay 제거.
8. Occluder 변동/scene unload/WorldHost detach/asset pool reuse/원격 클라이언트에서 누수 없이 제거.

### 대안 B: Dither-aware chunk meshing

현재 카메라/occluder set이 반영된 meshing snapshot을 만들어 누락된 인접 면을 일시적으로 생성. 장점은 기존 atlas/vertex 생성기 재사용, 단점은 occluder 이동마다 작업/청크 메쉬 재빌드 및 collider readiness/버전 충돌 위험이 큼. **분석 후 A가 부적합할 때에만 선택**하고 collider와 시각 메시를 명확히 분리해야 한다.

### 금지

- 가장 쉬운 해결책이라는 이유로 모든 solid 블록의 모든 내부 면을 무조건 그리는 것.
- 디더링 대상 아래 블록을 서버/Replica에 가짜로 삽입하거나 파괴.
- ChunkCollider를 재생성/수정해 플레이어가 바닥을 뚫거나 서버·클라이언트 충돌 불일치 발생.
- 카메라와 직접 상관없는 청크 전체를 매 프레임 리빌드.
- 4개 기본 material + occlusion material 간 submesh index mismatch.

## 5.4 기하학·표현 세부 사항

- `ChunkCoordinates.Split()`에서 사용하는 **센터 기준 block coordinate**와 `ChunkMeshBuildJob`의 face orientation/vertex winding을 그대로 따른다.
- 인접 solid의 표시 면은 노출된 dithered surface의 반대편으로 판단하되 카메라 방향에 따른 보이는 면을 검증. 아래쪽 면 하나만 생성하는 임시 처방은 금지(옆면/벽 상황).
- 기존 `ChunkMeshingSnapshot`의 border data를 사용해 청크 경계 이웃 조회. loaded/unloaded 처리 구분.
- 실제 air neighbor에는 누락된 내부 면이 없으므로 새 geometry가 필요하지 않다.
- Transparent/Cutout/Liquid은 `BlockTraitsKind`의 기존 `ShouldRenderFace`와 동일한 규칙을 고려하고, 물 internal face를 무조건 만들어 fill-rate를 폭발시키지 않는다.
- 여러 dithered blocks가 동일 backing face를 요청하면 중복 면 생성 방지.
- occluder가 이동 시 중복 set 비교만으로 충분한지 검사; PlayerOcclusionDither가 새로운 순서/카메라 변화에 반응하는지도 확인.
- 원본 `ChunkRendererPool`의 `OcclusionAlpha=0.35f`, `OcclusionClipThreshold=0.05f` 같은 설정은 시각 parity와 blend/depth 영향 실측 뒤 변경.

## 5.5 원인 검증/디버깅 절차

1. 디버그 장면에 3층 이상의 stone/grass 기둥, 2층 지면, 벽, 경사/계단을 배치한다.
2. Scene Gizmo로 `WorldCore.GetBlockOrAir()` 결과와 rendering mesh 삼각형을 비교하여 *block 존재 여부*부터 확인한다.
3. ProPixelizer dither On/Off 캡처, Unity Frame Debugger/RenderDoc 가능한 경우 occlusion submesh와 backing face draw call 조사.
4. 동일 프레임에서 `WorldHost`, chunk coordinate, occluder set, normal/cull face, mesh collider version을 로깅.
5. 원인이 실제 블록 누락이면 Generator/WorldCore 문제로 분기한 뒤 해당 근거를 남긴다.

## 5.6 테스트/합격 기준

- [ ] 디더링 전/후 플레이어 뒤의 실제 solid surface가 빈 하늘/공허로 보이지 않음.
- [ ] 평평한 지면, 깊은 지형, 벽/코너, 다층 지형, 1-block 간격, 경사 계단 통과.
- [ ] Chunk seam, 음수 좌표, y 경계, loaded/unloaded, 서버와 client 월드 Delta 변동.
- [ ] Dither Off/On/이동/teleport/씬 종료/새 스폰 시 오버레이 완전 정리.
- [ ] CollisionIndices/`MeshCollider`/서버 `WorldCore` 해시가 dither 전후 동일.
- [ ] 렌더러 풀/meshes dispose, managed/native memory 누수 없음.
- [ ] FPS, GC allocations, 추가 정점/삼각형 수를 수정 전/후 측정, 숫자 기록.
- [ ] Host와 원격 Client 각각 자기 owner camera 시점에서 독립적인 dither, 서버 전용 프로세스엔 적용하지 않음.

`ChunkMeshingTests`, `WorldHostOwnershipTests`, Dither tests를 추가. Runtime 시각 테스트는 비디오/스크린샷이나 프레임 디버거로 증거 첨부.
