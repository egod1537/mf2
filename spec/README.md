# MF2 `impl` 통합 구현 명세 패키지

작성 기준: 2026-11-07, GitHub에 게시된 코드 및 사용자 요구사항. 이 ZIP은 **명세와 Codex 실행 지침**이며 실제 Unity 코드, 디컴파일 결과, 그래픽 에셋을 포함하지 않는다.

## 목적

`egod1537/mf2`의 `impl` 브랜치에서 기존 FishNet/VContainer/WorldCore 아키텍처를 유지한 채 다음을 모두 구현한다.

1. Caveworks 인벤토리, 툴바, HUD의 기능/시각적 이식
2. 플레이어 가림 블록의 디더링 시 지형에 구멍이 생기는 문제 수정
3. 플레이어 이동 및 카메라 추적의 떨림 해결
4. Minefarm0 캐릭터 모델, 공격, 대시 이식
5. Title/Outgame/Ingame (및 Test) Hierarchy 정리
6. 실제 월드와 연동하는 미니맵
7. 도트 이미지를 블록/복셀로 변환하는 Unity Editor 도구
8. Minecraft Amplified 스타일 지형 생성

## 시작 방법

1. **`CODEX_MASTER_PROMPT.md` 전체를 Codex에 전달**한다.
2. Codex가 `docs/00_REQUIREMENTS_AND_NONNEGOTIABLES.md`와 `docs/01_SOURCE_MAP_AND_RECON.md`를 먼저 읽고 현행 브랜치/로컬 파일/툴 상태를 조사하게 한다.
3. 조사 후 `docs/02_EXECUTION_PLAN.md` 순서로 구현한다. 각 태스크의 상세 계약은 `docs/03_*`부터 `docs/10_*`까지에 있다.
4. 테스트는 `docs/11_TEST_MATRIX.md`, 결과 보고는 `docs/12_DELIVERY_AND_REPORTING.md`, 에셋 사용은 `docs/13_ASSET_PROVENANCE_AND_LICENSE.md`를 따른다.
5. Codex가 모든 작업을 마친 척하지 않도록 `templates/`의 보고 양식을 채운다.

## 중요 전제

- MF2: Unity `6000.3.28f1`, FishNet `4.7.3`, VContainer `1.19.0`을 레포 문서에서 확인했다.
- 현재 `impl` 브랜치 GitHub HEAD 확인 값: `4dbc84c90086308bb855ad2464a9a3fa88330946`. Codex 시작 시 `git rev-parse HEAD`, `git status`로 **반드시 재확인**한다. 이미 변경된 경우에는 최신 상태가 우선이다.
- Caveworks 공개 레포에는 분석 보고서 및 ILSpy 추출 스크립트가 있지만 **원본 게임 코드/에셋은 포함되지 않는다**. 사용자 PC의 합법적으로 설치된 게임에서 확인하고 필요한 범위만 추출한다.
- Minefarm0 공개 레포에는 `GreenIdol.fbx`, `Player.prefab`, animation, 대시/공격 코드가 있다.
- 이 명세 작성 과정에서 로컬 Unity 실행, 실제 게임 설치본 접근, 코드 빌드, 에셋 라이선스 심사는 **하지 않았다**. 추정 사항은 태스크별 `확인 필요`로 표시했다.

## 목차

| 문서 | 내용 |
|---|---|
| `CODEX_MASTER_PROMPT.md` | Codex에 직접 붙여넣을 최상위 지시 |
| `docs/00_REQUIREMENTS_AND_NONNEGOTIABLES.md` | 범위, 완료 정의, 보존 조건, 금지사항 |
| `docs/01_SOURCE_MAP_AND_RECON.md` | 실제 코드 경로, 사전 조사, 자료 확보 |
| `docs/02_EXECUTION_PLAN.md` | Phase, 종속성, 체크포인트 |
| `docs/03_CAVEWORKS_EXTRACTION.md` | 설치본 발견 → ILSpy → 에셋 추출 → 검증 |
| `docs/04_INVENTORY_HOTBAR_HUD.md` | 인벤토리 모델, 명령, UI 계약, 원본 동등성 |
| `docs/05_DITHER_OCCLUSION.md` | 내부 면 누락/가림 디더링 해결 설계 및 테스트 |
| `docs/06_PLAYER_CAMERA_JITTER.md` | 시간축 측정, Prediction, 카메라 보간 |
| `docs/07_PLAYER_MODEL_ATTACK_DASH.md` | Minefarm0 캐릭터/전투/대시 이식 |
| `docs/08_SCENE_HIERARCHY.md` | 씬 Hierarchy/Builder/LifetimeScope |
| `docs/09_MINIMAP.md` | 월드 연동 미니맵 |
| `docs/10_PIXEL_TO_VOXEL_AND_TERRAIN.md` | 에디터 변환기, Amplified 지형 |
| `docs/11_TEST_MATRIX.md` | 자동/수동/멀티/저장 검증 |
| `docs/12_DELIVERY_AND_REPORTING.md` | 커밋, 문서, 완료 보고, 블로커 |
| `docs/13_ASSET_PROVENANCE_AND_LICENSE.md` | 저작권/에셋 재배포/출처 기록 |
| `docs/14_RISKS_AND_DECISIONS.md` | 위험/결정/미해결 사항 |
| `templates/*` | 보고서 및 매니페스트 양식 |
| `tools/Inspect-CaveworksInstall.ps1` | 읽기 전용 로컬 Steam 설치 경로 탐색 도우미 |

## 절대적인 합격 기준

- 모든 기능이 *화면 표시만*이 아니라 실제 MF2 월드, 플레이어, 서버 상태를 사용한다.
- Host+1 Client, Dedicated Server+2 Clients, scene transition, disconnect/reconnect, late join, save/restore에 대해 영향을 받는 부분을 검증한다.
- Unity Editor Build/PlayMode 및 관련 EditMode 테스트 통과. 환경상 실행 불가 항목은 `미검증`으로 정직하게 표시하고 증거와 재현 방법을 남긴다.
- 씬/프리팹 생성기 재실행 후에도 기능, 참조, Hierarchy가 유지된다.
- 최종 산출물은 실제 소스 수정, 에셋/출처 관리, 테스트 로그, 작업 요약이다. 이 ZIP 자체는 구현 결과물이 아니다.
