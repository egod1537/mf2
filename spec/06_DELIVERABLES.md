# 06. Codex 산출물 및 완료 보고 형식

## 실제 코드 산출물

- P0: Water RenderPass/4 SubMesh, Solid-only Collider, Water-aware culling, collision readiness.
- P0: Liquid-safe Spawn + archive restore 시 안전 spawn; regression tests.
- P1: 실제 재현된 Session lifecycle/connection mapping 문제와 DI 잔여 항목만 수정.
- `docs/architecture/refactor-review-53fd8bd.md` 문서 작성.
- 변경 파일, 테스트 결과 및 미검증 사항을 모아 최종 응답.

## 반드시 설명할 사항

1. 시작 HEAD/종료 HEAD, working tree 변경과 생성된 파일.
2. 각 P0/P1 이슈의 `fixed / already fixed / blocked / deferred`, 수정 근거.
3. 물 블록의 렌더 pass, alpha, collision source, mesh disposal owner.
4. Session 복구 직후 PlayerId/World identity와 spawn position을 생성하는 근거.
5. 버전 기반 collider readiness가 참이 되는 조건.
6. 테스트 command, pass/fail/skipped, 빌드 및 네트워크 수동 테스트 증거.
7. 버전/에셋/serialization 수정이 있었다면 저장 호환성과 GUID 변화 검사.

## 금지와 정지 조건

- 실제 Unity 실행 없이 '모든 테스트 통과' 기재 금지.
- `git reset`, `git clean`, 강제 체크아웃 등으로 사용자의 미커밋 변경 제거 금지.
- 전면 재설계/계층 임의 추가/Shader 원본 침범/외부 패키지 업데이트 금지.
- `BlockRenderPass.Water`를 Opaque로 우회해 통과시키거나, 물의 Collider를 단순 비활성화하여 돌까지 통과하도록 만드는 임시 해법 금지.
- Save 파일을 삭제·재생성해서 복원 테스트가 통과한 것처럼 만들지 말 것.
- 어느 단계든 Unity 빌드 실패·핵심 테스트 회귀가 발생하면 이후 단계로 진행하지 말고 원인과 수정 범위 기록.

## 최종 체크리스트

- [ ] 기존 HEAD 및 사전 테스트 보고
- [ ] Water 4th submesh + material (render)
- [ ] Solid-only mesh collider (collision)
- [ ] Liquid-aware spawn
- [ ] Restored session safe spawn
- [ ] Session reload/late join identity tests
- [ ] Host + Client gameplay
- [ ] Dedicated + clients headless collision
- [ ] Repeat scope teardown 3 times
- [ ] All changed test suites executed or explicitly marked unexecuted
- [ ] Docs updated, no externally owned asset/scene/prefab damaged
