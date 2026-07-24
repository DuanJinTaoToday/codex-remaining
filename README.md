# Codex 剩余用量

面向 Windows 10 22H2 和 Windows 11 x64 的 Codex 用量悬浮胶囊。

## 使用

安装前请先安装并登录 Codex。运行 `CodexRemaining-Setup.exe` 后，悬浮胶囊会显示当前一周订阅额度的剩余百分比；展开后显示账号近 7 日 Token 和本机今日 Token，并默认随 Windows 启动。

- 左键点击：展开或收起一周进度、重置时间、Token 统计和更新时间
- 左键拖动：移动悬浮胶囊；靠近显示器边缘或角落时自动附着
- 右键菜单：立即刷新、选择 1/5/10 分钟自动刷新间隔、切换开机自启、选择小/中/大胶囊尺寸、重置位置或退出
- 位置恢复：保留尺寸和附着位置；显示器拔插或分辨率变化后自动迁移到可见工作区
- 自动刷新：默认每 5 分钟一次；各项失败时分别保留上次成功数据并标记为过期

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
