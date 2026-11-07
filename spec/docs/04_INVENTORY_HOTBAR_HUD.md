# 04. T1 — Caveworks Inventory / Hotbar / HUD 이식

## 4.1 사용자 경험 목표

Caveworks의 인벤토리, 플레이어 툴바, HUD가 MF2 `Ingame`에서 **원본과 시각·조작·상태 변화가 최대한 동일하게** 동작하게 한다. UI는 네트워크 복제된 실제 상태를 사용해야 하며 Slot을 선택하면 게임플레이가 바뀐다. Title/Outgame에 게임 HUD를 잘못 띄우지 않는다.

## 4.2 구현 이전 원본 분석표 (필수)

Codex가 실제 실행/디컴파일 내용을 기반으로 다음을 채운다. 아래 기능은 **가능성 목록**이지 원본 존재 확인이 아니다.

| 항목 | 조사할 내용 | 근거 |
|---|---|---|
| Inventory | 여닫는 키, 창 크기, 슬롯 열/행, 아이템 정렬 | C# 클래스·Prefab·스크린샷 |
| Toolbar/Quickbar | 칸 수, 숫자키, 휠, 선택 강조, held item 갱신 | 실제 입력/렌더/선택 이벤트 |
| Slot interaction | 좌/우 클릭, 드래그앤드롭, 합치기, 교환, 스택 분할 | 로직과 재현 테스트 |
| Equipment | 실제 장비 슬롯 존재 및 모델 반영 | 원본 모델·UI/코드 |
| HUD | HP, 레벨, 경험치, 자원, 사용/쿨다운 UI 등 | UI 구성/데이터 출처 |
| Persistence | 아이템 획득/소비/저장/로드 방식 | model/serializer/network |
| Multiplayer | 누구에게 무엇이 공개되는가 | 권한, RPC/SyncVar |
| Assets | icon atlas, 9-slice, font, sprite pivot | 추출 매니페스트 |

`CAVEWORKS_PARITY.md`에 원본/대상 결과를 표로 남긴다. 원본 기능이 확인되면 우선순위/완료 기준에 추가한다.

## 4.3 권장 데이터 모델과 불변식

실제 기존 MF2 Domain을 먼저 검사하고 맞는 필드를 재사용. 아래는 설계 계약이지 강제적인 클래스/메서드명은 아니다.

```text
ItemDefinition / ItemCatalog
  Stable ItemId (string/uint 식별자), icon ref, maxStack, usage policy
ItemStack
  ItemId + Count (+ 실제 필요한 durability/instanceData)
InventoryState
  PlayerId + fixed-size slots + version/revision
QuickbarSelection
  PlayerId + selectedIndex; referenced inventory slots or supported independent bar
Equipment/Stat (only if original feature proven)
```

필수 invariant:

- 빈 슬롯은 Count 0, 유효 슬롯은 Count > 0이고 `Count <= MaxStack`.
- 아이템 정의가 없는 ID를 클라이언트 입력에서 생성할 수 없음.
- Move/Swap/Split/Merge는 성공 시 **아이템 총량 보존**, 소비/획득은 명시적인 승인된 명령에서만 총량 변경.
- 음수 수량, out-of-range 슬롯, 가득 찬 슬롯에 오버플로, 중복 명령/replay, disconnect 중 입력을 거부.
- 선택 인덱스는 유효 범위이며 Inventory 변경/복원 후에도 장착/표시 상태가 일치.
- 서버가 authoritative Inventory를 소유. Client는 읽기 전용 Snapshot+delta, UI의 로컬 드래그는 임시 시각 상태일 뿐 성공 선언은 서버 승인 후.
- 동일 PlayerId에 재접속해도 새 View가 올바른 Replica를 구독하며 옛 구독/드래그 상태가 누수되지 않음.

## 4.4 권한 명령 및 복제 설계

흐름:

```text
Owner input/UI
  -> Inventory/Quickbar CommandSender
  -> FishNet authenticated connection
  -> PlayerId lookup, sequence/revision validation
  -> GameSession inventory system
  -> state mutation + Revision increment
  -> per-owner snapshot/delta
  -> ClientStateCoordinator readonly reader
  -> InventoryBinder / HotbarBinder / HUD binder
  -> uGUI view
```

**절대** `InventoryPanel`에서 Host의 `PlayerRegistry`를 직접 읽거나 `GameSession`을 resolve하지 않는다. Transport 패킷에 사용자가 제출한 `PlayerId`를 권한 근거로 쓰지 않는다.

권장 명령 분류:

- `SelectQuickbarSlot(index)` — 서버가 장착/사용 결과에 영향 있는 선택 확정. 선택 강조만 로컬 선반영 가능하나 승인 후 재조정.
- `MoveItem(from,to,count)` — 적법한 이동/교환/합치기 정책으로 atomic 수행.
- `UseSelectedItem(target)` — 상호작용 범위/쿨다운/소모량 서버 검증.
- `DropItem(from,count)` — 실제 원본 제공 및 MF2 월드 아이템 모델 가능할 때만 활성화.
- `PickupItem(...)` — 기존 아이템 획득 경로가 있으면 통합. 없다면 테스트용 서버 지급 액션을 개발/테스트 씬에서 분리하고 출시 UI로 남기지 않음.

