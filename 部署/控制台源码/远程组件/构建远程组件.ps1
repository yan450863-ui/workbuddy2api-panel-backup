param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$cachedGo = Join-Path $root '.audit-cache/go'
if (Test-Path -LiteralPath (Join-Path $cachedGo 'bin/go.exe')) {
    $env:GOROOT = $cachedGo
    $go = Join-Path $cachedGo 'bin/go.exe'
} else {
    $go = (Get-Command go -ErrorAction Stop).Source
}
$env:GOPATH = Join-Path $root '.audit-cache/remote-go'
$env:GOMODCACHE = Join-Path $env:GOPATH 'pkg/mod'
$env:GOCACHE = Join-Path $env:GOPATH 'build'
$env:GOPROXY = 'https://goproxy.cn,direct'
$env:TEMP = Join-Path $root '.audit-cache'
$env:TMP = $env:TEMP
$null = New-Item -ItemType Directory -Path $env:TEMP -Force
$output = Join-Path $root '部署/管理工具/WorkBuddyRemote.exe'
Push-Location $PSScriptRoot
try {
    if ($Test) {
        & $go test ./...
        if ($LASTEXITCODE -ne 0) { throw 'Remote component tests failed' }
    }
    & $go build -trimpath -ldflags '-s -w -H=windowsgui' -o $output .
    if ($LASTEXITCODE -ne 0) { throw 'Remote component build failed' }
    $licenses = Join-Path $root '部署/管理工具/远程组件许可证'
    $null = New-Item -ItemType Directory -Path $licenses -Force
    $modules = & $go list -m -f '{{.Path}}|{{.Version}}|{{.Dir}}' all
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate dependency licenses' }
    foreach ($line in $modules) {
        $parts = $line.Split('|')
        if ($parts.Count -lt 3 -or -not $parts[2] -or $parts[0] -eq 'workbuddy-local/remote') { continue }
        $files = @(Get-ChildItem -LiteralPath $parts[2] -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|PATENTS|AUTHORS)' })
        if ($files.Count -gt 0) {
            $target = Join-Path $licenses (($parts[0] + '@' + $parts[1]) -replace '[^a-zA-Z0-9._-]', '_')
            $null = New-Item -ItemType Directory -Path $target -Force
            $files | Copy-Item -Destination $target
        }
    }
    Copy-Item -LiteralPath (Join-Path $env:GOMODCACHE 'tailscale.com@v1.104.4/licenses') -Destination (Join-Path $licenses 'tailscale-third-party') -Recurse -Force
    $modules | ForEach-Object { ($_.Split('|')[0..1] -join ' ') } | Set-Content -LiteralPath (Join-Path $licenses '依赖清单.txt') -Encoding UTF8
    Get-Item -LiteralPath $output | Select-Object Name,Length
} finally { Pop-Location }
