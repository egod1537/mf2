# 08. Audio(FMOD), asmdef, 빌드·에셋 정리

## 사실 기반

- 최신 커밋 `7c511d0`에 `FMOD/MF2Audio`, Desktop/HTML5 테스트 Bank, `Assets/Tests/FMOD/FmodPlaybackSmokeTest.cs`, `Assets/Resources/FMOD/FMODStudioSettings.asset`가 추가돼 있다.
- 코드 감사 기준의 `ApplicationLifetimeScope.Configure`에는 별도 `IAudioService` 등록은 보이지 않음.
- Caveworks의 타입 인벤토리에는 `IAudioEngine`, `AudioEngine`, `FMODAudioEngine`, `AudioEngineScope`, `AudioTable`, `SoundTable`이 있으나, `egod1537/caveworks`에는 그 소스 본문이 저장되어 있지 않다.

## 리팩터링 범위

**이번에는 기존 오디오 프로젝트/Smoke Test를 보존하는 것이 P0.**

P2에서 실제 앱이 사운드를 재생하는 흐름이 존재하거나 테스트로 확인되면 최소 port 연결:

```text
GameSession event (PlayerJumped, BlockChanged ...) — authoritative semantic event
  → client Presentation AudioEventBridge (not a domain audio dependency)
  → IAudioService (process/client scope)
  → FMODAudioService (FMOD runtime) or NoOp (headless)
  → AudioEmitter for 3D position if needed
```

주의:
- 발소리·착지음은 서버 RPC를 전부 보내기보다 owner prediction/confirmed physics state와 재생 중복 정책을 먼저 정한다.
- 폭발 등 공유 이벤트는 서버 confirmed event에 기반. client local prediction + authoritative result 양쪽에서 재생할 때 double sound 방지.
- FMOD runtime/package 존재/라이선스/플랫폼 빌드 확인 없이 `FMODUnity.*` 타입을 Domain에 하드코딩하지 않는다.
- Application-level singleton lifetime을 써도 `AudioService.Instance` 전역 접근은 새로 도입하지 않는다.
- Dedicated에서 FMOD initialize/play를 시도하지 않거나 명확한 NoOp로 처리한다.

## asmdef 목표

```text
MF2.Game.Domain
  → Unity.Mathematics (allowed), MF2.World.Runtime (data/runtime contract)
  ✗ FishNet, UnityEngine.SceneManagement, GameObject, FMOD

MF2.Game (composition, network adapters)
  → Domain, FishNet, VContainer, Unity runtime

MF2.Entities.Player (Unity player presentation/prediction)
  → Domain contract, FishNet, Unity Input/Physics

MF2.World.Runtime
  → Unity.Burst/Collections/Mathematics, Unity rendering adapters where necessary
  (더 세분화가 실제 순환 의존 제거에 유리할 때만 분할)
```

**현재 asmdef의 실제 reference 목록/컴파일을 확인해서 조정**할 것. 구조 예시는 임의 새 어셈블리 생성 요구가 아니다. 반영 시 `Assets/Game/MF2.Game.asmdef`, `Assets/Game/Domain/MF2.Game.Domain.asmdef`, `Assets/Entities/Player/MF2.Entities.Player.asmdef`, `Assets/World/Runtime/MF2.World.Runtime.asmdef`, Test/Editor asmdef까지 영향 검사.

## Editor 및 에셋 경계

- `AGENTS.md`: `Assets/Entities/`에서 Prefab별 전용 Script/Material/Texture co-location. 여러 Prefab 공유 항목만 `Entities/Shared`.
- `MultiplayerSliceBuilder.cs`가 씬/Prefab을 재생성하므로 리팩터링 이후 실행해도 DI/Binding/Audio 필드 연결이 되도록 수정. 자동 재생성은 실제로 검증할 때만 수행하고 무관 씬 오염 방지.
- `Assets/Plugins/SceneToolbar`는 별도의 Editor 툴. 전체 코드 정리라는 이유로 제거/개조하지 않는다.
- 빌드 도중 Unity가 `.meta`를 자동 생성할 수 있다. 기존 GUID는 절대 임의 교체/삭제하지 말 것.
