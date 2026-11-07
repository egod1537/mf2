# 00. 요구사항, 품질 정의, 불변조건

## 0.1 사용자 요구사항 원문 의미 보존

| ID | 요구 | 완성된 상태의 의미 |
|---|---|---|
| T1 | Caveworks 인벤토리/툴바/HUD | 원본과 최대한 유사한 외관과 조작, 실제 아이템 상태 변경 및 동기화 |
| T2 | 가림 디더링 시 구멍 | 가려진 블록이 디더링되어도 실제 뒤쪽 지형을 볼 수 있고 빈 공간처럼 뚫리지 않음 |
| T3 | 플레이어·카메라 덜덜 떨림 | 정지/이동/대시/회전/네트워크 보정에도 부드러운 프레임 표현 |
| T4 | Minefarm0 모델/공격/대시 | `GreenIdol` 계열 모델과 애니메이션 사용, 게임 규칙과 동기화된 공격/대시 작동 |
| T5 | Ingame/Title/Outgame Hierarchy | 역할별 명확한 루트, Builder 재생성/씬 로딩 안전 |
| T6 | 미니맵 | 실제 월드 지형/플레이어 위치 반영, UI와 수명주기 연계 |
| T7 | 도트 이미지→복셀 에디터 | Texture 입력, 블록 팔레트 매핑, 미리보기, Undo, 월드 배치 |
| T8 | 증폭 지형 | 원래 Perlin 생성기를 보존하면서 더 극적인 산/계곡/절벽을 선택·설정 가능 |

모든 태스크는 코드를 추가했다는 사실만으로 완료하지 않는다. 사용자가 마우스/키보드로 실제 기능을 사용할 수 있고 Host/Client에서 예측 가능한 결과가 있어야 한다.

## 0.2 아키텍처 불변조건

1. Authoritative Model은 순수 C# Domain(`GameSession`, Registry, System/Command) 소유. Scene MonoBehaviour는 Model을 소유하지 않는다.
2. Host/Client 모두 프레젠테이션은 Replica를 읽는다. NetworkBehaviour, RPC, SyncVar는 Presentation 계약으로 새지 않는다.
3. FishNet Prediction/Reconcile을 우회하지 않는다. Player Rigidbody를 여러 시스템에서 동시에 갱신하지 않는다.
4. `WorldCore`는 유일한 블록 저장소이며 블록별 GameObject/Collider 생성 금지. 청크 렌더/물리 분리 유지.
5. `WorldHost` EditMode authoring과 PlayMode borrowed replica 소유권 구분 및 dispose 정책 유지.
6. 기존 48-block bounded network snapshot/delta, WorldReady(충돌 메시 readiness) 장벽 및 복원 generation 정책 보존.
7. `Title`의 `NetworkBootstrap`은 지속형. scene scope는 client presentation 자식. Dedicated Server에는 화면 UI/카메라가 없다.
8. `Assets/Entities/` Prefab별 Script/Material/Texture/Animation 묶음 배치, 공유 에셋은 `Assets/Entities/Shared/` 사용.
9. `MultiplayerSliceBuilder`가 만들어 주는 씬/프리팹은 생성기 실행 후에도 모든 기능을 유지해야 한다.
10. 기존 외부 플러그인(ProPixelizer, FMOD, SceneToolbar, TexturePack 등) 내부에 기능 구현을 몰아넣지 않고 연동 지점에서 해결한다.

## 0.3 분명히 금지하는 단축 구현

- 인벤토리에 모양만 있는 고정 아이콘을 넣고 실제 슬롯/아이템 데이터가 없게 처리.
- 공격 애니메이션만 재생하고 판정/쿨다운/검증이 없는 상태를 완료라고 주장.
- 대시를 로컬 `Rigidbody.AddForce()`로만 구현해 타인/서버에서는 불일치.
- 디더링 대상 블록을 서버 월드에서 지우거나 `MeshCollider`를 변경.
- 카메라 떨림을 무조건 임의 SmoothDamp로 가려 재조정/추적 지연을 악화.
- `Test`와 `Ingame` 중 하나에서만 작동, Host에서만 작동, 재접속시 참조가 꼬인 상태.
- 외부 에셋/코드 대량 복사 후 DLL/스크립트가 Missing 또는 라이선스 불명확.
- Unity Scene/Prefab을 생성기에서 다시 만들 때 변경사항이 사라지게 둠.
- 저장 버전 및 ItemId/BlockId가 변경됐는데 마이그레이션/호환성 검증을 생략.
- 환경상 테스트를 못 했으면서 테스트 통과라고 기록.

## 0.4 기능별 범위와 경계

- 원본 Caveworks UI의 **실제 제공 기능**을 조사해서 동등성 목표를 확정한다. 원본에 없는 제작/거래/퀘스트/가방 확장 같은 기능을 임의 범위에 추가하지 않는다.
- 원본 Minefarm0 액션은 재사용하지만 MF2의 물리/서버 권한 규칙에 맞게 재구현한다. 전면적인 RPG/몬스터/장비 시스템 이식은 범위 밖이다. 공격 검증을 위한 최소 대상 엔티티/테스트 더미는 허용한다.
- 미니맵은 지형/플레이어 기반 MVP를 완성한다. 대형 지도, 경로 탐색, 안개/탐험 시스템은 비목표.
- 이미지→복셀은 2D 도트 데이터를 3D 블록 배치로 변환하는 도구다. 일반 3D 메시 복셀라이저는 비목표.
- Amplified는 고도/지형 분포 강화다. 완전한 Minecraft 월드 생성기, 동굴, 구조물, 생물군계, 수압/유체 시뮬레이션은 비목표.

## 0.5 증거 기반 완료 판정

각 태스크마다 최소 다음을 기록한다.

- 수정 파일 및 핵심 API, 선택 이유
- 확인한 원본 클래스/에셋/화면과 정확한 동작
- 자동 테스트 명령/결과(로그 파일, 케이스 수)
- Unity Editor 실행 결과(가능 시 테스트 씬/스크린샷/동영상)
- Host + Client 결과, 발생한 네트워크 오류/경고
- 실패/미구현/미검증 항목 및 재현 방법

**P0 확인 전에는 아키텍처 대수술을 시작하지 않는다. 각 Phase는 증거와 함께 닫는다.**
