param([Parameter(Mandatory)][string]$Tag, [Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
if ($Tag -cnotmatch '^v\d+\.\d+\.\d+(\.\d+)?$') { throw 'Release tag must start with v followed by a numeric version.' }
Import-Module "$PSScriptRoot/../../Build/Build.psm1" -Force
$metadata = Get-EglBuildMetadata
$version = $metadata.ExtractedVersion
if ($Tag -cne "v$version") { throw "Release tag $Tag does not match Version.cs ($version)." }
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
if ((Test-Path -LiteralPath $packageRoot) -and (Get-ChildItem -LiteralPath $packageRoot -Force | Select-Object -First 1)) { throw 'Package directory must be empty; existing archives cannot be reused.' }
& dotnet build EricGameLauncher.csproj -c Release --nologo -p:NuGetAudit=true -warnaserror "-p:ReleasePackageDirectory=$packageRoot"
if ($LASTEXITCODE -ne 0) { throw "Release build failed: $LASTEXITCODE" }
$packageName = $metadata.ReleasePackageFileName
$packagePath = Join-Path $packageRoot $packageName
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "Build did not create $packageName." }
Assert-EglReleasePackage -PackagePath $packagePath
$digest = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "Built $packageName SHA256=$digest"
if ($env:GITHUB_OUTPUT) {
    @("version=$version", "package_path=$packagePath", "artifact_name=$($metadata.ReleaseArtifactName)", "package_name=$packageName") | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}
