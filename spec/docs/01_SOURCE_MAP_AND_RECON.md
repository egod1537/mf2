# 01. 레포 조사, 실제 경로, P0 체크리스트

## 1.1 기준 출처

| 프로젝트 | URL/브랜치 | 조사 시 확인된 commit | 직접 확인한 상태 |
|---|---|---|---|
| MF2 | https://github.com/egod1537/mf2/tree/impl | `4dbc84c90086308bb855ad2464a9a3fa88330946` | README/AGENTS/아키텍처 및 경로 일부 확인 |
| Caveworks | https://github.com/egod1537/caveworks/tree/main | `1e77cee9f7daf1e832fdc911e72c661d6cc4d53a` | 설치본 분석 레포, 게임 소스/에셋 미포함 |
| Minefarm0 | https://github.com/egod1537/minefarm0/tree/master | `e096241fbbebe55af23ef72934b7847386a864c1` | Unity 에셋 및 액션 C# 소스 존재 |

브랜치 HEAD는 변할 수 있다. 로컬 Git HEAD와 현재 package lock/manifest가 더 최신이면 실제 작업 시점을 기록하고 최신 상태에 맞춰 경로를 수정한다. 이 문서는 정적인 스냅샷이다.

## 1.2 MF2 확인된 파일 지도

### Player/Network/Presentation

```text
Assets/Entities/Player/
  BlockPlayer.prefab
  OfflineBlockPlayer.prefab
  BlockPlayerController.cs        # [Replicate]/[Reconcile]/PredictionRigidbody
  BlockPlayerInputSource.cs       # owner input Capture/Consume
  BlockPlayerView.cs              # visualRoot, facingRoot, limb animation, owner camera
  BlockIsometricCameraZoom.cs
  EntityViewBinder.cs
  PlayerPresentationBindingGate.cs
  PlayerPresentationState.cs
  PlayerSpawnContext.cs
  MVC.md
Assets/Game/Presentation/
  PlayerOcclusionDither.cs        # camera→target voxel trace, 최대 64 occluder 기본
  WorldCommandDebugInput.cs
Assets/Game/Networking/
  NetworkBootstrap.cs
  SessionController.cs
  PlayerSpawner.cs
  GameCommandNetworkAdapter.cs
  PlayerReplicationAdapter.cs
  WorldReplicationAdapter.cs
  ServerWorldCommandAuthorizer.cs
  ServerWorldCollisionBridge.cs
Assets/Game/Lifecycle/
  ApplicationLifetimeScope.cs
  ServerSessionLifetimeScope.cs
  ClientPresentationLifetimeScope.cs
  SceneLifetimeScope.cs
  ClientStateCoordinator.cs
  WorldViewBridge.cs
Assets/Game/Domain/
  GameSession.cs
  PlayerModel.cs
  PlayerRegistry.cs
  HealthSystem.cs
  SessionCommands.cs
  SimulationWorld.cs
  SessionArchiveSnapshot.cs
```

확인된 사실: `BlockPlayerController`는 `body.interpolation = RigidbodyInterpolation.None`으로 초기화하고 `TimeManager_OnTick()`에서 `PerformReplicate()`, `OnPostTick()`에서 Reconcile을 수행한다. 플레이어 CameraRig는 Builder에서 물리 루트의 자식으로 생성하고 기본 로컬 오프셋은 `(-8, 8, -8)`이다. 떨림 원인은 아직 **실측하지 않았다**.

### World/Meshing/Terrain/Scene

```text
Assets/World/Runtime/Core/
  WorldCore.cs
  WorldHost.cs                   # SetDitheredOccluders, EditMode/borrowed runtime
  WorldCommand.cs
Assets/World/Runtime/Rendering/
  ChunkMeshBuildJob.cs          # 인접 불투명 블록 면 제거
  ChunkMeshingSnapshotBuilder.cs
  ChunkRenderer.cs              # baseIndices + ditheredIndices/submesh
  ChunkRendererPool.cs          # SetDitheredBlocks, ProPixelizer dither material
  ChunkRenderPipeline.cs
  ChunkMeshApplier.cs
  ChunkMeshBuilder.cs
Assets/World/Editor/Tools/
  BlockPaintTool.cs
  BlockToolState.cs
  VoxelEditingGeometry.cs
Assets/Game/Domain/Terrain/
  PerlinTerrainGenerator.cs
  TerrainConfig.cs
  SpawnLocator.cs
Assets/Game/Editor/
  MultiplayerSliceBuilder.cs
Assets/Game/UI/
  NetworkSceneHud.cs
  TitleController.cs
  OutgameController.cs
Assets/Scenes/
  Title.unity
  Outgame.unity
  Test.unity
  Ingame.unity
```

확인된 사실: `PlayerOcclusionDither`는 camera/target의 여러 선분에서 solid voxels를 찾은 후 `WorldHost.SetDitheredOccluders`를 호출한다. `WorldHost`는 `ChunkRendererPool.SetDitheredBlocks()`로 전달하고, `ChunkRenderer`에서 **존재하는 메시의 submesh 인덱스**를 재배치한다. `ChunkMeshBuildJob.ShouldRenderFace()`가 인접한 `SolidOpaque`의 내부 면을 생성하지 않는 점이 핵심 의심 경로다. 콜라이더는 별개다.

### 스코프/생명주기 문서

