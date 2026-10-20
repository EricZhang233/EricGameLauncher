$ErrorActionPreference = 'Stop'
$script:unexpectedBuild = $false
function dotnet {
    if ($args[0] -eq 'msbuild') { & (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source @args; return }
    $script:unexpectedBuild = $true
    throw 'Invalid release input reached the compiler.'
}
$buildScript = Join-Path $PSScriptRoot '../../.github/scripts/build-release.ps1'
Import-Module "$PSScriptRoot/../../Build/Build.psm1" -Force
$version = (Get-EglBuildMetadata).ExtractedVersion
Import-Module "$PSScriptRoot/TestWorkspace.psm1" -Force
$fixture = New-EglTestDirectory 'EGL.ReleaseInputs.'
Remove-EglTestDirectory $fixture
$cases = @($version, "V$version", "v$version-beta", "v$version; exit 0", 'v9999.9999.9999')
$checks = 0
foreach ($tag in $cases) {
    $rejected = $false
    try { & $buildScript -Tag $tag -PackageDirectory $fixture } catch { $rejected = $true }
    if (-not $rejected -or $script:unexpectedBuild) { throw "Release input was not rejected before compilation: $tag" }
    Write-Output "PASS release input rejected before compilation: $tag"
    $checks++
}
New-Item -ItemType Directory -Path $fixture | Out-Null
$sentinel = Join-Path $fixture 'existing-package.zip'
try {
    [IO.File]::WriteAllText($sentinel, 'previous package')
    $rejected = $false
    try { & $buildScript -Tag "v$version" -PackageDirectory $fixture } catch { $rejected = $true }
    if (-not $rejected -or $script:unexpectedBuild -or [IO.File]::ReadAllText($sentinel) -ne 'previous package') { throw 'Existing package was not protected.' }
    Write-Output 'PASS existing package directory is rejected without changing its contents'
    $checks++
} finally {
    [IO.File]::Delete($sentinel)
    [IO.Directory]::Delete($fixture)
}
Write-Output "$checks checks passed."
