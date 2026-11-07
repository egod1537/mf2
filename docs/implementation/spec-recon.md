# MF2 P0 조사 보고서

작성일: 2026-11-07  
MF2: `C:\Users\egod1\proj\mf2`, branch `impl`, HEAD `4dbc84c90086308bb855ad2464a9a3fa88330946`  
Unity: `6000.3.28f1 (8f082d2fe476)`  
Packages: FishNet `4.7.3`, VContainer `1.19.0`, Input System `1.20.1`, URP `17.3.0`, Unity Pipeline `0.10.0-exp.1`

## 작업 트리 보호

착수 시 코드 변경은 없었고 `spec/` 패키지가 교체된 상태였다. 삭제/수정/추가된 `spec/` 파일은 사용자 작업으로 간주하여 수정하지 않는다. 구현 산출물은 `Assets/`와 `docs/implementation/`에만 추가한다.

## 외부 근거

### Caveworks

- Steam manifest: `C:\Program Files (x86)\Steam\steamapps\appmanifest_4281980.acf`
- App ID: `4281980`, build ID: `22068264`, depot manifest: `5021417510709035669`
- 설치 경로: `C:\Program Files (x86)\Steam\steamapps\common\Caveworks Demo`
- 실제 data 폴더: `Cave_Data`
- manifest SHA-256: `B447DEA8D61F4818D8413E778A0403ADF723E9BE0937354C964148DE618B694D`
- executable SHA-256: `78E9B7D063DBFD844B1334A59E86BA2F7947A8A6DB3C65032E8BFF92440E7EE8`
- `Game.UI.dll` SHA-256: `A35BAFC57702093F3CC0C5927AA8860BC8483A3C22F071440189AB7E90FEF01E`
- `Game.Ingame.Item.dll` SHA-256: `E599C50A07C6E1B52A8F3200A5455DDCDB0F5F923380264D5509568DFC6993CD`
- ILSpy 산출물: `C:\Users\egod1\proj\caveworks\recovered\mono`, 공개 분석 repo commit `1e77cee9f7daf1e832fdc911e72c661d6cc4d53a`

확인된 동작은 30칸 Bag, 7칸 QuickSlot, 6칸 Equipment, `E` 인벤토리, 숫자 `1..7` 선택, 좌클릭 전체 스택 선택, 우클릭 절반 선택, 드래그 배치, merge/swap, Shift quick action이다. 이 동작 계약만 재작성하며 DLL 또는 원본 유료 UI 에셋은 MF2에 복사하지 않는다.

### Minefarm0

- checkout: `C:\Users\egod1\proj\_external_work\minefarm0`
- commit: `e096241fbbebe55af23ef72934b7847386a864c1`
- source: `https://github.com/egod1537/minefarm0.git`
- `GreenIdol.fbx` SHA-256: `9CADA64ADD073F042F19D5F4114A871711BE022492FA61F37AD6FE92F3BCE768`
- `Player.prefab` SHA-256: `E2E9F7A33733011B0335139BCC57AFAE6D1E57BE5004713A3D3D9EB3FB4C7618`
- `Attack.anim` SHA-256: `33F8F6106DA0CA7D765229997405B7FE15461BCCA8107F7D825B2329B69EA992`

루트 라이선스와 GreenIdol 원저작/재배포 조건을 찾지 못했다. 따라서 모델·애니메이션 파일 복사는 차단하고, 공격/대시의 관찰 가능한 규칙만 MF2 Domain/FishNet 구조에 맞게 재작성한다. 출처가 확인되기 전 외부 FBX를 포함한 빌드를 완료로 표시하지 않는다.

## 시스템 현황

| 시스템 | 기존 소유자/경계 | 확인된 상태 | 변경 경계와 위험 |
|---|---|---|---|
| Player prediction | `BlockPlayerController`, FishNet `PredictionRigidbody` | Tick/POST tick Replicate/Reconcile, Rigidbody interpolation 없음 | 이동 루트를 보존하고 입력 데이터에 action edge만 추가한다. |
| Camera | Player root 자식 `OwnerCameraRig` | 물리 tick 이동을 자식 카메라가 그대로 받음 | presentation-only follow target을 LateUpdate에서 보간; physics root 쓰기 금지. |
| Occlusion | `PlayerOcclusionDither` → chunk triangle routing | 이미 노출된 면만 dither pass로 이동 | 내부 면 backing overlay가 없어 빈 공간처럼 보일 수 있음. WorldCore/Collider 불변으로 별도 visual indices 필요. |
| Chunk mesh | Burst job + 4 render pass + solid-only collision mesh | Water/collision 분리 완료 | dither backing은 visual 전용이며 collider indices를 바꾸지 않는다. |
| Inventory | 없음 | `PlayerModel`은 health/life/revision만 보유 | 고정 크기 immutable snapshot과 `InventorySystem`을 Domain에 추가한다. |
| Scene builder | `MultiplayerSliceBuilder` | Title/Outgame/Test/Ingame/Player prefab을 재생성 | 역할 root와 모든 새 UI/컴포넌트를 builder에서 생성한다. |
| Minimap | 없음 | Client replica `WorldCore`와 player view는 존재 | client read-only world에서 bounded top surface projection을 만든다. |
| Voxel editor | `BlockPaintTool`, `VoxelEditingGeometry` | 이미지 변환/preview 없음 | Editor-only Fluent IMGUI window와 순수 변환 코어를 추가한다. |
| Terrain | `TerrainConfig`, `PerlinTerrainGenerator` | Normal layered/water terrain과 batch invariant 존재 | 선택적 Amplified profile을 추가하고 Normal 결과를 byte-for-byte 유지한다. |
| Save/Restore | `SessionArchiveSnapshot`, `SessionJsonStorage` | player health/world 저장 | inventory/action persistent fields를 명시적으로 직렬화하고 이전 포맷 기본값을 유지한다. |

## 기준선

- Unity EditMode: **237/237 PASS**, duration `3.7s`, `unity command test run --mode editor` (2026-11-07).
- Compile: `script recompile` 결과 `up_to_date`, error 0.
- PlayMode/Host/remote client/dedicated: P0에서는 아직 실행하지 않음.
- 카메라 jitter 및 dither hole의 수치/영상 비교: 아직 미측정. 코드상 원인 후보와 재현 장치를 먼저 추가한다.

## 결정과 블로커

| ID | 사실 | 결정 |
|---|---|---|
| P0-01 | 서버 `GameSession`과 client replica는 sibling scope이다. | HUD/minimap은 `ClientStateCoordinator`만 읽는다. |
| P0-02 | Caveworks UI는 저작권/재배포 허가가 확인되지 않았다. | 동작과 레이아웃을 독립 구현하고 원본 에셋은 복사하지 않는다. |
| P0-03 | Minefarm0 GreenIdol의 루트 라이선스가 없다. | 파일 이식은 BLOCKED, procedural MF2 visual은 유지한다. |
| P0-04 | 기존 full EditMode가 깨끗하다. | 각 Phase마다 관련 test + full suite를 다시 실행한다. |
| P0-05 | 기존 generated scenes/prefabs가 builder 산출물이다. | YAML 수작업 대신 builder 실행으로 반영한다. |

