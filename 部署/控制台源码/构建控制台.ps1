$ErrorActionPreference = 'Stop'
$deployDirectory = Split-Path -Parent $PSScriptRoot
$outputFile = Join-Path $deployDirectory 'WorkBuddy控制台.exe'
$temporaryOutput = 'LocalReverseProxyLauncher.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }

$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/codepage:65001',
    ('/out:' + $temporaryOutput),
    '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll',
    '/reference:../Microsoft.Web.WebView2.Core.dll', '/reference:../Microsoft.Web.WebView2.WinForms.dll',
    'Program.cs', 'ConnectionInfo.cs', 'ManagementForm.cs', 'LanConnection.cs'
)
Push-Location $PSScriptRoot
try {
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw ('C# compiler exited with code ' + $LASTEXITCODE) }
    $temporaryPath = Join-Path $PSScriptRoot $temporaryOutput
    if (Test-Path -LiteralPath $outputFile) { [System.IO.File]::Delete($outputFile) }
    [System.IO.File]::Move($temporaryPath, $outputFile)
} finally {
    Pop-Location
}
Get-Item -LiteralPath $outputFile | Select-Object FullName,Length
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup>
  <runtime><loadFromRemoteSources enabled="true" /></runtime>
</configuration>
'@ | Set-Content -LiteralPath ($outputFile + '.config') -Encoding UTF8
