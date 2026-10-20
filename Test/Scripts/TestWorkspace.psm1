function New-EglTestDirectory([string]$Prefix) {
    $base = Join-Path ([IO.Path]::GetTempPath()) 'EricGameLauncher.Tests'
    $path = Join-Path $base ($Prefix + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return [IO.Path]::GetFullPath($path)
}

function Remove-EglTestDirectory([string]$Path) {
    $base = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'EricGameLauncher.Tests')) + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw "Test cleanup escaped its temporary directory: $resolved" }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

Export-ModuleMember -Function New-EglTestDirectory, Remove-EglTestDirectory
