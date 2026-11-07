# 12. 코드 산출물, 커밋, 문서와 최종 완료 보고

## 12.1 기대 산출물

1. `mf2` `impl` 브랜치의 실제 구현/프리팹/씬/테스트/새 Editor 도구.
2. Caveworks UI 기능 동등성 매트릭스, 재사용 그래픽 출처/라이선스, 추출 근거.
3. Minefarm0 캐릭터 FBX/Animator/Animation/Material 연결과 원본/대상 차이.
4. 카메라 떨림/디더링의 원인 분석 및 수정 전후 시각·성능 증거.
5. Inventory/Dash/Attack/Minimap/Terrain의 상태·서버 명령·저장/복제 설계.
6. Builder 생성 결과와 Hierarchy 구조 문서.
7. 자동 테스트 로그, Host+Client/Dedicated 테스트 기록, 미검증 항목.
8. 종합 README(사용 키/Editor 메뉴/설치필수 조건/새 world profile/주요 제한).

## 12.2 PR/단위 커밋 권장 구조

```text
chore: document source audit and asset provenance
fix: stabilize player presentation and camera tracking
refactor: organize multiplayer scene hierarchy via builder
feat: import minefarm0 player visual rig and animation
feat: add server-authoritative attack and predicted dash
feat: implement inventory and quickbar commands and UI
fix: render backing faces behind dithered occluders
feat: add block-backed minimap presentation
feat(editor): add pixel art to voxel authoring tool
feat(world): add deterministic amplified terrain profile
test: cover multiplayer gameplay and scene lifecycle
```

**현행 git 상태와 변경 충돌에 맞춰 조정**. 사용자 미커밋 변경에 덮어쓰는 reset/rebase/force push 금지. Remote push는 허가 없이 하지 않음. 에셋 추출 도구/로컬 game DLL/무관한 바이너리는 Git에 넣지 않음.

## 12.3 각 Phase 종료 보고

- Phase 이름/상태: PASS/PARTIAL/BLOCKED.
- 변경한 파일과 주된 함수, API/데이터 형식 변화.
- 원본 근거(스크립트/설치 에셋/화면)와 MF2 변경의 매핑.
- 실행한 테스트(환경/명령/로그/결과).
- 저장/네트워크/씬/빌더 영향을 받은 영역.
- 성능 전후 수치(없는 경우 미측정이라고 기재).
- 알려진 결함/차단, 정확한 재현 및 후속 조치.

## 12.4 최종 보고 포맷

```markdown
# MF2 impl 통합 작업 최종 보고

- 기준 Git SHA / 종료 Git SHA:
- Branch / Git dirty 상태:
- Unity/FishNet/OS/GPU:
- 원본 Caveworks 설치본/추출 검증 여부:
- 사용한 Minefarm0 assets/출처:

| Task | 상태 | 핵심 변경 | 검증 증거 | 미완료 |
|---|---|---|---|---|
| T1 Inventory/Hotbar/HUD | | | | |
| T2 Dither | | | | |
| T3 Camera Jitter | | | | |
| T4 Model/Attack/Dash | | | | |
| T5 Hierarchy | | | | |
| T6 Minimap | | | | |
| T7 Pixel to Voxel | | | | |
| T8 Amplified | | | | |

## Test matrix summary
PASS x / FAIL x / BLOCKED x / NOT_RUN x / PARTIAL x

## Performance/regression

## Changed files/commits

## Blocking issues & reproduction

## 사용 방법 (Controls/Editor Menus)
```

## 12.5 실험 및 에셋 파일의 저장 위치

- 프로젝트에 필요한 정식 에셋/프리팹/스크립트만 `Assets/` 아래에 포함.
- ILSpy 출력, 게임 DLL, 원본 추출 전체, 스크린샷 원본/중간 캐시, 도구 실행 로그는 Git 추적 외부 `_external_work` 등에 별도 보관.
- 프로파일/테스트 증거는 사용자에게 접근 가능한 위치로 경로와 필요한 백업 방법을 설명.
- `.meta`/GUID 유지, 새 파일의 Git 추적 상태 확인, `.gitignore`가 필요한데 빠졌다면 수정 여부 명시.
- `.sln/.csproj/Library/Temp/Obj/Builds` 등 Unity 생성물이 불필요하게 커밋되지 않도록 점검.

## 12.6 수동 검증 시 정확한 안내

1. Unity `6000.3.28f1`로 MF2 루트를 Open.
2. import/compile 성공 및 Package/Console error 확인.
3. Manifest를 수정한 경우 기존 메뉴 `Tools > Minecraft > Rebuild Block Registry Manifest`, `Tools > Minecraft > Rebuild Block Render Manifest` 필요 여부 안내.
4. `MF2 > Build Multiplayer Test Slice`로 씬/프리팹 재생성 후 Play.
5. Title → Outgame Host → Start Test/Ingame, 별도 Client Join, 캐릭터/UI/미니맵/월드/디더링 확인.
6. 도트→Voxel Editor 메뉴와 샘플 PNG Import→Preview→Apply→Undo→Save 테스트.
7. 신규 세션에서 `Normal`, `Amplified`로 seed/terrain 차이 확인.
8. Dedicated Server + 2 Clients와 복원/late join 검증.

이 단계는 단순 사용 안내다. Codex가 이를 실제 수행하지 못했다면 '사용자가 수행해야 할 수동 테스트'로 분류하고 PASS라고 쓰지 않는다.
