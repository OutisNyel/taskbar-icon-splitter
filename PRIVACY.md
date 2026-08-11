# Taskbar Icon Splitter 隐私政策

生效日期：2026 年 8 月 11 日

Taskbar Icon Splitter 由 Microsoft Edge 扩展和本机 Native Host 组成。本政策说明它们为了按网站整理窗口、设置任务栏身份和显示网站图标而访问、保存及传输的数据。

## 处理的数据

扩展会在本机访问以下信息：

- 普通 Edge 窗口中 HTTP(S) 标签页的 URL、网站图标 URL、标签 ID、窗口 ID、活动状态和置顶状态。
- 从 URL 计算出的注册域名，例如将 `www.github.com` 和 `gist.github.com` 归为 `github.com`。
- Native Host 为设置和恢复任务栏身份而使用的 Edge 窗口句柄、原始窗口身份及图标句柄。
- 各处理阶段的耗时、样本数和错误信息。

扩展不会处理 InPrivate 窗口、置顶标签、Edge 内部页或非 HTTP(S) 页面。

## 数据用途

上述数据只用于以下用户可见功能：

- 判断标签页应属于哪个网站窗口，并在 Edge 窗口之间移动标签页。
- 为网站窗口设置独立的 Windows 任务栏身份和图标。
- 在扩展弹窗中显示连接状态、受管窗口数量和本机性能统计。
- 诊断 Native Host 连接、窗口关联和图标处理问题。

项目不包含账号系统、广告、分析 SDK 或遥测服务。开发者不会接收、出售、出租或共享用户的浏览记录。

## 网络请求

为了取得网站图标，Native Host 可能请求：

- `https://<注册域名>/favicon.ico`；
- Edge 为当前标签页报告的 HTTP(S) favicon URL。

这些请求不携带 Edge 的登录 Cookie，也不会把完整浏览历史发送给开发者。和普通网络请求一样，目标网站或其图标 CDN 可能看到请求 URL、IP 地址、User-Agent 和时间。图标下载失败时，程序会在本机生成包含域名首字符的占位图。

除获取 favicon 外，扩展和 Native Host 不会把标签 URL、域名、窗口信息或性能统计发送到外部服务器。

## 本地保存

- Edge 的 `chrome.storage.local` 保存启用状态和累计耗时统计。
- Edge 的 `chrome.storage.session` 在当前浏览器会话内保存域名窗口绑定；其中可能包含域名、favicon URL、窗口 ID 和本机窗口句柄。浏览器会话结束后不会恢复这些句柄。
- `%LOCALAPPDATA%\TaskbarIconSplitter\icons` 保存网站图标缓存。
- `%LOCALAPPDATA%\TaskbarIconSplitter\logs` 保存 Native Host 诊断日志。日志可能包含注册域名、窗口句柄、耗时和错误文本，不包含网页正文、表单内容或 Cookie。日志超过 2 MiB 后会轮换为一个历史文件。

这些数据不会通过 Taskbar Icon Splitter 同步到云端。

## 权限用途

- `tabs`：读取标签页 URL、置顶状态和 favicon，并把标签页移动到正确窗口。
- `windows`：读取、创建、聚焦和管理普通 Edge 窗口。
- `nativeMessaging`：与本机 Native Host 通信，以使用 Windows 任务栏 API。
- `storage`：在本机保存开关、性能统计和当前会话的窗口绑定。

扩展只申请实现上述功能所需的权限。

## 用户控制与删除

用户可以随时在扩展弹窗中关闭“自动按域名拆分”。关闭后，扩展停止移动标签页并恢复仍受管窗口的默认 Edge 身份，但不会自动合并已经拆开的窗口。

要删除数据：

1. 从 `edge://extensions` 移除 Taskbar Icon Splitter，以删除 Edge 保存的扩展数据。
2. 运行仓库中的 `scripts\uninstall.ps1`，以删除 Native Messaging 注册、本机图标缓存和日志。
3. 也可以在完全退出 Edge 后手动删除 `%LOCALAPPDATA%\TaskbarIconSplitter`。

## 儿童隐私

Taskbar Icon Splitter 不面向儿童，不会主动收集年龄或身份信息。

## 政策更新与联系

功能或数据处理方式发生变化时，本政策会随项目更新并修改生效日期。问题或隐私请求可通过 [GitHub Issues](https://github.com/OutisNyel/taskbar-icon-splitter/issues) 提交；请勿在公开 Issue 中粘贴浏览记录、日志中的私人内容或其他敏感信息。
