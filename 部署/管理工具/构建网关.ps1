param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$go = Join-Path $projectDirectory '.audit-cache/go/bin/go.exe'
if (!(Test-Path -LiteralPath $go)) { throw '未找到项目缓存中的 Go 工具链。' }
$buildDirectory = Join-Path $projectDirectory '.audit-cache/gateway-build'
$null = New-Item -ItemType Directory -Path $buildDirectory -Force
$environmentNames = @('GOROOT','GOCACHE','GOMODCACHE','GOTMPDIR','TEMP','TMP')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $projectDirectory
try {
    $env:GOROOT = Join-Path $projectDirectory '.audit-cache/go'
    $env:GOCACHE = Join-Path $projectDirectory '.audit-cache/build'
    $env:GOMODCACHE = Join-Path $projectDirectory '.audit-cache/modules'
    $env:GOTMPDIR = Join-Path $buildDirectory 'tmp'
    $null = New-Item -ItemType Directory -Path $env:GOTMPDIR -Force
    $env:TEMP = $env:GOTMPDIR
    $env:TMP = $env:GOTMPDIR
    if ($Test) {
        & $go test -count=1 ./...
        if ($LASTEXITCODE -ne 0) { throw 'Go 测试失败，未继续构建。' }
    }
    $output = Join-Path $buildDirectory 'wb2api.exe'
    & $go build -trimpath -ldflags '-s -w' -o $output ./cmd/server
    if ($LASTEXITCODE -ne 0) { throw 'Go 编译失败。' }
    Get-Item -LiteralPath $output | Select-Object FullName,Length
    Get-FileHash -LiteralPath $output -Algorithm SHA256
} finally {
    Pop-Location
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
