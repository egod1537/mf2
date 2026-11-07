# 03. Caveworks 로컬 설치본 분석/에셋 추출 Playbook

## 3.1 목적과 작업 범위

사용자 PC에 설치된 Caveworks Demo에서 **인벤토리, 플레이어 툴바, HUD의 기능과 시각 리소스를 선별 추출**한다. 전체 Unity 게임 프로젝트 복원, 무관한 게임 시스템/Steam 의존성 이식은 목적이 아니다.

근거: `egod1537/caveworks` 공개 레포는 Unity 2022.3.62f2 Mono 빌드 분석용이다. `scripts/probe_installed_build.ps1`, `decompile_first_party.ps1`, `build_code_inventory.ps1`가 존재하지만 디컴파일 산출물/게임 에셋은 git에 없다.

## 3.2 원본 보존 및 안전한 작업 디렉터리

1. 원본 Steam 게임 디렉터리 읽기 전용 취급. 게임 폴더 내부 삭제/파일 재배치/패치 금지.
2. 작업용 폴더 예: `<mf2-parent>/_external_work/caveworks/` (MF2 git 추적 외부). `decompiled`, `extracted_assets`, `logs`, `reports`, `screenshots` 분리.
3. 파일 SHA256, 크기, 게임/Unity/assemblies 버전 기록, 원본과 작업복사본 일치 확인.
4. 라이선스와 재배포 가능 여부가 명확해질 때까지 원본 전체 폴더를 MF2 `Assets`에 복사하지 않는다.
5. 디컴파일/추출 도구 설치와 실행 출처를 검증한다. 필요 시 공식 릴리스와 해시를 기록한다.

## 3.3 설치 경로 발견

- Steam 일반 경로 `C:\Program Files (x86)\Steam\steamapps\common\Caveworks Demo`는 **예시**다. 자동으로 고정 경로로 확정하지 않는다.
- 모든 Steam library의 `steamapps\appmanifest_4281980.acf` 탐색; `libraryfolders.vdf`의 외부 드라이브도 고려.
- 확인된 install dir 하위의 `Caveworks Demo_Data`, `Managed`, `globalgamemanagers`, `resources.assets`, `sharedassets*` 등 실제 파일 존재 확인.
- 실행 파일 및 `Managed/Assembly-CSharp.dll`, `Game.UI.dll`, `Game.Ingame.Item.dll` 등 어셈블리 존재 여부를 **실제 파일 목록 기준**으로 기록. 없는 파일명을 만들어내지 않는다.
- 본 패키지 `tools/Inspect-CaveworksInstall.ps1`는 읽기 전용 후보 탐색용이며, 경로 발견 실패가 게임 미설치의 증거는 아니다.

## 3.4 코드 분석

1. Caveworks 레포의 공식 `README.md`, `reports/build_and_extraction.md`, `reports/code_architecture.md`를 읽는다.
2. 기존 스크립트가 사용 가능한지 확인하고 ILSpy CLI를 설치/기동한다(공식 배포처, 지원 버전 확인). `scripts/decompile_first_party.ps1 -BuildRoot <실제 게임 경로>` 등 원본 스크립트 인자를 우선 사용한다.
3. 추출 결과에서 `Game.UI`, `Game.Ingame.Item`, `Ingame`, `Player`, `Inventory`, `Hotbar`, `QuickSlot`, `HUD`, `Toolbar`, `Equipment`, `Drag`, `Split`, `Tooltip`, `Item` 관련 타입 및 연결을 검색.
4. 반드시 **class → method → Event/UI binding → data owner → persistence → network/RPC** 그래프를 기록.
5. 원본 UI 기능 목록(열기/닫기, 슬롯 클릭, 단축키, 스크롤, 드래그, 수량, 툴팁, 장비, 선택, 소비 등)은 실제 코드/실행 화면에서 근거를 수집하고 `templates/RECON_REPORT_TEMPLATE.md`에 기재.
6. 해당 로직을 MF2 Domain/Presentation/Networking 관례에 맞춰 재작성한다. 복원 C#을 무분별하게 복사하면 Unity 2022, MessagePack, UniTask, VContainer, FishNet 위버, DLL 종속성에 걸릴 수 있다.

## 3.5 그래픽 에셋 추출

