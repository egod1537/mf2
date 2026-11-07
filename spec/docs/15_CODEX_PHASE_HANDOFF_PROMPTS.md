# 15. 긴 Codex 세션을 나눠 진행할 때 사용할 Phase별 지시문

`CODEX_MASTER_PROMPT.md`를 먼저 전달한다. 컨텍스트가 길어졌거나 세션을 나눠야 하면 해당 Phase 지시를 추가한다. 각 지시는 Git branch/dirty state/이전 Phase 보고서를 재확인하는 것으로 시작한다.

## P0 — 조사와 스냅샷

> 현재 `mf2` `impl`의 branch/status/head, package/Unity 환경, 기존 테스트 결과를 확인하라. `docs/01_SOURCE_MAP_AND_RECON.md`와 Caveworks 설치본을 읽기 전용으로 탐색하여 원본 UI 코드/에셋의 실제 파일을 목록화하라. `templates/RECON_REPORT_TEMPLATE.md`와 `ASSET_EXTRACTION_MANIFEST.csv`를 채워라. 접근 불가/원본 불명 내용을 지어내지 마라. P1 구현 전에 baseline/log를 저장하라.

## P1 — 카메라와 Hierarchy

> `docs/06_PLAYER_CAMERA_JITTER.md`와 `docs/08_SCENE_HIERARCHY.md`를 구현하라. 원인을 프레임/틱 로그로 분리하여 FishNet Prediction을 보존한 Camera/Visual smoothing을 도입하고 Builder로 모든 씬의 역할별 루트/프리팹을 정리하라. Host+Client와 Builder idempotence가 합격해야 P1 완료다.

## P2 — Minefarm0 액션

> `docs/07_PLAYER_MODEL_ATTACK_DASH.md`에 따라 GreenIdol/Player.prefab/Animator/Attack anim을 선별 import하고 기존 BlockPlayer Physics/Network Object를 보존하라. Attack/Dash를 서버 검증과 Prediction tick 흐름에 통합하고 owner/remote visibility 및 reconnect를 테스트하라. .meta/라이선스를 기록하라.

## P3 — Caveworks UI

> `docs/03_CAVEWORKS_EXTRACTION.md` 및 `docs/04_INVENTORY_HOTBAR_HUD.md`에 따라 실제 Caveworks inventory/toolbar/HUD 기능과 그래픽 리소스를 확인·추출하라. MF2 Domain/ClientState/FishNet/uGUI를 통한 동작하는 UI를 구현하고 Caveworks 기능·화면 동등성 매트릭스를 채워라. 서버 authoritative inventory/slot invariants, save/restore, Host+Client를 검증하라.

## P4 — 디더링 결손

> `docs/05_DITHER_OCCLUSION.md`에 따라 `PlayerOcclusionDither→WorldHost→ChunkRendererPool→ChunkRenderer→ChunkMeshBuildJob`을 조사하고 실제 블록 데이터 존재 여부와 내부 면 생략을 재현하라. 필요한 렌더 전용 backing face만 생성해 디더링 구멍을 해결하고 collider/WorldCore 불변 및 chunk boundary/perf를 검증하라.

## P5 — 미니맵

> `docs/09_MINIMAP.md`에 따라 Client Replica WorldCore를 read-only로 샘플링하는 캐시 기반 미니맵을 구현하라. player heading, surface/water/block delta, restore/reconnect, builder/inventory/HUD 연결을 완료하고 모든 관련 테스트를 실행하라.

## P6 — 도트→복셀 에디터

> `docs/10_PIXEL_TO_VOXEL_AND_TERRAIN.md` Part A에 따라 MF2 WorldHost/EditMode/Fluent IMGUI에 통합되는 EditorWindow를 구현하라. Texture/Sprite→Palette→Preview→Commit→Undo/Redo→Save가 되어야 하며, 권한 있는 authoring path만 사용하고 chunk/collider/serialization을 검증하라.

## P7 — Amplified 지형

> `docs/10_PIXEL_TO_VOXEL_AND_TERRAIN.md` Part B에 따라 opt-in Amplified profile을 추가하라. Normal baseline seed/signature를 유지하면서 ridge/valley/warp 기반 더 극적인 지형을 생성하고 max block budget, spawn, water, save/restore, deterministic/latency tests를 통과하라.

## P8 — 통합 검증

> `docs/11_TEST_MATRIX.md`의 전체 케이스를 환경상 가능한 만큼 수행하여 PASS/FAIL/BLOCKED/NOT_RUN/PARTIAL과 증거를 기록하라. 특히 Host+1 Client, Dedicated+2 Clients, WorldReady, LateJoin, scene transition, disconnect/rejoin, restore, Builder 2회 재생성, asset license를 확인하고 `docs/12_DELIVERY_AND_REPORTING.md`에 따라 최종 보고서를 작성하라. 실패를 숨기지 마라.
