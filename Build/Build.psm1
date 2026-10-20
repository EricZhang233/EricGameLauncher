function Get-EglBuildMetadata {
    $project = Join-Path $PSScriptRoot '../EricGameLauncher.csproj'
    $result = & dotnet msbuild $project -nologo '-getProperty:ExtractedVersion,AssemblyVersion,ReleasePackageFileName,ReleaseArtifactName,OutputPath,IntermediateOutputPath'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to evaluate project release metadata.' }
    $properties = (($result -join "`n") | ConvertFrom-Json -ErrorAction Stop).Properties
    if ([string]::IsNullOrWhiteSpace($properties.ExtractedVersion)) { throw 'Version.cs must define a release version.' }
    return $properties
}

function Assert-EglReleasePackage {
    [CmdletBinding(DefaultParameterSetName = 'Archive')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Archive')][string]$PackagePath,
        [Parameter(Mandatory, ParameterSetName = 'Directory')][string]$DirectoryPath
    )
    $names = [Collections.Generic.List[string]]::new()
    if ($PSCmdlet.ParameterSetName -eq 'Archive') {
        $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
        try { foreach ($entry in $archive.Entries) { $names.Add($entry.FullName.Replace('\', '/')) } }
        finally { $archive.Dispose() }
    } else {
        $root = [IO.Path]::GetFullPath($DirectoryPath)
        foreach ($entry in Get-ChildItem -LiteralPath $root -Recurse -Force) {
            $relative = [IO.Path]::GetRelativePath($root, $entry.FullName).Replace('\', '/')
            if ($entry.PSIsContainer) { $relative += '/' }
            $names.Add($relative)
        }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $names) {
        if ($name -match '(^/|^[a-zA-Z]:|(^|/)\.\.(/|$))') { throw "Release package contains an unsafe path: $name" }
        if (-not $seen.Add($name)) { throw "Release package contains a duplicate path: $name" }
        if ($name -match '\.pdb$') { throw "Release package contains debug symbols: $name" }
        if ($name -match '(^|/)Microsoft\.UI\.Xaml\.dll$') { throw "Release package contains bundled Windows App SDK runtime: $name" }
        if ($name -match '(^|/)(Data|Cache)(/|$)|\.(log|bak)$|(^|/)(settings\.yaml|items\.yaml|storage\.mode)$') { throw "Release package contains private data: $name" }
    }
    foreach ($required in @('EricGameLauncher.exe', 'EricGameLauncher.dll', 'EricGameLauncher.Cli.exe', '.install.cmd')) {
        if (-not $seen.Contains($required)) { throw "Release package is missing $required." }
    }
}

Export-ModuleMember -Function Get-EglBuildMetadata, Assert-EglReleasePackage
