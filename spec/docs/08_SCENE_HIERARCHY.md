# 08. T5 — Title / Outgame / Ingame / Test Hierarchy 정리

## 8.1 목표

Unity Editor Hierarchy의 GameObject가 **역할/수명/권한 기준**으로 정리되어 개발자가 파일과 컴포넌트를 찾기 쉬워야 한다. Scene scope와 지속 객체의 생명주기를 깨뜨리지 않으며 Builder를 재실행해도 동일한 구조를 유지한다.

## 8.2 현행 제약

- `Assets/Game/Editor/MultiplayerSliceBuilder.cs`의 `MF2 > Build Multiplayer Test Slice`가 `Title`, `Outgame`, `Test`, `Ingame` 씬과 Player/OfflinePlayer prefab을 생성한다.
- `Title` 씬의 `NetworkBootstrap`은 FishNet, 네트워크 infra, `ApplicationLifetimeScope`를 만들고 지속형(`DontDestroyOnLoad`)으로 유지된다.
- `Test`, `Ingame`: `SceneLifetimeScope`, WorldHost, WorldViewBridge, SpawnPoints, HUD, F6 debug input 구성.
- `ClientPresentationLifetimeScope`는 ServerSession과 형제 관계. Scene scope는 client 자식.
- Network Player는 **씬에 serialized NetworkObject로 배치하지 않으며** 인증된 connection에 대해 `PlayerSpawner`가 Spawn.
- `MinecraftBlockMap` 별도 전시 씬 및 외부 자산/SceneToolbar, 기존 GUI 이벤트 바인딩을 파괴하지 않는다.

## 8.3 권장 논리 Hierarchy (실제 경로/생명주기 기반 조정 허용)

### Title

```text
Title
├── 00_Application
│   └── NetworkBootstrap                 # 반드시 하나, 지속형
├── 10_SceneContext
│   └── TitleSceneLifetimeScope
├── 20_Presentation
│   ├── TitleCamera
│   └── Lighting (실제 필요 시)
└── 40_UI
    ├── TitleCanvas
    └── EventSystem
```

### Outgame

```text
Outgame
├── 10_SceneContext
│   └── OutgameSceneLifetimeScope
├── 20_Presentation
│   └── OutgameCamera (실제 필요 시)
└── 40_UI
    ├── OutgameCanvas (Host/Join/Address/Port/Disconnect)
    └── EventSystem
```

### Ingame

```text
Ingame
├── 10_SceneContext
│   └── SceneLifetimeScope
├── 20_World
│   ├── WorldHost
│   ├── WorldViewBridge
│   └── SpawnPoints
├── 30_Gameplay
│   ├── SceneControllers
│   └── WorldCommandDebugInput (Test 전용 또는 Debug 영역)
├── 40_Presentation
│   ├── SceneCamera (Player Owner Camera 활성 전까지, 필요 시)
│   ├── Lighting
│   └── VFX
├── 50_UI
│   ├── HUDCanvas
│   │   ├── Hotbar
│   │   ├── Status
│   │   └── Minimap
│   ├── InventoryCanvas
│   ├── NetworkSceneHud
│   └── EventSystem
└── 90_Debug (선택)
```

### Test

Ingame의 같은 논리 루트를 사용하되 테스트 전용 HUD/Spawner/Debug 입력을 명시적으로 별도 루트에 배치. Product Ingame과 같은 기능은 동일 Builder helper/Prefab을 공유하여 drift 방지.

숫자 접두사는 **가독성 제안**이며 코드는 이름 문자열 경로에 과도하게 의존하지 말아야 한다. 기존 serialized references는 재연결하고 `transform.Find("...")` 하드코딩을 최소화한다.

## 8.4 Player Prefab 경계

```text
BlockPlayer (NetworkObject + Rigidbody + CapsuleCollider + prediction, authoritative movement)
├── VisualRoot (visual-only)
│   └── FacingPivot
│       └── Minefarm0Model (Animator, mesh, held item view)
├── Owner Camera Rig (owner-only presentation, parent/follow 방식은 T3 결정)
└── (필요한 client-only attachment points)
```

- CameraRig를 Player root에 고정 자식으로 둘지 로컬 Presentation 서비스가 spawn 뒤 소유할지는 T3 jitter 해결에 따라 확정. 어떤 선택이든 오브젝트 수명/owner gating/생성기 재실행을 테스트.
- per-player `LifetimeScope`는 필요성이 검증되지 않는 한 추가 금지.
- Player 프리팹 내부 Script/Material/Animation은 `Assets/Entities/Player/` co-location.
- spawned Players를 Scene root 밑의 일반 정적 자식처럼 생성기에 직렬화하지 않음.

## 8.5 구현 절차

1. 현재 Scene YAML/Hierarchy/serialized callback/component inventory를 추출.
2. `MultiplayerSliceBuilder`의 `CreateTitleScene`, `CreateOutgameScene`, `CreateGameScene`, `ConfigurePlayerPrefab`, `ConfigureOfflinePlayerPrefab`를 확인하고 공통 `CreateSceneRoot`/`Attach` helper로 역할별 루트 생성.
3. EventSystem/UI/World/Scope 생성 순서와 DI injection timing 유지, Awake/OnEnable 순서로 오류 발생하지 않게 테스트.
4. 생성기 실행 시 모든 새 GameObject의 parent, layer, tag, activeState, prefab reference, UI button events를 명시.
5. 활성 Camera/AudioListener 중복과 DontDestroyOnLoad 중복 부트스트랩 방지.
6. `MultiplayerSliceBuilderTests`를 확장해 root count, component, serialized ref, 2회 재실행 idempotence, Scene load/unload 확인.
7. 생성기 재실행 전후 Scene/Prefab diff를 검토, 무관한 user-authored `MinecraftBlockMap`은 건드리지 않음.

## 8.6 합격 기준

- [ ] 각 Scene에 역할별 최상위 루트가 정리되고 의미 없는 GameObject/중복된 빈 오브젝트가 제거.
- [ ] Title → Outgame → Test/Ingame → Outgame 흐름 보존, Host/Join/Start/Disconnect 버튼 모두 동작.
- [ ] `NetworkBootstrap`는 한 번만 생성되어 Session/ClientPresentation scope와 FishNet 수명 정상.
- [ ] `SceneLifetimeScope` 해제 시 scene-owned UI, renderer, camera, listener 구독/메모리 제거.
- [ ] Dedicated Server에서 Presentation/UI/Audio listener 생성하지 않음.
- [ ] Editor Scene/Prefab 생성기 2회 연속 실행해도 구조, GUID/serialized references, BuildSettings 일관.
- [ ] `MultiplayerSliceBuilderTests`, scene load/unload, reconnect/late join 통과.
