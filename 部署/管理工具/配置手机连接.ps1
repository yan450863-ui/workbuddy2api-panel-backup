param(
    [ValidateSet('Enable','Disable')][string]$Action = 'Enable',
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $powershell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Action ' + $Action
    if ($Quiet) { $arguments += ' -Quiet' }
    Start-Process -FilePath $powershell -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden
    exit
}
$deployDirectory = Split-Path -Parent $PSScriptRoot
$resultDirectory = Join-Path $deployDirectory 'data'
$null = New-Item -ItemType Directory -Path $resultDirectory -Force
$resultPath = Join-Path $resultDirectory 'lan-settings-result.json'
function Save-Result([bool]$ok, [string]$errorMessage) {
    @{ok=$ok;error=$errorMessage;action=$Action} | ConvertTo-Json -Compress | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
try {
    $gateway = Join-Path $deployDirectory 'wb2api.exe'
    if (!(Test-Path -LiteralPath $gateway)) { throw '未找到本项目的 wb2api.exe。' }
    $config = Get-Content -LiteralPath (Join-Path $deployDirectory 'config.json') -Raw | ConvertFrom-Json
    if ($Action -eq 'Enable' -and [string]::IsNullOrWhiteSpace($config.api_key)) { throw '请先设置 API 密钥，再开启手机连接。' }
    if ($config.listen -notmatch ':(\d+)$') { throw '无法读取当前网关端口。' }
    $port = [int]$Matches[1]
    $name = 'WorkBuddy2API-LAN-7863'
    $existing = Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
    $ip = ''
    if ($Action -eq 'Disable') {
        if ($null -ne $existing) { Disable-NetFirewallRule -Name $name | Out-Null }
    } else {
        $network = Get-NetIPConfiguration | Where-Object {
            $null -ne $_.IPv4DefaultGateway -and @($_.IPv4Address | Where-Object {
                $_.IPAddress -match '^192\.168\.|^10\.|^172\.(1[6-9]|2[0-9]|3[01])\.'
            }).Count -gt 0
        } | Select-Object -First 1
        if ($null -eq $network) { throw '没有找到局域网连接，请连接家庭 Wi-Fi 或网线后再试。' }
        $ip = @($network.IPv4Address | Where-Object {
            $_.IPAddress -match '^192\.168\.|^10\.|^172\.(1[6-9]|2[0-9]|3[01])\.'
        })[0].IPAddress
        $options = @{Name=$name;Enabled='True';Direction='Inbound';Action='Allow';Profile='Any';Protocol='TCP';LocalPort=$port;LocalAddress=$ip;RemoteAddress='LocalSubnet';InterfaceAlias=$network.InterfaceAlias;Program=$gateway}
        if ($null -ne $existing) {
            Set-NetFirewallRule @options | Out-Null
        } else {
            New-NetFirewallRule @options -DisplayName 'WorkBuddy2API 手机局域网连接' | Out-Null
        }
    }
    Save-Result $true ''
    if (-not $Quiet) {
        Add-Type -AssemblyName System.Windows.Forms
        $message = if ($Action -eq 'Disable') { '局域网防火墙放行已关闭。' } else {
            '防火墙设置完成。软件的“局域网连接”开关须保持开启。' + [Environment]::NewLine + 'API Base URL: http://' + $ip + ':' + $port + '/v1' + [Environment]::NewLine + 'API 路径: /chat/completions'
        }
        [Windows.Forms.MessageBox]::Show($message, 'WorkBuddy 手机连接') | Out-Null
    }
    exit 0
} catch {
    Save-Result $false $_.Exception.Message
    if (-not $Quiet) {
        Add-Type -AssemblyName System.Windows.Forms
        [Windows.Forms.MessageBox]::Show($_.Exception.Message, '手机连接设置失败') | Out-Null
    }
    exit 1
}
