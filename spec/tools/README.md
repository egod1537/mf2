# 작업 보조 도구

`Inspect-CaveworksInstall.ps1`은 Windows PowerShell에서 설치본을 **읽기 전용으로** 탐색하는 보조 도구다. 다음 항목만 수행한다.

- 일반 Steam root와 Windows Steam 사용자 레지스트리 read
- `steamapps/libraryfolders.vdf`, `appmanifest_4281980.acf` read
- 후보 `Caveworks Demo` 게임 경로, `*_Data` 경로 출력

디컴파일, 에셋 추출, 외부 다운로드, 게임 수정, 파일 복사 등은 일절 수행하지 않는다. 탐색 결과는 반드시 사람이/에이전트가 실제 파일 존재를 검증해야 한다.

실행 예:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Inspect-CaveworksInstall.ps1
```

실제 C# 추출 작업은 Caveworks 레포 `scripts/decompile_first_party.ps1`와 실설치 경로를 사용한다. 게임에 포함된 어떤 DLL/그래픽 에셋도 이 ZIP에는 들어 있지 않다.
