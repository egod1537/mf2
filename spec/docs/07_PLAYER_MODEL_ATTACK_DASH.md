# 07. T4 — Minefarm0 캐릭터 모델, 공격, 대시 이식

## 7.1 참조 소스와 차이점

| 소스 | 레포의 실제 경로 | 의도 |
|---|---|---|
| 캐릭터 FBX | `Assets/Model/Entity/Actor/[0] Player/GreenIdol.fbx` | 실제 모델 geometry/rig |
| 캐릭터 Prefab | `Assets/Model/Entity/Actor/[0] Player/Player.prefab` | 스켈레톤/렌더러 참조 |
| Animator Controller | `Assets/Model/Entity/Actor/[0] Player/animation/[0]Player.controller` | 상태와 Transition 조사 |
| 공격 애니메이션 | `Assets/Model/Entity/Actor/[0] Player/animation/Top/Attack.anim` | 실제 타격 표현 |
| 대시 트리거 | `Assets/Script/Scenes/InGame/Entity/PlayerController.cs` | `Dash()`, 입력 구독 |
| 대시 로직 | `Assets/Script/Scenes/InGame/Entity/Actor/Actorables/IDashable/PlayerDashable.cs` | 충전/쿨다운/임펄스 동작 |
| 공격 로직 | `Assets/Script/Scenes/InGame/Entity/Actor/Actorables/IAttackable/ActorAttackable.cs` | 공격 판정/데미지 참조 |
| Actor Controller | `Assets/Script/Scenes/InGame/Entity/Actor/ActorController.cs` | `Attack(target, damage)` 이벤트 |

Minefarm0 README 기준 마우스 좌클릭은 공격, 우클릭은 대시. 소스에서 확인된 대시 상수 `DELAY_DASH=0.5f`, `CONSTANT_DASH_FORCE=10f`, 0.25초 후 역방향 Impulse 약 절반, dashCount 감소. 이 값을 *조작감 참고*로 활용하되 실제 MF2 FishNet 움직임/충돌에 맞춰 튜닝한다.

## 7.2 모델 이식

1. 필요한 FBX, Prefab, Material, Texture, Animation Controller, Clips, 관련 `.meta`를 종속성 그래프로 수집. 원본 전체 Unity 프로젝트/전용 플러그인 무더기 복사 금지.
2. 모델 원본의 라이선스/출처 확인(예: 원본 외부 3D asset pack 가능성). 사용자 소유 프로젝트라도 제3자 라이선스는 별개.
3. `Assets/Entities/Player/`에 **MF2 전용 Visual prefab** 또는 visual subroot로 통합. 공유 리소스는 `Assets/Entities/Shared/`.
4. 기존 `BlockPlayer.prefab`의 `NetworkObject`, `Rigidbody`, `CapsuleCollider`, `BlockPlayerController`, InputSource, View, Binder, BindingGate는 그대로 유지. **렌더링 계층만 교체**하고 물리 캡슐/네트워크 루트는 보존.
5. FBX Rig Humanoid/Generic, bone, bind pose, material/shader, scale, pivot, forward axis를 Unity 6.3에서 검증.
6. Idle/Walk/Run/Jump/Fall/Attack/Dash 등 실제 존재하는 Clip만 명확하게 연결. 없는 Clip을 임의로 존재하는 것으로 간주하지 않는다. 필요한 경우 새 애니메이션 제작/절차 애니메이션으로 보완한 사실을 기록.
7. `BlockPlayerView.ConfigureVisual`/`ConfigureRig`, `SetFacing`과 Animator 사이 책임 정리: facingRoot에 캐릭터 회전, Animator는 visual root, physics root에 root motion 쓰기 금지.
8. Owner/Remote 모두 모델이 보이고, Owner camera/AudioListener는 owner에서만 활성. Offline Prefab도 같은 표시/시각 기준으로 동작하도록 Builder 수정.

## 7.3 Dash 설계 — 물리/권한 분리

최종 구조:

```text
Owner Input edge (dash pressed once)
    -> MoveData / predicted action request
    -> PredictionRigidbody impulse/velocity at tick
    -> Server validates dash availability
    -> Reconcile on authority correction
    -> ClientState dash charge/cooldown/action revision
    -> View/Animator + HUD dash bar
```