- `AGENTS.md` — Prefab 단위 co-location, Editor Fluent IMGUI DSL.
- `README.md` — Build/Host/Client/Dedicated 운영, 현재 아키텍처.
- `docs/07_TECHNICAL_DECISIONS.md` — 수용된 ADR의 권위 있는 출처.
- `docs/architecture/architecture.md`, `lifecycle.md` 등 — 범위/책임/복원과 테스트.
- `Assets/Game/Tests/Editor/` — `PlayerMvcTests`, `PerlinTerrainGeneratorTests`, `MultiplayerSliceBuilderTests`, `ReplicationTests` 등.
- `Assets/World/Tests/Editor/` — `ChunkMeshingTests`, `EditorToolTests`, `WorldHostOwnershipTests` 등.

## 1.3 Caveworks 확인 사항

`egod1537/caveworks` README:
- Steam Demo App ID `4281980`, depot `4281981`, build `22068264` 대상 정적 분석.
- Unity `2022.3.62f2`, Mono scripting backend.
- ILSpy로 C# 약 2,679개 복원 가능하다고 보고.
- game-facing 주요 어셈블리 `Game.UI`, `Game.Ingame.Item`, `Game.Ingame.Entity` 등.
- 공개 GitHub 레포에는 *ILSpy 추출 스크립트/보고서/인벤토리*가 있고 **실제 디컴파일 산출물, 게임 바이너리, 그래픽 에셋은 없음**.

관련 파일:
- `reports/build_and_extraction.md`
- `reports/code_architecture.md`
- `inventories/types.csv`, `inventories/namespaces.csv`
- `scripts/probe_installed_build.ps1`
- `scripts/decompile_first_party.ps1`
- `scripts/build_code_inventory.ps1`

**확인 필요:** 원본 실제 Inventory/Quickbar/HUD 클래스, UI Canvas/Prefab 계층, 이벤트 연결, 보유/장착/드래그 등 세부 동작. 디컴파일된 실제 C#과 Unity Assets 검색 후 동등성 목록을 작성할 것. 이 문서에서 임의의 Caveworks 클래스명을 확정하지 않는다.

## 1.4 Minefarm0 확인된 파일 지도

```text
Assets/Model/Entity/Actor/[0] Player/
  GreenIdol.fbx
  Player.prefab
  animation/
    [0]Player.controller
    Top/Attack.anim              # 실제 존재 확인
  material/
Assets/Script/Scenes/InGame/Entity/
  PlayerController.cs           # Move, Dash, InputHandler, ForwardAction
  Actor/ActorController.cs      # Attack(target, damage)
  Actor/Actorables/IAttackable/
    IAttackable.cs
    ActorAttackable.cs
  Actor/Actorables/IDashable/
    IDashable.cs
    PlayerDashable.cs
Assets/Script/Scenes/InGame/UI/
  Minimap/ViewExtendMinimap.cs
  Top/ViewDashBar.cs
  Top/ViewPlayerHp.cs
  Bottom/QuickSlot/
```

확인된 `PlayerDashable` 원본 구현은 `DELAY_DASH=0.5f`, `CONSTANT_DASH_FORCE=10f`, Rigidbody Impulse와 0.25초 후 일부 역방향 Impulse, dashCount를 사용한다. **행동 참고 값이지 MF2에서 직접 복사할 코드는 아니다.** 원본은 UniRx/여타 종속성을 사용한다. Minefarm0 README 조작: `WASD` 이동, Space 점프, 좌클릭 공격, 우클릭 대시.

**확인 필요:** 실제 AnimationController의 상태/Parameter, FBX Rig 설정과 라이선스, UI 연결, 원본 공격 대상과 범위/타격감, 대시 충전/재생 타이밍.

## 1.5 P0 수행 체크리스트

- [ ] `git status --short`, current branch/head, 최근 커밋, 수정된/추적되지 않은 파일 보존 계획 작성.
- [ ] Unity, Package Manifest, FishNet/VContainer/ProPixelizer/URP 버전 실제 확인.
- [ ] 현재 Player Prefab/Offline Prefab/Camera/Scene Builder 상태와 serialized reference 조사.
- [ ] Host + 1 Client baseline 구동/로그 기록, 증상이 있다면 영상/로그/프레임 환경 기록.
- [ ] Caveworks 설치 경로, 파일 버전, Assets/Managed 파일 SHA256 확인.
- [ ] Minefarm0 아트 파일/코드/메타 파일 및 사용 조건 확인.
- [ ] 현재 Scene, save/archive, replication, WorldHost 프레임/메시 예산 조사.
- [ ] `templates/RECON_REPORT_TEMPLATE.md`를 실제 사실로 채워 구현 설계와 변경 파일 예상 목록 확정.

## 1.6 권장 조사 명령

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
rg -n 'SetDitheredOccluders|SetDitheredBlocks|ShouldRenderFace|OcclusionSubmeshIndex' Assets
rg -n 'Replicate|Reconcile|OnTick|OnPostTick|RigidbodyInterpolation|LateUpdate' Assets/Entities/Player Assets/Game
rg -n 'CreateTitleScene|CreateOutgameScene|CreateGameScene|ConfigurePlayerPrefab' Assets/Game/Editor
rg -n 'Inventory|Hotbar|QuickSlot|Dash|Attack' Assets
```

각 `rg` 결과는 실제 경로/라인으로 보고서에 기록. Path의 대소문자/공백/Unity 프로젝트 루트를 고려한다.
