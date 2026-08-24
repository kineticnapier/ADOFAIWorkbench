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

$project = Join-Path $PSScriptRoot "src\ADOFAIWorkbench.csproj"
& $msbuild $project /t:Rebuild /p:Configuration=$Configuration /p:UmmDir="$UmmDir"
if ($LASTEXITCODE -ne 0) { throw "ADOFAIWorkbench build failed with exit code $LASTEXITCODE" }

$bin = Join-Path $PSScriptRoot "src\bin\$Configuration\ADOFAIWorkbench.dll"
if (-not (Test-Path $bin)) { throw "Expected DLL was not produced: $bin" }

$infoPath = Join-Path $PSScriptRoot "Info.json"
$info = Get-Content $infoPath -Raw | ConvertFrom-Json
$out = Join-Path $PSScriptRoot "release\ADOFAIWorkbench"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item $bin $out
Copy-Item $infoPath $out

$zip = Join-Path $PSScriptRoot ("ADOFAIWorkbench-v{0}.zip" -f $info.Version)
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Host "Built: $zip"
Write-Host "Mode: standalone WinForms tool window (no Unity Canvas or EditorToolkit dependency)."
