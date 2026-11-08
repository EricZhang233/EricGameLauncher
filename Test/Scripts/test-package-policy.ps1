$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/../../.github/scripts/ReleaseTools.psm1" -Force
$metadata = Get-EglBuildMetadata
Import-Module "$PSScriptRoot/TestWorkspace.psm1" -Force
$root = New-EglTestDirectory 'package-policy-'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$checks = 0
$required = @('EricGameLauncher.exe', 'EricGameLauncher.dll', 'EricGameLauncher.Cli.exe', 'EricGameLauncher.Cli.dll')
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAIL $name" }
    $script:checks++
    Write-Output "PASS $name"
}
function Write-Package([string]$path, [string[]]$names) {
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $names) {
            if ($name.EndsWith('/')) { $null = $archive.CreateEntry($name); continue }
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($name).Open())
            try { $writer.Write('isolated package fixture; never executable') } finally { $writer.Dispose() }
        }
    } finally { $archive.Dispose() }
}
function dotnet {
    if ($args[0] -eq 'msbuild') { & (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source @args; return }
    if ($args[0] -ne 'build') { throw 'Unexpected command in package fixture.' }
    $destination = ($args | Where-Object { $_ -like '-p:ReleasePackageDirectory=*' } | Select-Object -First 1).Substring('-p:ReleasePackageDirectory='.Length)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $fileName = (Get-EglBuildMetadata).ReleasePackageFileName
    Copy-Item -LiteralPath $global:EglPackagePolicyFixture -Destination (Join-Path $destination $fileName)
    Copy-Item -LiteralPath $global:EglPackagePolicyFixture -Destination (Join-Path $destination (Get-EglBuildMetadata).ReleaseInstallerFileName)
    $global:LASTEXITCODE = 0
}
$savedOutput = $env:GITHUB_OUTPUT
$env:GITHUB_OUTPUT = $null
try {
    $clean = Join-Path $root 'clean.zip'
    Write-Package $clean ($required + @('SKILL.md', 'Microsoft.WindowsAppRuntime.Bootstrap.dll'))
    Assert-EglReleasePackage -PackagePath $clean
    $cleanDirectory = Join-Path $root 'clean'
    Expand-Archive -LiteralPath $clean -DestinationPath $cleanDirectory
    Assert-EglReleasePackage -DirectoryPath $cleanDirectory
    Check $true 'shared package policy accepts a clean directory and archive'
    foreach ($name in @('publish/', 'publish/stale.dll')) {
        $package = Join-Path $root ([guid]::NewGuid().ToString('N') + '.zip')
        Write-Package $package ($required + $name)
        $rejected = $false
        try { Assert-EglReleasePackage -PackagePath $package } catch { $rejected = $_.Exception.Message.StartsWith('Release package contains a temporary publish directory:') }
        $directory = Join-Path $root ([guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $package -DestinationPath $directory
        $directoryRejected = $false
        try { Assert-EglReleasePackage -DirectoryPath $directory } catch { $directoryRejected = $_.Exception.Message.StartsWith('Release package contains a temporary publish directory:') }
        Check ($rejected -and $directoryRejected) "directory and archive reject temporary publish output: $name"
    }
    $legacyInstaller = Join-Path $root 'legacy-installer.zip'
    Write-Package $legacyInstaller ($required + '.install.cmd')
    $rejected = $false
    try { Assert-EglReleasePackage -PackagePath $legacyInstaller } catch { $rejected = $_.Exception.Message.StartsWith('Release package contains the retired shortcut installer:') }
    Check $rejected 'retired shortcut installer cannot reappear in release payloads'
    foreach ($name in @('ico.ico', 'nested/ICO.ICO')) {
        $package = Join-Path $root ([guid]::NewGuid().ToString('N') + '.zip')
        Write-Package $package ($required + $name)
        $rejected = $false
        try { Assert-EglReleasePackage -PackagePath $package } catch { $rejected = $_.Exception.Message.StartsWith('Release package contains an external default icon:') }
        $directory = Join-Path $root ([guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $package -DestinationPath $directory
        $directoryRejected = $false
        try { Assert-EglReleasePackage -DirectoryPath $directory } catch { $directoryRejected = $_.Exception.Message.StartsWith('Release package contains an external default icon:') }
        Check ($rejected -and $directoryRejected) "directory and archive reject the external default icon: $name"
    }
    foreach ($name in @('EricGameLauncher.runtimeconfig.json', 'EricGameLauncher.deps.json', 'EricGameLauncher.Cli.runtimeconfig.json', 'EricGameLauncher.Cli.deps.json', 'EricGameLauncher.pri', 'nested/RESOURCES.PRI', 'nested/metadata.JSON')) {
        $package = Join-Path $root ([guid]::NewGuid().ToString('N') + '.zip')
        Write-Package $package ($required + $name)
        $archiveRejected = $false
        try { Assert-EglReleasePackage -PackagePath $package } catch { $archiveRejected = $_.Exception.Message.StartsWith('Release package contains external startup or resource metadata:') }
        $directory = Join-Path $root ([guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $package -DestinationPath $directory
        $directoryRejected = $false
        try { Assert-EglReleasePackage -DirectoryPath $directory } catch { $directoryRejected = $_.Exception.Message.StartsWith('Release package contains external startup or resource metadata:') }
        Check ($archiveRejected -and $directoryRejected) "directory and archive reject external metadata: $name"
    }
    foreach ($name in @('App.xbf', 'MainWindow.xbf', 'UI/MainView.XBF', 'UI/MainView.xaml')) {
        $package = Join-Path $root ([guid]::NewGuid().ToString('N') + '.zip')
        Write-Package $package ($required + $name)
        $archiveRejected = $false
        try { Assert-EglReleasePackage -PackagePath $package } catch { $archiveRejected = $_.Exception.Message.StartsWith('Release package contains loose XAML resources:') }
        $directory = Join-Path $root ([guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $package -DestinationPath $directory
        $directoryRejected = $false
        try { Assert-EglReleasePackage -DirectoryPath $directory } catch { $directoryRejected = $_.Exception.Message.StartsWith('Release package contains loose XAML resources:') }
        Check ($archiveRejected -and $directoryRejected) "directory and archive reject loose XAML resources: $name"
    }
    foreach ($name in @('settings.yaml', 'items.yaml', 'storage.mode', 'SeTTings.YAML', 'nested/settings.yaml', 'Data/user.txt', 'nested/Cache/state.dat', 'nested/session.log', 'nested/session.bak')) {
        $package = Join-Path $root ([guid]::NewGuid().ToString('N') + '.zip')
        Write-Package $package ($required + $name)
        $archiveRejected = $false
        try { Assert-EglReleasePackage -PackagePath $package } catch { $archiveRejected = $_.Exception.Message.StartsWith('Release package contains private data:') }
        $directory = Join-Path $root ([guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $package -DestinationPath $directory
        $directoryRejected = $false
        try { Assert-EglReleasePackage -DirectoryPath $directory } catch { $directoryRejected = $_.Exception.Message.StartsWith('Release package contains private data:') }
        Check ($archiveRejected -and $directoryRejected) "directory and archive reject the same private entry: $name"
    }
    foreach ($name in @('Microsoft.UI.Xaml.dll', 'nested/Microsoft.ui.xaml.DLL')) {
        $package = Join-Path $root ([guid]::NewGuid().ToString('N') + '.zip')
        Write-Package $package ($required + $name)
        $archiveRejected = $false
        try { Assert-EglReleasePackage -PackagePath $package } catch { $archiveRejected = $_.Exception.Message.StartsWith('Release package contains bundled Windows App SDK runtime:') }
        $directory = Join-Path $root ([guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $package -DestinationPath $directory
        $directoryRejected = $false
        try { Assert-EglReleasePackage -DirectoryPath $directory } catch { $directoryRejected = $_.Exception.Message.StartsWith('Release package contains bundled Windows App SDK runtime:') }
        Check ($archiveRejected -and $directoryRejected) "directory and archive reject bundled Windows App SDK runtime: $name"
    }
    foreach ($name in @('EricGameLauncher.pdb', 'nested/Symbols.PDB')) {
        $package = Join-Path $root ([guid]::NewGuid().ToString('N') + '.zip')
        Write-Package $package ($required + $name)
        $archiveRejected = $false
        try { Assert-EglReleasePackage -PackagePath $package } catch { $archiveRejected = $_.Exception.Message.StartsWith('Release package contains debug symbols:') }
        $directory = Join-Path $root ([guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $package -DestinationPath $directory
        $directoryRejected = $false
        try { Assert-EglReleasePackage -DirectoryPath $directory } catch { $directoryRejected = $_.Exception.Message.StartsWith('Release package contains debug symbols:') }
        Check ($archiveRejected -and $directoryRejected) "directory and archive reject debug symbols: $name"
    }
    foreach ($name in @('../escape.txt', '..\escape.txt', '/absolute.txt', 'C:/absolute.txt')) {
        $package = Join-Path $root ([guid]::NewGuid().ToString('N') + '.zip')
        Write-Package $package ($required + $name)
        $rejected = $false
        try { Assert-EglReleasePackage -PackagePath $package } catch { $rejected = $_.Exception.Message.StartsWith('Release package contains an unsafe path:') }
        Check $rejected "unsafe archive entry is rejected before extraction: $name"
    }
    $missing = Join-Path $root 'missing.zip'
    Write-Package $missing @('EricGameLauncher.exe')
    $rejected = $false
    try { Assert-EglReleasePackage -PackagePath $missing } catch { $rejected = $_.Exception.Message.StartsWith('Release package is missing') }
    Check $rejected 'required package files are enforced'
    $duplicate = Join-Path $root 'duplicate.zip'
    Write-Package $duplicate ($required + 'ERICGAMELAUNCHER.EXE')
    $rejected = $false
    try { Assert-EglReleasePackage -PackagePath $duplicate } catch { $rejected = $_.Exception.Message.StartsWith('Release package contains a duplicate path:') }
    Check $rejected 'case-colliding archive entries are rejected'
    $bad = Join-Path $root 'bad-release.zip'
    Write-Package $bad ($required + 'settings.yaml')
    $global:EglPackagePolicyFixture = $bad
    $rejected = $false
    try { & "$PSScriptRoot/../../.github/scripts/build-release.ps1" -Tag "v$($metadata.ExtractedVersion)" -PackageDirectory (Join-Path $root 'release') }
    catch { $rejected = $_.Exception.Message.StartsWith('Release package contains private data:') }
    Check $rejected 'actual release script rejects root settings after the fixture build'
    $rejected = $false
    $unpack = Join-Path $root 'must-not-extract'
    try { & "$PSScriptRoot/test-package.ps1" -PackagePath $bad -FixtureDirectory $unpack -MetricsPath (Join-Path $root 'unused.json') }
    catch { $rejected = $_.Exception.Message.StartsWith('Release package contains private data:') }
    Check ($rejected -and -not (Test-Path -LiteralPath $unpack)) 'package smoke rejects the same archive before extraction or execution'
    Write-Output "$checks checks passed."
} finally {
    $env:GITHUB_OUTPUT = $savedOutput
    Remove-Variable EglPackagePolicyFixture -Scope Global -ErrorAction SilentlyContinue
    Remove-EglTestDirectory $root
}
