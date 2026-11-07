# 05. 테스트 매트릭스 및 수용 조건

## 자동 테스트 (기존 수정 및 신규)

| 레이어 | 테스트 파일 | 필수 검증 |
|---|---|---|
| Terrain | `Assets/Game/Tests/Editor/PerlinTerrainGeneratorTests.cs` | Seed 결과, Grass/Dirt/Stone/Water 레이어, Block budget, delta signature |
| Terrain config | `TerrainConfigTests.cs` | WaterLevel/DirtDepth/Palette validation, 구버전 생성자 회귀 |
| Spawn | `SpawnLocatorTests.cs` | Water 지지 제외, body clearance, 음수 bounds, 모두 물인 world, 복구 후 valid spawn |
| Meshing | `Assets/World/Tests/Editor/ChunkMeshingTests.cs` | 4 submesh pass, water-water/stone-water culling, solid-only collider |
| WorldHost | `WorldHostOwnershipTests.cs` | owned vs borrowed WorldCore, Scene reload/Pool reuse, double-dispose 없음 |
| Physics | `Assets/Game/Tests/Editor/PhysicsIntegrationTests.cs` | 물 통과/고체 충돌, Jump/grounded, PhysX SyncTransforms |
| Network | `ReplicationTests.cs`, `WorldReplicaReadinessTests.cs` | SessionId/Generation/Tick, stale ACK reject, water snapshots/edits |
| Session | `SessionCoordinatorTests.cs`, `SessionPersistenceTests.cs` | candidate atomic swap, restored mapping, 저장 데이터 보존 |
| DI | `LifetimeScopeTopologyTests.cs`, `ArchitectureBoundaryTests.cs` | sibling scope 격리, 구독/Dispose 멱등성, core ref 없음 |
| Editor | `MultiplayerSliceBuilderTests.cs`, `Assets/World/Tests/Editor/EditorToolTests.cs` | Test+Ingame ProPixelizer, manifest/asset regenerate 보존 |

**중요:** 이름은 현재 존재하는 파일에서 확인했다. 신규 테스트는 적절한 기존 파일에 추가하거나 필요할 때만 신설한다.

## 수동 통합 테스트 (자동화 가능하면 더 좋음)

1. **Offline Test/Ingame**: 지형 레이어, 호수/수면 시각화, 물 아래 돌 렌더, 물 비충돌, 고체 충돌.
2. **Host + Client**: 초기 접속, 서로 다른 WorldCore, 늦은 접속, block set/remove, 물 변경 후 갱신.
3. **Dedicated + 2 Clients**: 헤드리스 렌더링 없음, collider-only ready, 플레이어 지형 위 spawn.
4. **Save/Restore live session**: 이전 PlayerId 매핑 보존/재구축, 후보 세션 완료 전에 이전 세션 유지, reconnect/late join.
5. **Repeat**: Host stop/start/scene switch/restore 3회 후 NativeContainer·Mesh·Collider·event 리소스 유실/중복 없음.
6. **UI + Visual**: ProPixelizer camera, Test/Ingame rendering, player camera ownership, zoom, FMOD smoke.

## 실행 기록 규칙

Unity Editor/CLI 실행 가능하면 선택된 EditMode suite부터 실행하고 정확한 pass/fail 및 로그/테스트 결과 파일 경로를 작성한다. 예시 (실제 Unity 경로·라이선스 확인 후):

```bash
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD"   -runTests -testPlatform EditMode -testResults ./artifacts/EditMode.xml -quit
```

실행 불가하거나 타깃 프레임/Host 테스트 불가능하면 '**미실행**'으로 명시. 단위 테스트 통과가 실제 Host/Dedicated 성공을 의미하지는 않는다.

## P0 수용 게이트

- Water가 **물 전용 submesh / material slot**을 사용, 오브젝트 단위 solid-only Collider는 물/비고체를 포함하지 않는다.
- Solid-Water 경계 및 chunk border에서 hole/ghost face, collider 없는 고체 면이 없다.
- 기존 ChunkMesh rebuild/stale job reject, `ChunkReadiness` 통과.
- 플레이어는 물 위를 고체처럼 밟지 않고, 호수에 들어갈 수 있다(수영 구현은 범위 밖).
- Spawn은 물 surface를 선택하지 않고, restored world에서 지형 내부 spawn하지 않는다.
- Current/backup archive 복구 시 snapshot/identity map/regeneration이 유효하다.

## P1 수용 게이트

- Host 및 Dedicated 모드에서 world + player snapshot/ready/spawn 이벤트 순서가 검증됐다.
- 인증/서버 권위 명령 경로, FishNet Replicate/Reconcile, 이동·점프 그대로 유지.
- 네트워크 리스너·VContainer Scope·Chunk Pool의 반복 생성/파괴가 누수 없이 완료.
- ProPixelizer, 씬 생성기, FMOD smoke, 저장 포맷 회귀 없음.
