# 10. T7 도트 이미지→Voxel Editor, T8 Amplified 지형

# Part A — T7 PNG / Texture2D → MF2 Block 데이터 변환 Editor

## A.1 결과물과 편집 경험

Unity 메뉴에서 열 수 있는 별도의 EditorWindow를 제공하고 Pixel Art PNG/Sprite/Texture2D를 선택해 각 픽셀을 대응하는 MF2 블록으로 바꿔 **Preview → Validate → Commit → Undo/Redo → Save/Reload**할 수 있게 한다.

이 도구는 3D mesh voxelizer가 아니라 **2D 래스터 픽셀을 정해진 방향/두께로 extrude한 블록 아트 배치기**다. 1 pixel=1 block이 기본값이며 축/회전/깊이를 선택할 수 있다.

## A.2 프로젝트 구조와 기존 API

- `Assets/World/Editor/Tools/BlockPaintTool.cs`, `BlockToolState.cs`, `VoxelEditingGeometry.cs` 재사용 여부 조사.
- `Assets/World/Runtime/Core/WorldCore.cs`, `WorldHost.cs`, `WorldCommand.cs`의 Editor-authoring API를 우선 사용.
- `AGENTS.md`의 Editor DSL: 구현은 기존 Editor assembly 패턴을 따르고 공개 UI 진입점은 `MF2.EditorUI.Gui`, 설정 후 `.Begin()`과 `.Draw()`에서 평가. 실제 `Assets/Editor/FluentIMGUI/README.md` 참조. DSL이 지원하지 않는 기능은 제한된 원시 EditorGUI로 조합하되 spacing 영향 확인.
- EditMode WorldHost가 authoring WorldCore의 owner, PlayMode WorldHost는 replica borrowed. **PlayMode 클라이언트 프레젠테이션에서 WorldCore 직접 수정 금지.** PlayMode 적용 기능이 추가된다면 별도 서버 검증 Command로만 구현.

## A.3 UI 필드

| 그룹 | 필드 | 검사 |
|---|---|---|
| Input | Texture2D/Sprite 파일, 선택된 sprite rect/atlas 여부 | null/unsupported/readability 메시지 |
| Palette | BlockRegistry/ID ↔ Color 목록, 정확 매핑/최근접 색상 | air/투명, 존재하지 않는 BlockId 검증 |
| Alpha | threshold(0–255), 투명 픽셀 = skip/air 선택 | alpha 미포함 PNG 처리 |
| Geometry | pixel scale(기본 1 block), depth/thickness(>=1), origin XYZ, XY/XZ/YZ orientation, flip X/Y, rotation 90° 단위 | 음수 coords, 범위 overflow |
| Placement | replace/skip non-air/only air, center-anchor/first-pixel pivot | 기존 블록 덮어쓰기 경고 |
| Preview | target volume bounds, voxels count, ignored alpha count, conflicts, 예상 chunk count | WorldHost/SceneView overlay |
| Action | Dry Run, Apply, Undo, Redo, Cancel, Export mapping preset | 실패 시 부분적 적용 금지 |

색상↔블록 매핑은 스크립터블 세팅/프로파일로 유지하되 Importer의 원본 텍스처 수정은 지양한다. nearest mapping을 사용할 경우 `sRGB→linear?` 여부를 명확히 정의하고 alpha는 색상 거리에서 분리.

## A.4 좌표/변환 정의

원본 픽셀 좌표 `(px, py)`는 이미지 좌측 상단 또는 하단 중 하나를 UI에 명확히 표시하고, 기본 값은 SceneView에서 직관적으로 위쪽이 +Y가 되도록 정의한다.

예시 (XY plane, +Z thickness, bottom-left pivot):

```text
worldX = originX + px * blockScale
worldY = originY + py * blockScale
worldZ = originZ + layer * blockScale
layer ∈ [0, thickness-1]
```

XZ/YZ plane/flip/90° rotation은 정수 좌표 변환 함수를 **단일 테스트 가능한 Pure function**으로 구현하고 음수 좌표, 이미지 직사각형(W≠H), chunk border가 정확히 동작해야 한다.

## A.5 처리 파이프라인

```text
Texture/Sprite pixel buffer
  -> crop/sprite rect
  -> alpha threshold
  -> exact/nearest Palette mapping
  -> coordinate transform + extrusion
  -> bounds/budget/conflict validation
  -> grouped WorldCommand / WorldCore authoring batch
  -> dirty chunks rebuild once per batch
  -> editor Undo + serialized records + RegionJSON save path
```

