param([Parameter(Mandatory)][string]$MainDirectory)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/../../Build/Build.psm1" -Force
$metadata = Get-EglBuildMetadata
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$checks = 0
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAIL $name" }
    $script:checks++
    Write-Output "PASS $name"
}
foreach ($assembly in @(
    (Join-Path $MainDirectory 'EricGameLauncher.dll'),
    (Join-Path $MainDirectory 'EricGameLauncher.Cli.dll'),
    (Join-Path $repo 'Components/updater.main/bin/Debug/net10.0/win-x64/publish/updater.main.dll'),
    (Join-Path $repo 'Components/updater.cfgver/bin/Debug/net10.0/win-x64/publish/updater.cfgver.dll')
)) {
    Check ([Reflection.AssemblyName]::GetAssemblyName($assembly).Version.ToString() -eq $metadata.AssemblyVersion) "assembly version comes from Version.cs: $([IO.Path]::GetFileName($assembly))"
    Check ([Diagnostics.FileVersionInfo]::GetVersionInfo($assembly).FileVersion -eq $metadata.AssemblyVersion) "file version comes from Version.cs: $([IO.Path]::GetFileName($assembly))"
}
$manifest = [xml](Get-Content -LiteralPath (Join-Path $repo "$($metadata.IntermediateOutputPath)app.manifest") -Raw)
Check ($manifest.assembly.assemblyIdentity.version -eq $metadata.AssemblyVersion) 'generated native manifest uses the shared version'
Check ((Test-Path -LiteralPath (Join-Path $MainDirectory 'Microsoft.WindowsAppRuntime.Bootstrap.dll')) -and -not (Test-Path -LiteralPath (Join-Path $MainDirectory 'Microsoft.UI.Xaml.dll'))) 'default build uses external Windows App SDK runtime like the previous release'
Write-Output "$checks checks passed."
