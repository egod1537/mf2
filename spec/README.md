# MF2 리팩터링 리뷰 보완 명세 — Codex 실행 패키지

- 저장소: `egod1537/mf2`, 브랜치: `impl`
- **정적 코드 감사 기준 HEAD:** `53fd8bda2d7b14808e98ea414f8dbcb9522a1c3a` (2026-11-07 21:17 KST)
- 비교 기준: `53f47a14dec54627a8acdd1147180296613e42a0` (바로 전 커밋)
- 목적: *이미 작성한 전체 아키텍처 리팩터링 명세*를 유지하면서, 최신 커밋에서 확인된 문제를 P0/P1로 우선 해결한다.
- **우선순위:** 현 HEAD 확인 → P0 물 메시·충돌 → P0 안전 Spawn·복원 → P1 세션 교체 재동기화 테스트 → P1 DI/Scope 잔여 정리 → 회귀 테스트와 보고.

## 적용 방식

압축을 MF2 레포 루트에 풀고 **이 ZIP의 최상위 `CODEX_TASK.md`** 를 Codex에게 지정한다.
`original_architecture_spec/`는 예전 베이스 (`7c511d0`)에 대해 작성한 원본 전체 리팩터링 문서이며, 이번 최신 코드에 대해 다음과 같이 우선순위가 있다.

1. 현재 실제 소스, `AGENTS.md` 및 Git 상태
2. 이 패키지 최상위 `CODEX_TASK.md` 및 `0x_*.md` (최신 리뷰 보완 사항)
3. `original_architecture_spec/` 안의 아키텍처 목표·실행 단계 (최신 코드와 충돌 시 수정/생략)

기존에 이미 구현된 지형 레이어, ProPixelizer 카메라, Test/Ingame 렌더링 통일 및 안전 장치는 **재구현하거나 되돌리지 않는다.**

## 포함 문서

| 문서 | 내용 |
|---|---|
| `CODEX_TASK.md` | 최신 기준 Codex 최상위 작업 계약 |
| `01_SOURCE_FINDINGS.md` | 확인된 불일치, 리스크, 증거 파일 |
| `02_WATER_RENDER_COLLISION.md` | Water 렌더 패스, 4 SubMesh, Solid-only Collider |
| `03_SAFE_SPAWN_RESTORE.md` | Liquid 제외 Spawn, 복구 후 지형/좌표, 재접속 |
| `04_NETWORK_AND_SCOPE.md` | 서버/클라이언트/Host 이벤트 경계, DI 잔여사항 |
| `05_TEST_ACCEPTANCE.md` | EditMode, PlayMode, 수동 네트워크 테스트와 완료 게이트 |
| `06_DELIVERABLES.md` | 산출물, 중단 기준, 테스트 결과 템플릿 |
| `original_architecture_spec/` | 앞서 제공한 13개 Markdown 전체 리팩터링 명세 |

## 검증 범위의 한계

이 패키지는 GitHub의 원격 파일과 커밋을 정적 검사해 작성했다. **Unity Editor를 실행하거나 실제 게임을 플레이한 결과가 아니다.** 테스트 통과를 추측하지 말고 Codex가 직접 실행 결과와 환경상 미실행을 구분해야 한다.