- `Texture2D.isReadable == false`면 임의로 원본 importer를 조용히 바꾸지 말고 안전한 임시 readable copy 경로 또는 사용자 확인/명확한 오류를 제공. GPU ReadPixels 사용 시 GPU/RenderTexture lifetime 복원 및 `RenderTexture.active` 정리.
- Sprite가 atlas의 일부이면 rectangle과 pivot을 반영, Sprite texture rect 기반으로만 복사.
- 색상 팔레트 매핑은 결정적. 동일 입력/설정=동일 블록 명령과 동일 결과.
- 실제 GameObject per block 생성 금지. 기존 `WorldCore.Enqueue`/batch/dirty-chunk 시각 프리뷰 사용.
- 큰 PNG는 Preview cap/배치 크기/취소 대응. max voxel count는 기존 WorldCore/메모리/월드 구성과 일치시켜 검증.
- Undo는 Editor의 일반 `Undo.RecordObject`가 native WorldCore 상태를 전부 다루지 못할 수 있으므로 **복원 가능한 authoring state snapshot 또는 inverse commands**를 사용. Scene YAML serialization/Region JSON과 일관성 확인.
- Undo/Redo 직후 collider/renderer가 동일한 월드를 보여주어야 한다.
- 발행 중 실패 시 반쯤 적용하지 않도록 미리 검증하고 트랜잭션/rollback 전략.
- WorldHost가 없는 씬에서 Apply 비활성화 및 해결책 안내.

## A.6 테스트 — 예제 입력 명세

1. **2×2 컬러 + 투명 1 픽셀**: expected block count 3, 팔레트 정확 매핑, preview↔apply 동일.
2. **1×1 pixel, depth=3**: origin 포함 3개 블록 생성.
3. **2×3 비정방형, XY/XZ/YZ**: 좌표/상하 반전/90° 회전/중심 정렬 결정성.
4. **음수 좌표 + chunk seam**: 인접 chunk 모두 dirty, 재생성 후 경계 누락 없음.
5. **기존 solid 위 배치 정책**: replace/skip/only-air 별 결과, Undo 전후 원본 보존.
6. **Palette unknown ID/empty input/unreadable texture/OOM-size**: 데이터 변조 없이 오류.
7. **64×64/128×128**: 배치 소요시간/GC/최종 블록 수 기록, PlayMode와 분리.
8. **save/reopen/reload/Undo/Redo**: serialize/cache/render mesh 일치.

# Part B — T8 Minecraft Amplified 스타일 지형

## B.1 기존 동작과 목표

현행 `Assets/Game/Domain/Terrain/PerlinTerrainGenerator.cs`는 `Unity.Mathematics.noise.snoise`의 여러 octave를 가중 합산한 뒤 `BaseHeight + normalized * Amplitude`와 `MinimumY/MaximumY` clamp로 높이를 만든다. `TerrainConfig` 기본값은 64×64, Y 0..16, BaseHeight 8, Amplitude 5, Frequency 0.025, Octaves 3, seed 12345(기준 commit 코드 확인). 새 세션에 stone→dirt→grass와 water를 배치하고, 기존 복원 세션은 **지형을 다시 생성하지 않는다**.

목표는 기본 설정의 동일 결과를 유지하면서 `Normal`/`Amplified` 중 선택할 수 있고, Amplified는 산맥의 높이/날카로운 능선/깊은 계곡/급경사 등의 변화를 크게 하는 것. 단순히 amplitude 한 값만 키우는 구현보다 실제 생김새가 구분되는 지형을 우선한다.

## B.2 생성 프로필/API 계약

**권장 옵션 (기존 `TerrainConfig` 구조에 맞춰 정확한 타입과 세터를 결정):**

```text
TerrainProfile = Normal | Amplified
AmplifiedTerrainSettings:
  RidgeStrength      >= 0 (ridged multifractal 강조)
  MountainMaskScale  > 0 (산맥 지역 마스크)
  DomainWarpStrength >= 0 (반복 패턴 줄이는 좌표 왜곡)
  ElevationScale     > 0 (고도 진폭)
  ValleyStrength    >= 0 (골짜기 음의 변위)
  NoiseOctaves       1..8 또는 기존 config 범위
  OptionalTerracing  >= 0 (필요할 때만, 기본 off)
```