Caveworks 원본에 없는 command는 추가하기보다 최소 실제 gameplay 동작을 제공하는 것으로 범위 조정. Item 기반 블록 배치/파괴 연동은 원본 기능 및 MF2 WorldCommand 경로를 확인해 승인된 블록 ID/사거리/권한을 검증하고 구현한다.

### Snapshot/저장 정책

- 현재 `SessionSnapshot`, `SessionArchiveSnapshot`, 클라이언트 replication serialization을 확인한 후 InventoryState 저장/복제 전략을 선택.
- 기존 `WorldSnapshotTransfer`의 최대 48-block 전송 정책과 분리. 아이템 전송은 별도 MTU/부분 적용/배열 길이 검증 정책 수립.
- Persistence version 변경 시 이전 데이터 기본값 및 migration/rollback 테스트. 구저장을 묵살하고 빈 새 세션으로 만드는 것은 금지.
- 가령 게임 도중 Restore generation 변경 시 stale item delta가 새 inventory에 적용되지 않음.

## 4.5 UI 설계 계약

### 구성 (실제 원본과 비교 후 결정)

```text
Ingame/UI
  HUDCanvas
    StatusPanel (health/confirmed metrics)
    HotbarRoot
      QuickbarSlots[]
      SelectionHighlight
      ItemIcon, Count, CooldownOverlay
    MinimapRoot (T6)
  InventoryCanvas / InventoryPanel
    InventoryGrid
    ItemTooltip
    DragGhost
    EquipmentPanel (if original)
  Pause/NetworkHud (legacy preserved)
```

- UI는 uGUI 중심. Sprite, slicing, font, input events는 Caveworks 원본에 가깝게. 해상도 16:9, 16:10, 21:9 및 Windows scaling/CanvasScaler 대응.
- Inventory Open/Close는 사용자 입력/화면 표시/커서 상태를 일관성 있게 전환. UI 위 클릭이 세계 공격/파괴로 전달되지 않게 EventSystem/UI raycast를 사용.
- 숫자키/휠은 Hotbar 선택, 열려 있는 inventory의 스크롤은 원본 동작과 겹치지 않음.
- DragGhost는 버전 변경 또는 서버 reject 시 자동 취소/정정. Owner change/Scene unload에서도 리소스와 listener cleanup.
- HUD status는 Replica의 health 등 **실제 값**만 사용. 원본에 경험치/대시/스태미나 표시가 있으면 이에 대응하는 Domain state와 system을 먼저 구현 또는 연결.
- Controller가 데이터 조작/서버 명령 전송, Binder는 read-only snapshot을 view로 투영. Unity Button callback은 Builder/Prefab에 연결하여 재생성 후 작동.
- UI Image/Icon atlas는 가능한 원본 대비 픽셀 단위로 비교; 저작권 불확실하면 로컬 적용 또는 대체 에셋 결정과 차이 명시.

## 4.6 입력 우선순위

1. 채팅/텍스트 입력 활성 시 세계 조작 차단 (관련 UI가 없다면 적용하지 않음).
2. Inventory 열린 상태: 슬롯 조작/취소 우선, Attack/Break/Place 등 월드 입력 차단.
3. 오버레이/메뉴: Pause/Disconnect 정책 보존, 필수 메뉴 키 처리.
4. 일반 플레이: 이동, 공격, 대시, Hotbar, 상호작용.
5. 서로 다른 InputSystem/legacy Input 혼용 여부 조사 후 일관된 매핑으로 구성. 정확한 Key Binding을 문서/게임 HUD 양쪽에 표시.

## 4.7 테스트/수용 기준

- [ ] Caveworks 원본 UI 열림/닫힘/선택/아이템 스택 표시와 MF2 화면 비교.
- [ ] 정상 클릭, 드래그, empty swap, 동일 아이템 merge, 상이 아이템 swap, 부분 split 등 **원본에서 지원하는 실제 조작** 확인.
- [ ] 슬롯 외 인덱스, count 0/음수, 허용치 초과, 임의 ItemId, 중복/역순 revision 명령 거부.
- [ ] 서버·Owner Client에서 아이템 총량 불변식 유지 및 동일 선택 상태 수렴.
- [ ] Remote Client가 다른 사람의 사유 인벤토리를 무단 수정/보지 못함(설계된 공개 정보 제외).
- [ ] Host/Client UI, WorldReady 이후 초기 상태, late join, reconnect, restore generation 교체 통과.
- [ ] UI Open 중 공격/대시 비활성 조건 및 Escape/마우스 포커스 테스트.
- [ ] 생성기 재실행 후 모든 버튼과 Binding 정상, Missing Sprite/Script 없고 Font/Canvas 깨짐 없음.
- [ ] Item 사용/선택/장착이 실제 플레이어 상태에 영향을 미침.

**문제 해결 우선순위:** 데이터/권한 무결성 → 기능 동작 → 라이프사이클 → 원본 시각적 동등성 → 미세 UX. 기능 누락을 미관으로 숨기지 않는다.
