param([Parameter(Mandatory)][string]$DirectoryPath)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Build.psm1" -Force
Assert-EglReleasePackage -DirectoryPath $DirectoryPath