- 실제 코드에서 `TerrainConfig` 생성자의 유효성 검증, equality/hash/serialize, default config와 호환.
- Normal은 기존 SampleHeight 공식을 변경하지 않고 동일 seed와 기본값에서 **동일 블록 배치/signature**가 나오도록 유지.
- Amplified는 `height = BaseHeight + baseFBM + mountainMask*ridgedFBM*scale - valleyFactor` 등 조합을 검토하되 선형/비선형 표현을 테스트 시그니처 및 예시 지형으로 선택. 
- 난수/노이즈는 seed로 결정적이고 플레이어 카메라·현재 시각·GPU 상태에 따라 결과가 달라지지 않음.
- `MinimumY..MaximumY` 범위 준수, 급경사도 정수 블록으로 연속된 지면 생성. `snoise` output 범위/NaN, 극단적인 설정을 validate.
- `WorldCore`에 FillRegion을 일정 배치로 주입, FlushAndCapture/메모리 예산/Replication policy 유지.
- `TerrainConfig.DefaultMaximumBlockBudget = 1_000_000`과 높이 범위 검증이 존재. Amplified world bounds 확대 시 **최악의 수용량**을 검증하고 예산 초과를 조용히 무시하지 않는다.

## B.3 산/계곡 UX 및 옵션

- Ingame 신규 세션에서 지형 프로필/seed/강도를 선택할 수 있는 Debug/설정 경로를 두되, 정식 메뉴와 네트워크 수명주기 충돌 방지. 서버가 지형 Config를 결정하고 client는 snapshot을 받기만 한다.
- 에디터 Terrain preview로 heightmap/최대 고도/블록 사용량/마른 spawn 후보 수를 확인할 수 있으면 좋다. 필수는 아니지만 가능하다면 `WorldHost`의 authoring preview와 통합.
- Amplified 옵션은 `Profile`, `Seed`, params를 JSON/Session metadata에 안정적으로 저장하는지 조사. 기존 Save에 추가한다면 필수 migration/default Normal.
- 기존 save/restore 시 Config가 달라도 기존 블록을 재생성하지 않음. Restored generation/WorldCore를 그대로 사용.
- 지형 높이가 달라진 뒤 `SpawnLocator`가 안전한 마른 solid 바닥/충분한 머리 공간/위험한 절벽 지점을 구별하고 무한 탐색하지 않게 함.
- waterLevel, grass/dirt depth, stone base, water transparency/collision의 기존 정책 유지. 물을 땅으로 잘못 채우지 않음.
- World 초기 snapshot이 너무 커지면 48-block packet batching, bounded full snapshot, late join flow/최대 world budget을 보존. 필요 시 규모/성능을 실제 측정하고 과한 world size는 안전하게 제한.

## B.4 품질 및 성능 검증

- **Normal regression:** 기존 `PerlinTerrainGeneratorTests`의 sample height/terrain signature를 변경 전후 비교, seed 12345와 2개 이상의 다른 seed.
- **Amplified shape:** 산/계곡 높이 표본, min/max/std-dev, 경사 분포, 산맥/계곡 구별 가능한 side-view/top view 이미지. `Normal`과의 차이를 수치/화면으로 기록.
- **Determinism:** 같은 Config/Seed 반복 → 동일 heightmap/WorldCore blocks/checksum.
- **Bounds:** negative coordinate, edge, max Y, min Y, overflow/invalid params reject.
- **Layers:** stone/dirt/grass/water, 3층 dirt, waterLevel, 물 collision 제외, 고도별 스폰.
- **Save/Restore:** 새 Amplified save/reload/restore 이후 같은 월드, 기존 Normal save 호환.
- **Server:** server-only generation, Client receives consistent replica, Host/Client/late join no mismatch.
- **Performance:** 생성 시간, generatedBlockCount, flushCount, memory/chunk count, 초기 WorldReady latency를 기록. 대규모 설정에서 안전하게 실패하고 메모리 폭증 방지.

## B.5 구현 완료 정의

- [ ] `Normal`과 `Amplified` 설정이 실제로 존재하고 새 월드에 적용된다.
- [ ] Normal 결과가 기존 설정에서 달라지지 않는다.
- [ ] Amplified가 한눈에 다르고 과도한 뒤틀림/독립 기둥/스폰 불가 문제가 없다.
- [ ] 지형/물/충돌/스폰/저장/복제 모두 정상.
- [ ] 기존 save, error inputs, max block budget, deterministic tests 통과.
- [ ] 사용자에게 지형 생성 프로필 사용법과 조정 가능한 파라미터가 문서화된다.
