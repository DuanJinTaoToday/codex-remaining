# 通过 Codex app-server 读取用量

软件发现本机已有的 `codex.exe`，独立启动并管理 `codex app-server` 连接，通过 `account/rateLimits/read` 方法获取订阅用量，并按 `windowDurationMins` 识别一周用量窗口；它不依附 Codex 桌面客户端是否正在运行。安装包不捆绑 Codex CLI，也不实现独立登录。相比直接读取认证文件、调用未封装的后端接口或识别界面截图，这一边界不接触凭据内容，且直接提供已用百分比与重置时间；代价是要求本机已经安装并登录 Codex，需要管理 app-server 子进程并适配其协议变化。
