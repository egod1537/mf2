# 05. GameSession, MVC, Entity Binding, Physics 설계

## 상태·행동 구분

- **Model**: `PlayerModel`, `EntityState` 등 `struct` 또는 DTO; 단일 Registry가 보관. `struct`라고 모든 소유를 Scope에 개별 등록할 필요 없음.
- **Domain System / CommandProcessor**: pure C#에서 게임 규칙/명령 처리, 권위 있는 Model mutation.
- **Controller : MonoBehaviour**: Unity input/출력 경계; `BlockPlayerController`는 기존 FishNet Prediction entrypoint라는 구체적인 이유로 `TickNetworkBehaviour`를 유지.
- **View : MonoBehaviour**: 외형/Animator/Camera/HUD/VFX/Audio emitter.
- **Binder**: `PlayerId`/EntityId + `IEntityStateReader`로 읽은 Snapshot을 View에 반영, 변경 이벤트에 구독/해제.

## Player 이동과 점프의 실제 흐름

```text
Owner InputSource.Capture
   ↓ FishNet [Replicate] movement input
BlockPlayerController/PredictionRigidbody
   ↓ Unity Physics collision/movement
Rigidbody.position/velocity  (physics-owned)
   ↓ IPhysicsQuery -> optional Domain snapshot
GameSession systems  (health/inventory/commands; not transform owner)
   ↓ Player state replication
ClientStateCoordinator -> EntityViewBinder -> BlockPlayerView
```

**실제 Player Prediction MoveData의 처리/리플레이/재조정은 기존 메서드 경로를 유지한다.** 일반 `ServerRpc` 명령으로 전환하거나 Controller/Binder가 강제로 Rigidbody.position을 매 프레임 쓰면 안 된다.

## Binder 개선 가능 지점

현재 `BlockPlayerController`, `PlayerPresentationBindingGate`, `EntityViewBinder`, `PlayerReplicationAdapter`가 identity/bind readiness를 일부 분담한다. 각 중간 상태 값의 단일 소유자를 표로 작성한 뒤, 상태 중복이 확인되면 축소한다.

- Binder에는 *해당 EntityId의 Model 읽기/렌더 적용*만 남긴다.
- Network identity/Spawn 상태는 FishNet Adapter와 binding gate의 좁은 경계에 남긴다.
- `PlayerPresentationBindingGate`가 필요 이상의 readiness flag를 갖고 있다면 테스트 추가 후 결합을 줄이되, **비동기 입장/늦은 Snapshot/Host**에서 premature bind가 발생하지 않게 유지.
- Reader 교체, 동일 PlayerId bind 재호출, Despawn, scene unload, pool reuse 시 event handler 수를 일정하게 유지.
- 읽기 Snapshots의 revision/latency를 고려해 View가 동일 revision이면 불필요한 재적용 생략할 수 있다. 미리 성능 최적화하지 말 것.

## Physics에 대한 권고

- `IPhysicsQuery`는 Raycast/Body snapshot 조회, `IPhysicsCommands`는 Force/Teleport 등 명시적 요청. 구현체 `UnityPhysicsAdapter`는 ServerSession scope 내에서 등록된 Rigidbody/Collider 맵을 관리.
- Dynamic Rigidbody는 **물리 엔진 원본**, `WorldState.Position`을 매 Tick 쓰지 않음. WorldState를 읽기 캐시로 추가할 때 origin/tick/version 기록.
- `Teleport`은 물리 객체의 권위 있는 위치를 변경하고 적절한 Physics.SyncTransforms/Prediction reset/reconcile에 반영할 필요가 있는지 검증.
- `PhysicsScene.Simulate`는 FishNet TimeManager가 PhysicsMode.TimeManager를 소유하므로 중복 수동 호출 금지.
- Physics result를 도메인이 읽어야 할 때 **고정 Tick 경계에서 일관된 Snapshot**을 제공. 게임 규칙을 Unity `OnCollisionEnter`에 직접 누적하지 말고 Adapter가 명령/사실 Event로 변환.
- `PlayerId`가 모든 Collider를 표현하기 적절한지 점검. 아직 Player만 있다면 범용 `EntityId` 전면 변경은 불필요. 새 타입을 도입할 경우 마이그레이션 비용/테스트를 먼저 제출.

## 기능별 테스트

- `PlayerModel` struct 읽기 Snapshot 수정이 Registry 원본을 바꾸지 않음.
- 게임 규칙만 단독 Unit Test 가능, MonoBehaviour/NetworkManager 없이 플레이어 등록/Health command 처리.
- Controller는 InputSource 없이 서버에서 안전한 No-op.
- owner-only camera 한 개, remote player view 카메라·AudioListener 활성화 없음.
- Collider/PhysicsSnapshot의 PlayerId map register/unregister 반복 후 Count=0.
- 죽은 오브젝트나 다른 세션의 Reader를 Binder가 참조하지 않음.
