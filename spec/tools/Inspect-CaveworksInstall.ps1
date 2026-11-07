<#
  Read-only helper to locate potential Steam Caveworks Demo installation paths.
  Does NOT decompile, copy, delete, install, or modify Steam/Unity files.
  Suggested use from PowerShell:
    powershell -ExecutionPolicy Bypass -File .\tools\Inspect-CaveworksInstall.ps1
  Never treat its output as definitive proof that the game is absent.
#>
[CmdletBinding()]
param(
  [string]$GameFolderName = 'Caveworks Demo',
  [string]$AppId = '4281980'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$steamRoots = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

function Add-ExistingRoot([string]$Value) {
  if (-not [string]::IsNullOrWhiteSpace($Value) -and (Test-Path -LiteralPath $Value -PathType Container)) {
    [void]$steamRoots.Add((Resolve-Path -LiteralPath $Value).Path)
  }
}

if ($env:ProgramFiles) { Add-ExistingRoot (Join-Path $env:ProgramFiles 'Steam') }
if (${env:ProgramFiles(x86)}) { Add-ExistingRoot (Join-Path ${env:ProgramFiles(x86)} 'Steam') }
if ($env:LOCALAPPDATA) { Add-ExistingRoot (Join-Path $env:LOCALAPPDATA 'Steam') }
try {
  $p = Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction Stop
  if ($p.SteamPath) { Add-ExistingRoot ([string]$p.SteamPath) }
} catch { }

$libraries = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($root in $steamRoots) {
  [void]$libraries.Add($root)
  $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
  if (Test-Path -LiteralPath $vdf -PathType Leaf) {
    foreach ($line in Get-Content -LiteralPath $vdf -ErrorAction SilentlyContinue) {
      # Typical VDF format: "path" "D:\\SteamLibrary". Backslashes are escaped in VDF.
      if ($line -match '^\s*"path"\s+"([^"]+)"') {
        $lib = $matches[1] -replace '\\\\', '\'
        if (Test-Path -LiteralPath $lib -PathType Container) {
          [void]$libraries.Add((Resolve-Path -LiteralPath $lib).Path)
        }
      }
    }
  }
}

$found = @()
foreach ($lib in $libraries) {
  $steamapps = Join-Path $lib 'steamapps'
  $manifest = Join-Path $steamapps ("appmanifest_{0}.acf" -f $AppId)
  $installDir = $GameFolderName
  if (Test-Path -LiteralPath $manifest -PathType Leaf) {
    $content = Get-Content -LiteralPath $manifest -Raw
    if ($content -match '"installdir"\s+"([^"]+)"') {
      $installDir = $matches[1]
    }
  }
  $candidate = Join-Path (Join-Path $steamapps 'common') $installDir
  $exists = Test-Path -LiteralPath $candidate -PathType Container
  $dataDirs = @()
  if ($exists) {
    $dataDirs = @(Get-ChildItem -LiteralPath $candidate -Directory -Filter '*_Data' -ErrorAction SilentlyContinue |
      Select-Object -ExpandProperty FullName)
  }
  $found += [pscustomobject]@{
    SteamLibrary = $lib
    AppManifestFound = (Test-Path -LiteralPath $manifest -PathType Leaf)
    CandidateGamePath = $candidate
    CandidateExists = $exists
    UnityDataDirectories = ($dataDirs -join '; ')
  }
}

$found | Sort-Object SteamLibrary -Unique | Format-Table -AutoSize -Wrap
Write-Host 'Read-only scan completed. Confirm executable, build version, Data and Managed paths manually.'
