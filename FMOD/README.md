# MF2 FMOD audio project

## Versions

- Unity: 6000.3 LTS
- FMOD Studio: 2.04.00
- FMOD for Unity: 2.04.00 (`com.fmod.fmod-for-unity.v204`)

FMOD Studio and the Unity Integration must stay on the same `2.04` major/minor line.

## Project and banks

- Studio project: `FMOD/MF2Audio/MF2Audio.fspro`
- Bank output: `FMOD/MF2Audio/Build/<platform>`
- Unity source-project setting: `FMOD/MF2Audio/MF2Audio.fspro`
- Unity bank loading: all banks at initialization
- Unity bank deployment: `StreamingAssets/FMOD` during builds

Open the `.fspro` in FMOD Studio and use **File > Build** after changing events. The generated Desktop banks are kept with the project so the Unity Editor can refresh and play events without rebuilding them first.

## Smoke-test content

- `event:/Test 2D Tone`: non-spatial 2D event
- `event:/Test 3D Tone`: event with FMOD spatialization
- Source tone: `FMOD/SourceAudio/TestTone.wav`

`FMOD/Automation/CreateTestContent.js` records how the two events were created. It refuses to run when either event already exists.

The Editor-only component at `Assets/Tests/FMOD/FmodPlaybackSmokeTest.cs` verifies automatic bank loading, event lookup, 2D/3D dimensionality, and both playback states. Add it temporarily to a GameObject in Play Mode with an `FMODUnity.StudioListener` present if the smoke test needs to be repeated. It is excluded from player builds by `UNITY_EDITOR`.

## Unity Integration package

The integration was installed from the official downloaded UPM tarball. `Packages/manifest.json` currently points to the local tarball at `C:/Users/egod1/Downloads/fmodstudio20400.tgz`. On another workstation, download the matching FMOD for Unity 2.04.00 tarball and replace that local `file:` reference through Unity Package Manager.

The existing Unity audio system and project scenes were not replaced or modified for this integration.
