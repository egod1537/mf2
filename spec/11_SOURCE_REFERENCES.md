# 11. 출처 및 검증 범위

## 현재 프로젝트

- MF2 repository: https://github.com/egod1537/mf2
- Audit branch: https://github.com/egod1537/mf2/tree/impl
- Audit commit: https://github.com/egod1537/mf2/commit/7c511d0148708575a0bc555273446a73bd182371
- 직전 architecture commit: https://github.com/egod1537/mf2/commit/71ca0c572517e8662dcbc460e104d1a824d8481f
- Versions (from `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt`): Unity 6000.3.28f1, FishNet 4.7.3, VContainer 1.19.0.
- Current tests: `Assets/Game/Tests/Editor/`, `Assets/World/Tests/Editor/`, `Assets/Tests/FMOD/`; 이전 architecture commit 메시지는 EditMode 203/203 및 API-driven Host vertical slice PASS를 **기록**하지만 이 패키지 작성자가 재실행한 것은 아님.

## 우선 읽을 MF2 실제 파일

| 경계 | 경로 |
|---|---|
| Root/Scene scopes | `Assets/Game/Lifecycle/ApplicationLifetimeScope.cs`, `SceneLifetimeScope.cs` |
| Server sessions | `Assets/Game/Lifecycle/ServerSessionLifetimeScope.cs`, `ServerSessionCoordinator.cs`, `ServerSessionRunner.cs` |
| Client presentation | `Assets/Game/Lifecycle/ClientPresentationLifetimeScope.cs`, `ClientPresentationCoordinator.cs`, `ClientStateCoordinator.cs`, `WorldViewBridge.cs` |
| Domain | `Assets/Game/Domain/GameSession.cs`, `SimulationWorld.cs`, `PlayerRegistry.cs`, `PhysicsContracts.cs`, `SessionCommands.cs` |
| Player MVC | `Assets/Entities/Player/BlockPlayerController.cs`, `BlockPlayerInputSource.cs`, `BlockPlayerView.cs`, `EntityViewBinder.cs`, `PlayerPresentationBindingGate.cs`, `PlayerSpawnContext.cs` |
| FishNet | `Assets/Game/Networking/PlayerSpawner.cs`, `GameCommandNetworkAdapter.cs`, `PlayerReplicationAdapter.cs`, `WorldReplicationAdapter.cs`, `ServerWorldCollisionBridge.cs` |
| World | `Assets/World/Runtime/Core/WorldCore.cs`, `WorldHost.cs`, `Rendering/ChunkReadiness.cs`, `Rendering/ChunkRenderPipeline.cs` |
| Save/Restore | `Assets/Game/Persistence/SessionJsonStorage.cs`, `Assets/Game/Domain/SessionArchiveSnapshot.cs`, `Assets/World/Runtime/Storage/WorldStorageSession.cs` |
| Scene generator | `Assets/Game/Editor/MultiplayerSliceBuilder.cs` |
| FMOD | `FMOD/README.md`, `Assets/Tests/FMOD/FmodPlaybackSmokeTest.cs`, `Assets/Resources/FMOD/` |

## Caveworks — 참고 자료이며 원본 소스가 아님

- Caveworks analysis repo: https://github.com/egod1537/caveworks
- 보고서 commit: https://github.com/egod1537/caveworks/commit/1e77cee9f7daf1e832fdc911e72c661d6cc4d53a
- `reports/code_architecture.md`: Read/Write Model, Controller, Proxy, View, Scope 패턴 요약.
- `reports/terrain_sdf_chunks.md`: Chunk streaming, collision/visual rebuild, model state persistence 구조 요약.
- `reports/networking.md`: Snapshot/Delta, ACK, 권한/검증 공백 가능성(실제 runtime 검증은 안 됨).
- `inventories/types.csv`: AudioEngine/AudioEngineScope/IAudioEngine 등의 타입명 확인에만 사용.

**중요 제한:** Caveworks 저장소는 빌드 분석 보고서·타입 인벤토리 중심이며 디컴파일 원본 C# 소스/바이너리가 포함되지 않는다. 설계 원칙을 참고하되 정확한 구현체 로직/라이선스/성능을 보고서만으로 확정하거나 그대로 이식하지 않는다.

## 정적 분석 vs 실행 증거

이 ZIP의 설계 근거는 GitHub 코드/트리와 커밋의 실제 내용 열람이다. 런타임/Unity Editor 테스트 결과를 새로 재실행한 자료는 포함되지 않는다. Codex 작업은 이 차이를 유지하고, 각 P0/P1 위험을 테스트해 결함인지 확인해야 한다.
