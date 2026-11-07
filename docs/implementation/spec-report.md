# 통합 명세 구현 보고서

작성일: 2026-11-08  
기준 branch/HEAD: `impl` / `4dbc84c90086308bb855ad2464a9a3fa88330946`  
Unity: `6000.3.28f1`, FishNet `4.7.3`, VContainer `1.19.0`

이 보고서는 교체된 `spec/` 작업 트리를 수정하지 않고 실제 구현과 실행 증거를 기록한다.

## Phase 결과

| Phase | 결과 | 구현 근거 |
|---|---|---|
| P0 조사/출처 | 완료 | `spec-recon.md`, `asset-provenance.csv`, Caveworks build/hash/IL 조사, Minefarm0 commit/hash 조사 |
| 카메라/Hierarchy | 완료 | `PlayerCameraFollow`, 역할별 scene root, Builder/Prefab 재생성. physics/prediction root는 쓰지 않음 |
| 모델/공격/대시 | 부분 완료 | 서버 권위 attack/dash, 입력/Prediction/Reconcile/replication/HUD 구현. GreenIdol FBX/Attack.anim은 라이선스 부재로 BLOCKED |
| Inventory/Quickbar/HUD | 기능 완료, 시각 부분 | 30/7/6 슬롯, full/half, merge/swap, Shift quick transfer, owner-only replica, HP/dash HUD. Caveworks 유료 UI 에셋은 미복사 |
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
| EditMode full suite | **260/260 PASS**, failed 0, skipped 0, duration 8.6s |
| Builder | `MF2/Build Multiplayer Test Slice` 실행 성공; 생성 씬/프리팹 wiring 테스트 PASS |
| Single-Editor Host | PASS: `Connected|Host|server=True|client=True`, connection 1 |
| Host → Test | PASS: player 1, inventory HUD 1, minimap 1, camera follow 1 |
| Disconnect → Outgame | PASS: `Disconnected`, server/client false |
| Game screenshot | `Temp/spec-host-test.png` 캡처. 프로젝트 산출물로 commit하지 않음 |
| PlayMode test inventory | 등록 테스트 0개 |
| Remote client / Dedicated+2 | **NOT RUN** — 별도 player/build 인스턴스를 이번 실행에서 띄우지 않음 |
| Camera jitter 수치 A/B | **NOT RUN** — telemetry는 추가했으나 외부 client 이동 캡처 미수행 |
| Save/restore 수동 smoke | **NOT RUN** — persistence/inventory/action 자동 회귀 테스트는 PASS |

Host 첫 실행에서 camera GameObject 비활성화 중 `OnDisable` hierarchy 변경 오류를 재현했다.
비활성화 전에 명시적으로 parent를 복원하도록 수정했고, 동일 Host→Test→Disconnect 재실행에서
해당 오류가 사라졌다. 현재 Unity console의 유일한 오류는 수정 범위 밖의 기존
`dev.tnayuki.unterm` native `unterm.dll` load failure다.

## 호환성과 제한

- 기존 FishNet Prediction, TimeManager physics, water/WorldCore/save format, ProPixelizer,
  scene/prefab GUID와 WorldHost authoring 계약을 보존했다.
- Caveworks texture/font/audio/DLL은 재배포 허가를 확인하지 못해 복사하지 않았다.
- Minefarm0 `GreenIdol.fbx`, `Player.prefab`, `Attack.anim`은 공개 checkout에 루트 라이선스와
  원저작/재배포 근거가 없어 이식하지 않았다. 공격/대시 동작은 MF2 블록 rig로 독립 구현했다.
- 완전한 Caveworks drag ghost/compact와 held-item mesh는 후속 시각 parity 항목이다.
- Remote/Dedicated/late-join의 실제 멀티프로세스 증거는 아직 없으므로 완료로 주장하지 않는다.
