param(
    [ValidateSet('All', 'Integration', 'Contracts', 'Packaging')][string]$Area = 'All',
    [ValidateRange(1, 100)][int]$LifecycleCycles = 1,
    [ValidateRange(30, 3600)][int]$StepTimeoutSeconds = 600,
    [string]$ReportsRoot
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $ReportsRoot) { $ReportsRoot = Join-Path $repo 'Test/artifacts/machine-tests' }
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$reportDirectory = Join-Path ([IO.Path]::GetFullPath($ReportsRoot)) $runId
Import-Module "$PSScriptRoot/TestWorkspace.psm1" -Force
$testRoot = New-EglTestDirectory 'EGL.MachineTest.'
New-Item -ItemType Directory -Path $reportDirectory, $testRoot -Force | Out-Null
$records = [Collections.Generic.List[object]]::new()
$script:activeLog = $null
$script:checks = 0
$script:errorMessage = $null
$script:finished = $false
$failed = $false
$lockHeld = $false
$mutex = $null
$started = [DateTime]::UtcNow
Import-Module "$PSScriptRoot/../../Build/Build.psm1" -Force
$metadata = Get-EglBuildMetadata
$main = Join-Path $repo $metadata.OutputPath
$packages = Join-Path $testRoot 'packages'
$fixture = Join-Path $testRoot 'release-smoke'
$version = $metadata.ExtractedVersion
$package = Join-Path $packages $metadata.ReleasePackageFileName

function Save-Report {
    $result = [ordered]@{
        run_id = $runId; started_utc = $started.ToString('O'); updated_utc = [DateTime]::UtcNow.ToString('O')
        status = $(if ($failed) { 'failed' } elseif ($script:finished -and $records.Count -gt 0 -and @($records | Where-Object { $_.status -ne 'passed' }).Count -eq 0) { 'passed' } else { 'running' })
        checks_passed = $script:checks; lifecycle_cycles = $(if ($Area -in @('All', 'Packaging')) { $LifecycleCycles } else { 0 }); version = $version; area = $Area
        error = $script:errorMessage
        test_data = $testRoot; test_data_retained = (Test-Path -LiteralPath $testRoot); phases = @($records.ToArray())
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reportDirectory 'summary.json') -Encoding utf8
    $lines = @('# EricGameLauncher machine tests', '', "Status: $($result.status)", "Checks passed: $script:checks", '', '| Phase | Status | Seconds | Log |', '|---|---|---:|---|')
    $lines += $records | ForEach-Object { "| $($_.name) | $($_.status) | $($_.seconds) | [$($_.log)]($($_.log)) |" }
    if ($script:errorMessage) { $lines += @('', "Error: $script:errorMessage") }
    $lines += @('', 'Automated checks do not establish visual correctness, all UWP/multimonitor combinations, or long-session leak freedom.')
    $lines | Set-Content -LiteralPath (Join-Path $reportDirectory 'summary.md') -Encoding utf8
}

function Invoke-Native([string]$Executable, [string[]]$CommandArguments) {
    $command = Get-Command $Executable -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $start = [Diagnostics.ProcessStartInfo]::new($command.Source)
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $start.Environment['TEMP'] = $testRoot
    $start.Environment['TMP'] = $testRoot
    $start.Environment['EglTestBuildRoot'] = (Join-Path $testRoot 'build') + [IO.Path]::DirectorySeparatorChar
    foreach ($argument in $CommandArguments) { $start.ArgumentList.Add($argument) }
    [IO.File]::AppendAllText($script:activeLog, "COMMAND: $Executable $($CommandArguments -join ' ')`n")
    $child = [Diagnostics.Process]::Start($start)
    try {
        $child.StandardInput.Close()
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        $timedOut = -not $child.WaitForExit($StepTimeoutSeconds * 1000)
        if ($timedOut) {
            $child.Kill($true)
            $child.WaitForExit(10000) | Out-Null
        }
        $output = $stdout.WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
        $errorOutput = $stderr.WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
        [IO.File]::AppendAllText($script:activeLog, $output + "`nSTDERR:`n" + $errorOutput)
        if ($timedOut) { throw "Step exceeded $StepTimeoutSeconds seconds; its process tree was stopped." }
        if ($child.ExitCode -ne 0) {
            $tail = (($output + "`n" + $errorOutput) -split '\r?\n' | Select-Object -Last 30) -join "`n"
            throw "$Executable exited with code $($child.ExitCode). See $script:activeLog`n$tail"
        }
        foreach ($match in [regex]::Matches($output, '(?m)^(\d+) checks passed\.')) { $script:checks += [int]$match.Groups[1].Value }
    } finally {
        if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit(10000) | Out-Null }
        $child.Dispose()
    }
}

