# WorkBuddy2API Windows 便携版 1.5.2

这是社区保存的上游网关加本地 Windows 启动器，非 WorkBuddy 官方产品。网关版本为 1.13.0-panel+73a7560.local，启动器版本为 1.5.2，内置私人远程组件使用 Tailscale tsnet v1.104.4。

## 首次使用

1. 完整解压到可写目录，双击 WorkBuddy控制台.exe。不要在压缩包内直接运行。
2. 首次启动会生成随机 API 密钥和仅本机监听的 config.json；后续启动保留已有配置。
3. 点击“启动服务”→“打开管理页面”。在主窗口复制密钥登录，在管理页添加自己的账号。
4. 本机 Base URL 为 http://127.0.0.1:7863/v1；OpenAI API 路径为 /chat/completions。模型以管理页实际列表为准。

需要 Windows 10/11 x64、.NET Framework 4.8 和 Microsoft Edge WebView2 Runtime。包内已带 WebView2 SDK DLL，运行时仍需系统安装。微软官方下载：https://developer.microsoft.com/microsoft-edge/webview2/ 。程序未做 Authenticode 签名，可按发布页 SHA256SUMS 核对来源。

## 手机与远程连接

同一家庭网络：展开“局域网连接”，勾选并应用设置。Windows 请求管理员权限时确认，然后将显示的 API Base URL 填到手机；API Key 使用主窗口密钥。手机选 OpenAI，关闭 Response API。

在外面使用：点击“远程连接”→“开启远程”→“登录授权”。电脑端已内置 Tailscale 节点，无需另装客户端；手机安装官方 Tailscale，登录同一账号并开启连接，再复制远程窗口的 Base URL。授权网页无法在内嵌窗口登录时，可复制登录链接到自己的手机浏览器完成授权。

远程入口只接受节点所属本人账号和正确 API 密钥，只开放 /v1/ API，不开放管理页。电脑需保持联网、网关运行且不睡眠。局域网与远程连接相互独立。首次授权及手机实际链路需要使用者验证，项目未完成手机端到端验证。

## 关闭与升级

关闭启动器窗口不会停止网关或远程节点。需要停止时先点“关闭远程”和“停止服务”。电脑重启后按需手动启动，账号身份保存在本地。

升级前先停止相关程序，备份自己的 config.json、auths/、data/，再更新程序文件，保留个人数据；不要把一个包含新 config.json 的整目录覆盖到已有配置上。本发布包没有预置 config.json。

## 隐私、来源和许可

config.json、auths/、data/ 包含密钥、账号、远程节点身份和网页会话，不要上传或分享。本发布包不含这些个人数据。

上游快照：linguo2625469/workbuddy2api-panel 的 73a7560efc7a88ebd9865db4c71ae7f6d85859f8；上游于 2026-10-21 停止维护。源码与发布入口：https://github.com/yan450863-ui/workbuddy2api-panel-backup 。保留上游 MIT LICENSE；WebView2 与 Tailscale 及其依赖许可证见 licenses/。

非官方请求可能受上游限流、渠道校验或账号限制影响。新增功能与发布包不代表上游官方恢复维护，也不保证服务长期可用。
