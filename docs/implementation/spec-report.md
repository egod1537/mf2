# 통합 명세 구현 보고서

작성일: 2026-11-08  
기준 branch/base: `impl` / `4dbc84c90086308bb855ad2464a9a3fa88330946`  
소유 에셋 통합 시작점: `790bbfb2c676f765bcfc11d136c895c167d9028f`  
Unity: `6000.3.28f1`, FishNet `4.7.3`, VContainer `1.19.0`

이 보고서는 교체된 `spec/` 작업 트리를 수정하지 않고 실제 구현과 실행 증거를 기록한다.

## Phase 결과

| Phase | 결과 | 구현 근거 |
|---|---|---|
| P0 조사/출처 | 완료 | `spec-recon.md`, `asset-provenance.csv`, Caveworks build/hash/IL 조사, Minefarm0 commit/hash 조사 |
| 카메라/Hierarchy | 완료 | `PlayerCameraFollow`, 역할별 scene root, Builder/Prefab 재생성. physics/prediction root는 쓰지 않음 |
| 모델/공격/대시 | 완료 | 서버 권위 attack/dash와 기존 Prediction/Reconcile 유지. 소유자 허가에 따라 Minefarm0 GreenIdol/Player/Animator를 visual-only child로 연결하고 Attack sequence를 원본 Attack state에 바인딩 |
| Inventory/Quickbar/HUD | 완료(Compact 제외) | 30/7/6 슬롯, full/half, merge/swap, Shift quick transfer, owner-only replica, drag ghost/drop, HP/dash HUD. Caveworks Panel/Slot/Title/Selection/HP Bar 적용 |
| Dither occlusion | 완료 | 선택 voxel의 visual-only backing face, cross-chunk 중복 방지, collider/WorldCore 불변 |
| Minimap | 완료 | 49×49 bounded replica surface sampling, local marker/facing, change-driven redraw |
| Pixel→Voxel | 완료 | Fluent IMGUI Editor window, preview, palette conversion, safety cap, WorldHost batch/Undo/Save |
| Amplified terrain | 완료 | Normal 보존, deterministic Amplified profile, `-mf2Terrain amplified` 선택 |

## 주요 소유권과 신뢰 경계

- Inventory/action 원본은 ServerSession의 `PlayerRegistry`가 소유한다.
- network DTO는 client-supplied PlayerId/target을 받지 않는다. FishNet connection mapping과
  서버 physics/distance/LOS가 공격 대상을 결정한다.
- dash impulse는 기존 FishNet `[Replicate]` 안에서만 실행하고 reconcile state에 charge와
  cooldown을 포함한다. `NetworkTransform`이나 별도 `Physics.Simulate`는 추가하지 않았다.
- HUD/minimap은 `ClientStateCoordinator`의 replica만 읽는다. Host에서도 server session을
  presentation scope에 누출하지 않는다.
- dither backing mesh는 render 전용이며 collision mesh와 `WorldCore` block을 변경하지 않는다.
- Pixel→Voxel은 Editor authoring `WorldHost.Enqueue/Flush` 계약만 사용한다.

## 실행 검증

| 검증 | 실제 결과 |
|---|---|
| Unity compile | PASS, errors 0 / warnings 0 |
| EditMode full suite | **260/260 PASS**, failed 0, skipped 0, final duration 9.35s |
| Builder | `MF2/Build Multiplayer Test Slice` 실행 성공; 생성 씬/프리팹 wiring 테스트 PASS |
| Single-Editor Host | PASS: `Connected|Lobby|server=True|client=True`, connection 1 |
| Host → Test | PASS: player 1, Minefarm Animator 1 (`[0]Player`), inventory HUD 1, minimap 1, camera follow 1 |
| Disconnect → Outgame | PASS: `Disconnected`, server/client false |
| Game screenshots | `Temp/owned-assets-host-test.png`, `Temp/owned-assets-inventory-final.png` 캡처. 프로젝트 산출물로 commit하지 않음 |
| PlayMode test inventory | 등록 테스트 0개 |
| Remote client / Dedicated+2 | **NOT RUN** — 별도 player/build 인스턴스를 이번 실행에서 띄우지 않음 |
| Camera jitter 수치 A/B | **NOT RUN** — telemetry는 추가했으나 외부 client 이동 캡처 미수행 |
| Save/restore 수동 smoke | **NOT RUN** — persistence/inventory/action 자동 회귀 테스트는 PASS |

Host 첫 실행에서 camera GameObject 비활성화 중 `OnDisable` hierarchy 변경 오류를 재현했다.
비활성화 전에 명시적으로 parent를 복원하도록 수정했고, 동일 Host→Test→Disconnect 재실행에서
해당 오류가 사라졌다. 소유 모델 통합 후에는 inactive Animator에 `Animator.Update`를 호출하는
경고를 재현해 active hierarchy에서만 호출하도록 수정했다. 최종 Host→Test 재실행은 warning 0이며,
Unity console의 유일한 오류는 수정 범위 밖의 기존 `dev.tnayuki.unterm` native
`unterm.dll` load failure다.

## 호환성과 제한

- 기존 FishNet Prediction, TimeManager physics, water/WorldCore/save format, ProPixelizer,
  scene/prefab GUID와 WorldHost authoring 계약을 보존했다.
- 프로젝트 소유자가 2026-11-08 Minefarm0/Caveworks 통합을 명시적으로 허가했다. 가져온 파일,
  source revision, SHA-256과 목적지는 `asset-provenance.csv`에 기록했다.
- Caveworks DLL/font/audio는 필요하지 않아 포함하지 않았고 MF2 서버 권위 로직을 유지했다.
- Caveworks compact 명령과 별도 held-item mesh는 후속 시각/편의 parity 항목이다. Drag ghost/drop은 구현했다.
- Remote/Dedicated/late-join의 실제 멀티프로세스 증거는 아직 없으므로 완료로 주장하지 않는다.
