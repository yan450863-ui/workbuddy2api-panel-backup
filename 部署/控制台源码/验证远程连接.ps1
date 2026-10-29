$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$deploy = Join-Path $root '部署'
$checks = New-Object 'Collections.Generic.List[string]'
function Assert([bool]$ok, [string]$name) { if (-not $ok) { throw ('FAILED: ' + $name) }; $checks.Add($name) }
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $deploy 'WorkBuddy控制台.exe')))
$remote = $assembly.GetType('WorkBuddyLocalConsole.RemoteConnection', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
function Call([string]$name) { $remote.GetMethod($name,$flags).Invoke($null,[object[]]@([string]$deploy)) }
function State { Call 'Load' }
function Value($object,[string]$name) { $object.GetType().GetField($name).GetValue($object) }
$before = (Get-FileHash -LiteralPath (Join-Path $deploy 'config.json')).Hash
$form = $null
try {
    Call 'Start'
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    do {
        Start-Sleep -Milliseconds 500
        $state = State
    } while ((Value $state 'LoginUrl') -eq '' -and (Value $state 'Address') -eq '' -and [DateTime]::UtcNow -lt $deadline)
    Assert (Value $state 'Running') 'Embedded node starts without installing Tailscale'
    Assert (((Value $state 'LoginUrl') -ne '') -or ((Value $state 'Address') -ne '')) 'Node reaches official authentication or connected state'
    Call 'Start'
    $exe = Join-Path $deploy '管理工具/WorkBuddyRemote.exe'
    Assert (@(Get-Process -Name WorkBuddyRemote | Where-Object { $_.Path -eq $exe }).Count -eq 1) 'Repeated enable does not duplicate the node'
    Assert (-not (Test-Path 'C:\Program Files\Tailscale\tailscale.exe')) 'No system Tailscale client installed'
    Assert (@(Get-NetTCPConnection -LocalPort 17863 -State Listen -ErrorAction SilentlyContinue).Count -eq 0) 'No public or LAN TCP listener on remote port'
    $type = $assembly.GetType('WorkBuddyLocalConsole.RemoteConnectionForm', $true)
    $form = $type.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic',$null,[Type[]]@([string]),$null).Invoke([object[]]@([string]$deploy))
    $form.ShowInTaskbar = $false
    $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object Drawing.Point(-3000,-3000)
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    Assert $form.Visible 'Remote window opens independently'
    $bitmap = New-Object Drawing.Bitmap($form.Width,$form.Height)
    try {
        $form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height)))
        $bitmap.Save((Join-Path $root '检查资料/启动器界面-内置远程.png'),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    Call 'Stop'
    Assert (-not (Value (State) 'Running')) 'Disable stops only the embedded node'
    Call 'Start'
    Assert (Value (State) 'Running') 'Node restarts with persisted identity'
    Assert ((Get-FileHash -LiteralPath (Join-Path $deploy 'config.json')).Hash -eq $before) 'Remote lifecycle preserves gateway config and API key'
    @{date=(Get-Date -Format 'yyyy-MM-dd');version=$assembly.GetName().Version.ToString();checks=@($checks);result='PASS';phone_verified=$false;awaiting_personal_login=$true} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root '检查资料/内置远程验证-20261028.json') -Encoding UTF8
    'PASS: ' + $checks.Count + ' embedded remote checks; phone authorization remains pending.'
} finally {
    if ($form) { $form.Dispose() }
}
