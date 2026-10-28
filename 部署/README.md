# Windows 启动器源码

## 构建网关

安装 Go（版本要求见根目录 go.mod），在仓库根目录执行：

```powershell
go test ./...
go build -trimpath -ldflags "-s -w" -o 部署/wb2api.exe ./cmd/server
```

将根目录 config.example.json 复制为本目录 config.json，填写你自己的随机 API 密钥；仅本机使用时将 listen 设为 `127.0.0.1:7863`。真实配置、账号和数据已加入忽略规则，不应提交。

## 准备 WebView2

Windows 需安装 .NET Framework 4.8 和 Microsoft Edge WebView2 Runtime。

从微软 NuGet 包 `Microsoft.Web.WebView2` 版本 `1.0.4314.35` 提取以下三个文件到本目录：

- `lib/net462/Microsoft.Web.WebView2.Core.dll`
- `lib/net462/Microsoft.Web.WebView2.WinForms.dll`
- `runtimes/win-x64/native/WebView2Loader.dll`

包地址： https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4314.35/microsoft.web.webview2.1.0.4314.35.nupkg

## 构建和运行启动器

运行 `控制台源码/构建控制台.ps1`，生成本目录 `WorkBuddy控制台.exe` 和 `.exe.config`。随后双击程序。账号通过应用内管理页添加，不需要复制其他人的凭证。

手机接入时展开局域网连接抽屉，启用并应用设置；同意 Windows 管理员授权后，按抽屉显示的 Base URL 和 API 路径填写。不要把服务无保护地开放到公网。

## 回归脚本

`控制台源码/验证启动器.ps1` 和 `验证局域网抽屉.ps1` 使用临时合成服务验证界面与生命周期。前者还会只读加载本机已启动的管理页；执行前须创建根目录检查资料/，并允许本机 WebView2 Runtime 启动。不会替你登录真实账号或发出模型请求。
