# 10. 산출물 / 코드 리뷰 / 완료 기준

## Codex가 레포에 남길 것

1. `docs/architecture/refactor-baseline.md`: 시작 SHA, 실제 확인한 참조/수명 표, 결함 vs 가설, 테스트 기준선.
2. `docs/architecture/architecture.md`: 최종 DI Scope 구조, 논리 Domain vs Presentation, 네트워크/물리/영속화 데이터 흐름, 명시적 소유권 표.
3. `docs/architecture/lifecycle.md`: Title/Host/Client/Dedicated/Spawn/Despawn/Restore/Scene unload 이벤트 순서와 failure rollback.
4. `docs/architecture/refactor-report.md`: 단계별 변경 이유, 파일, 테스트, 실행하지 못한 항목, 커밋/SHA, 향후 개선 사항.
5. 변경된 코드에 대응하는 실제 테스트/테스트 실행 절차.
6. 필요한 경우 README에 로컬 실행·Host/Client/Dedicated 방법 업데이트.

## 필수 완료 기준 (Definition of Done)

- [ ] `AGENTS.md` 위배 없음, 원래 씬/Prefab GUID와 에셋 보존.
- [ ] Scope 및 managed/native/scene/network object에 **명확한 단일 소유자** 지정.
- [ ] ServerSession/ClientReplica/SceneScope 간 의존성 방향 정리, Host shared-state leak 없음.
- [ ] Domain에 FishNet/Unity GameObject/FMOD dependency 없음, 기존 WorldCore 기반 기능 유지.
- [ ] `PlayerModel`은 Registry 소유, Controller/View/Binder의 권한 분리와 Spawn lifecycle 정리.
- [ ] Player Prediction·Reconcile, WASD·Sprint·Jump·Landing, Owner camera 유지.
- [ ] Terrain deterministic 생성, Collider 준비 전 Spawn 금지, Editor WorldHost 기능 유지.
- [ ] Snapshot/Delta/Ready ACK에 SessionId/Generation/Tick/Revision 검증.
- [ ] Session save/restore/backup failure safe, 자원 leak 없음. 물리 완전 복구 미지원이면 명시.
- [ ] FMOD smoke/SceneToolbar/기존 플러그인/World 저장 포맷 호환.
- [ ] 컴파일 및 자동 테스트 실제 실행 결과 제시; 미실행은 NOT RUN으로 정직하게 보고.
- [ ] 코드 diff를 Phase 단위로 묶고 독립적으로 review/rollback할 수 있음.

## 중요한 동작 동일성 확인

- Unity Editor/Host + ParrelSync Client UI 및 Network Session 흐름 유지.
- Dedicated `-port`/`-mf2Port` 파라미터/Start Test 시나리오 유지.
- `MultiplayerSliceBuilder`를 다시 실행해도 SceneScope, Player view/binder, collider readiness 연결이 누락되지 않음.
- Git Status에서 의도하지 않은 Third-party asset regeneration / meta GUID churn 없음.
- `WorldCore`/`ChunkPool` 성능 핵심 경로와 NativeJob 선후 관계 보존.

## Codex 최종 답변 템플릿

```text
### 개요
- Refactor branch / base SHA / final SHA:
- Completed phases:
- Deferred phases and why:

### 변경 사항
- Phase 1: ... (file paths)
- Phase 2: ...

### 검증
- Unity compile: PASS/FAIL/NOT RUN + logs
- EditMode: x/y (actual run)
- PlayMode: x/y (actual run)
- Host+Client: PASS/FAIL/NOT RUN
- Dedicated+2 Client: PASS/FAIL/NOT RUN
- Save/Restore + Editor World: PASS/FAIL/NOT RUN

### 위험 및 후속
- Regression / known limitations:
- Differences from specification and rationale:
- Required manual follow-up:
```

## PR 리뷰 체크포인트

- 리뷰어가 **새 코드보다 삭제된 중복 구독/중복 소유권**을 확인하도록 관련 전후 관계 링크 남길 것.
- VContainer `Lifetime.Singleton`/`Scoped`가 어느 LifetimeScope인지 주석 또는 설계표로 명확히.
- PlayerViewBinder의 reader 교체 테스트, stale generation, disposal on scene change 포함 여부 확인.
- `RestoreServerSession` 전환 시 old/new session object, WorldCore, Physics registry, pending spawns, clients readiness의 소유권 확인.
- Network message 검증 cap/error/authorization이 Domain과 Network 양쪽에서 무한 중복되지 않는지.

이 문서의 명칭/폴더는 필요하면 프로젝트 관례에 맞게 축소해도 좋다. **테스트와 설계 근거가 더 중요**하다.
