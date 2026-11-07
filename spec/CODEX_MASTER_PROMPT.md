# Codex 최상위 작업 지시 — 그대로 실행

당신은 로컬 Windows/Unity 프로젝트에 접근 가능한 소프트웨어 엔지니어이며, `egod1537/mf2` 레포의 **`impl` 브랜치**에서 작업한다. 함께 제공된 `MF2_Codex_Impl_Spec_2026-11-07` 패키지를 요구사항의 단일 출처로 사용한다.

## 해야 할 일

1. `README.md`와 `docs/00_...`, `docs/01_...`, `docs/02_...`를 읽고 실제 소스, 의존성, 워킹트리 상태를 조사하라. 특히 MF2 자체 `AGENTS.md`, `README.md`, `docs/07_TECHNICAL_DECISIONS.md`, `docs/architecture/*`를 우선 준수하라.
2. 구현 전 `templates/RECON_REPORT_TEMPLATE.md`를 채워 **확인된 사실/추정/블로커**를 분리하라. 로컬 Caveworks Steam 설치 경로는 실제로 검색하고, 찾은 경로만 사용하라. Minefarm0 로컬 체크아웃이 없으면 정당한 GitHub 접근 방식으로 clone/fetch하라.
3. Caveworks의 인벤토리/툴바/HUD를 실제 빌드와 디컴파일 자료를 통해 조사하고 필요한 UI 리소스만 선택적으로 추출하라. 추출한 파일은 에셋 출처 매니페스트와 라이선스 확인을 수행한 뒤 통합하라. 허가가 불명확한 제3자 유료 에셋은 무단 재배포하지 마라.
4. `docs/03`부터 `docs/10`까지 요구하는 **8가지 사용자 태스크를 전부 구현**하라. 기존 MF2 구현을 우선 재사용하고, 근거 없는 API/클래스/경로를 가정하지 마라. 원본 종속성을 그대로 대량 이식하지 말고 현재 Domain/Networking/Presentation 층에 맞춰 재작성하라.
5. 각 Phase가 끝날 때마다 컴파일, 관련 EditMode/PlayMode 테스트, 생성기 재실행, Host/Client 회귀 테스트를 수행하라. 실패하면 수정하고 다시 확인하라. 실행할 수 없는 검증은 생략 사실과 원인을 명확히 기록하라.
6. 코드 변경은 의미 있는 단위로 나누고, 다른 사람의 미커밋 변경사항을 덮어쓰지 마라. 필요하면 별도 커밋을 만들되 **원격 push/force push/rebase/reset/대규모 삭제는 명시적 사용자 허락 없이 하지 마라.**
7. 구현이 완료되면 `docs/11`과 `docs/12`의 기준으로 근거 있는 최종 보고서를 작성하라. 무엇이 완료/부분 완료/차단/미검증인지 구체적으로 명시하라.

## 우선 적용할 구조적 제약

- Unity `6000.3.28f1`, FishNet `4.7.3`, VContainer `1.19.0` 환경. 해당 환경의 실제 package manifest를 먼저 확인한다.
- `ServerSessionLifetimeScope`와 `ClientPresentationLifetimeScope`는 형제 scope다. 서버 권한 모델은 `GameSession`/`PlayerRegistry`/`SimulationWorld`, 클라이언트는 `ClientStateCoordinator`를 읽는다. Host도 서버 모델로 UI에 직접 접근하지 않는다.
- Dynamic Player의 위치/회전/속도 원본은 Unity Rigidbody와 FishNet `PredictionRigidbody`다. `Replicate/Reconcile` 경로를 유지하고 `NetworkTransform`, 별도 `Physics.Simulate`, Binder가 Transform 강제 변경, 임의의 authoritative 위치 중복 저장을 만들지 않는다.
- 블록 월드는 `WorldCore`, 렌더링은 청크 메시다. 블록당 GameObject를 만들거나 디더링 해결을 위해 권한 월드 데이터/Collider를 변조하지 않는다.
- 로컬 UI는 uGUI를 기본으로 하며 DI 패턴을 보존한다. Unity Editor 툴은 프로젝트 `AGENTS.md`의 Fluent IMGUI DSL 지침을 따른다. UI Toolkit으로 강제 전면 교체하지 않는다.
- `MultiplayerSliceBuilder`가 `Title`, `Outgame`, `Test`, `Ingame`, 플레이어 프리팹을 생성/재생성하므로 변경은 생성기에 반영해야 한다. 에디터에서 수동으로만 변경한 씬/프리팹은 완료가 아니다.
- `Assets/Entities/`의 prefab 단위 co-location 규칙, 원본 `.meta` GUID 보존, Render Manifest/Registry Manifest 재생성 절차를 준수한다.
- 기존 저장 파일 포맷, 세션 복원, world replication(48 block partition), MTU/월드 예산 제한을 호환성 검토 없이 깨지 않는다.

## 실행 계획

Phase 0 사전 조사/스냅샷 → Phase 1 카메라 떨림/Hierarchy → Phase 2 모델/공격/대시 → Phase 3 Caveworks UI → Phase 4 디더링 → Phase 5 미니맵 → Phase 6 이미지→복셀 에디터 → Phase 7 Amplified 지형 → Phase 8 통합 검증/정리.

작업 간 병렬화는 가능하나, 네트워크 모델/입력/플레이어 프리팹/씬 Builder를 동시에 충돌 수정하지 마라. 임시 리소스는 별도 작업 디렉터리에 유지하며 원본 레포와 설치된 Caveworks를 불필요하게 변경하지 마라.

## 성공 판정

- '다 됐다'는 말은 실제 코드, 직렬화 참조, 테스트 로그 및 실행 증거가 있을 때만 쓸 것.
- 원본 Caveworks 구현과 행동 차이가 발견되면 동등성 체크리스트에 차이를 명시하고 수정할 것.
- 완료하지 못한 항목은 유사한 더미로 대체해서 완료 처리하지 말고, 원인과 정확한 다음 작업을 기록할 것.
- 마지막에는 Phase별 파일 목록/커밋/테스트 결과/성능 영향/남은 블로커를 한국어로 보고할 것.

**지금 바로 Phase 0 조사부터 시작해서, 가능 범위에서 모든 Phase를 순서대로 구현하고 검증하라. 불필요한 확인 질문보다는 현재 코드와 실제 로컬 환경을 조사하여 판단하되, 제3자 라이선스나 파괴적 작업 등 반드시 승인이 필요한 상황은 안전하게 중단·보고하라.**
