# Codex 剩余用量

面向 Windows 10 22H2 和 Windows 11 x64 的 Codex 一周订阅用量悬浮胶囊。

## 使用

安装前请先安装并登录 Codex。运行 `CodexRemaining-Setup.exe` 后，悬浮胶囊会显示当前一周订阅额度的剩余百分比，并默认随 Windows 启动。

- 左键点击：展开或收起重置时间、更新时间
- 左键拖动：移动悬浮胶囊
- 右键菜单：立即刷新、切换开机自启、重置位置或退出
- 自动刷新：每 5 分钟一次；失败时保留上次成功数据并标记为过期

## 构建

需要 .NET 10 SDK 和 Inno Setup 6。执行：

```powershell
.\scripts\package.ps1
```

安装包输出到 `artifacts\CodexRemaining-Setup.exe`。

## 设计文档

- [产品规格](./docs/product-spec.md)
- [架构](./docs/architecture.md)
- [实施计划](./docs/implementation-plan.md)
- [领域词汇](./CONTEXT.md)
- [架构决策](./docs/adr/)