$definitions = @(
    @{ Name = 'Preflight'; Action = {
        if (-not $IsWindows) { throw 'Run machine tests on Windows.' }
        foreach ($tool in @('pwsh', 'dotnet')) { Get-Command $tool -CommandType Application -ErrorAction Stop | Out-Null }
        if ($Area -in @('All', 'Contracts')) { Get-Command 'node' -CommandType Application -ErrorAction Stop | Out-Null }
        if (Get-Process -Name EricGameLauncher -ErrorAction SilentlyContinue) { throw 'Close running EricGameLauncher windows before testing. They have not been modified or stopped.' }
        $key = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($repo.ToLowerInvariant())))
        $script:mutex = [Threading.Mutex]::new($false, "Local\EricGameLauncher.MachineTests.$key")
        try { $script:lockHeld = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $script:lockHeld = $true }
        if (-not $lockHeld) { throw 'Another machine-test run is using this checkout.' }
        Invoke-Native 'dotnet' @('--version')
        if ($Area -in @('All', 'Contracts')) { Invoke-Native 'node' @('--version') }
    } },
    @{ Name = 'Test-runner safeguards'; Action = { Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-File', 'Test/Scripts/test-machine-runner.ps1') } },
    @{ Name = 'Dependency audit'; Action = { Invoke-Native 'dotnet' @('restore', 'EricGameLauncher.csproj', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-warnaserror') } },
    @{ Name = 'Debug build'; Action = { Invoke-Native 'dotnet' @('build', 'EricGameLauncher.csproj', '-c', 'Debug', '--nologo', '-p:NuGetAudit=false', '-warnaserror') } },
    @{ Name = 'Build version and runtime'; Action = { Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-File', 'Test/Scripts/test-build-output.ps1', '-MainDirectory', $main) } },
    @{ Name = 'Incremental payload repair'; Action = { Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-File', 'Test/Scripts/test-payload-repair.ps1') } },
    @{ Name = 'Core regressions'; Action = {
        Invoke-Native 'dotnet' @('build', 'Test/Regression/Regression.csproj', '--nologo', '-p:NuGetAudit=false', '-warnaserror')
        $probe = Join-Path $main 'EricGameLauncher.Regression.dll'
        Copy-Item -LiteralPath (Join-Path $testRoot 'build/Regression/bin/Debug/net10.0-windows10.0.26100/EricGameLauncher.Regression.dll') -Destination $probe
        try { Invoke-Native 'dotnet' @('exec', '--runtimeconfig', "$main/EricGameLauncher.runtimeconfig.json", '--depsfile', "$main/EricGameLauncher.deps.json", $probe, "$main/EricGameLauncher.Cli.exe") }
        finally { Remove-Item -LiteralPath $probe -ErrorAction SilentlyContinue }
    } },
    @{ Name = 'Updater integrity and command modes'; Action = { Invoke-Native 'dotnet' @('run', '--project', 'Test/UpdateIntegrity/UpdateIntegrity.csproj', '-p:NuGetAudit=false', '--', 'Components/updater.main/bin/Debug/net10.0/win-x64/updater.main.exe') } },
    @{ Name = 'Log pressure'; Action = { Invoke-Native 'dotnet' @('run', '--project', 'Test/LogStress/LogStress.csproj', '-p:NuGetAudit=false') } },
    @{ Name = 'Promotion gates'; Action = { Invoke-Native 'node' @('Test/Scripts/test-promotion.mjs') } },
    @{ Name = 'Failed release draft recovery'; Action = { Invoke-Native 'node' @('Test/Scripts/test-release-withdrawal.mjs') } },
    @{ Name = 'Release body lifecycle'; Action = { Invoke-Native 'node' @('Test/Scripts/test-release-body.mjs') } },
    @{ Name = 'Release input gates'; Action = { Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-File', 'Test/Scripts/test-release-inputs.ps1') } },
    @{ Name = 'Shared package policy'; Action = { Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-File', 'Test/Scripts/test-package-policy.ps1') } },
    @{ Name = 'Runtime preparation metadata'; Action = { Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-File', 'Test/Scripts/test-runtime-metadata.ps1') } },
    @{ Name = 'Release build'; Action = { Invoke-Native 'dotnet' @('build', 'EricGameLauncher.csproj', '-c', 'Release', '--nologo', '-p:NuGetAudit=false', '-p:TestHostRunning=true', '-warnaserror', "-p:ReleasePackageDirectory=$packages") } },
    @{ Name = 'Package and lifecycle'; Action = { Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-File', 'Test/Scripts/test-package.ps1', '-PackagePath', $package, '-FixtureDirectory', $fixture, '-Cycles', "$LifecycleCycles", '-MetricsPath', (Join-Path $reportDirectory 'lifecycle.json')) } }
)
$selected = switch ($Area) {
    'Integration' { @('Preflight', 'Debug build', 'Build version and runtime', 'Incremental payload repair', 'Core regressions', 'Updater integrity and command modes', 'Log pressure') }
    'Contracts' { @('Preflight', 'Test-runner safeguards', 'Promotion gates', 'Failed release draft recovery', 'Release body lifecycle', 'Release input gates', 'Shared package policy', 'Runtime preparation metadata') }
    'Packaging' { @('Preflight', 'Dependency audit', 'Release build', 'Package and lifecycle') }
    default { @($definitions | ForEach-Object { $_.Name }) }
}
$definitions = @($definitions | Where-Object { $_.Name -in $selected })
foreach ($definition in $definitions) { $records.Add([pscustomobject]@{ name = $definition.Name; status = 'not_run'; seconds = 0; log = ('{0:D2}.log' -f $records.Count) }) }
Push-Location $repo
try {
    Save-Report
    for ($index = 0; $index -lt $definitions.Count; $index++) {
        $record = $records[$index]
        $record.status = 'running'
        $script:activeLog = Join-Path $reportDirectory $record.log
        $timer = [Diagnostics.Stopwatch]::StartNew()
        Write-Host "RUN  [$($index + 1)/$($definitions.Count)] $($record.name)"
        Save-Report
        try {
            & $definitions[$index].Action
            $record.status = 'passed'
            Write-Host "PASS $($record.name)"
        } catch {
            $record.status = 'failed'
            $failed = $true
            $_ | Out-String | Add-Content -LiteralPath $script:activeLog
            throw
        } finally {
            $record.seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 2)
            Save-Report
        }
    }
} catch {
    $failed = $true
    $script:errorMessage = $_.Exception.Message
    Write-Host "FAILED: $($_.Exception.Message)"
} finally {
    if ($lockHeld) {
        try { Invoke-Native 'dotnet' @('msbuild', 'EricGameLauncher.csproj', '-nologo', '-target:CleanLauncherIntermediate', '-p:TestHostRunning=true') }
        catch { $failed = $true; $script:errorMessage = "Cleanup failed: $($_.Exception.Message)"; Write-Host $script:errorMessage }
    }
    try { Remove-EglTestDirectory $testRoot }
    catch { $failed = $true; $script:errorMessage = "Temporary test cleanup failed: $($_.Exception.Message)"; Write-Host $script:errorMessage }
    $script:finished = $true
    Save-Report
    if ($env:GITHUB_STEP_SUMMARY) { Get-Content -LiteralPath (Join-Path $reportDirectory 'summary.md') | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY }
    if ($lockHeld) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
    Pop-Location
}
Write-Host "Report: $reportDirectory"
if ($failed) { Write-Host "VALIDATION FAILED: $script:errorMessage"; exit 1 }
Write-Host "VALIDATION PASSED: $script:checks checks. Area=$Area."
exit 0
