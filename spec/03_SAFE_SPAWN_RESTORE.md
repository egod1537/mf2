# 03. P0 — 액체 Spawn / 세션 Restore 안전성

## 실제 코드 경로

- `Assets/Game/Domain/Terrain/SpawnLocator.cs`는 `IsAir == false`면 지지 블록 후보로 본다. 따라서 water는 `IsAir == false`이고, water surface 위의 공기 공간이 통과되면 물 위에 spawn할 수 있다.
- `Assets/Game/Domain/GameSession.cs`, `Restore(...)`는 archived logical terrain을 복원하면서 `Terrain = null`로 설정한다.
- `Assets/Game/Networking/PlayerSpawner.cs`, `SpawnFor(...)`는 `activeSession.Terrain != null`일 때만 `spawnLocator`를 호출하므로, 복원된 세션에서 기본 씬 SpawnPoint(`y = 0.1` 등의 값)를 사용할 수 있다.
- `Assets/Game/Networking/GameCommandNetworkAdapter.cs`: `TryRestoreConnection` 및 `ReleaseConnectionMapping`은 new mapping을 복원하되 PlayerId가 active registry에 존재하는지 검사한다.

## 기본 Spawn 규칙

1. **지지 블록**: 반드시 `BlockTraitsCatalog.IsSolid(block) == true`; water/liquid/air는 지지면이 아니다. `SolidOpaque`가 아닌 신규 분류에도 동작하도록 추상화 가능.
2. **충분한 빈 공간**: player capsule이 차지하는 머리·몸 공간은 물/기타 유해한 또는 비고체 점유 블록을 어떻게 처리할지 정책을 명시한다. 기본 구현은 `Block.Air` 이상의 clearance 요구로 **수면 아래 스폰 금지**.
3. world bound·지형 수정 후에도 in-bounds, 유효한 출발 위치 보장. 최소 y 최대 y 검사에서 headroom이 bounds 밖이라면 월드 밖이 진짜 빈 공간인지 검사하고, Collider와 일치하는 세계 좌표를 사용한다.
4. 양·음수 x,z 좌표, 서로 다른 connectionId, 반복 spawn에서 결정적이고 중복이 최소화되도록 안정적인 후보 순서 유지.
5. 안전한 육지 후보가 없으면 명시적으로 spawn 연기 또는 오류 결과. 돌에 박히는 fallback **금지**.

## Restored Session 처리

기존 archive에 TerrainGenerationResult 전체 설정이 없다고 해서 현재 default config를 무조건 복원된 월드의 지형 메타데이터라고 간주하지 않는다. 가능한 접근:

- **권장 1**: restored world snapshot/loaded chunk bounds와 read-only block queries에서 안전한 standable 지점을 직접 탐색하는 `WorldSpawnLocator`, 기존 PlayerSpawner가 생성·복구 모두 동일 API 사용. 낡은 `TerrainConfig`가 없어도 동작해야 한다.
- **권장 2**: 지형 설정 메타데이터가 정말 필수라면 현재 archive 포맷과 하위 호환을 검토하여 versioned terrain metadata를 추가한다. 예전 save migration 및 모르는 설정 처리 명시.

복원 시 이미 PlayerModel이 존재하는 `PlayerId`는 재등록/중복 등록하지 말 것. 실제 connection의 identity는 서버가 관리하며 클라이언트의 임의 ID를 신뢰하지 않는다. 부활 시 이전 NetworkObject, PhysicsBody, event subscription, SpawnGuard은 올바르게 정리한다. 서버가 다른 SessionId/Generation으로 전환했으면 이전 ACK/레플리카/네트워크 매핑을 그대로 사용하지 않는다.

## 회귀 시나리오

- 새 세션 생성 → spawn → save → restore → world collider ready → spawn: 발이 공기/액체가 아닌 고체 위, 몸이 흙/돌/물에 박히지 않음.
- archived player의 stable PlayerId 재연결; 불일치 id와 중복 connection 주장 거부.
- session restore 중 I/O, corrupted archive 실패: running previous session/SpawnMap/world intact.
- restore 후 첫 tick의 player & world snapshot, client ACK, player spawn 순서 테스트.
- 전부 물인 월드: '바닥 없음' 실패를 정상 처리하며 즉시 world origin으로 떨어뜨리지 않음.
- scenario별 Host + 1 Client, Dedicated + 2 Clients, disconnected late join.

**Gate:** 저장·복원 후 플레이어의 발 위치가 실제 solid collider와 맞고, 점프/착지/이동이 정상, 데이터 손실 없음.
