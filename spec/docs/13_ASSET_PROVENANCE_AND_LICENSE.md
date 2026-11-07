# 13. 원본 게임 코드/그래픽 에셋의 출처, 라이선스, 재배포

## 13.1 목적

사용자 본인이 Caveworks를 개발했고 PC에 설치되어 있더라도, 게임에 포함된 모든 리소스의 재배포/변형/타 프로젝트 사용 권한이 자동으로 동일해지는 것은 아니다. 특히 외부에서 구매한 Unity 에셋, 폰트, 이미지 팩, 소프트웨어 플러그인, 사운드, FBX 원작자의 라이선스 범위가 다를 수 있다.

Codex는 정당하게 접근 가능한 사용자 설치본/공개 레포만 분석하고, **대상 프로젝트에 실제 포함해도 되는 파일만** 선택적으로 이식한다. 원본 바이너리 전체나 추출 스크립트/분석 결과에 포함된 third-party DLL을 결과 레포에 무단 업로드하지 않는다.

## 13.2 에셋 출처 기록 필수 조건

각 import 대상으로:

1. `SourceProject` — Caveworks / Minefarm0, 기준 commit / Steam 빌드 버전.
2. `SourceAsset` — 설치본 내부 path 또는 Minefarm0 git path, GUID/local ID를 가능하면 함께.
3. `SourceSHA256` 및 추출물 SHA256, tool name/version.
4. `AssetType` — Sprite/Texture/FBX/Material/Animation/Font/Prefab 등.
5. `Creator/ThirdPartyOrigin`, `License`, `RedistributionAllowed`, `AttributionRequired`, `Decision`.
6. `DestinationPath`와 Unity `.meta` GUID, 실제 사용 중인 View/Prefab.
7. import settings(pixel filter, PPU, alpha/atlas slice 등)과 최종 스크린샷.

`templates/ASSET_EXTRACTION_MANIFEST.csv`를 편집하고 누락 항목은 `unknown`, 보류 상태는 `needs_review`로 명시한다. 출처 없는 에셋은 무조건 승인하지 않는다.

## 13.3 코드와 에셋에 대한 분리 판단

- 원본 Caveworks UI **동작/UX 조사**는 추출한 코드의 인터페이스/동작/데이터 흐름을 파악하여 MF2에 맞게 재작성한다. Decompiled code를 무조건 그대로 복사하지 않는다.
- Minefarm0 공개 코드와 original asset도 라이선스를 확인한다. Repo 공개 여부만으로 재배포 라이선스가 있는 것은 아니다.
- Unity Asset Store/서드파티 에셋인 경우 해당 계약의 seat/project/re-distribution 제한을 확인한다. 확실하지 않으면 user consent/license decision을 받아야 한다.
- GUI Font, Sound, Materials, Shader, Pixel Art는 서로 별도 라이선스로 취급할 수 있다.
- Unity/ProPixelizer/FishNet/FMOD 플러그인의 DLL/내부 소스는 재추출·복제보다 MF2의 기존 정식 package/reference를 재사용.
- 원본 에셋을 *권한상 사용할 수 있지만 GitHub 공개 배포는 허용되지 않을 수 있음*: 이 경우 private internal package/local dependency 형태로 전달하는 방법을 검토하고 public push 금지.

## 13.4 재현 가능한 import 기준

- assets를 추출한 버전/툴/스크립트와 파일 Hash를 기록한다.
- `.meta` 파일 GUID/TextureImporter/FbxImporter의 원래 설정이 복원 가능하면 보존하고, MF2에 충돌 GUID가 있으면 명시적으로 재생성 후 새 참조를 기록.
- 질량/용량이 큰 원본 Texture를 필요 이상 복제하지 않는다. 원본 누락 시 임시 파일로 '완료' 처리를 하지 않는다.
- C# Decompiled output 및 스팀 게임 데이터 전체의 단순 git 업로드 금지.
- 새 README의 Attribution/ThirdPartyNotices에 필요한 내용을 포함한다.

## 13.5 릴리스/커밋 전 라이선스 Gate

- [ ] 재사용 대상 각각의 권한과 원작자 출처를 확인.
- [ ] Allowed/NeedsReview/Excluded 상태가 설정됨.
- [ ] 프로젝트 외부 바이너리 전체가 git 상태에 포함되지 않음.
- [ ] 필요한 고지/저작권 표기 문서가 반영됨.
- [ ] 원본 설치 게임을 수정·훼손하거나 라이선스 보호를 우회하지 않음.
- [ ] 공개 원격 push 이전에 재배포 범위를 별도로 승인받음.
