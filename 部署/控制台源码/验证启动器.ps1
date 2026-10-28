param([string]$CheckedDate = (Get-Date -Format 'yyyy-MM-dd'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot '检查资料'
$taskTemp = Join-Path ([IO.Path]::GetTempPath()) ('wb-launcher-check-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($taskTemp)
$taskChecks = New-Object 'System.Collections.Generic.List[string]'
$form = $null
function Assert-Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw ('FAILED: ' + $name) }
    $taskChecks.Add($name)
}
function Field([string]$name) { $formType.GetField($name, [Reflection.BindingFlags]'Instance,NonPublic').GetValue($form) }
function Reload { $formType.GetMethod('ReloadConnection', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($form, @()) | Out-Null }
function Snapshot([string]$name) {
    [Windows.Forms.Application]::DoEvents()
    $bitmap = New-Object Drawing.Bitmap($form.Width, $form.Height)
    try { $form.DrawToBitmap($bitmap, (New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height))); $bitmap.Save((Join-Path $taskOutput $name), [Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
}
try {
    $wbFixture = @{ listen='127.0.0.1:17863'; api_key='fixture-workbuddy-key' } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $taskTemp 'config.json'), $wbFixture)
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $taskRoot '部署\WorkBuddy控制台.exe')))
    $formType = $assembly.GetType('WorkBuddyLocalConsole.MainForm', $true)
    $constructor = $formType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic', $null, [Type[]]@([string]), $null)
    $form = $constructor.Invoke([object[]]@([string]$taskTemp))
    $form.ShowInTaskbar = $false
    $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object Drawing.Point(-3000,-3000)
    $form.Show()
    Assert-Check ((Field 'apiAddress').Text -eq 'http://127.0.0.1:17863/v1') 'WorkBuddy uses configured API address'
    Assert-Check ((Field 'apiKey').Text -eq 'fixture-workbuddy-key') 'WorkBuddy reads its own API key'
    Assert-Check ((Field 'apiKey').UseSystemPasswordChar) 'API key masked by default'
    Assert-Check ((Field 'apiAddress').ReadOnly -and (Field 'apiKey').ReadOnly) 'Connection fields are read-only'
    Assert-Check ((Field 'managementAddress').Text -eq 'http://127.0.0.1:17863/panel/') 'WorkBuddy management address matches API port'
    Assert-Check ((Field 'serviceSelector').Items.Count -eq 1) 'Only WorkBuddy remains'
    Snapshot '启动器界面-WorkBuddy.png'
    [IO.File]::WriteAllText((Join-Path $taskTemp 'config.json'), '{invalid')
    Reload
    Assert-Check ((Field 'apiKey').Text.Length -eq 0 -and -not (Field 'copyKeyButton').Enabled) 'Invalid config clears credentials'
    [IO.File]::Delete((Join-Path $taskTemp 'config.json'))
    Reload
    Assert-Check ((Field 'apiKey').Text.Length -eq 0 -and (Field 'messageLabel').Text.Contains('未找到')) 'Missing config guidance'
    # 用合成后台服务执行实际启停脚本，验证同一窗口可连续操作。
    [IO.File]::WriteAllText((Join-Path $taskTemp 'config.json'), $wbFixture)
    $managerDirectory = Join-Path $taskTemp '管理工具'
    $null = [IO.Directory]::CreateDirectory($managerDirectory)
    Copy-Item -LiteralPath (Join-Path $taskRoot '部署/管理工具/管理服务.ps1') -Destination (Join-Path $managerDirectory '管理服务.ps1')
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $fakeSource = Join-Path $taskTemp 'FixtureService.cs'
    [IO.File]::WriteAllText($fakeSource, 'class FixtureService { static void Main() { System.Threading.Thread.Sleep(120000); } }')
    & $compiler /nologo /target:exe (('/out:') + (Join-Path $taskTemp 'wb2api.exe')) $fakeSource
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compile failed' }
    $runManager = $formType.GetMethod('RunManager', [Reflection.BindingFlags]'Instance,NonPublic')
    foreach ($operation in @('Start','Start','Stop','Stop')) {
        $runManager.Invoke($form, @($operation)) | Out-Null
        Assert-Check ((Field 'openButton').Enabled -and (Field 'refreshButton').Enabled) ($operation + ' keeps management and refresh usable')
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not (Field 'startButton').Enabled -and [DateTime]::UtcNow -lt $deadline) {
            [Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 30
        }
        Assert-Check ((Field 'startButton').Enabled -and (Field 'stopButton').Enabled) ($operation + ' restores buttons without reopening')
        Assert-Check ((Field 'actionLabel').Text.Contains('命令已完成')) ($operation + ' completes successfully')
        $fixtureProcesses = @(Get-Process -Name wb2api -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $taskTemp 'wb2api.exe') })
        Assert-Check (($operation -eq 'Start' -and $fixtureProcesses.Count -eq 1) -or ($operation -eq 'Stop' -and $fixtureProcesses.Count -eq 0)) ($operation + ' has expected process count')
    }
    Add-Type -TypeDefinition @"
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
public static class PanelProbe {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern bool SetDllDirectory(string path);
    static TcpListener listener;
    public static int Start() {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var thread = new Thread(delegate() {
            try {
                while (true) {
                    using (var client = listener.AcceptTcpClient()) {
                        var stream = client.GetStream();
                        var buffer = new byte[8192];
                        stream.ReadTimeout = 3000;
                        stream.Read(buffer, 0, buffer.Length);
                        var body = Encoding.UTF8.GetBytes("<!doctype html><html><head><title>Fixture Panel</title></head><body><button id='probe' onclick='document.body.dataset.clicked=1'>ready</button></body></html>");
                        var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                        stream.Write(header, 0, header.Length);
                        stream.Write(body, 0, body.Length);
                    }
                }
            } catch { }
        });
        thread.IsBackground = true;
        thread.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    public static void Stop() { if (listener != null) listener.Stop(); }
}
"@
    $deploymentPath = Join-Path $taskRoot '部署'
    [PanelProbe]::SetDllDirectory($deploymentPath) | Out-Null
    foreach ($dependency in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')) {
        [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $deploymentPath $dependency))) | Out-Null
    }
    function Pump-Until([scriptblock]$condition, [int]$seconds=25) {
        $limit = [DateTime]::UtcNow.AddSeconds($seconds)
        while (-not (& $condition) -and [DateTime]::UtcNow -lt $limit) {
            [Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 25
        }
        if (-not (& $condition)) { throw 'Timed out waiting for embedded panel' }
    }
    function Get-Private($target, [string]$name) {
        $target.GetType().GetField($name, [Reflection.BindingFlags]'Instance,NonPublic').GetValue($target)
    }
    $probePort = [PanelProbe]::Start()
    [IO.File]::WriteAllText((Join-Path $taskTemp 'config.json'), (@{listen=('127.0.0.1:'+$probePort);api_key='fixture-key'} | ConvertTo-Json))
    (Field 'openButton').PerformClick()
    $panel = Field 'managementForm'
    Pump-Until { (Get-Private $panel 'PageLoaded') -or (Get-Private $panel 'Failure').Length -gt 0 }
    Assert-Check ((Get-Private $panel 'PageLoaded')) ('Embedded panel loads successfully: ' + (Get-Private $panel 'Failure'))
    $webview = Get-Private $panel 'view'
    Assert-Check ($webview.CoreWebView2.DocumentTitle -eq 'Fixture Panel') 'Embedded page renders expected document'
    $scriptTask = $webview.CoreWebView2.ExecuteScriptAsync("document.getElementById('probe').click(); document.body.dataset.clicked")
    Pump-Until { $scriptTask.IsCompleted }
    Assert-Check ($scriptTask.Result -eq '"1"') 'Embedded JavaScript button works'
    (Field 'openButton').PerformClick()
    Assert-Check ([object]::ReferenceEquals($panel, (Field 'managementForm'))) 'Repeated open reuses management window'
    Assert-Check ((Field 'startButton').Enabled -and (Field 'stopButton').Enabled -and (Field 'openButton').Enabled) 'Management window does not lock launcher buttons'
    $panel.Close()
    (Field 'openButton').PerformClick()
    $panel = Field 'managementForm'
    Pump-Until { (Get-Private $panel 'PageLoaded') -or (Get-Private $panel 'Failure').Length -gt 0 }
    Assert-Check ((Get-Private $panel 'PageLoaded')) 'Management window can close and reopen'
    $webview = Get-Private $panel 'view'
    $webview.CoreWebView2.Navigate('http://127.0.0.1:1/panel/')
    Pump-Until { (Get-Private $panel 'Failure').Length -gt 0 }
    Assert-Check (-not (Get-Private $panel 'PageLoaded') -and (Get-Private $panel 'Failure').Length -gt 0) 'Unavailable service reports a load failure'
    $panel.Close()
    [PanelProbe]::Stop()
    # 只读加载正式管理页，不填写凭证、不调用模型。
    $managementType = $assembly.GetType('WorkBuddyLocalConsole.ManagementForm', $true)
    $managementConstructor = $managementType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic', $null, [Type[]]@([string],[string]), $null)
    $realPanel = $managementConstructor.Invoke([object[]]@('http://127.0.0.1:7863/panel/', [string]$taskTemp))
    $realPanel.ShowInTaskbar = $false
    $realPanel.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $realPanel.Location = New-Object Drawing.Point(-3000,-3000)
    $realPanel.Show()
    Pump-Until { (Get-Private $realPanel 'PageLoaded') -or (Get-Private $realPanel 'Failure').Length -gt 0 }
    Assert-Check ((Get-Private $realPanel 'PageLoaded')) ('Real WorkBuddy management page loads in WebView2: ' + (Get-Private $realPanel 'Failure'))
    $realView = Get-Private $realPanel 'view'
    Assert-Check ($realView.CoreWebView2.DocumentTitle.Contains('WorkBuddy')) 'Real embedded management page has correct title'
    $captureFile = [IO.File]::Create((Join-Path $taskOutput '管理页内嵌验证.png'))
    try {
        $captureTask = $realView.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png, $captureFile)
        Pump-Until { $captureTask.IsCompleted }
        $captureTask.GetAwaiter().GetResult() | Out-Null
    } finally { $captureFile.Dispose() }
    $realPanel.Close()
    $report = @{ checked_date=$CheckedDate; launcher_version=$assembly.GetName().Version.ToString(); result='PASS'; checks=@($taskChecks); fixtures_only=$false; real_panel_read_only=$true; model_requests_sent=0; clipboard_changed=$false }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput ('启动器验证-' + $CheckedDate.Replace('-','') + '.json')) -Encoding UTF8
    'PASS: ' + $taskChecks.Count + ' checks; real management page verified read-only; no model requests or credential entry.'
} finally {
    if ($realPanel) { $realPanel.Dispose() }
    if ($panel) { $panel.Dispose() }
    if ($form) { $form.Dispose() }
    if ("PanelProbe" -as [type]) { [PanelProbe]::Stop() }
    if ([IO.File]::Exists((Join-Path $taskTemp 'config.json'))) { [IO.File]::Delete((Join-Path $taskTemp 'config.json')) }
    Get-Process -Name wb2api -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $taskTemp 'wb2api.exe') } | Stop-Process -ErrorAction SilentlyContinue
    # 临时目录含合成服务与日志，仅清理本次生成的 GUID 目录。
    if ($taskTemp.StartsWith([IO.Path]::GetTempPath()) -and (Split-Path $taskTemp -Leaf).StartsWith('wb-launcher-check-')) {
        for ($cleanupAttempt=0; $cleanupAttempt -lt 20; $cleanupAttempt++) {
            try { [IO.Directory]::Delete($taskTemp, $true); break } catch { Start-Sleep -Milliseconds 250 }
        }
    }
}



