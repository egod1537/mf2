# 11. 자동/수동 검증 매트릭스와 성능 벤치마크

## 11.1 테스트 레벨

- **Static:** 컴파일/asmdef/Serialized reference/생성기 의존성/에셋 GUID/Source maps.
- **Unity EditMode:** Domain invariants, WorldCore, Terrain, Mesher, Editor utilities, Asset manifest.
- **Unity PlayMode:** local owner/remote view bind, camera, mesh overlay, HUD updates, Scene lifecycle.
- **End-to-End:** Editor Host+빌드 Client, Dedicated+2 clients, reconnect/late join, save/restore.
- **Visual A/B:** Caveworks 원본 UI, Minefarm0 원본 모델/애니메이션, MF2 디더링/카메라/Amplified 비교.
- **Performance:** Baseline vs 새 코드의 CPU(ms), GC alloc(B/frame), 렌더 triangles/DrawCalls, Chunk build time, memory/WorldReady.

테스트 자동화는 설치된 Unity Test Runner/CI 설정에 맞춰 수행한다. GUI가 필요한 항목을 EditMode UnitTest만으로 '통과' 처리하지 않는다.

## 11.2 필수 테스트 케이스 (고유 ID)

| ID | 대상 | 절차/조건 | 기대 결과 |
|---|---|---|---|
| BASE-01 | Branch | branch/head/status 기록 | 기존 변경 보존, 명세와 정확한 baseline 차이 기록 |
| BASE-02 | Compile | Unity Editor open/Import/compile | 새 콘솔 Error 0, 누락된 ASMDEF 없음 |
| BASE-03 | Builder | `MF2 > Build Multiplayer Test Slice` 2회 실행 | 4개 씬/2개 prefab 동일 구조, Missing 0 |
| BASE-04 | Network | Title→Outgame→Host→Test/Ingame | 기존 경로/모드 정상 |
| BASE-05 | Net Client | separate client join/late join | 올바른 WorldReady 이후 spawn |
| BASE-06 | Dedicated | headless server + 2 clients | 서버 UI/Audio 0, 권한 정상 |
| BASE-07 | Restore | 세션 Save→Restore / disconnect/rejoin | 같은 PlayerId/World/Data, 오래된 subscription 제거 |
| INV-01 | Slot | empty/filled, full stack, swap/merge/split(지원 시) | 무결성, 클라이언트 수렴 |
| INV-02 | Security | 외부 ItemId, count<0, out-of-range, replay | 서버 거부, state 변화 없음 |
| INV-03 | HUD | inventory open/close/selected hotbar/cooldown | UI와 실제 selected item/state 일치 |
| INV-04 | Original parity | Caveworks GUI 동일 상태 캡처 | 의도된 동등성 및 차이 기록 |
| INV-05 | Lifecycle | scene change/rejoin/restore | listener/drag state cleanup, 올바른 초기 상태 |
| DIT-01 | Solid occlusion | grass/stone 3층 기둥/바닥/벽 | 가림 dither 뒤 공허 대신 실제 지형 노출 |
| DIT-02 | Chunk seam | +/− coords, chunk border | 누락/중복 backing face 없음 |
| DIT-03 | Layer | opaque/cutout/transparent/water | false geometry/물리 변경 없음 |
| DIT-04 | No mutation | dither on/off 중 WorldCore/Collider checksum | 완전히 동일, render-only |
| DIT-05 | Pool | teleport, despawn, unload, reuse | stale dither mesh/alloc 없음 |
| CAM-01 | Stationary | owner/remote idle 30/60/120 FPS | 불필요한 화면 떨림 없음 |
| CAM-02 | Motion | walk/sprint/diagonal/jump/fall | visual/camera 일관, 이전보다 안정 |
| CAM-03 | Net correction | latency + reconciliation 재현 | 근거 없는 순간 이동/추적 오차 최소 |
| CAM-04 | Camera config | zoom/ProPixelizer ON/OFF/scene transition | owner camera/audio 하나, 안정적 |
| MOD-01 | Character | player prefab/Animator/rig | 눈에 보이는 새 모델, 기존 Capsule/NetworkObject 유지 |
| MOD-02 | Anim | idle/walk/run/jump/attack/dash | 실제 애니메이션/이벤트 연결 |
| ACT-01 | Dash | 1 press, charges/cooldown, spam, wall/jump | 한 번 발동·반복 거부·상태 동기화 |
| ACT-02 | Attack | valid target, invalid/out-of-range, spam | 서버 판정, 유효한 damage만 기록 |
| ACT-03 | Remote | Host+Client, Dedicated+2 | 타인 액션/모션 동기화 |
| SCN-01 | Hierarchy | Title/Outgame/Test/Ingame | 역할별 root와 불변 lifetimes |
| SCN-02 | Builder | regenerate + re-enter Play | UI/Camera/Player refs 유지 |
| MAP-01 | Startup | WorldReady/initial snapshot | minimap terrain/marker 정상 |
| MAP-02 | Delta | place/break/terrain restore | dirty column 업데이트 |
| MAP-03 | Edge | water/negative coords/chunk seam | 잘못된 지도 샘플 없음 |
| MAP-04 | Performance | static world 120초 | full map 매 frame rebuild 없음 |
| VOX-01 | 2×2 texture | 1 transparent, 3 colored | 정확히 3개 block, 색/좌표 일치 |
| VOX-02 | Depth | 1×1 extrude depth=3 | 3개 블록 |
| VOX-03 | Placement | XY/XZ/YZ, rotation, negative coords | 예상 변환 동일 |
| VOX-04 | Undo | Apply/Undo/Redo/save/reopen | WorldCore+Scene/Region JSON 일치 |
| VOX-05 | Failure | bad palette/unreadable/too big | 세계 무변경, 명확한 오류 |
| TER-01 | Normal baseline | same seed+config | 동일 height signature/blocks |
| TER-02 | Amplified | >=3 seeds, varied params | 결정적, 급경사/능선/계곡 구별 |
| TER-03 | Water/Spawn | high peak/water valley | 마른 안전 spawn, layer 유지 |
| TER-04 | Budget | maximum bounds, overflow invalid | 제한 안전, 미완성 월드 노출 없음 |
| TER-05 | Persistence | Normal old save/Amplified new save | 이전 저장 호환, no regeneration |
| INTEG-01 | Combined | Amplified+player dash+minimap+dither | 4개 기능 동시 충돌 없음 |
| INTEG-02 | Combined | inventory open, attack/dash input | 월드 입력 관통 없음 |
| INTEG-03 | Network | late join during chunk updates | WorldReady/Inventory/Player 정합 |
| INTEG-04 | Scene | Title→Outgame→Test↔Ingame→Outgame | scope/resubscribe/graphics clean |

