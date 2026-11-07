# 14. 위험 요인, 선택해야 할 설계 결정, 미확인 사항

## 14.1 위험 목록

| ID | 위험 | 가능성/영향 | 완화 및 차단 조건 |
|---|---|---|---|
| R01 | Caveworks 원본 에셋/코드 로컬 경로 접근 불가 | 중/높음 | 실제 appmanifest/Steam library 탐색, P0 보고, 동등성 임의 주장 금지 |
| R02 | 원본 UI Prefab 스크립트 GUID/Shader 손실 | 높음/중 | Sprite/Texture selective extraction, uGUI Prefab 재구성, 시각 A/B |
| R03 | 외부 에셋 라이선스 불명 | 중/높음 | provenance/approval Gate, public commit/upload 제한 |
| R04 | Minefarm0 직접 AddForce와 FishNet Prediction 충돌 | 높음/높음 | Tick-integrated dash, authoritative state, reconcile tests |
| R05 | 카메라와 물리 root 갱신 주기 차이 | 중/높음 | frame/tick telemetry, render target smoothing 한 경로로 통합 |
| R06 | ProPixelizer / pixel snapping이 jitter로 오인 | 중/중 | ON/OFF A/B와 수치 실측 |
| R07 | Dither overlay 내부 면 증가/Depth artifact | 중/높음 | 필요한 face만 생성, Render-only, shader/state ordering 검증 |
| R08 | Dither set이 자주 바뀌어 chunk rebuild 스파이크 | 높음/중 | diff, reusable per-chunk overlays, dirty updates 제한 |
| R09 | Inventory/Attack Dash 신규 프로토콜과 저장 호환성 | 중/높음 | schema version/migration, stale gen reject, security tests |
| R10 | Builder 재생성이 UI/Camera/Asset을 덮어씀 | 높음/높음 | Builder 수정 우선, 2회 idempotence tests |
| R11 | MiniMap의 전체 WorldCore 스캔으로 GC/CPU 부하 | 중/중 | chunk cache/heightmap/delta invalidation |
| R12 | Editor tool Undo가 Native WorldCore 복원을 못함 | 중/높음 | inverse commands 또는 authoring snapshot 테스트 |
| R13 | Amplified 높이 증가로 월드 예산/메시/스폰 실패 | 높음/중 | bounded config, gen instrumentation, spawn test |
| R14 | 원본 Mines/FBX rig와 MF2 capsule/facing 차이 | 중/중 | visual-only rig, axis/scale/Animator import 확인 |
| R15 | Host에서만 성공하며 Dedicated/Remote 실패 | 중/높음 | server/client scope 분리 강제, dedicated+2 matrix |
| R16 | Unity Console compile/prefab reference 오류 | 중/높음 | import log, asmdef dependency, GUID 보존, Builder tests |

## 14.2 구현 중 결정을 기록할 항목

| ID | 결정 주제 | 기본 제안 | 확정해야 하는 근거 |
|---|---|---|---|
| D01 | Inventory 레이아웃/슬롯 수 | Caveworks 동등성 우선 | 실제 디컴파일/UI 화면 |
| D02 | Inventory 저장/복제 범위 | PlayerId owner-only authoritative | 기존 SessionArchive/ClientState schema |
| D03 | Dash input과 world interaction 충돌 | Minefarm0 우클릭 참고 | MF2 InputSystem/Inventory UI/Camera |
| D04 | Dash 충전/쿨다운 | 원본 수치 참고, tick 기반 | 실제 플레이 감각/서버 검증 |
| D05 | Attack target 판정 | 서버 authoritative 거리/LOS | 기존 Targeting/HealthSystem 및 원본 |
| D06 | Camera follow 소유권 | presentation-only render rig | jitter 원인 재현/제거 검증 |
| D07 | Dither 내부 면 생성 | A: transient visual overlay | mesh 데이터/Depth 문제/프로파일 |
| D08 | Minimap 좌표/회전 | north-up + player arrow | HUD 원본/사용자 경험 |
| D09 | Pixel art mapping | exact palette + optional nearest | 현재 BlockRenderManifest/TextureImporter |
| D10 | Editor Undo | snapshot/inverse command | WorldHost serial state & lifecycle |
| D11 | Amplified noise 모델 | mask + ridged fBm + warp opt-in | 3+ seed terrain previews |
| D12 | Save migration | 이전 save default/버전 호환 | `SessionArchiveSnapshot` 구현 |
| D13 | Imported assets public commit | only approved | 라이선스/사용자 결정 |

각 결정은 `날짜 / 변경파일 / 선택 / 대안 / 측정/근거 / 회귀영향`을 적어 `docs/DECISIONS_MF2_PORT.md` 같은 작업 로그로 남긴다. 프로젝트가 이미 ADR 문서를 사용하므로 기존 ADR 승인 사항을 조용히 뒤집지 않는다.

## 14.3 불확실성 상태

**확인된 사실:** MF2 `impl` 코드 경로/버전, Occlusion Dither→Chunk mesh 흐름, Minefarm0의 GreenIdol FBX/Attack.anim/PlayerDashable, Caveworks 공개 레포의 분석 자료 성격.

**실행 확인 필요:** jitter의 정확한 원인, Caveworks UI 세부 조작, 실제 추출 가능한 Caveworks 에셋 목록, Minefarm0 rig/material/animation 상태, 원본 외부 에셋 라이선스, 샘플 월드에서 Dither의 물리 블록 존재 여부, 사용자 PC Unity Editor/그래픽 상태.

**설계 제안 (아직 구현되지 않음):** Inventory network model/API, transient dither backing mesh, Presentation camera render smoothing, block-based minimap cache, pixel-to-voxel EditorWindow, Amplified ridge+domain warp.

이 세 범주를 최종 구현 보고에서 섞지 않는다.
