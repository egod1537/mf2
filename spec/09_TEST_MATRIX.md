# 09. 자동 + 수동 검증 매트릭스

## 테스트 원칙

- 테스트를 통과시켰다고 기록하려면 Codex가 **그 실행 결과**를 남겨야 한다. 이전 커밋 메시지의 `203/203`은 기준 참고일 뿐 현재 결과가 아니다.
- Unity 컴파일은 C# 파일만 검사하지 않고 씬/Prefab/asmdef/패키지/Weaver/Native Jobs에서 발생하는 오류까지 확인해야 한다.
- 런타임 테스트 불가 시 해당 row는 **NOT RUN**, 원인 기록. 테스트 케이스를 통과한 것으로 표시하지 않는다.
- 가능하면 Unity Test Runner EditMode → PlayMode → 실제 Host/Dedicated 네트워크 수직 슬라이스 순서로 반복.

## 자동 테스트 (EditMode/PlayMode)

| ID | 목표 | 테스트 내용 | 완료 기준 |
|---|---|---|---|
| T01 | Domain purity | MF2.Game.Domain asmdef 참조/코드 스캔 | FishNet/MonoBehaviour/GameObject/Scene/Audio 참조 0 |
| T02 | WorldCore invariants | Block/Region Fill, Negative coords, Dirty neighbor, ChunkLease, Dispose | 기존 World 테스트 보존 |
| T03 | Deterministic terrain | 동일 seed/settings → 동일 world signature, 변경 seed의 예측 가능성 | 결과 일관, 기존 golden 테스트 통과 |
| T04 | Session lifecycle | Create/Start/Step/Pause/Resume/Stop/Dispose, 재시작 | 중복 생성/이벤트/Dispose 없음 |
| T05 | Session restore | archive roundtrip, corrupted/partial failure rollback, generation monotonic | 실패 시 기존 세션 보존, 최신 generation |
| T06 | Client gating | snapshot before delta, old generation, tick/Revision stale, reordered arrivals | 불일치 데이터 거절, 정상 스냅샷 수신 |
| T07 | Bind/unbind | 동일 reader rebind, different reader, unspawn, pool reuse | event handler/identity stale 0 |
| T08 | Physics contract | Rigidbody registry + Raycast + Force/Teleport + unregister | correct EntityId, no missing body leak |
| T09 | Collision readiness | data built vs collider built, version stale, world replaced | collider 미생성 시 ready=false |
| T10 | Network command validation | invalid block ID, Fill extent, duplicate/out-of-order sequence, disconnect | rejected, no authoritative mutation |
| T11 | Scope disposal | session/scene/client scope 여러 번 create/dispose | Disposable/NativeContainer leak 및 double-dispose 없음 |
| T12 | Editor world | Paint, save/load, scene serialization, borrowed runtime world attach/detach | Editor와 Runtime 모두 원본 동작 |
| T13 | FMOD smoke | test bank load/one-shot playback or project verification | 기존 Smoke Test 영향 없음; 실행 불가 사유 기록 |

## 실제 시나리오 (수동/자동 통합)

| ID | 환경 | 절차 | 기대 결과 |
|---|---|---|---|
| I01 | Editor Host + remote client | Title→Outgame→Host→Start Test→Join | 양쪽 동일 terrain; 각각 Player 1개, owner camera/input 정상 |
| I02 | Host + remote client | WASD, Shift, Space, 점프·착지 | Prediction/Reconcile 유지, 원격 View 정상 |
| I03 | Dedicated Server + 2 clients | -batchmode -nographics server, 2 Join | 서버 UI 없이 Collider 준비, 모두 정상 Spawn/점프 |
| I04 | 3 peers | Client A sends world block command | Server+Client A+Client B 동일 block 최종 값 |
| I05 | Late join | 기존 player가 월드 변경 → 새 연결 | 최신 snapshot 후 spawn; 물리 충돌 정상 |
| I06 | Disconnect/reconnect | Client A 종료 후 동일/다른 연결 재접속 | stale mapping/원격 View/Ready ACK 없음 |
| I07 | Scene transition | Test→Ingame/Outgame→재입장 | Native/renderer/camera orphan 없음 |
| I08 | Restore | 저장→서버 세션 복원→재참가 | 기존/새 연결 stale generation 차단; world와 player state 일관 |
| I09 | Error/fault | 저장파일 손상 또는 Scope creation/Spawn 중 예외 | 오류 전 구동 상태 보존, 후보 리소스 정리 |
| I10 | Performance | 작은 Perlin world 초기화 및 20회 블록 수정 | 기존 기준 대비 의미 있는 CPU/GC/peak memory 퇴행 없음 |

## 실행 명령 (환경에 맞게 수정)

Unity 6 EditMode 예시. 설치 경로는 사용자 환경에 맞게 확인하고, **프로젝트에 테스트가 이미 있으면 정확한 asmdef/필터 활용**:

```bash
Unity -batchmode -nographics -projectPath "<PROJECT_PATH>" \
  -runTests -testPlatform EditMode \
  -testResults "<OUTPUT>/editmode-results.xml" -quit
```

Windows Editor 예시:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe' `
  -batchmode -nographics -projectPath '<PROJECT_PATH>' `
  -runTests -testPlatform EditMode `
  -testResults '<OUTPUT>\editmode-results.xml' -quit
```

FishNet의 Host/Client/Dedicated 통합은 별도 Build/ParrelSync/자동화 인터페이스가 필요할 수 있음. 실제 실행하지 않고 `host vertical slice passed`라고 적지 않는다.

## 필요 추가 테스트 위치 (기존 구조를 따를 것)

- `Assets/Game/Tests/Editor/`: Scope/Session/Replica/Command/Physics tests.
- `Assets/World/Tests/Editor/`: Chunk/Render readiness/WorldCore/WorldHost tests.
- `Assets/Entities/Player/`: 현재 별도 테스트 asmdef가 없다면 existing Game tests 또는 필요한 최소 test assembly.
- 기존 `Assets/Tests/FMOD/FmodPlaybackSmokeTest.cs` (삭제하지 않음).

## 회귀 성능 기준

- 리팩터링 전후 동일 seed/size에서 Terrain generation wall time, `WorldCore.Flush` 비용, peak GC alloc, renderer/collider build time 3회 이상 비교.
- 성능 목표값은 임의 고정하지 말고 먼저 baseline 측정. `3x` 등 임의 성공 숫자를 사후에 지어내지 않는다.
- vContainer per-entity Scope 도입 전 Entity count/Alloc estimate 기록. 블록 1개=GameObject 1개는 금지.

## 테스트 보고 서식

```text
Environment: Unity 6000.3.28f1, FishNet 4.7.3, VContainer 1.19.0, OS/build target
HEAD before: ...   HEAD after: ...
T01 PASS   Unity EditMode file: ...
T02 PASS   command: ...   tests: x/y   duration: ...
I01 NOT RUN (reason: dedicated build unavailable)
Issue: symptom, reproduction, root cause, fix, retest
Known gaps: ...
```
