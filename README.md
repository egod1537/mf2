# MF2

## FishNet 멀티플레이 테스트

Unity `6000.3.28f1`, FishNet `4.7.3` 기준의 직접 IP 멀티플레이 수직 슬라이스다. 시작 씬은 `Assets/Scenes/Title.unity`이며 네트워크 게임 씬 전환은 서버가 FishNet Global Scene으로 수행한다.

규범적 아키텍처 결정, 비목표, 현재 검증 기록은
[`docs/07_TECHNICAL_DECISIONS.md`](docs/07_TECHNICAL_DECISIONS.md)에 정리되어 있다.
현재 코드 기준의 감사 결과와 운영 순서는
[`refactor-baseline`](docs/architecture/refactor-baseline.md),
[`architecture`](docs/architecture/architecture.md),
[`lifecycle`](docs/architecture/lifecycle.md),
[`refactor-report`](docs/architecture/refactor-report.md),
[`53fd8bd review`](docs/architecture/refactor-review-53fd8bd.md)에서 확인할 수 있다.

## Runtime architecture

```text
ApplicationLifetimeScope                 persistent NetworkBootstrap infrastructure
├── ServerSessionLifetimeScope           server/host authority only
│   ├── GameSession + PlayerRegistry
│   ├── SimulationWorld (authoritative WorldCore owner)
│   ├── domain systems / command authorization
│   └── Unity physics + FishNet server adapters
└── ClientPresentationLifetimeScope      client/host local presentation only
    ├── ClientStateCoordinator           read-only replicated player/world state
    └── SceneLifetimeScope               Test/Ingame scene views and input
        ├── Player View/Binder context
        └── WorldViewBridge + chunk renderer projection
```

Host에서도 server scope와 client-presentation scope는 형제다. View/Binder는
`ClientStateCoordinator`만 읽으며 authoritative `GameSession`이나 서버 `PlayerRegistry`를
resolve하지 않는다. Dedicated Server는 client-presentation/scene scope를 만들지 않는다.

Player의 GameObject 책임은 다음과 같이 나뉜다.

- `PlayerModel`/`PlayerRegistry`: Session이 소유하는 health/life-state struct 원본.
- `BlockPlayerController`: FishNet `Replicate`/`Reconcile`과 `PredictionRigidbody` 접착제.
- `BlockPlayerInputSource`: local owner 입력을 수집하고 prediction tick에서 atomic consume.
- `BlockPlayerView`: Minefarm0 Animator/visual root와 owner camera/audio만 제어.
- `EntityViewBinder`: `PlayerId`로 client replica를 읽어 View에 즉시/변경 시 투영.
- `PlayerPresentationBindingGate`: client replica dependency와 replicated `PlayerId`가 모두
  준비된 뒤 Bind하고 despawn/pool reuse에서 이전 구독과 ID를 제거.

Dynamic Player transform의 원본은 Unity `Rigidbody`와 FishNet prediction이다. Domain
`PlayerModel`에는 별도 mutable position을 두지 않으며 Binder가 Transform을 쓰지 않는다.
`NetworkBootstrap`이 `PhysicsMode.TimeManager`를 설정하고, 프로젝트 코드는 별도
`Physics.Simulate`를 호출하지 않는다. Domain tick은 FishNet post-physics tick에서 한 번만
실행된다.

World는 block별 GameObject/Binder를 만들지 않는다. EditMode의 `WorldHost`는 authoring과
preview용 `WorldCore`를 소유하지만, PlayMode에서는 client replica의 `WorldCore`를 빌려
기존 chunk mesh pipeline에 dirty chunk를 투영한다. Authoritative PlayMode `WorldCore`의
단일 소유자는 server `SimulationWorld`다.

### Layered terrain and water

