# Caveworks ↔ MF2 Inventory / Quickbar / HUD 동등성

원본 근거: Caveworks Steam build `22068264`, ILSpy recovered source. 원본 게임 실행 화면 비교는 아직 NOT RUN이다.

| ID | Caveworks 확인 동작 | 근거 타입 | MF2 목표 | P0 상태 |
|---|---|---|---|---|
| UI-01 | `E`로 Inventory toggle | `PlayerInputConfigTable`, `PlayerBrain` | Input System `E`, cursor/UI input priority | DONE |
| UI-02 | Bag 30 / Quick 7 / Equipment 6 | `BagSlotID`, `QuickSlotID`, `EquipmentSlotID` | 동일 고정 용량, 서버 소유 | DONE |
| UI-03 | 숫자 1..7 quickslot | `SlotSelectionUIControllerBase` | 숫자/휠 선택, 서버 승인 | DONE |
| UI-04 | 좌클릭 전체, 우클릭 절반 | `ItemSlotSelectionSession` 호출부 | full/half selection | DONE |
| UI-05 | drag, place-one, merge, swap, compact | `SlotSelectionUIControllerBase`, `ContainerItemTransfer` | 원자적 명령, 총량 보존 | PARTIAL — merge/swap/Shift quick transfer, 2-click 및 drag ghost/drop 구현; compact 명령 없음 |
| UI-06 | held item change event | `HUDQuickSlotUIController` | selected stack가 gameplay/view에 반영 | PARTIAL — 서버 승인 선택과 HUD 반영; 별도 held-item mesh 없음 |
| UI-07 | HP/상태 gauge | `UIPlayerLifeGauge`, HUD scopes | client replica health + dash charges | DONE |
| UI-08 | popup 종료 시 selection 복원 | `PlayerInventoryPopupUIController.OnVisible` | close/rebind/reject 시 drag state 취소 | DONE |
| UI-09 | owned player context만 private inventory 표시 | `IOwnedPlayerContext` | remote inventory 비공개 | DONE |
| UI-10 | 동적 slot prefab/list | `UIQuickSlotList` | builder-generated uGUI fixed layout | DONE |

프로젝트 소유자의 2026-11-08 명시적 허가에 따라 Caveworks Panel/Slot/Title/Selection/HP Bar texture와 `.meta`를 이식했다. UI 로직과 서버 명령은 MF2 구현을 유지하며 Caveworks DLL/font/audio는 필요하지 않아 포함하지 않는다.

MF2 구현은 Caveworks DLL을 포함하지 않는다. 서버 `PlayerRegistry`가 inventory 원본을 소유하고,
FishNet 어댑터가 인증된 connection의 명령만 전달하며, local HUD는 owner-only replica만 읽는다.

