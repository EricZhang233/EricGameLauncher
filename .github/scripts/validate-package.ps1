param([Parameter(Mandatory)][string]$DirectoryPath)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/ReleaseTools.psm1" -Force
Assert-EglReleasePackage -DirectoryPath $DirectoryPath
