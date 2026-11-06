# 에셋 관리 규칙

## Entities 폴더 규칙

- `Assets/Entities/`는 게임에서 사용하는 Prefab과 관련 에셋을 관리한다.
- **Prefab 단위로 폴더를 구성**하고, 해당 Prefab 전용 Script, Material, Texture, Sprite, Mesh 등을 같은 폴더에 배치한다.
- 여러 Prefab에서 공유하는 리소스는 `Assets/Entities/Shared/`에 배치한다.
- 파일 확장자별 분류보다 기능별 응집도(Co-location)를 우선한다.
- 외부 에셋과 자동 생성 원본 데이터는 기존 디렉터리를 유지한다.

예시:

```text
Assets/Entities/
├── Player/
│   ├── Player.prefab
│   ├── Player.cs
│   └── Player.mat
├── Tree/
│   ├── Tree.prefab
│   └── Tree.png
└── Shared/
```

## Fluent IMGUI 에디터 DSL

- 구현은 `Assets/Scripts/Editor/FluentIMGUI/`의 Editor 전용 `MF2.EditorUI` 어셈블리에 둔다.
- 공개 진입점은 `Gui`이며, 레이아웃은 설정 후 `.Begin()`, 위젯은 설정 후 `.Draw()`에서만 평가한다.
- `.Spacing()` 자동 간격은 DSL로 그린 직접 자식 위젯과 중첩 레이아웃에만 적용된다. 원시 `GUILayout.*` 호출은 추적하지 않는다.
- 사용법과 제약은 `Assets/Editor/FluentIMGUI/README.md`를 따른다.
