param([Parameter(Mandatory)][string]$ExecutablePath, [Parameter(Mandatory)][string]$WorkingDirectory, [ValidateRange(1, 100)][int]$Cycles = 1, [string]$MetricsPath)
$ErrorActionPreference = 'Stop'
if (-not ('EricGameLauncher.Testing.ProcessSnapshot' -as [type])) { Add-Type -Path "$PSScriptRoot/../Support/ProcessSnapshot.cs" }
$samples = @()
for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $child = Start-Process -FilePath $ExecutablePath -ArgumentList '-debug' -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 200
            $child.Refresh()
            if ($child.HasExited) { throw "Cycle $cycle exited before showing its window." }
        } while ($child.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
        if ($child.MainWindowHandle -eq 0) { throw "Cycle $cycle did not expose a main window." }
        Start-Sleep -Seconds 2
        $child.Refresh()
        $samples += [pscustomobject]@{ Cycle = $cycle; Handles = $child.HandleCount; PrivateMB = [Math]::Round($child.PrivateMemorySize64 / 1MB, 1); StartupMs = $timer.ElapsedMilliseconds }
        $processes = [EricGameLauncher.Testing.ProcessSnapshot]::Read()
        if ($child.Id -notin $processes.ProcessId) { throw 'Process snapshot did not include the test process.' }
        $ownedIds = [Collections.Generic.HashSet[int]]::new()
        [void]$ownedIds.Add($child.Id)
        do {
            $added = $false
            foreach ($entry in $processes) {
                if ($ownedIds.Contains([int]$entry.ParentProcessId) -and $ownedIds.Add([int]$entry.ProcessId)) { $added = $true }
            }
        } while ($added)
        if (-not $child.CloseMainWindow()) { throw "Cycle $cycle did not accept normal close." }
        if (-not $child.WaitForExit(10000)) { throw "Cycle $cycle did not exit within ten seconds." }
        if ($child.ExitCode -ne 0) { throw "Cycle $cycle failed with exit code $($child.ExitCode)." }
        Start-Sleep -Milliseconds 500
        $liveIds = [Collections.Generic.HashSet[int]]::new([int[]][EricGameLauncher.Testing.ProcessSnapshot]::Read().ProcessId)
        $remaining = @($ownedIds | Where-Object { $_ -ne $child.Id -and $liveIds.Contains($_) })
        if ($remaining.Count) { throw "Cycle $cycle left child processes: $($remaining -join ',')" }
        Write-Output "PASS lifecycle cycle=$cycle normalExit=0 remainingChildren=0"
    } finally {
        if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $child.Dispose()
    }
}
$samples | ConvertTo-Json -Compress | Write-Output
if ($MetricsPath) { $samples | ConvertTo-Json | Set-Content -LiteralPath $MetricsPath -Encoding utf8 }
Write-Output "$Cycles lifecycle cycles passed. Fresh-process samples do not establish long-session leak freedom."
