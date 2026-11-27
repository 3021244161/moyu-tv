# Requires Windows PowerShell 5.1 or PowerShell 7 on 64-bit Windows.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $projectRoot '摸鱼电视机.exe'
$library = Join-Path $projectRoot 'lib'

if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The x64 .NET Framework compiler is missing. Install .NET Framework 4.8 on Windows.'
}

$references = @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll')
foreach ($file in ($references + 'WebView2Loader.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $library $file) -PathType Leaf)) {
        throw "Missing bundled WebView2 SDK component: lib\$file"
    }
}

$options = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/langversion:5',
    '/codepage:65001', "/out:$output",
    ('/win32manifest:' + (Join-Path $projectRoot 'build\app.manifest')),
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Configuration.dll', '/reference:System.Web.Extensions.dll'
)
$icon = Join-Path $projectRoot 'assets\tv.ico'
if (Test-Path -LiteralPath $icon -PathType Leaf) {
    $options += "/win32icon:$icon"
}
foreach ($reference in $references) {
    $options += '/reference:' + (Join-Path $library $reference)
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File | Sort-Object Name)
if ($sources.Count -eq 0) { throw 'No sources found in src.' }

& $compiler @options $sources.FullName
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\app.config') -Destination "$output.config" -Force
Write-Output "Built $output"