새 서버 세션의 기본 Perlin 지형은 stone → dirt 3칸 → grass 표면으로 생성되고, 높이 7
아래의 골짜기는 정적인 `minecraft:water` 블록으로 채워진다. 물은 별도 Water 렌더 패스와
투명 머티리얼을 사용하며, 같은 물끼리의 내부 면은 만들지 않는다. 지형과 맞닿은 물은
시각 메시에는 포함되지만 solid 전용 `MeshCollider`에서는 제외되므로 걸어 설 수 없다.
스폰 탐색도 block trait을 사용해 물이 아닌 마른 solid 바닥만 선택한다.

초기 월드 복제는 Tugboat MTU보다 작은 최대 48-block 파트로 전송하고, 모든 파트를 검증한
뒤 client `WorldCore`를 한 번에 교체한다. collider 재구축 전에는 WorldReady를 보내지 않는다.
일반 Tick delta도 48 blocks를 넘지 않는다. 그보다 큰 Fill 결과는 같은 bounded snapshot
경로로 재동기화해 동일 Tick의 일부만 client에 적용되는 상태를 만들지 않는다.
블록/렌더 manifest를 다시 만들 때는 Unity 메뉴의 다음 항목을 순서대로 실행한다.

1. `Tools > Minecraft > Rebuild Block Registry Manifest`
2. `Tools > Minecraft > Rebuild Block Render Manifest`

두 빌더 모두 built-in water 항목과 일반/ProPixelizer water 머티리얼을 결정적으로 다시
생성한다. 기존 저장은 재생성하거나 재레이어링하지 않으며, water ID도 다른 블록처럼
Region JSON의 `Block(Id, State)`로 그대로 보존된다.

### Startup and scene lifecycle

1. `Title`의 persistent `NetworkBootstrap`이 application scope와 FishNet infrastructure를
   만든다.
2. 서버/Host 시작 시 server session scope가 match당 한 번 생성된다.
3. 일반 client/Host는 별도의 client-presentation scope를 만든다.
4. `Test`/`Ingame`의 `SceneLifetimeScope`가 scene root를 주입한다. Scene unload는 View만
   제거하며 server session을 종료하지 않는다.
5. 서버 `PlayerSpawner`는 connection을 stable `PlayerId`로 매핑하고 Rigidbody와 현재
   `NetworkObject`를 session map에 등록한다. Network scene 교체에서는 같은 ID를 유지한다.
6. client spawn에서는 `PlayerId` sync와 replica 준비 순서에 관계없이 gate가 둘을 기다린
   뒤 Binder를 연결한다. Despawn/disconnect/reconnect는 중복 안전하게 Unbind한다.

서버 world command는 인증된 FishNet connection에서만 받고, 서버가 생성한 `PlayerId`,
등록된 Rigidbody, block manifest, interaction reach를 검증한 뒤 `GameSession`에 제출한다.
`Test`/`Ingame`에서는 F6으로 근처 debug block을 toggle하여 command → replication →
`WorldViewBridge` → chunk renderer 경로를 확인할 수 있다.

### Editor Host + Client

1. `Title` 씬에서 Play 후 `Start`를 누른다.
2. Outgame에서 기본 Port(현재 Tugboat 설정값, 기본 7770)로 `Host`를 누른다.
3. 연결 후 서버 전용 `Start Test`를 누른다.
4. 별도 빌드 또는 ParrelSync clone에서 `127.0.0.1`과 같은 Port로 `Join`한다.
5. 각 창에서 WASD 이동, Shift 달리기, Space 점프를 확인한다.

추가 조작은 다음과 같다.

- 마우스 이동: 캐릭터가 월드 바닥면의 포인터 방향을 바라본다.
- 좌클릭: 서버 판정 근접 공격, 우클릭: Prediction 경로의 대시.
- `E`: 인벤토리 열기/닫기. 열린 동안 이동·점프·공격·대시는 입력되지 않는다.
- `1`~`7` 또는 마우스 휠: 퀵슬롯 선택.
- 슬롯 좌클릭: 전체 스택 선택, 우클릭: 절반 선택, Shift+클릭: 빠른 이동.
- 슬롯 드래그: Caveworks selection/drag ghost로 대상 슬롯에 drop.

