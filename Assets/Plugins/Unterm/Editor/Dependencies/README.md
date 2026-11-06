# Unterm Editor dependency

`System.Runtime.CompilerServices.Unsafe.dll` is the .NET Standard 2.0 assembly from
Microsoft's `System.Runtime.CompilerServices.Unsafe` NuGet package, version `6.0.0`.

Unity-Unterm's bundled Roslyn 4.8 assemblies reference this exact assembly version.
Keeping it under an `Editor` folder makes it available to the Unity Editor without
including it in player builds.

- Source: https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/6.0.0
- Package SHA-256: `6C41B53E70E9EEE298CFF3A02CE5ACDD15B04125589BE0273F0566026720A762`
- License and third-party notices are stored alongside the DLL.