## 11.3 시험 환경 보고 필드

- OS/GPU/Unity Edition/Unity version/Scripting backend/Render Pipeline/ProPixelizer enabled.
- FishNet tick rate, PhysicsMode, transport, Host/Client RTT/PacketLoss, Player count.
- VSYNC/TargetFPS, 30/60/120 FPS 제약, fullscreen/window mode, 해상도와 DPI.
- Scene, Test Seed, World bounds, terrain profile, loaded chunk count.
- 테스트 전후 Git SHA, Date/time, Editor 로그, Build 로그, screenshot/video 위치.
- *Blocked/Skipped* reason과 실제 실행한 범위. 환경 부족을 Success로 간주하지 않는다.

## 11.4 정량 성능 기준 설정

고정된 절대 FPS 수치는 사용자의 PC/에디터 상태와 무관하게 합격선으로 지정할 수 없다. Phase 0에서 Baseline 측정 후 다음을 실제로 기록하여 **유의미한 회귀를 금지**한다.

| 지표 | 수집 위치 | 해석 |
|---|---|---|
| Camera world/visual Δ | LateUpdate instrumentation | tick stutter 감소 확인 |
| Player correction magnitude | FishNet prediction telemetry | correction 폭/빈도 추적 |
| DrawCalls, vertices, triangles | Unity Profiler/Stats | occlusion overlay 추가 비용 |
| Dynamic mesh builds | ChunkRenderPipeline metrics | Dither set 변동이 전체 리빌드 유발하는지 |
| WorldReady latency | Session logs | Amplified/restore 맵 준비 완료 지연 |
| Inventory command RTT/Rejects | GameCommandAdapter metrics | UI 지연/권한 검증 정상 |
| Minimap raster time/GC | Unity Profiler | 매 프레임 전체 스캔 여부 |
| Voxel import duration | Editor profiler | 64×64+ batch/undo 비용 |
| Save/Restore checksum | Domain/World snapshot | 정확한 데이터 보존 |

성능 회귀는 숫자와 원인(부하 증가/도시락 계산)을 함께 보고하고, 불필요한 루프/GC를 제거한다. Unity Editor vs Player build 측정 차이를 구분한다.

## 11.5 테스트 보고 상태 코드

- `PASS`: 명세와 기대값 만족, 근거 로그/캡처 있음.
- `FAIL`: 실제 실행해 불합격, 재현 방법과 bug ID 있음.
- `BLOCKED`: 환경/라이선스/접근 권한 등으로 수행 불가.
- `NOT_RUN`: 실행하지 않았음. 의도적으로 생략한 이유 필요.
- `PARTIAL`: 일부 수행/일부 합격, 남은 세부 항목 명시.

각 테스트는 이 중 정확히 하나로 분류하고 증거 경로를 남긴다.
