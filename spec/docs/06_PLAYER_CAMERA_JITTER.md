# 06. T3 — 플레이어 이동/카메라 떨림(Jitter) 해결

## 6.1 현행 구조에서 확인된 것

- `BlockPlayerController`가 `TickNetworkBehaviour` 기반 FishNet `[Replicate]`, `[Reconcile]` 및 `PredictionRigidbody` 사용.
- `TimeManager_OnTick()`에서 `PerformReplicate`, `OnPostTick`에서 `CreateReconcile` 수행.
- `Awake()`에서 `Rigidbody.interpolation=None`으로 설정하고 PhysX/FishNet이 movement state를 소유.
- `BlockPlayerView`는 visual/facing/camera 참조 및 `LateUpdate()` 내 애니메이션 처리.
- `MultiplayerSliceBuilder`가 `OwnerCameraRig`를 **Rigidbody 플레이어 root의 자식**으로 생성하고 원근/픽셀 안정 렌즈 설정.

위 내용은 코드 사실이다. 어느 원인이 실제 jitter를 발생시키는지는 **동작 실측 전에는 확정하지 않는다.**

## 6.2 진단 우선 순서

1. 기준 시나리오: Editor Host 단독, 빌드 Host, 별도 Client, LAN/localhost, 정지→WASD 직진→대각→점프/낙하→대시→회전. FPS 30/60/120, TimeManager tick 30/60 등 가능한 설정에서 반복.
2. 프레임별 로그에 다음을 같은 시간축으로 기록: `Time.frameCount`, `Time.time`, `Time.fixedTime`, FishNet tick, `Rigidbody.position`, visualRoot world position, Camera world position, actual target position, `linearVelocity`, reconcile 횟수/보정량.
3. 플레이어/카메라의 **스크린좌표 프레임간 델타**와 예상 속도 오차를 수치화. 30fps에서 자연스러운 큰 delta와 지터(고빈도 방향 반전)를 구분.
4. 부모-자식 관계, Update/LateUpdate/FixedUpdate/TimeManager.OnTick 중 **누가 마지막으로 Transform을 쓰는지** 확인.
5. FishNet prediction 자체 visual smoothing 기능/권장 설정 조사. `RigidbodyInterpolation`과 FishNet 보간의 **중복 적용** 여부 확인.
6. ProPixelizer pixel snapping, camera orthographic lens, screen-space quantization이 시각적 덜덜거림을 유발하는지 ON/OFF A/B 비교.
7. 원인을 `Player physics`, `Prediction correction`, `Camera tracking`, `Pixel snapping`, `Animation visual offset`로 분리하여 수정.

## 6.3 목표 아키텍처

```text
Server authoritative physics + FishNet Prediction
              │
       Rigidbody root (물리)
              │ read-only sample
       Render interpolation target (표현 전용)
           ├── VisualRoot / Animator / facing
           └── Owner camera follow target
```

카메라·visual은 **같은 보간된 상태**를 기준으로 렌더하고, 움직이는 물리 부모 아래 Camera가 별도 조치 없이 매 tick 텔레포트하는 구조를 제거한다. 필요하면 Owner camera rig의 부모를 로컬 Presentation root로 옮기거나 parent motion을 상쇄하고, 대상의 위치/회전 업데이트를 LateUpdate/카메라 업데이트 시점에 수행한다. `NetworkObject`/Rigidbody를 다른 부모로 무조건 이동하지 않는다.

### 구현 주의

- `PlayerId`/owner gating, AudioListener 1개, scene camera disable 규칙 유지.
- 카메라 **위치**를 smooth하되 입력 기준 카메라 방향과 visual facing을 지나치게 지연시키지 않는다. Isometric zoom 및 orthographic scale/픽셀 정렬은 보존.
- Physics root position은 `BlockPlayerController`/Prediction 외 코드에서 직접 쓰지 않음. Binder는 상태만 그린다.
- interpolation sample이 충분하지 않은 첫 프레임, respawn, teleport, restore 시 스냅 처리. 누적 지연/overshoot 방지.
- remote player와 local owner는 표시 정책이 다를 수 있으나, 모두 동일한 state ownership을 준수.
- `FixedUpdate`와 `TimeManager`의 중복 시뮬레이션/속도 쓰기 금지. `Physics.Simulate` 수동 호출 금지.
- Camera 자체 smoothing과 FishNet graphical interpolation을 중복 사용하지 않도록 하나의 책임점을 선정.

## 6.4 설계/수정 결정 기록

각 후보 아래에서 실제 원인과 배제 근거를 남긴다.

| 원인 후보 | 관찰 방법 | 권장 해결 |
|---|---|---|
| Rigidbody tick-step을 Camera가 따라감 | frame sample vs tick sample/자식 camera | render target interpolation / camera follow 분리 |
| Reconcile correction | correction magnitude/네트워크 지연 비교 | prediction pipeline 설정·시각 보간 조정 |
| 중복 Rigidbody interpolation | 활성 보간 경로 수/모드 확인 | 한 경로만 사용 |
| pixel snapping | ProPixelizer OFF A/B | pixel grid 기준/Camera 렌즈 및 presentation 정렬 조정 |
| movement 구현 불연속 | Rigidbody velocity/time 로그 | tick input consume/속도 적용 순서 수정 |
| Animator root motion | Animator/rig 이동 검사 | visual-only animation, physics writer 중복 제거 |

## 6.5 완료 기준

- [ ] Host 단독, Host+1 Client, Dedicated+2 Clients에서 owner/remote 움직임 테스트.
- [ ] WASD 직진/대각/정지/걷기-달리기 전환/점프/낙하/대시/벽 접촉/회전/줌.
- [ ] 30/60/120fps(가능한 환경)에서 **수정 전후 카메라/플레이어 위치 델타 통계와 녹화** 제출.
- [ ] Player root와 visual/camera의 누가 Transform을 변경하는지 코드상 단일 책임 확보.
- [ ] Owner 카메라는 자신의 플레이어만 따라가며 재스폰·씬 전환·Disconnect 때 참조/리스너 정리.
- [ ] `PredictionRigidbody`, Replicate/Reconcile, collision/ground checks, movement speed, jump behavior 유지.
- [ ] Dedicated server는 Camera/AudioListener를 만들지 않음.
- [ ] Pixelizer ON/OFF에서도 특별한 눈에 띄는 떨림이나 누적 추적 오차 없음.

정량적인 '완전히 0 jitter'를 무근거로 약속하지 말고, 비교 가능 조건에서 유의미한 개선을 실제 증거로 제시한다.
