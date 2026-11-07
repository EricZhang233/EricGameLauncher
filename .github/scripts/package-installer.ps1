param([Parameter(Mandatory)][string]$PackagePath, [Parameter(Mandatory)][string]$InstallerStub, [Parameter(Mandatory)][string]$DestinationPath)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/ReleaseTools.psm1" -Force
Assert-EglReleasePackage -PackagePath $PackagePath
$destination = [IO.Path]::GetFullPath($DestinationPath)
$temporary = $destination + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
try {
    $output = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $stub = [IO.File]::OpenRead([IO.Path]::GetFullPath($InstallerStub))
        try {
            if ($stub.ReadByte() -ne 77 -or $stub.ReadByte() -ne 90) { throw 'Installer stub must be a Windows executable.' }
            $stub.Position = 0
            $stub.CopyTo($output)
        } finally { $stub.Dispose() }
        $source = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
        try {
            $archive = [IO.Compression.ZipArchive]::new($output, [IO.Compression.ZipArchiveMode]::Create, $true)
            try {
                foreach ($entry in $source.Entries) {
                    $copy = $archive.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Optimal)
                    $copy.LastWriteTime = $entry.LastWriteTime
                    $input = $entry.Open()
                    $target = $copy.Open()
                    try { $input.CopyTo($target) } finally { $target.Dispose(); $input.Dispose() }
                }
            } finally { $archive.Dispose() }
        } finally { $source.Dispose() }
        $output.Flush($true)
    } finally { $output.Dispose() }
    Assert-EglReleasePackage -PackagePath $temporary
    [IO.File]::Move($temporary, $destination, $true)
    Write-Output "Built installer $destination"
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
