param([Parameter(Mandatory)][string]$MainDirectory)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/../../.github/scripts/ReleaseTools.psm1" -Force
$metadata = Get-EglBuildMetadata
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$checks = 0
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAIL $name" }
    $script:checks++
    Write-Output "PASS $name"
}
foreach ($assembly in @(
    (Join-Path $MainDirectory 'EricGameLauncher.dll'),
    (Join-Path $MainDirectory 'EricGameLauncher.Cli.dll'),
    (Join-Path $repo 'Components/updater.main/bin/Debug/net10.0/win-x64/publish/updater.main.dll'),
    (Join-Path $repo 'Components/updater.cfgver/bin/Debug/net10.0/win-x64/publish/updater.cfgver.dll')
)) {
    Check ([Reflection.AssemblyName]::GetAssemblyName($assembly).Version.ToString() -eq $metadata.AssemblyVersion) "assembly version comes from Version.cs: $([IO.Path]::GetFileName($assembly))"
    Check ([Diagnostics.FileVersionInfo]::GetVersionInfo($assembly).FileVersion -eq $metadata.AssemblyVersion) "file version comes from Version.cs: $([IO.Path]::GetFileName($assembly))"
}
$manifest = [xml](Get-Content -LiteralPath (Join-Path $repo "$($metadata.IntermediateOutputPath)app.manifest") -Raw)
Check ($manifest.assembly.assemblyIdentity.version -eq $metadata.AssemblyVersion) 'generated native manifest uses the shared version'
Check ((Test-Path -LiteralPath (Join-Path $MainDirectory 'Microsoft.WindowsAppRuntime.Bootstrap.dll')) -and -not (Test-Path -LiteralPath (Join-Path $MainDirectory 'Microsoft.UI.Xaml.dll'))) 'default build uses external Windows App SDK runtime like the previous release'
$dependencies = Get-Content -LiteralPath (Join-Path $MainDirectory 'EricGameLauncher.deps.json') -Raw | ConvertFrom-Json -AsHashtable
$unusedPackages = @($dependencies.libraries.Keys | Where-Object { $_ -match '^(WinUIEx|Microsoft\.WindowsAppSDK(?:\.(?:AI|ML|Widgets|DWrite))?|System\.Numerics\.Tensors|Microsoft\.ML\.OnnxRuntime)/' })
Check ($unusedPackages.Count -eq 0) 'runtime dependency graph excludes the unused SDK modules and window extension library'
$unusedFiles = @(Get-ChildItem -LiteralPath $MainDirectory -Recurse -File | Where-Object { $_.Name -match '^(WinUIEx|Microsoft\.ML\.OnnxRuntime|System\.Numerics\.Tensors|Microsoft\.Windows\.Widgets\.Projection)\.dll$|^Microsoft\.Windows\.AI\..*\.dll$' })
Check ($unusedFiles.Count -eq 0) 'build output contains no unused AI, machine learning, Widgets or WinUIEx assemblies'
$looseXaml = @(Get-ChildItem -LiteralPath $MainDirectory -Recurse -File | Where-Object { $_.Extension -in '.xbf', '.xaml' })
Check ($looseXaml.Count -eq 0) 'build output does not copy assembly-embedded XBF or source XAML'
Check (@(Get-ChildItem -LiteralPath $MainDirectory -Recurse -File -Filter '*.pri').Count -eq 0) 'build output contains no PRI files'
Check (-not (Test-Path -LiteralPath (Join-Path $MainDirectory 'ico.ico'))) 'build output does not copy the embedded default icon'
$mainAssembly = [Reflection.Assembly]::LoadFile((Join-Path $MainDirectory 'EricGameLauncher.dll'))
$embeddedXbf = @($mainAssembly.GetManifestResourceNames() | Where-Object { $_.StartsWith('EricGameLauncher.Xaml.') -and $_.EndsWith('.xbf') })
$xamlSources = @('App.xaml', 'MainWindow.xaml') + @(Get-ChildItem -LiteralPath (Join-Path $repo 'UI') -File -Filter '*.xaml' | ForEach-Object { 'UI/' + $_.Name })
Check ($embeddedXbf.Count -eq $xamlSources.Count) 'assembly contains exactly one XBF for every source XAML file'
foreach ($source in $xamlSources) {
    $relative = [IO.Path]::ChangeExtension($source, '.xbf')
    $resource = 'EricGameLauncher.Xaml.' + $relative.Replace('/', '.')
    $stream = $mainAssembly.GetManifestResourceStream($resource)
    if ($null -eq $stream) { throw "Missing embedded XAML: $resource" }
    try {
        $buffer = [IO.MemoryStream]::new()
        try { $stream.CopyTo($buffer); $actual = [Convert]::ToBase64String($buffer.ToArray()) }
        finally { $buffer.Dispose() }
    } finally { $stream.Dispose() }
    $expected = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $repo ($metadata.IntermediateOutputPath + $relative))))
    Check ($actual -ceq $expected) "assembly resource matches compiled XBF: $relative"
}
foreach ($entry in @('EricGameLauncher', 'EricGameLauncher.Cli')) {
    $runtime = Get-Content -LiteralPath (Join-Path $MainDirectory "$entry.runtimeconfig.json") -Raw | ConvertFrom-Json -AsHashtable
    $frameworks = @($runtime.runtimeOptions.frameworks) + @($runtime.runtimeOptions.framework)
    Check (@($frameworks | Where-Object { $_ -and $_.name -eq 'Microsoft.WindowsDesktop.App' }).Count -eq 0) "$entry does not require the Windows Desktop runtime"
}
Write-Output "$checks checks passed."
