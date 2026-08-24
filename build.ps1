param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice",
    [string]$GameManagedDir = "",
    [string]$UmmDir = "",
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GameManagedDir)) {
    $GameManagedDir = Join-Path $GameDir "A Dance of Fire and Ice_Data\Managed"
}
if ([string]::IsNullOrWhiteSpace($UmmDir)) {
    $candidates = @(
        (Join-Path $GameManagedDir "UnityModManager"),
        (Join-Path $GameDir "UnityModManager"),
        $GameManagedDir
    )
    $UmmDir = $candidates | Where-Object { Test-Path (Join-Path $_ "UnityModManager.dll") } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($UmmDir)) { throw "UnityModManager.dll not found. Pass -UmmDir." }

$umm = Join-Path $UmmDir "UnityModManager.dll"
if (-not (Test-Path $umm)) { throw "Required reference not found: $umm" }

$msbuild = (Get-Command msbuild.exe -ErrorAction SilentlyContinue).Path
if (-not $msbuild) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    }
}
if (-not $msbuild) { throw "msbuild.exe not found. Install Visual Studio Build Tools with .NET desktop build tools." }

$modProject = Join-Path $PSScriptRoot "src\ADOFAIWorkbench.csproj"
$hostProject = Join-Path $PSScriptRoot "src\Host\ADOFAIWorkbench.Host.csproj"

Write-Host "Building Unity-side Workbench bridge..."
& $msbuild $modProject /t:Rebuild /p:Configuration=$Configuration /p:UmmDir="$UmmDir"
if ($LASTEXITCODE -ne 0) { throw "ADOFAIWorkbench bridge build failed with exit code $LASTEXITCODE" }

Write-Host "Restoring external DockPanel host packages..."
& $msbuild $hostProject /t:Restore /p:Configuration=$Configuration
if ($LASTEXITCODE -ne 0) { throw "ADOFAIWorkbench.Host restore failed with exit code $LASTEXITCODE" }

Write-Host "Building external .NET Framework DockPanel host..."
& $msbuild $hostProject /t:Rebuild /p:Configuration=$Configuration
if ($LASTEXITCODE -ne 0) { throw "ADOFAIWorkbench.Host build failed with exit code $LASTEXITCODE" }

$modBinDir = Join-Path $PSScriptRoot "src\bin\$Configuration"
$hostBinDir = Join-Path $PSScriptRoot "src\Host\bin\$Configuration"
$bin = Join-Path $modBinDir "ADOFAIWorkbench.dll"
$hostExe = Join-Path $hostBinDir "ADOFAIWorkbench.Host.exe"
$dockDll = Join-Path $hostBinDir "WeifenLuo.WinFormsUI.Docking.dll"
$themeDll = Join-Path $hostBinDir "WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll"
foreach ($p in @($bin, $hostExe, $dockDll, $themeDll)) {
    if (-not (Test-Path $p)) { throw "Expected build output was not produced: $p" }
}

$infoPath = Join-Path $PSScriptRoot "Info.json"
$noticePath = Join-Path $PSScriptRoot "THIRD_PARTY_NOTICES.md"
$dockLicensePath = Join-Path $PSScriptRoot "licenses\DockPanelSuite-MIT.txt"
foreach ($p in @($infoPath, $noticePath, $dockLicensePath)) {
    if (-not (Test-Path $p)) { throw "Required release file not found: $p" }
}

$info = Get-Content $infoPath -Raw | ConvertFrom-Json
$out = Join-Path $PSScriptRoot "release\ADOFAIWorkbench"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item $bin $out
Copy-Item $hostExe $out
Copy-Item $dockDll $out
Copy-Item $themeDll $out
Copy-Item $infoPath $out
Copy-Item $noticePath $out
$licensesOut = Join-Path $out "licenses"
New-Item -ItemType Directory -Force -Path $licensesOut | Out-Null
Copy-Item $dockLicensePath $licensesOut

$zip = Join-Path $PSScriptRoot ("ADOFAIWorkbench-v{0}.zip" -f $info.Version)
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Host "Built: $zip"
Write-Host "Runtime: Unity/Mono bridge + external .NET Framework 4.8 DockPanel host"
Write-Host "Docking: DockPanel Suite 3.1.1 / VS2015 Dark theme"
Write-Host "Third-party license notices: included"
