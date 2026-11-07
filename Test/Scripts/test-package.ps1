param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$FixtureDirectory,
    [ValidateRange(1, 100)][int]$Cycles = 1,
    [Parameter(Mandatory)][string]$MetricsPath
)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/../../.github/scripts/ReleaseTools.psm1" -Force
if (Test-Path -LiteralPath $FixtureDirectory) { throw 'Package fixture must be a new directory.' }
Assert-EglReleasePackage -PackagePath $PackagePath
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
try { foreach ($entry in $archive.Entries) { Write-Output "Package entry: $($entry.FullName) bytes=$($entry.Length) zipBytes=$($entry.CompressedLength)" } }
finally { $archive.Dispose() }
Expand-Archive -LiteralPath $PackagePath -DestinationPath $FixtureDirectory
New-Item -ItemType Directory -Path (Join-Path $FixtureDirectory 'Data') | Out-Null
'quickStart: false' | Set-Content -LiteralPath (Join-Path $FixtureDirectory 'Data/settings.yaml')
$runtimeCache = $FixtureDirectory + '.runtime-cache'
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $FixtureDirectory 'EricGameLauncher.Cli.exe'))
$start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $runtimeCache
$start.WorkingDirectory = $FixtureDirectory
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
foreach ($argument in @('-debug', 'list', '--json')) { $start.ArgumentList.Add($argument) }
$child = [Diagnostics.Process]::Start($start)
try {
    $child.StandardInput.Close()
    $output = $child.StandardOutput.ReadToEndAsync()
    $errorOutput = $child.StandardError.ReadToEndAsync()
    if (-not $child.WaitForExit(30000)) {
        $child.Refresh()
        $title = $child.MainWindowTitle
        $child.Kill($true)
        $child.WaitForExit(10000) | Out-Null
        $stdout = $output.WaitAsync([TimeSpan]::FromSeconds(3)).GetAwaiter().GetResult()
        $stderr = $errorOutput.WaitAsync([TimeSpan]::FromSeconds(3)).GetAwaiter().GetResult()
        throw "Release CLI exceeded thirty seconds. Window='$title'. Verify .NET Runtime and Windows App Runtime are installed. STDOUT: $stdout STDERR: $stderr"
    }
    $json = $output.GetAwaiter().GetResult()
    if ($child.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($json)) { throw "Release CLI failed: $($errorOutput.GetAwaiter().GetResult())" }
    $json | ConvertFrom-Json -ErrorAction Stop | Out-Null
    Write-Output 'PASS Release CLI output is valid JSON'
} finally {
    if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit(10000) | Out-Null }
    $child.Dispose()
}
$savedRuntimeCache = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $runtimeCache
    & "$PSScriptRoot/test-lifecycle.ps1" -ExecutablePath (Join-Path $FixtureDirectory 'EricGameLauncher.exe') -WorkingDirectory $FixtureDirectory -Cycles $Cycles -MetricsPath $MetricsPath
} finally { $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $savedRuntimeCache }
$logs = (Get-ChildItem -LiteralPath (Join-Path $FixtureDirectory 'Cache/log') -Recurse -File | Get-Content) -join "`n"
if (-not $logs.Contains("InstallDir=$FixtureDirectory ResourceDir=$FixtureDirectory")) { throw 'Directory release must load runtime resources from its installation directory.' }
if (Test-Path -LiteralPath $runtimeCache) { throw 'Directory release unexpectedly extracted a single-file bundle.' }
$looseXaml = @(Get-ChildItem -LiteralPath $FixtureDirectory -Recurse -File | Where-Object { $_.Extension -in '.xbf', '.xaml', '.pri' })
if ($looseXaml.Count) { throw 'Directory release must load its compiled XAML from the assembly without PRI, XBF or XAML files.' }
if (-not $logs.Contains('XAML resources=assembly; no PRI extraction') -or -not $logs.Contains('XAML resource loaded from assembly name=UI/MainView.xbf')) { throw 'Release GUI did not resolve its XAML from assembly resources.' }
if (-not $logs.Contains('AppIconService loaded default HICON from embedded resource') -or -not $logs.Contains('AppIconService decoded embedded default icon in memory')) { throw 'Release GUI did not load its default icon from embedded resources.' }
if (Test-Path -LiteralPath (Join-Path $FixtureDirectory 'ico.ico')) { throw 'Release unexpectedly created an external default icon.' }
if (Test-Path -LiteralPath (Join-Path $FixtureDirectory 'Cache/EricGameLauncher_TempIcon.ico')) { throw 'Release unexpectedly extracted its default icon.' }
Write-Output 'PASS Release GUI loads assembly-embedded XAML and embedded startup configuration without extracting files'