게임 씬 우측 상단 미니맵은 authoritative snapshot/delta로 만든 client replica만 읽는다.
서버 `GameSession`이나 서버 `WorldCore`를 직접 참조하지 않는다.

클라이언트가 먼저 Join하여 실패해도 Outgame에서 Host가 준비된 뒤 다시 Join할 수 있다. Disconnect는 플레이어를 서버에서 Despawn하고 Outgame으로 복귀시킨다.

### Dedicated Server

Dedicated Server 빌드 타깃 또는 `-batchmode -nographics`로 `Title` 시작 씬을 실행한다. UI, 카메라, 로컬 플레이어는 비활성화되고 서버만 시작한다. Port는 `-port 7770` 또는 `-mf2Port 7770`으로 바꿀 수 있다. 최초 인증 클라이언트가 접속하면 `Test` 씬을 한 번 자동 시작하며, 이후 참가자는 FishNet Global Scene 동기화로 같은 씬에 진입한다.

NAT traversal, relay, matchmaking, room browser는 범위 밖이다. 인터넷 연결 시 Tugboat UDP Port에 대한 방화벽/포트 포워딩을 별도로 설정해야 한다.

### Terrain profile and pixel-to-voxel tool

기본 지형은 기존 `Normal` 프로필이다. 서버/Host 실행 인수에
`-mf2Terrain amplified`를 추가하면 같은 seed에서 결정적인 Amplified 높이 프로필을 쓴다.
클라이언트는 지형을 독립 생성하지 않고 서버 snapshot을 받는다.

Unity 메뉴 `Tools > World > Pixel Art to Voxels`는 Texture2D/Sprite의 불투명 픽셀을
현재 프로젝트 블록 팔레트에 매핑한다. XY/XZ/ZY 평면, pixel scale, depth와 alpha threshold를
지정할 수 있으며 SceneView preview 후 선택한 Editor `WorldHost`에 한 번의 command batch로
적용한다. 최대 65,536 블록 제한과 Undo/Redo를 제공한다.

### Owner-authorized visual assets

프로젝트 소유자의 명시적 허가에 따라 Minefarm0 플레이어 model/material/animation은
`Assets/Entities/Player/MinefarmVisual`, Caveworks 공용 inventory sprites는
`Assets/Entities/Shared/CaveworksUI`에 원본 `.meta`와 함께 배치한다. Scene/Player prefab을
다시 생성해도 `MF2 > Build Multiplayer Test Slice`가 모델 Animator, ProPixelizer material,
Caveworks Panel/Slot/Title/Selection/HP Bar 연결을 동일하게 복구한다. 출처 revision과
SHA-256은 `docs/implementation/asset-provenance.csv`에 기록한다.

### 검증

- EditMode: Domain snapshot copy, command validation, client snapshot/delta ordering,
  Binder immediate render/rebind/unbind, scope topology, spawn reuse, physics/network maps,
  WorldHost ownership, chunk/native memory 회귀 테스트.
- 수동: Host + 1 Client에서 이동/달리기/점프/owner camera/remote animation, Test↔Ingame
  전환, disconnect/rejoin, F6 world replication을 확인한다.
- Dedicated + 2 Clients에서 renderer/UI 없이 physics와 authoritative session이 유지되는지,
  client별 PlayerId/명령 권한과 late-join full snapshot을 확인한다.
- PlayMode stop/start와 Builder 재생성 뒤에도 Prefab component/reference, SceneScope,
  SpawnPoint/HUD/EventSystem, network prefab registry가 동일한지 확인한다.
- 씬/Prefab 재생성이 필요하면 Unity 메뉴 `MF2 > Build Multiplayer Test Slice`를 사용한다.

`BlockPlayer`는 `NetworkObject + Rigidbody + CapsuleCollider`이며 FishNet `Replicate`, `Reconcile`, `PredictionRigidbody`와 TimeManager 물리 틱을 사용한다. 같은 오브젝트에 NetworkTransform은 두지 않는다.
