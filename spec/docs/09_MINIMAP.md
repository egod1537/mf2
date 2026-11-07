# 09. T6 — 실제 월드 기반 미니맵

## 9.1 사용자 목표

Ingame HUD에 플레이어 위치를 쉽게 파악할 수 있는 미니맵을 추가한다. 플레이어가 이동/회전하면 마커가 이동/회전하고, 블록을 설치/파괴하거나 월드가 변경되면 지도 표현도 갱신된다. Host뿐 아니라 원격 Client에서도 동작한다.

## 9.2 원본 참고와 제외

Minefarm0에는 `Assets/Script/Scenes/InGame/UI/Minimap/ViewExtendMinimap.cs`가 존재한다. 해당 파일은 이름상 확대/접기 View이며 **완성형 MF2 minimap을 제공한다는 근거는 없다**. 필요한 UI 동작만 참고하고 월드 데이터/Camera 정책은 MF2에 맞춰 설계한다. Caveworks HUD와의 위치/레이아웃 호환도 고려.

## 9.3 권장 설계 — Block 기반 2D 지도

```text
ClientStateCoordinator (readonly WorldCore replica)
    -> MinimapDataSource (column-top lookup/height/material category)
    -> Per-chunk cached raster/tiles
    -> MinimapPresenter (player position, orientation, zoom)
    -> uGUI RawImage/UIImage + marker
```

- 무조건 두 번째 Camera를 두어 3D Chunk를 반복 렌더하는 대신 **정적/변경 블록을 기반으로 2D 표현**을 우선 사용. 필요 시 프로파일링 근거로 RenderTexture 방식 검토.
- X/Z tile와 world coordinate 변환은 `WorldHost`/`ChunkCoordinates` 변환을 따른다. 음수좌표와 경계 포함.
- 각 Column에서 표시할 높이(위에서 처음 만나는 **렌더 가능한** 블록, 물/opaque/grass 등)와 색은 BlockRegistry/RenderManifest를 통해 가져오며, 블록은 air·solid·water·transparent 특성을 구분.
- 클라이언트 WorldReplica가 아직 준비 전이면 빈 지도/로딩 표시, WorldReady 이후만 world query. Snapshot replace는 cache 전량 invalidate, Delta는 dirty region만 invalidate.
- 예를 들어 기본 view radius 32–64 block, step=1–2 block은 초기 제안. 실제 성능 측정에 따라 조정.
- 첫 프레임 build와 많은 dirty chunk에도 프레임 지연이 크지 않게 작업 분할/기존 cache 재사용. `Texture2D.Apply`는 dirty range에만 호출 가능성을 검토. 매 프레임 allocations/Pixels scan 금지.
- PlayerMarker는 `PlayerId`의 로컬 위치 및 owner interpolation target을 읽음. 다른 플레이어 마커는 공개되어 있는 replicated world state만으로 표시, 소유자 정보 임의 유출 금지.
- 카메라 기준 고정 북쪽/플레이어 회전 지도 중 하나로 결정하고 명확하게 표시. 초기 기본은 북쪽 고정 + 방향을 표시하는 플레이어 화살표, 필요 시 회전 모드 지원.
- UI는 Caveworks HUD/Hotbar의 input 영역과 겹치지 않게 anchor/pivot/sorting order 설계.

## 9.4 UI/입력 상세

- 작은 상시 미니맵(가로세로 160~220 logical pixel 권장, 실제 원본 UI와 screen scale에 맞춤), 플레이어 위치/방향 화살표.
- 지도 배경/외곽선/필드 색, 물/풀/돌 등 블록 유형별 읽기 쉬운 팔레트.
- 지도 확대/접기(옵션), zoom 범위 제한, 메뉴/인벤토리 열린 상태의 클릭 관통 방지.
- UI 시스템이 실제 월드 바뀜을 관찰하도록 data source 수명은 `ClientPresentationLifetimeScope` 또는 scene-local Presenter로 분리. `GameSession` 직접 참조 금지.
- 미니맵 마커에는 별도의 NetworkTransform 없이 기존 predicted/replicated Player presentation 데이터 사용.

## 9.5 캐시 무효화/수명

- `OnWorldSnapshotChanged(SessionId,Generation)`: 전체 cache invalidate와 renderer 재생성.
- `OnChunkDelta(ChunkCoord,Version)`: affected chunk와 heightmap column만 다시 계산. 블록 파괴로 표면이 내려간 경우 아래 block 재탐색.
- `OnSceneUnload/OnDisconnect`: 이벤트 구독 해제, temporary texture/material RenderTexture dispose, references null.
- `OnPlayerSpawn`: Local PlayerId/Camera/marker 바인딩; despawn 때 해제, rejoin 재바인딩.
- `Dedicated Server`: 아무 GUI/texture resource도 생성하지 않음.

## 9.6 테스트/완료 조건

- [ ] 첫 연결, late join, snapshot replacement 직후 현재 지형과 마커를 표시.
- [ ] 움직임/회전/줌 시 지도/마커 자연스럽고 Camera jitter 수정과 충돌 없음.
- [ ] 블록 설치/파괴, delta, 복원 시 해당 column 갱신, 변한 표면이 반영.
- [ ] 물·잔디·돌·빈 공간·경계/음수좌표·높은 지형/Amplified 경사 구분.
- [ ] 여러 플레이어 marker(구현하기로 선택한 경우) ID/연결/권한 일관.
- [ ] Host/Client, Disconnect/Rejoin, Test↔Ingame, save/restore 동작.
- [ ] 정지 상태에서 매 프레임 전체 texture rebuild 없고 GC alloc/CPU 비용 측정 기록.
- [ ] Dedicated Server에서 texture/UI 스폰 없음.
- [ ] Builder가 Ingame/Test HUD에 미니맵을 생성, 실행 후 Missing refs 없음.