1. AssetRipper, UnityPy 또는 검증된 Unity AssetBundle 추출 도구를 사용할 수 있다. 실제 설치된 도구의 GUI/CLI 지원을 먼저 확인한다. **존재하지 않는 CLI 옵션을 지어내지 않는다.**
2. 대상 입력: 설치본의 `*_Data` 리소스, 별도 assetbundle이 실제로 발견되면 그 파일.
3. 우선 추출: 인벤토리 창 배경/프레임, 슬롯 배경(선택/비선택/hover), 툴바 프레임, Item/Icon Sprite, 체력/상태바, 버튼, 폰트(권리 확인), UI Material, 관련 Prefab/Layout.
4. Renderer/Shader가 복원되지 않으면 UI 시각 구현에 필요한 텍스처/스프라이트만 가져오고 MF2 uGUI에서 Material/Canvas를 다시 작성한다.
5. Texture import: Sprite mode, slice/border, pixels per unit, filter mode(Point/Bilinear 원본에 맞춤), alpha, compression, color space, UI masking의 스텐실 등 실제 원본 외형과 비교.
6. Unity Prefab은 원본 스크립트 GUID가 깨지기 쉬우므로 Prefab 자동 이식보다 **스프라이트 추출 + MF2 UI Prefab 재구성**을 기본 전략으로 삼는다.
7. 아틀라스/시트 이미지는 셀 rect/pivot/border/UV를 기록, 원본과 1:1 시각 비교. 굳이 큰 Textures를 모두 import하지 않는다.

## 3.6 파일 추출 매니페스트 필수 필드

`templates/ASSET_EXTRACTION_MANIFEST.csv` 참고.

- source project/install build, source file path/name, Unity asset path 또는 original asset identifier
- Unity GUID/LocalID (복원 가능한 경우), source SHA256, export SHA256
- asset type, dimensions/import settings, chosen license/third-party origin, reuse decision
- destination in MF2, imported `.meta` GUID, reference owner, validation screenshot
- `approved/needs_review/excluded`와 이유

## 3.7 MF2에 이식하면서 반드시 충족할 것

- `Assets/Entities/Shared/` 또는 Inventory/HUD 관련 기능 폴더로 자산을 묶는다. 외부 추출 원본은 작업용 디렉터리 유지.
- uGUI(현재 `NetworkSceneHud` 등 사용)와 기존 `SceneLifetimeScope`에 통합.
- 그래픽 리소스만으로 완료하지 않고 실제 입력/아이템 상태 동기화에 연결.
- UI 프리팹/씬 계층 변경은 `MultiplayerSliceBuilder`에 반영. 씬 생성기 재실행 시 Missing references 없어야 한다.
- 원본 UI와 MF2 UI의 **동일 상태 스크린샷 비교표** 작성: 닫힌 HUD, inventory open, slot selected, item stack, drag/swap, empty/full, different resolutions.

## 3.8 복원 실패의 경계

| 실패 | 대응 |
|---|---|
| ILSpy assembly 일부가 난독화/누락 | Game.UI 관련 기능을 실행 화면과 타입/참조에서 역추적, 복원 가능한 로직만 근거로 작성 |
| UI Prefab script missing | 스프라이트, rect, anchors, 이벤트 명세 기반 재구성 |
| ShaderGraph/Material 불일치 | 기존 MF2 UI compatible shader로 최소 시각 오차 재구현 |
| 폰트 상용 라이선스 불분명 | 복사 보류 후 라이선스 적합한 대체 폰트, 차이 명시 |
| 원본 프로젝트에 특정 UI 기능이 없음 | 없는 기능을 원본과 동일하다고 주장하지 말고 별도 요청 여부 기록 |
| 설치 경로/권한 접근 불가 | 자동 탐색 로그를 남기고, 사용자 접근이 가능한 환경에서 재시도할 정확한 단계 작성 |

## 3.9 해당 Phase 완료 조건

- 출처 매니페스트, 클래스/이벤트/GUI 계층 다이어그램, 동등성 체크리스트 완성.
- 적법하게 재사용 가능한 에셋을 필요한 수만큼 import, MF2 `.meta`/UI prefab 연결 완료.
- Caveworks 원본과 비교 가능한 스크린샷 및 기능 테스트 결과가 있다.
- 원본 디컴파일 코드나 게임 바이너리 전체를 MF2 Git 저장소에 커밋하지 않는다.