- 입력은 `BlockPlayerInputSource.Capture()`에서 프레임 입력을 수집하고 prediction tick `Consume()`에서 **atomic 1회 소비**. 프레임 경계에서 누락/중복 dash 방지.
- `BlockPlayerController.MoveData`를 확장하거나 명시적 action-prediction pipeline을 정의. Dash initiation의 tick 및 direction을 명확히 기록, prediction replay 중 중복 충전/공격 이벤트 발생 방지.
- Dash 방향은 플레이어 facing/이동/카메라 방향 중 원본 동작과 기존 MF2 조작을 비교하여 확정. 길이가 0인 방향 및 입력 미존재 처리.
- 가속/감속/마찰, 0.25초 delayed brake 등을 **Unity Time.deltaTime 기반 Coroutine으로 server/client 각각 실행하는 식으로 구현하면 불일치 위험**. prediction tick 기반 `dashRemainingTicks` 또는 시간 누적 상태로 결정적 입력 효과를 설계.
- Dash 가능 여부는 Domain `DashSystem` 또는 Player capability 상태로 권위 있게 검증: charges, cooldown, 상태(사망/경직/충돌 등 실제 도입된 상태), rate limit.
- `Rigidbody.AddForce()`를 기존 예측 경로 외 별도 Unity Update에서 호출하지 않는다. `PredictionRigidbody`가 사용되는 동일 예측 작업 단계에서 한 번만 적용.
- 예측 중 시각 FX/애니메이션은 즉시 재생 가능하나 server reject/reconcile을 받아 올바른 상태로 수렴.
- HUD dash count/remaining cooldown은 실제 replicated 상태에 연결. 저장/복원 정책(충전 상태 유지 여부)을 문서화.
- 물리적 벽/바닥 충돌과 2D 카메라 표현/실제 3D velocity가 모순되지 않도록 테스트.

## 7.4 Attack 설계 — 서버 권한과 시각 분리

```text
Mouse Left Click -> InputController
  -> AttackRequest(attack sequence/tick, target hint)
  -> authenticated server command validation
  -> AttackSystem + HealthSystem
  -> damage event / state revision
  -> client replica event -> attack/hit animation & VFX
```

- 공격 대상을 client가 임의 지정했다고 서버가 그대로 믿지 않는다. 거리/방향/FOV(필요 시)/LOS/cooldown/target lifecycle/Owner connection/rate limit 검증.
- 실제 원본이 근접/원거리/채굴 중 무엇인지 디컴파일과 source code 확인 후 default 공격 경로 확정. MF2에 적 대상 NPC가 없다면 최소 `AttackableTestTarget` 등을 `Test` 씬에 명확히 개발용으로 두고 엔티티 상태/HealthSystem과 연결.
- `Attack.anim` 재생은 공격 성립/시작과 연결. 타격 프레임과 서버 damage 적용 타이밍, latency 보정 정책을 명시. 애니메이션 Event에서 권위 데미지를 직접 적용하지 않는다.
- attack/interaction 및 Hotbar selected item과의 우선순위 정의: UI open이면 공격 차단; item use/attack이 충돌하지 않음.
- client VFX/animation이 `OnStartClient`/reconnect 이후 중복 구독하여 두 번 실행되지 않음.
- 원본 타격감(사운드/FM0D 가능하면, swing, hit, cooldown)을 참고하되 사운드 에셋 라이선스 확인, Dedicated에서 오디오 처리 금지.

## 7.5 PlayerModel, 저장, 재접속과 상태 정의

- 기존 `PlayerModel`의 health/life state를 확장해야 할 때에는 Domain System이 유일한 작성자.
- runtime transient action state와 저장 가능한 player state를 분리(예: 대시 남은 Tick은 저장 정책 선택). 새 필드에 대한 migration/default behavior 정의.
- Animator state는 authoritative source가 아님. 네트워크에 전송할 최소 이벤트(attack sequence, dash start tick/direction, health change)와 보간 가능한 state만 보낸다.
- 게임 씬 교체에서도 PlayerId 유지, visual은 rebind, old subscriptions/coroutines/timers 정리.
- `BlockPlayerController`의 `SyncVar<Guid> playerId`, FishNet Prediction/NetworkObject 직렬화가 기존처럼 정상 동작.

## 7.6 합격 기준

- [ ] 새 모델이 Owner/Remote/Offline 프리팹에 정확한 scale/facing/collider alignment로 출력.
- [ ] Idle/Walk/Run/Jump/Fall/Attack/Dash 중 실제 구현된 동작을 검증, 원본과 비교한 animation/rig 캡처 존재.
- [ ] 대시 입력 1회=1회 발동, charges/cooldown 감소·회복·표시, 예측/재조정 시 중복 없음.
- [ ] 벽/경사/점프/공중 Dash 정책 결정과 테스트.
- [ ] 공격은 target과 거리·가시성·쿨다운·생존 상태 검증, 서버에서 HP 변경.
- [ ] 중복/위조/빠른 연타/늦게 도착한 입력/다른 플레이어 target 공격에 대해 서버가 안전하게 처리.
- [ ] Host+Client 및 Dedicated+2에서 owner/remote attack/dash animation 동기화.
- [ ] scene transition, owner despawn/rejoin, late join, save/restore 이후 상태/visual 정합.
- [ ] Builder 재실행 후 스크립트/Animator/Material/Camera 참조가 깨지지 않음.
- [ ] 기존 movement, jump, capsule physics, WorldReady가 회귀하지 않음.

## 7.7 원본과 다른 아키텍처 유지 이유

Minefarm0 `PlayerDashable`은 UniRx Update 흐름과 직접 Rigidbody Impulse/지연 호출을 사용한다. MF2는 FishNet PredictionRigidbody와 Domain authority를 사용한다. **원본을 바이너리/코드 1:1 복붙하는 것이 목표가 아니라 모델과 애니메이션/조작감을 보존하되 MF2에 맞게 책임을 이식하는 것**이다.
