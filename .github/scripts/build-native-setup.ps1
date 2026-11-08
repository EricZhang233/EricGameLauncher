param([Parameter(Mandatory)][string]$Version, [switch]$BuildTests)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Invalid native installer version.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$intermediate = Join-Path $repo 'Components/setup/obj/native'
$output = Join-Path $repo 'Components/setup/bin/native'
New-Item -ItemType Directory -Path $intermediate, $output -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $visualStudio) { throw 'Visual C++ x64 build tools are required.' }
Import-Module (Join-Path $visualStudio 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $visualStudio -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
$sourceArchive = Join-Path $intermediate 'miniz-3.1.2.zip'
$sourceHash = 'f0446d863f9c19926ad9483c523fdc42e42b8d4a6a431d27e09d49c79a140d9a'
if (-not (Test-Path -LiteralPath $sourceArchive) -or (Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceHash) {
    Invoke-WebRequest 'https://github.com/richgel999/miniz/releases/download/3.1.2/miniz-3.1.2.zip' -OutFile $sourceArchive -TimeoutSec 60
}
if ((Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceHash) { throw 'miniz source integrity check failed.' }
$thirdParty = Join-Path $intermediate 'miniz'
if (-not (Test-Path -LiteralPath (Join-Path $thirdParty 'miniz.h'))) { [IO.Compression.ZipFile]::ExtractToDirectory($sourceArchive, $thirdParty) }
$versionHeader = Join-Path $intermediate 'Version.h'
[IO.File]::WriteAllText($versionHeader, "#pragma once`n#define EGL_VERSION L`"$Version`"`n", [Text.UTF8Encoding]::new($false))
$common = @('/nologo', '/std:c++20', '/O1', '/GL', '/Gy', '/Gw', '/MT', '/EHsc', '/utf-8', '/W4', '/WX', '/DUNICODE', '/D_UNICODE', '/D_WIN32_WINNT=0x0A00', '/DWINVER=0x0A00', '/D_CRT_SECURE_NO_WARNINGS', "/FI$versionHeader", "/I$thirdParty", '/DMINIZ_NO_ARCHIVE_WRITING_APIS', '/DMINIZ_NO_ZLIB_APIS', '/DMINIZ_NO_STDIO')
function Compile([string]$Source, [string]$Name, [string[]]$Extra = @()) {
    $object = Join-Path $intermediate "$Name.obj"
    & cl.exe @common @Extra /c (Join-Path $repo $Source) "/Fo$object" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Native compilation failed: $Source" }
    return $object
}
function Build-Resource([string]$Name, [string]$Uninstaller) {
    $resource = Join-Path $intermediate "$Name.rc"
    $fileVersion = if ($Version.Split('.').Count -eq 3) { $Version + '.0' } else { $Version }
    $lines = @(
        '#include <windows.h>',
        ('100 ICON "' + (Join-Path $repo 'ico.ico').Replace('\', '\\') + '"'),
        ('101 RCDATA "' + (Join-Path $repo 'text.yaml').Replace('\', '\\') + '"'),
        ('103 RCDATA "' + (Join-Path $thirdParty 'LICENSE').Replace('\', '\\') + '"'),
        ('1 24 "' + (Join-Path $repo 'Components/setup/setup.manifest').Replace('\', '\\') + '"'),
        '1 VERSIONINFO', ('FILEVERSION ' + $fileVersion.Replace('.', ',')), ('PRODUCTVERSION ' + $fileVersion.Replace('.', ',')),
        'FILEOS VOS_NT_WINDOWS32', 'FILETYPE VFT_APP', 'BEGIN', 'BLOCK "StringFileInfo"', 'BEGIN', 'BLOCK "040904B0"', 'BEGIN',
        'VALUE "CompanyName", "EricSoft\0"', ('VALUE "FileDescription", "EricGameLauncher ' + $Name + '\0"'),
        ('VALUE "FileVersion", "' + $fileVersion + '\0"'), ('VALUE "ProductVersion", "' + $Version + '\0"'),
        'VALUE "ProductName", "EricGameLauncher\0"', 'END', 'END', 'BLOCK "VarFileInfo"', 'BEGIN', 'VALUE "Translation", 0x409, 1200', 'END', 'END'
    )
    if ($Uninstaller) { $lines += '102 RCDATA "' + $Uninstaller.Replace('\', '\\') + '"' }
    [IO.File]::WriteAllLines($resource, $lines, [Text.UTF8Encoding]::new($false))
    $compiled = Join-Path $intermediate "$Name.res"
    & rc.exe /nologo /c65001 "/fo$compiled" $resource | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Native resource compilation failed: $Name" }
    return $compiled
}
function Link([string]$Name, [string[]]$Objects, [string]$Resource, [string]$Subsystem = 'WINDOWS') {
    $destination = Join-Path $output "$Name.exe"
    & link.exe /NOLOGO /LTCG /OPT:REF /OPT:ICF /DYNAMICBASE /HIGHENTROPYVA /NXCOMPAT /GUARD:CF /MACHINE:X64 "/SUBSYSTEM:$Subsystem" /MANIFEST:NO "/OUT:$destination" @Objects $Resource kernel32.lib user32.lib gdi32.lib shell32.lib ole32.lib oleaut32.lib advapi32.lib propsys.lib uuid.lib bcrypt.lib comctl32.lib dwmapi.lib uxtheme.lib | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Native link failed: $Name" }
    return $destination
}
$shared = Compile 'Components/setup.core/Installation.cpp' 'Installation'
$window = Compile 'Components/setup/Window.cpp' 'Window'
$uninstall = Compile 'Components/setup.core/Uninstaller.cpp' 'Uninstaller'
$uninstallEntry = Compile 'Components/setup/Entry.cpp' 'UninstallEntry' @('/DEGL_UNINSTALL')
$uninstallResource = Build-Resource 'Uninstall' ''
$uninstallPath = Link 'EricGameLauncher.Uninstall' @($shared, $window, $uninstall, $uninstallEntry) $uninstallResource
$compression = Join-Path $intermediate 'miniz.obj'
& cl.exe /nologo /O1 /GL /Gy /Gw /MT /TC /W0 /D_CRT_SECURE_NO_WARNINGS /DMINIZ_NO_ARCHIVE_WRITING_APIS /DMINIZ_NO_ZLIB_APIS /DMINIZ_NO_STDIO /c (Join-Path $thirdParty 'miniz.c') "/Fo$compression"
if ($LASTEXITCODE -ne 0) { throw 'Native ZIP library compilation failed.' }
$install = Compile 'Components/setup.core/Installer.cpp' 'Installer'
$entry = Compile 'Components/setup/Entry.cpp' 'SetupEntry'
$resource = Build-Resource 'Setup' $uninstallPath
$installerPath = Link 'EricGameLauncher.Setup' @($shared, $window, $install, $entry, $compression) $resource
if ($BuildTests) {
    $tests = Compile 'Test/Installation/InstallationTests.cpp' 'InstallationTests'
    $null = Link 'EricGameLauncher.InstallationTests' @($shared, $install, $uninstall, $tests, $compression) $resource 'CONSOLE'
}
Write-Host "Native installer: $installerPath ($((Get-Item -LiteralPath $installerPath).Length) bytes)"
Write-Host "Native uninstaller: $uninstallPath ($((Get-Item -LiteralPath $uninstallPath).Length) bytes)"
