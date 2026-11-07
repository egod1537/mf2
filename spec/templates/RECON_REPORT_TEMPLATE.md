# MF2 P0 조사 보고서 — TEMPLATE

**작성자/날짜:**
**MF2 작업 폴더 / Git branch / HEAD / git status:**
**Caveworks 로컬 설치 경로 / App manifest / Build ID / SHA256:**
**Minefarm0 checkout / commit:**
**Unity 버전 / Packages / FishNet / VContainer / ProPixelizer:**
**보존해야 할 미커밋 변경사항:**

## 1. 시스템 현황

| 시스템 | 기존 파일/클래스/장소 | 현재 동작 | 변경 후보 | 위험 |
|---|---|---|---|---|
| Player Prediction | | | | |
| Camera | | | | |
| Occlusion | | | | |
| Chunk mesh | | | | |
| Inventory | | | | |
| Scene Builder | | | | |
| World generator | | | | |
| Save/Restore | | | | |

## 2. Caveworks 실제 추출 결과

| UI/행동 | 원본 C# 타입/메서드 | Asset identifier | 실제 확인 근거 | MF2 구현 계획 |
|---|---|---|---|---|
| 인벤토리 Open/Close | | | | |
| 슬롯 선택/이동 | | | | |
| 툴바 | | | | |
| Item 사용 | | | | |
| HUD/Status | | | | |

## 3. Minefarm0 실제 이식 대상

| 원본 파일 | 리소스 유형 | 의존성 | MF2 도착 경로 | 라이선스/검증 |
|---|---|---|---|---|
| `GreenIdol.fbx` | FBX | | | |
| `Player.prefab` | Prefab | | | |
| `Attack.anim` | Animation | | | |
| `PlayerDashable.cs` | Logic reference | | | |

## 4. 현행 재현 결과

- Camera jitter 재현 환경/방법/로그:
- Dither hole 재현 환경/방법/WorldCore 값/메시:
- Asset extraction 성공/실패:
- Unity Editor/Host+Client baseline:

## 5. 결정 및 차단

| ID | 확인된 사실 | 가설 또는 추정 | 블로커/위험 | 조치 |
|---|---|---|---|---|

## 6. 다음 Phase 착수 기준

- [ ] Repo/Working Tree 보호
- [ ] Source/Asset manifest
- [ ] Baseline screenshots/logs
- [ ] P1 변경 파일/담당 경계 결정
