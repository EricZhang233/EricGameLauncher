param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$FixtureDirectory,
    [ValidateRange(1, 100)][int]$Cycles = 1,
    [Parameter(Mandatory)][string]$MetricsPath
)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/../../Build/Build.psm1" -Force
if (Test-Path -LiteralPath $FixtureDirectory) { throw 'Package fixture must be a new directory.' }
Assert-EglReleasePackage -PackagePath $PackagePath
Expand-Archive -LiteralPath $PackagePath -DestinationPath $FixtureDirectory
New-Item -ItemType Directory -Path (Join-Path $FixtureDirectory 'Data') | Out-Null
'quickStart: false' | Set-Content -LiteralPath (Join-Path $FixtureDirectory 'Data/settings.yaml')
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $FixtureDirectory 'EricGameLauncher.Cli.exe'))
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
        throw "Release CLI exceeded thirty seconds. Window='$title'. Verify .NET Desktop Runtime and Windows App Runtime are installed. STDOUT: $stdout STDERR: $stderr"
    }
    $json = $output.GetAwaiter().GetResult()
    if ($child.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($json)) { throw "Release CLI failed: $($errorOutput.GetAwaiter().GetResult())" }
    $json | ConvertFrom-Json -ErrorAction Stop | Out-Null
    Write-Output 'PASS Release CLI output is valid JSON'
} finally {
    if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit(10000) | Out-Null }
    $child.Dispose()
}
& "$PSScriptRoot/test-lifecycle.ps1" -ExecutablePath (Join-Path $FixtureDirectory 'EricGameLauncher.exe') -WorkingDirectory $FixtureDirectory -Cycles $Cycles -MetricsPath $MetricsPath
