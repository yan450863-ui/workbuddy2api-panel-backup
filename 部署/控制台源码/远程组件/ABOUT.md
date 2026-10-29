# 内置私人远程组件

- main.go：使用 Tailscale 官方 tsnet 库运行独立用户态节点，向本人账号开放 /v1/ API，转发到电脑回环地址；不开放管理页。
- main_test.go：验证账号边界、API 密钥、管理路径隔离及密钥更换。
- go.mod、go.sum：固定依赖及校验值。
- 构建远程组件.ps1：构建部署/管理工具/WorkBuddyRemote.exe，并收集依赖许可证。

认证状态保存在部署/data/private-remote/，不要分享或上传。未使用系统级 Tailscale 服务或虚拟网卡。
