param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice",
    [string]$GameManagedDir = "",
    [string]$UmmDir = "",
    [string]$EditorToolkitRoot = "",
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GameManagedDir)) {
    $GameManagedDir = Join-Path $GameDir "A Dance of Fire and Ice_Data\Managed"
}
if ([string]::IsNullOrWhiteSpace($UmmDir)) {
    $candidates = @((Join-Path $GameManagedDir "UnityModManager"), (Join-Path $GameDir "UnityModManager"), $GameManagedDir)
    $UmmDir = $candidates | Where-Object { Test-Path (Join-Path $_ "UnityModManager.dll") } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($UmmDir)) { throw "UnityModManager.dll not found. Pass -UmmDir." }

if ([string]::IsNullOrWhiteSpace($EditorToolkitRoot)) {
    $parent = Split-Path $PSScriptRoot -Parent
    $EditorToolkitRoot = @((Join-Path $parent "AdofaiEditorToolkit"), (Join-Path $parent "ADOFAI.EditorToolkit")) |
        Where-Object { Test-Path (Join-Path $_ "src\ADOFAI.EditorToolkit\ADOFAI.EditorToolkit.csproj") } |
        Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($EditorToolkitRoot)) { throw "AdofaiEditorToolkit source not found next to this repository. Pass -EditorToolkitRoot." }

$toolkitCoreProject = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit\ADOFAI.EditorToolkit.csproj"
$toolkitGameProject = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit.ADOFAI\ADOFAI.EditorToolkit.ADOFAI.csproj"
$toolkitUiHost = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit.ADOFAI\ADOFAIEditorUiHost.cs"
if (-not (Test-Path $toolkitUiHost)) { throw "ADOFAIWorkbench requires AdofaiEditorToolkit feature/editor-ui-host or newer." }

$required = @(
    "Assembly-CSharp.dll",
    "RDTools.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.SceneManagementModule.dll",
    "UnityEngine.UIModule.dll"
) | ForEach-Object { Join-Path $GameManagedDir $_ }
$required += Join-Path $UmmDir "UnityModManager.dll"
foreach ($p in $required) { if (-not (Test-Path $p)) { throw "Required reference not found: $p" } }

$dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Path
if (-not $dotnet) { throw "dotnet.exe not found." }
$msbuild = (Get-Command msbuild.exe -ErrorAction SilentlyContinue).Path
if (-not $msbuild) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) { $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1 }
}
if (-not $msbuild) { throw "msbuild.exe not found." }

& $dotnet restore $toolkitCoreProject --force --nologo
if ($LASTEXITCODE -ne 0) { throw "EditorToolkit restore failed." }
& $dotnet build $toolkitCoreProject -c $Configuration -f net48 --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw "EditorToolkit core build failed." }

$toolkitCoreDll = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit\bin\$Configuration\net48\ADOFAI.EditorToolkit.dll"
& $msbuild $toolkitGameProject /t:Rebuild /p:Configuration=$Configuration /p:GameManagedDir="$GameManagedDir" /p:EditorToolkitCoreDll="$toolkitCoreDll"
if ($LASTEXITCODE -ne 0) { throw "EditorToolkit adapter build failed." }
$toolkitGameDll = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit.ADOFAI\bin\$Configuration\ADOFAI.EditorToolkit.ADOFAI.dll"

$project = Join-Path $PSScriptRoot "src\ADOFAIWorkbench.csproj"
& $msbuild $project /t:Rebuild /p:Configuration=$Configuration /p:GameManagedDir="$GameManagedDir" /p:UmmDir="$UmmDir" /p:EditorToolkitRoot="$EditorToolkitRoot" /p:EditorToolkitCoreDll="$toolkitCoreDll" /p:EditorToolkitGameDll="$toolkitGameDll"
if ($LASTEXITCODE -ne 0) { throw "ADOFAIWorkbench build failed with exit code $LASTEXITCODE" }

$bin = Join-Path $PSScriptRoot "src\bin\$Configuration\ADOFAIWorkbench.dll"
$infoPath = Join-Path $PSScriptRoot "Info.json"
$info = Get-Content $infoPath -Raw | ConvertFrom-Json
$out = Join-Path $PSScriptRoot "release\ADOFAIWorkbench"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item $bin $out
Copy-Item $toolkitCoreDll $out
Copy-Item $toolkitGameDll $out
Copy-Item $infoPath $out

$zip = Join-Path $PSScriptRoot ("ADOFAIWorkbench-v{0}.zip" -f $info.Version)
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Host "Built: $zip"
