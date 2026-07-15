# 使用 .NET 10 和 WPF 构建 Windows 客户端

客户端采用 C#、.NET 10 和 WPF，并以 `win-x64` 自包含方式打包进 `Setup.exe`。项目只需支持 Windows 10 22H2 与 Windows 11，WPF 能直接处理透明置顶窗口、多显示器和 Win32 全屏检测；相比 Tauri、Avalonia 或 Electron，它不承担本项目不需要的跨平台层或浏览器运行时。Codex 用量读取与界面保持分离，以便数据协议变化时只替换集成模块。
