param([string]$CheckedDate = (Get-Date -Format 'yyyy-MM-dd'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixture = Join-Path $root ('.audit-cache/lan-drawer-check-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $fixture '管理工具') -Force
$checks = [Collections.Generic.List[string]]::new()
$form = $null
Add-Type -TypeDefinition @"
public static class FirewallProbe {
    public static string Error;
    public static int Calls;
    public static string Apply(bool enabled) { Calls++; return Error; }
}
"@
function Assert([bool]$value, [string]$name) { if (-not $value) { throw ('FAILED: ' + $name) }; $checks.Add($name) }
function Field([string]$name) { $type.GetField($name, [Reflection.BindingFlags]'Instance,NonPublic').GetValue($form) }
function Invoke-Form([string]$name, [object[]]$arguments=@()) { $type.GetMethod($name, [Reflection.BindingFlags]'Instance,NonPublic').Invoke($form, $arguments) | Out-Null }
function Wait-Ready {
    $limit = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Field 'startButton').Enabled -and [DateTime]::UtcNow -lt $limit) {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }
    Assert ((Field 'startButton').Enabled -and (Field 'lanApplyButton').Enabled) 'Settings restore controls without reopening'
}
function Fixture-Processes { @(Get-Process -Name wb2api -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $fixture 'wb2api.exe') }) }
try {
    $configPath = Join-Path $fixture 'config.json'
    [IO.File]::WriteAllText($configPath, '{"listen":"127.0.0.1:17863","api_key":"fixture-key","custom":{"preserve":123}}')
    Copy-Item -LiteralPath (Join-Path $root '部署/管理工具/管理服务.ps1') -Destination (Join-Path $fixture '管理工具/管理服务.ps1')
    $fakeSource = Join-Path $fixture 'FixtureService.cs'
    [IO.File]::WriteAllText($fakeSource, 'class FixtureService { static void Main() { System.Threading.Thread.Sleep(120000); } }')
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    & $compiler /nologo /target:exe (('/out:') + (Join-Path $fixture 'wb2api.exe')) $fakeSource
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compile failed' }
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root '部署/WorkBuddy控制台.exe')))
    $type = $assembly.GetType('WorkBuddyLocalConsole.MainForm', $true)
    $constructor = $type.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic', $null, [Type[]]@([string],[Func[bool,string]]), $null)
    $apply = [Delegate]::CreateDelegate([Func[bool,string]], [FirewallProbe].GetMethod('Apply'))
    $form = $constructor.Invoke([object[]]@([string]$fixture,$apply))
    $form.ShowInTaskbar = $false
    $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object Drawing.Point(-3000,-3000)
    $form.Show()
    Assert (-not (Field 'lanDrawer').Visible -and $form.ClientSize.Width -eq 760) 'Drawer starts collapsed'
    Invoke-Form 'ToggleLanDrawer'
    Assert ((Field 'lanDrawer').Visible -and $form.ClientSize.Width -eq 1140) 'Drawer opens on the right'
    Assert ((Field 'lanBaseUrl').Text.EndsWith(':17863/v1') -and -not (Field 'lanBaseUrl').Text.Contains('0.0.0.0')) 'Phone Base URL uses network IP and configured port'
    Assert ((Field 'lanApiPath').Text -eq '/chat/completions') 'Drawer shows Chat Completions path'
    Assert ((Field 'lanBaseUrl').ReadOnly -and (Field 'lanApiPath').ReadOnly) 'Connection fields are read-only'
    Invoke-Form 'RunManager' @('Start')
    Wait-Ready
    Assert (@(Fixture-Processes).Count -eq 1) 'Synthetic gateway started'
    (Field 'lanEnabled').Checked = $true
    (Field 'lanApplyButton').PerformClick()
    Wait-Ready
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    Assert ($config.listen -eq '0.0.0.0:17863') 'Enable persists LAN listener'
    Assert ($config.api_key -eq 'fixture-key' -and $config.custom.preserve -eq 123) 'Changing listener preserves key and unknown fields'
    Assert (@(Fixture-Processes).Count -eq 1) 'Running gateway restarts once after enabling'
    Assert ((Field 'lanEnabled').Checked) 'Saved enabled state is reloaded'
    $bitmap = New-Object Drawing.Bitmap($form.Width,$form.Height)
    try {
        $form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height)))
        $bitmap.Save((Join-Path $root '检查资料/启动器界面-局域网抽屉.png'),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    (Field 'lanEnabled').Checked = $false
    (Field 'lanApplyButton').PerformClick()
    Wait-Ready
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    Assert ($config.listen -eq '127.0.0.1:17863') 'Disable restores loopback listener'
    Assert (@(Fixture-Processes).Count -eq 1) 'Disabling keeps local gateway running'
    $before = [IO.File]::ReadAllText($configPath)
    [FirewallProbe]::Error = '已取消 Windows 管理员授权，设置未更改。'
    (Field 'lanEnabled').Checked = $true
    (Field 'lanApplyButton').PerformClick()
    Wait-Ready
    Assert ([IO.File]::ReadAllText($configPath) -eq $before) 'Cancelled authorization preserves config bytes'
    Assert ((Field 'lanStatus').Text.Contains('已取消')) 'Cancellation displays inline feedback'
    Assert (@(Fixture-Processes).Count -eq 1) 'Cancellation does not stop gateway'
    Invoke-Form 'RunManager' @('Stop')
    Wait-Ready
    [FirewallProbe]::Error = $null
    (Field 'lanEnabled').Checked = $true
    (Field 'lanApplyButton').PerformClick()
    Wait-Ready
    Assert (@(Fixture-Processes).Count -eq 0) 'Changing LAN settings does not start a stopped gateway'
    Invoke-Form 'ToggleLanDrawer'
    Assert (-not (Field 'lanDrawer').Visible -and $form.ClientSize.Width -eq 760) 'Drawer collapses to original layout'
    @{checked_date=$CheckedDate;version=$assembly.GetName().Version.ToString();result='PASS';checks=@($checks);real_firewall_modified=$false;real_gateway_restarted=$false} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root ('检查资料/局域网抽屉验证-'+$CheckedDate.Replace('-','')+'.json')) -Encoding UTF8
    'PASS: ' + $checks.Count + ' drawer and lifecycle checks; only synthetic firewall and gateway used.'
} finally {
    if ($form) { $form.Dispose() }
    Fixture-Processes | Stop-Process -ErrorAction SilentlyContinue
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ($resolved.StartsWith((Join-Path $root '.audit-cache')+[IO.Path]::DirectorySeparatorChar) -and (Split-Path $resolved -Leaf).StartsWith('lan-drawer-check-')) {
        [IO.Directory]::Delete($resolved, $true)
    }
}

