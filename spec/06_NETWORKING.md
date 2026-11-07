# 06. FishNet networking, authority, replication

## 기존 구조 유지

| 기존 구현 | 유지해야 할 동작 |
|---|---|
| `BlockPlayerController` | FishNet 4.7.3 `[Replicate]`, `[Reconcile]`, `PredictionRigidbody`, tick callbacks |
| `SessionController` | Host/Join/Disconnect, scene/global load, Dedicated auto start |
| `GameCommandNetworkAdapter` | authenticated connection → PlayerId, reliable command ingress, monotonic sequence |
| `WorldReplicationAdapter` | SessionId/Generation/Tick + snapshot/delta + readiness ACK |
| `PlayerReplicationAdapter` | player state snapshot/delta + Spawn binding |
| `ClientStateCoordinator` | 권위 상태와 다른 client replica, player/world tick and generation gate |

## 명령 경로

```text
Client UI/Controller -> IGameCommandSender -> network transport
  -> authenticated NetworkConnection
  -> server ConnectionId→PlayerId mapping
  -> envelope (sequence, connection, session generation) 검증
  -> GameSession.SubmitCommand
  -> IGameCommandAuthorizer + command validation
  -> CommandProcessor + WorldCore mutation exactly once
  -> TickCompleted delta
  -> interested clients
```

검증 정책 (현 기능에서 필요한 최소):
- client-supplied PlayerId를 신뢰하지 않음; 인증된 connection에서 결정.
- `None`/unknown command, invalid block ID, invalid bounds, 비정상 FillRegion volume, 허용되지 않은 operation 거절.
- per-connection sequence monotonic; reconnect 시 sequence initial state 정책을 명시하고 연결/세션 종료 시 guard 제거.
- world edit 권한/최대 거리 등 게임 정책이 아직 없다면 최소 BlockId/영역/volume 검증과 권한 전략만. 하드코딩 계정 권한 확장은 하지 않는다.
- DTO의 max size/array allocation/byte count를 검증. 너무 큰 Snapshot은 측정 후 별도 phase 제안.

## 복제 (권위·세대·단조성)

- 서버에서 `GameSession`만 mutation한다. 클라이언트 replica는 snapshot+delta 적용만 수행.
- 복제 envelope는 **SessionId + Generation + TickIndex** (player state Revision) 식별자 일관성을 유지.
- 세션 재시작/Restore의 기존 generation보다 오래된 packet/ready ACK는 Drop.
- Snapshot 도착 전 Delta 거부 또는 명시적 queue policy. 무한 보관 금지.
- 동일 tick delta가 여러 개 가능한지 확인: 현재 ClientStateCoordinator는 `sourceTickIndex <= worldTickIndex`를 거절하므로, 같은 Tick에 여러 batch로 나누면 수신 계약을 추가해야 함.
- full snapshot에 world data가 존재하는지 확인하고 그 후 collider built/ready. body spawn은 readiness 이후.
- Host의 서버 physics와 클라이언트 replica collider 렌더 동기화는 같은 Unity process여도 도메인 객체 재사용 금지.

## 지금 코드에서 집중해서 볼 지점

1. `WorldReplicationAdapter.OnSessionTickCompleted`는 `BlockChanges` 배열을 한 델타로 방송. 대규모 fill이나 Perlin 초기 Snapshot은 페이로드 크기·전송 가능 범위를 측정할 것.
2. `WorldReplicationAdapter.OnSnapshotReceived`는 block 배열을 전부 materialize한 뒤 `ClientStateCoordinator.ApplySnapshot` 사용; 대형 world에서 메모리/시간 부담이 있어 테스트해야 함. 구현 범위 밖 신규 streaming protocol을 독단 도입하지 말 것.
3. `WorldReplicationAdapter.OnWorldReadyReceived`는 SessionId/Generation/Tick 필터를 구현. ACK가 실제 collision readiness에 기반하는지 교차 확인.
4. `PlayerSpawner`는 pending spawn/registration/world ready 경로가 복잡하므로 disconnect/scene unload/session restore 때 더 이상 pending이 남지 않는지 검증.
5. `GameCommandNetworkAdapter` sequence/guard가 재접속·새 세션에서 적절히 리셋되는지.
6. `PlayerReplicationAdapter`의 `OnClientSpawned`와 replicated PlayerId SyncVar 수신 순서가 바뀌어도 Binder가 한 번만 연결되는지.

## 테스트 케이스 (최소)

- [ ] 서버만 변경한 블록이 Host/원격 클라이언트에 동일하게 적용.
- [ ] Client A가 명령 보내면 Server/Client A/Client B 모두 같은 블록 결과 (3 피어).
- [ ] 해당 Connection이 소유하지 않은 PlayerId가 포함된 임의 요청이 허용되지 않음.
- [ ] 모르는 BlockId/큰 FillRegion/중복 Sequence/역순 Sequence 모두 거절.
- [ ] OldGeneration Snapshot/Delta/Ready ACK가 Restore 이후 새 세션을 오염시키지 않음.
- [ ] Late join이 현재 변경 지형을 포함한 최신 Snapshot 후 Spawn.
- [ ] Dedicated + 2 Clients에서 physics collider와 Spawn barrier가 통과.
- [ ] Client/Host reconnect 시 stale map/예전 player clone 없음.
- [ ] FishNet Prediction의 jump/landing/sprint jitter가 기존 대비 악화하지 않음.

## 범위 제약

- 전송계층 Tugboat/Steam 교체 금지.
- 일반 명령 RPC화 때문에 Player movement prediction 제거 금지.
- `SyncVar`를 억지로 pure C# GameSession에 옮기지 않는다.
- streaming/interest-management 전면 개발은 이번 리팩터링의 필수조건 아님. 필요하면 `docs/architecture/future-streaming.md`로 남긴다.
