$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$checks = 0
foreach ($case in @(
    @{Target='BuildMainUpdater'; File='Components/updater.main/bin/Debug/net10.0/win-x64/publish/updater.main.exe'},
    @{Target='BuildCfgUpdater'; File='Components/updater.cfgver/bin/Debug/net10.0/win-x64/publish/updater.cfgver.deps.json'}
)) {
    $path = [IO.Path]::GetFullPath((Join-Path $repo $case.File))
    if (-not $path.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload fixture escaped repository.' }
    $original = [IO.File]::ReadAllBytes($path)
    try {
        [IO.File]::Delete($path)
        & dotnet msbuild (Join-Path $repo 'EricGameLauncher.csproj') -nologo "-target:$($case.Target)" -p:Configuration=Debug -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $path)) { throw "FAIL missing payload is not regenerated: $($case.File)" }
        $checks++
        Write-Output "PASS missing payload is regenerated: $($case.File)"
    } finally {
        if (-not (Test-Path -LiteralPath $path)) { [IO.File]::WriteAllBytes($path, $original) }
    }
}
Write-Output "$checks checks passed."
