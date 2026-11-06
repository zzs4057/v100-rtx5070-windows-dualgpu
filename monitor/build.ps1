$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "C# compiler not found: $csc"
}

$icon = Join-Path $root 'assets\app.ico'
$output = Join-Path $root 'V100Monitor.exe'
$source = Join-Path $root 'V100Monitor.cs'
$assembly = Join-Path $root 'AssemblyInfo.cs'
$manifest = Join-Path $root 'app.manifest'

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/codepage:65001',
    '/warn:4',
    ('/out:' + $output),
    ('/win32manifest:' + $manifest),
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Management.dll',
    '/reference:System.ServiceProcess.dll'
)

if (Test-Path -LiteralPath $icon) {
    $arguments += ('/win32icon:' + $icon)
}

$arguments += $assembly
$arguments += $source

& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Get-Item -LiteralPath $output |
    Select-Object FullName, Length, LastWriteTime |
    Format-List
