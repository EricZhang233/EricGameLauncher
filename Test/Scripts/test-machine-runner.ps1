$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot 'RunTests.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($sourcePath, [ref]$null, [ref]$null)
$functionNode = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-Native' }, $true)
if (-not $functionNode) { throw 'Machine-test command runner was not found.' }
. ([scriptblock]::Create($functionNode.Extent.Text))
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Import-Module "$PSScriptRoot/TestWorkspace.psm1" -Force
$testRoot = New-EglTestDirectory 'EGL.RunnerCheck.'
$script:checks = 0
$passed = 0
$succeeded = $false
$StepTimeoutSeconds = 30
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL $Name" }
    $script:passed++
    Write-Output "PASS $Name"
}
try {
    $script:activeLog = Join-Path $testRoot 'success.log'
    Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-Command', "Write-Output '2 checks passed.'; [Console]::Error.WriteLine('stderr-marker')")
    Check ($script:checks -eq 2 -and [IO.File]::ReadAllText($script:activeLog).Contains('stderr-marker')) 'runner captures output, stderr and successful check count'
    $script:activeLog = Join-Path $testRoot 'failure.log'
    $rejected = $false
    try { Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-Command', "Write-Output 'failure-marker'; exit 7") }
    catch { $rejected = $_.Exception.Message.Contains('code 7') -and $_.Exception.Message.Contains('failure-marker') }
    Check ($rejected -and [IO.File]::ReadAllText($script:activeLog).Contains('failure-marker')) 'runner propagates nonzero exit and retains failure output'
    Check ($script:checks -eq 2) 'failed command does not increase successful check count'
    $script:activeLog = Join-Path $testRoot 'timeout.log'
    $StepTimeoutSeconds = 3
    $rejected = $false
    $command = @'
$ErrorActionPreference = 'Stop'
$nested = Start-Process -FilePath (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 60') -WindowStyle Hidden -PassThru
Write-Output "owned-child=$($nested.Id)"
Start-Sleep -Seconds 60
'@
    try { Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-Command', $command) }
    catch { $rejected = $_.Exception.Message.Contains('exceeded') }
    $match = [regex]::Match([IO.File]::ReadAllText($script:activeLog), '(?m)^owned-child=(\d+)')
    $remaining = $null
    if ($match.Success) {
        $deadline = [DateTime]::UtcNow.AddSeconds(3)
        do { $remaining = Get-Process -Id ([int]$match.Groups[1].Value) -ErrorAction SilentlyContinue; if ($remaining) { Start-Sleep -Milliseconds 100 } } while ($remaining -and [DateTime]::UtcNow -lt $deadline)
    }
    Check ($rejected -and $match.Success -and -not $remaining) 'timeout stops owned parent and child processes and retains partial output'
    $succeeded = $true
    Write-Output "$passed checks passed."
} finally {
    if (-not $succeeded) { Get-ChildItem -LiteralPath $testRoot -Filter '*.log' | ForEach-Object { Get-Content -LiteralPath $_.FullName } }
    Remove-EglTestDirectory $testRoot
}
