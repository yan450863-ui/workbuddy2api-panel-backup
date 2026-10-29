# Windows 启动器 1.5.2

便携包在 [Releases](https://github.com/yan450863-ui/workbuddy2api-panel-backup/releases) 下载，完整使用说明见 [Windows-release-README.md](../scripts/Windows-release-README.md)。原始网关 MIT 版权与历史保持不变。

## 运行便携包

完整解压后双击 WorkBuddy控制台.exe。首次运行自动生成独立随机 API 密钥、config.json 和空数据目录；默认只监听 127.0.0.1:7863。点击“启动服务”及“打开管理页面”，使用复制的密钥登录并添加自己的账号。

需要 Windows 10/11 x64、.NET Framework 4.8 和 WebView2 Runtime。包内附 SDK DLL，系统运行时从 [微软](https://developer.microsoft.com/microsoft-edge/webview2/) 安装。电脑上的 Tailscale 已内置，无需另外安装；手机端仍需官方 Tailscale 客户端，登录同一账号。

关闭窗口不停止服务；关闭远程入口用“关闭远程”，停止网关用“停止服务”。电脑重启后按需手动开启。升级保留自己的 config.json、auths/ 和 data/，不要把这些目录上传。

## 构建

1. 网关：安装根目录 go.mod 要求的 Go，在仓库根目录执行 `go build -trimpath -ldflags "-s -w" -o 部署/wb2api.exe ./cmd/server`。
2. 远程组件：安装 Go 1.27.3 或更新版本，运行 `部署/控制台源码/远程组件/构建远程组件.ps1 -Test`，生成 `部署/管理工具/WorkBuddyRemote.exe` 并收集依赖许可证。它是独立 Go 模块。
3. WebView2 SDK：从 NuGet `Microsoft.Web.WebView2` 版本 `1.0.4314.35` 提取 `lib/net462/Microsoft.Web.WebView2.Core.dll`、`lib/net462/Microsoft.Web.WebView2.WinForms.dll`、`runtimes/win-x64/native/WebView2Loader.dll` 到部署目录。
4. 运行 `部署/控制台源码/构建控制台.ps1` 构建启动器。自行运行源码构建产物时，将根目录 config.example.json 复制到部署目录作为首次初始化模板；打包脚本会自动提供模板。
5. 安装 Python 3，在仓库根目录执行 `python scripts/package-windows.py --output 资料/发布包/v1.5.2`，生成经过白名单打包和本地已知凭据扫描的便携 ZIP。

## 验证范围

发布包已在独立临时目录解压，验证首次初始化、随机密钥隔离、已有配置保留、依赖文件及无账号管理页启动，共 11 项通过。原有启动器 33 项、局域网抽屉 23 项回归检查以及远程组件接口边界与启停检查已通过。

本地内置节点曾达到已授权 Running 状态；手机外网端到端验证未完成。发布和测试通过不代表上游渠道长期可用。

`验证发布包.ps1 -Archive <ZIP绝对路径>` 只使用临时随机密钥和空账号目录，不连接个人 Tailscale 节点。其他验证脚本有各自运行前提，执行前请阅读源码。
