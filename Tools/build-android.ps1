<#
.SYNOPSIS
    Builds the Android player headlessly.

.DESCRIPTION
    Wraps `Unity.exe -batchmode -executeMethod DinoBattle.EditorTools.AndroidBuilder.*`.
    Set the editor path once in Tools/local.build.props (gitignored) or pass -UnityPath.

.EXAMPLE
    ./Tools/build-android.ps1
    ./Tools/build-android.ps1 -Aab
    ./Tools/build-android.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe"
#>
[CmdletBinding()]
param(
    [string]$UnityPath,
    [switch]$Aab,
    [string]$LogFile = "Logs/android-build.log"
)

$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot

# ---- resolve the editor -------------------------------------------------------
if (-not $UnityPath) {
    $propsFile = Join-Path $PSScriptRoot 'local.build.props'
    if (Test-Path $propsFile) {
        $UnityPath = (Get-Content $propsFile | Where-Object { $_ -match '^UnityPath=' }) -replace '^UnityPath=', ''
        $UnityPath = $UnityPath.Trim('"').Trim()
    }
}

if (-not $UnityPath) {
    $hubRoot = 'C:\Program Files\Unity\Hub\Editor'
    if (Test-Path $hubRoot) {
        $candidate = Get-ChildItem $hubRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'Editor\Unity.exe' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($candidate) { $UnityPath = $candidate }
    }
}

if (-not $UnityPath -or -not (Test-Path $UnityPath)) {
    Write-Error @"
Unity editor not found.

Fix it one of these ways:
  1. Create Tools/local.build.props containing:
       UnityPath=C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe
  2. Pass -UnityPath "<path to Unity.exe>"

Install Unity 6.5 (6000.5.x) with the "Android Build Support" module via Unity Hub first.
See Docs/setup.md.
"@
    exit 1
}

# ---- run ----------------------------------------------------------------------
$method = if ($Aab) { 'DinoBattle.EditorTools.AndroidBuilder.BuildAab' }
          else      { 'DinoBattle.EditorTools.AndroidBuilder.BuildApk' }

$logPath = Join-Path $projectPath $LogFile
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

Write-Host "Editor : $UnityPath"
Write-Host "Method : $method"
Write-Host "Log    : $logPath"
Write-Host ''

$unityArgs = @(
    '-quit', '-batchmode', '-nographics',
    '-projectPath', $projectPath,
    '-executeMethod', $method,
    '-logFile', $logPath
)

& $UnityPath @unityArgs
$exit = $LASTEXITCODE

if ($exit -ne 0) {
    Write-Host ''
    Write-Host '--- last 60 log lines ---' -ForegroundColor Yellow
    if (Test-Path $logPath) { Get-Content $logPath -Tail 60 }
    Write-Error "Unity exited with code $exit"
    exit $exit
}

Write-Host ''
Write-Host 'Build succeeded.' -ForegroundColor Green
Get-ChildItem (Join-Path $projectPath 'Build/Android') -ErrorAction SilentlyContinue |
    Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
