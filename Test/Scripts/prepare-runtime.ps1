param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $repo 'EricGameLauncher.csproj'
& dotnet restore $project --nologo -p:NuGetAudit=true -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Runtime preparation dependency restore failed.' }
$result = & dotnet msbuild $project -nologo '-getProperty:MicrosoftWindowsAppSDKPackageDir'
if ($LASTEXITCODE -ne 0) { throw 'Unable to locate the Windows App SDK runtime package.' }
$packageRoot = ($result -join "`n").Trim()
$metadata = Get-Content -LiteralPath (Join-Path $packageRoot 'WindowsAppSDK-VersionInfo.json') -Raw | ConvertFrom-Json
$version = [IO.Path]::GetFileName($packageRoot.TrimEnd('\', '/'))
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected Windows App SDK runtime package version '$version'; expected a stable three-part NuGet version." }
$channel = "$($metadata.Release.Major).$($metadata.Release.Minor)"
$url = "https://aka.ms/windowsappsdk/$channel/$version/windowsappruntimeinstall-x64.exe"
Write-Output "Test runtime $channel ($version), framework minimum $($metadata.Runtime.Version.String)"
Write-Output "Runtime installer: $url"
if ($ValidateOnly) { return }
Import-Module "$PSScriptRoot/TestWorkspace.psm1" -Force
$directory = New-EglTestDirectory 'runtime-setup-'
$installer = Join-Path $directory 'WindowsAppRuntimeInstall-x64.exe'
$reports = Join-Path $repo 'Test/artifacts/runtime-setup'
New-Item -ItemType Directory -Path $reports -Force | Out-Null
$process = $null
try {
Invoke-WebRequest -Uri $url -OutFile $installer -TimeoutSec 120
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
    throw 'Runtime installer does not have a valid Microsoft signature.'
}
$process = Start-Process -FilePath $installer -ArgumentList '--quiet' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $reports 'install.stdout.log') -RedirectStandardError (Join-Path $reports 'install.stderr.log')
    if (-not $process.WaitForExit(120000)) { throw 'Windows App Runtime installation exceeded two minutes.' }
    if ($process.ExitCode -ne 0) { throw "Windows App Runtime installation failed: $($process.ExitCode). See $reports" }
    Write-Output 'PASS Windows App Runtime installed for the test user'
} finally {
    if ($process) {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit(10000) | Out-Null }
        $process.Dispose()
    }
    Remove-EglTestDirectory $directory
}
