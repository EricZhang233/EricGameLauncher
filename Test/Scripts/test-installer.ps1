$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/../../.github/scripts/ReleaseTools.psm1" -Force
Import-Module "$PSScriptRoot/TestWorkspace.psm1" -Force
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$root = New-EglTestDirectory 'EGL.Installer.'
$metadata = Get-EglBuildMetadata
$checks = 0
function Check([bool]$Condition, [string]$Name) { if (-not $Condition) { throw "FAIL $Name" }; $script:checks++; Write-Output "PASS $Name" }
function Invoke-Process([string]$File, [string[]]$Arguments, [int]$ExpectedExit = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['DOTNET_ROOT'] = Join-Path $root 'missing-dotnet'
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) { $process.Kill($true); throw 'Native installer test timed out.' }
        $text = $output.GetAwaiter().GetResult() + $errorOutput.GetAwaiter().GetResult()
        Write-Output $text
        if ($process.ExitCode -ne $ExpectedExit) { throw "Native installer exited with $($process.ExitCode); expected $ExpectedExit." }
    } finally { $process.Dispose() }
}
function Package([string]$Name, [string[]]$Entries) {
    $path = Join-Path $root "$Name.zip"
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $Entries) {
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($entry).Open())
            try { $writer.Write("fixture $Name $entry") } finally { $writer.Dispose() }
        }
    } finally { $archive.Dispose() }
    return $path
}
try {
    & "$repo/.github/scripts/build-native-setup.ps1" -Version $metadata.ExtractedVersion -BuildTests
    $native = Join-Path $repo 'Components/setup/bin/native'
    $stub = Join-Path $native 'EricGameLauncher.Setup.exe'
    $uninstall = Join-Path $native 'EricGameLauncher.Uninstall.exe'
    foreach ($file in @($stub, $uninstall)) {
        $managed = $true
        try { [Reflection.AssemblyName]::GetAssemblyName($file) | Out-Null } catch [BadImageFormatException] { $managed = $false }
        Check (-not $managed) "native PE has no managed assembly: $([IO.Path]::GetFileName($file))"
        Check ([Diagnostics.FileVersionInfo]::GetVersionInfo($file).FileVersion -eq $metadata.AssemblyVersion) 'native version comes from Version.cs'
        Check ([Diagnostics.FileVersionInfo]::GetVersionInfo($file).ProductName -ceq 'EricGameLauncher') 'native product name has no inserted spaces'
    }
    Check ((Get-Item -LiteralPath $uninstall).Length -lt 1MB) 'native uninstaller is below one MiB'
    Invoke-Process $uninstall @('--quiet') 1
    Check $true 'silent native uninstall rejects a missing data choice before touching registration or files'
    Invoke-Process $uninstall @('--help', '--keep-data')
    Invoke-Process $uninstall @('--help', '--remove-data')
    $required = @('EricGameLauncher.exe', 'EricGameLauncher.dll', 'EricGameLauncher.Cli.exe', 'EricGameLauncher.Cli.dll')
    $first = Package 'first' ($required + 'obsolete.dll')
    $second = Package 'second' ($required + 'feature/new.dll')
    $unsafe = Package 'unsafe' ($required + '../escape.txt')
    $sfx = Join-Path $root 'setup.exe'
    & "$repo/.github/scripts/package-installer.ps1" -PackagePath $first -InstallerStub $stub -DestinationPath $sfx
    $archive = [IO.Compression.ZipFile]::OpenRead($sfx)
    try { Check ($archive.Entries.Count -eq 5 -and -not ($archive.Entries.FullName -match 'Uninstall|Setup|\.install\.cmd')) 'EXE exposes only application files through standard ZIP APIs' } finally { $archive.Dispose() }
    Invoke-Process $sfx @('--help')
    $destination = Join-Path $root 'native-extract'
    Invoke-Process $sfx @('--quiet', '--extract', '--directory', $destination)
    $destination = Join-Path $destination 'EricGameLauncher'
    Check ((Get-ChildItem -LiteralPath $destination -Recurse -File).Count -eq 5 -and -not (Test-Path -LiteralPath (Join-Path $destination 'Uninstall.exe'))) 'native EXE extraction leaves no installer or temporary files'
    Invoke-Process (Join-Path $native 'EricGameLauncher.InstallationTests.exe') @($root, $sfx, $second, $unsafe)
    Write-Output "$checks checks passed."
} finally { Remove-EglTestDirectory $root }
