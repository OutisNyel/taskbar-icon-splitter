# 发布到 Microsoft Edge 加载项商店

普通用户不应该从源码构建本项目。正式发布由两个独立产物组成：

- `TaskbarIconSplitter-Edge-<version>.zip`：上传到 Microsoft Edge Partner Center 的扩展包。
- `TaskbarIconSplitter-Setup-x64.exe`：放在 GitHub Release 中供首次使用页下载的 Companion 安装程序。

Companion 已包含 .NET 运行时，按当前 Windows 用户安装到 `%LOCALAPPDATA%\TaskbarIconSplitter`，并写入 Edge Native Messaging 注册。用户不需要管理员权限、Node.js、.NET 或任何 SDK。

## 首次发布前

1. 在 Partner Center 创建产品并取得最终的 32 位 Edge 扩展 ID。
2. 准备用于 Authenticode 的代码签名证书。未签名安装程序会触发 Windows SmartScreen 警告，不应作为正式商城下载提供。
3. 确认扩展版本与 `extension/manifest.json` 一致。

## 构建

开发机器需要 PowerShell 7、Node.js 20+、.NET 8 SDK 和 Inno Setup 6。这些都只是构建依赖，不是最终用户依赖。

```powershell
winget install --id JRSoftware.InnoSetup --exact
.\scripts\build-release.ps1 `
    -StoreExtensionId <Partner Center 中的扩展 ID> `
    -SigningCertificateThumbprint <代码签名证书指纹> `
    -RequireSignature
```

脚本会运行类型检查与全部测试，构建无 source map 的商店扩展，生成按用户安装的 Companion EXE，并输出 SHA-256 校验文件。产物位于 `artifacts\`。

如果只是本地验证安装包，可以省略签名参数；这种未签名产物不能当作正式商城下载发布。

## 上传顺序

1. 用 `TaskbarIconSplitter-Setup-x64.exe` 创建 GitHub Release，资产文件名必须保持不变；首次使用页使用固定的 `releases/latest/download` 地址。
2. 上传 `TaskbarIconSplitter-Edge-<version>.zip` 到 Partner Center。
3. 在商店资料中填写：
   - 网站：`https://github.com/OutisNyel/taskbar-icon-splitter`
   - 支持：`https://github.com/OutisNyel/taskbar-icon-splitter/issues`
   - 隐私政策：`https://github.com/OutisNyel/taskbar-icon-splitter/blob/main/PRIVACY.md`
4. 从商店安装审核包，确认首次使用页能下载 Companion，安装后能从“未连接”自动恢复为“已连接”。

Native Messaging 的 `allowed_origins` 必须包含最终商店扩展 ID。构建脚本同时保留 manifest 公钥派生的开发 ID，便于解压缩加载测试。
