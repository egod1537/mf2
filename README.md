# MF2

## FishNet 멀티플레이 테스트

Unity `6000.3.28f1`, FishNet `4.7.3` 기준의 직접 IP 멀티플레이 수직 슬라이스다. 시작 씬은 `Assets/Scenes/Title.unity`이며 네트워크 게임 씬 전환은 서버가 FishNet Global Scene으로 수행한다.

### Editor Host + Client

1. `Title` 씬에서 Play 후 `Start`를 누른다.
2. Outgame에서 기본 Port(현재 Tugboat 설정값, 기본 7770)로 `Host`를 누른다.
3. 연결 후 서버 전용 `Start Test`를 누른다.
4. 별도 빌드 또는 ParrelSync clone에서 `127.0.0.1`과 같은 Port로 `Join`한다.
5. 각 창에서 WASD 이동, Shift 달리기, Space 점프를 확인한다.

클라이언트가 먼저 Join하여 실패해도 Outgame에서 Host가 준비된 뒤 다시 Join할 수 있다. Disconnect는 플레이어를 서버에서 Despawn하고 Outgame으로 복귀시킨다.

### Dedicated Server

Dedicated Server 빌드 타깃 또는 `-batchmode -nographics`로 `Title` 시작 씬을 실행한다. UI, 카메라, 로컬 플레이어는 비활성화되고 서버만 시작한다. Port는 `-port 7770` 또는 `-mf2Port 7770`으로 바꿀 수 있다. 최초 인증 클라이언트가 접속하면 `Test` 씬을 한 번 자동 시작하며, 이후 참가자는 FishNet Global Scene 동기화로 같은 씬에 진입한다.

NAT traversal, relay, matchmaking, room browser는 범위 밖이다. 인터넷 연결 시 Tugboat UDP Port에 대한 방화벽/포트 포워딩을 별도로 설정해야 한다.

### 검증

- EditMode 순수 로직 테스트: Endpoint 입력 검증, Connection별 중복 Spawn 가드.
- 수동: Host + 1 Client, Dedicated + 2 Clients, 실패 후 재접속, Client/Host 종료 정리, Test/Ingame 전환.
- 씬/Prefab 재생성이 필요하면 Unity 메뉴 `MF2 > Build Multiplayer Test Slice`를 사용한다.

`BlockPlayer`는 `NetworkObject + Rigidbody + CapsuleCollider`이며 FishNet `Replicate`, `Reconcile`, `PredictionRigidbody`와 TimeManager 물리 틱을 사용한다. 같은 오브젝트에 NetworkTransform은 두지 않는다.
