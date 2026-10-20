$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/TestWorkspace.psm1" -Force
$fixtureRoot = New-EglTestDirectory 'runtime-metadata-'
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$global:EglRuntimeMetadataFixturePath = ''
$global:EglRuntimeMetadataRestoreFailed = $false
$global:EglRuntimeMetadataEvaluationFailed = $false
$checks = 0
function dotnet {
    if ($args[0] -eq 'restore') { $global:LASTEXITCODE = $(if ($global:EglRuntimeMetadataRestoreFailed) { 1 } else { 0 }); return }
    if ($args[0] -eq 'msbuild') {
        $global:LASTEXITCODE = $(if ($global:EglRuntimeMetadataEvaluationFailed) { 1 } else { 0 })
        Write-Output $global:EglRuntimeMetadataFixturePath
        return
    }
    throw 'Unexpected dotnet command in runtime metadata fixture.'
}
function Invoke-WebRequest { throw 'Validation must not download or install a runtime.' }
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAIL $name" }
    $script:checks++
    Write-Output "PASS $name"
}
try {
    foreach ($version in @('1.8.260101001', '1.8.260101001.1', '1.8.260101001-preview')) {
        $package = Join-Path $fixtureRoot "cache with spaces/$version"
        New-Item -ItemType Directory -Path $package -Force | Out-Null
        '{"Release":{"Major":1,"Minor":8},"Runtime":{"Version":{"String":"8000.731.1532.0"}}}' | Set-Content -LiteralPath (Join-Path $package 'WindowsAppSDK-VersionInfo.json')
        $global:EglRuntimeMetadataFixturePath = $package + [IO.Path]::DirectorySeparatorChar
        $errorMessage = ''
        $output = ''
        try { $output = (& "$PSScriptRoot/prepare-runtime.ps1" -ValidateOnly) -join "`n" }
        catch { $errorMessage = $_.Exception.Message }
        if ($version -eq '1.8.260101001') {
            Check ($errorMessage -eq '' -and $output.Contains('https://aka.ms/windowsappsdk/1.8/1.8.260101001/windowsappruntimeinstall-x64.exe')) "actual preparation script accepts the three-part NuGet version and builds its matching URL; error=$errorMessage"
        } else {
            Check ($errorMessage.Contains("version '$version'")) "invalid stable runtime version is rejected with its actual value: $version"
        }
    }
    $global:EglRuntimeMetadataFixturePath = (Join-Path $fixtureRoot 'cache with spaces/1.8.260101001').Replace('\', '/') + '/'
    $output = (& "$PSScriptRoot/prepare-runtime.ps1" -ValidateOnly) -join "`n"
    Check ($output.Contains('1.8.260101001/windowsappruntimeinstall-x64.exe')) 'forward-slash package paths retain the NuGet version'
    $global:EglRuntimeMetadataRestoreFailed = $true
    $message = ''
    try { & "$PSScriptRoot/prepare-runtime.ps1" -ValidateOnly } catch { $message = $_.Exception.Message }
    Check ($message.Contains('dependency restore failed')) 'restore failure stops runtime preparation'
    $global:EglRuntimeMetadataRestoreFailed = $false
    $global:EglRuntimeMetadataEvaluationFailed = $true
    $message = ''
    try { & "$PSScriptRoot/prepare-runtime.ps1" -ValidateOnly } catch { $message = $_.Exception.Message }
    Check ($message.Contains('Unable to locate')) 'MSBuild evaluation failure stops runtime preparation'
    Write-Output "$checks checks passed."
} finally {
    Remove-Variable EglRuntimeMetadataFixturePath,EglRuntimeMetadataRestoreFailed,EglRuntimeMetadataEvaluationFailed -Scope Global -ErrorAction SilentlyContinue
    Remove-EglTestDirectory $fixtureRoot
}
