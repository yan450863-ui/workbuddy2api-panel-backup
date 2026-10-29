param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixture = Join-Path $root ('.audit-cache/release-check-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture -Force
Expand-Archive -LiteralPath $Archive -DestinationPath $fixture
$package = (Get-ChildItem -LiteralPath $fixture -Directory | Select-Object -First 1).FullName
$checks = New-Object 'Collections.Generic.List[string]'
function Assert([bool]$ok,[string]$name) { if (-not $ok) { throw ('FAILED: '+$name) }; $checks.Add($name) }
Assert (-not (Test-Path -LiteralPath (Join-Path $package 'config.json'))) 'Archive contains no runtime config'
Assert (-not (Test-Path -LiteralPath (Join-Path $package 'auths'))) 'Archive contains no account directory'
Assert (-not (Test-Path -LiteralPath (Join-Path $package 'data'))) 'Archive contains no sessions or remote identity'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $package 'WorkBuddy控制台.exe')))
$method = $assembly.GetType('WorkBuddyLocalConsole.FirstRun',$true).GetMethod('Initialize',[Reflection.BindingFlags]'Static,NonPublic')
$method.Invoke($null,[object[]]@([string]$package))
$configPath = Join-Path $package 'config.json'
$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($config.api_key.Length -eq 43) 'First run creates a 256-bit random API key'
Assert ($config.listen -eq '127.0.0.1:7863') 'First run binds only loopback'
$before = [IO.File]::ReadAllText($configPath)
$method.Invoke($null,[object[]]@([string]$package))
Assert ([IO.File]::ReadAllText($configPath) -ceq $before) 'Existing config is preserved byte for byte'
$second = Join-Path $fixture 'second-install'
$null = New-Item -ItemType Directory -Path $second
Copy-Item -LiteralPath (Join-Path $package 'config.example.json') -Destination $second
$method.Invoke($null,[object[]]@([string]$second))
$other = Get-Content -LiteralPath (Join-Path $second 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($config.api_key -cne $other.api_key) 'Independent installs generate different keys'
Assert (Test-Path -LiteralPath (Join-Path $package '管理工具/WorkBuddyRemote.exe')) 'Embedded remote binary is included'
Assert (Test-Path -LiteralPath (Join-Path $package 'licenses/WebView2-LICENSE.txt')) 'Dependency notices are included'
$listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
$listener.Start(); $port=$listener.LocalEndpoint.Port; $listener.Stop()
$config.listen = '127.0.0.1:'+$port
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
$service = $null
try {
    $service = Start-Process -FilePath (Join-Path $package 'wb2api.exe') -ArgumentList @('-config','config.json') -WorkingDirectory $package -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $fixture 'stdout.log') -RedirectStandardError (Join-Path $fixture 'stderr.log')
    $code = 0
    for ($i=0;$i -lt 30;$i++) {
        Start-Sleep -Milliseconds 200
        try { $code=(Invoke-WebRequest -Uri ('http://127.0.0.1:'+$port+'/healthz') -UseBasicParsing -TimeoutSec 2).StatusCode; break } catch {
            if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 503) { $code=503; break }
        }
    }
    Assert ($code -eq 503) 'Fresh gateway reports unready health until user adds accounts'
    $panel = Invoke-WebRequest -Uri ('http://127.0.0.1:'+$port+'/panel/') -UseBasicParsing -TimeoutSec 5
    Assert ($panel.StatusCode -eq 200) 'Extracted gateway serves management page without account credentials'
    @{version=$assembly.GetName().Version.ToString();result='PASS';checks=@($checks);personal_credentials_used=$false;remote_phone_test_performed=$false} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $Archive) 'release-verification.json') -Encoding UTF8
    'PASS: '+$checks.Count+' clean portable release checks.'
} finally {
    if ($service -and -not $service.HasExited) { Stop-Process -Id $service.Id -ErrorAction SilentlyContinue }
    $resolved=[IO.Path]::GetFullPath($fixture)
    if ($resolved.StartsWith((Join-Path $root '.audit-cache')+[IO.Path]::DirectorySeparatorChar) -and (Split-Path $resolved -Leaf).StartsWith('release-check-')) { [IO.Directory]::Delete($resolved,$true) }
}
