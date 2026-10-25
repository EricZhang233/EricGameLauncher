母法：https://github.com/EricZhang233/EricRepoRule —— 需要完全读取并遵守此仓库所有规范，本文件其余部分为本仓库私有规范。

---

## 本仓库私有规范

### 一、第 5 条补充：发布说明
- 编写规范以母法 `releasenoteguide.md` 为准，本仓库不复制规范正文。
- 本仓库的实际发布说明为根目录 `.releasenote.md`，发布工作流直接以其作为 Release 正文。

### 二、第 6 条补充：CLI 接入文档
- 发布目录根目录附带 `SKILL.md`，供 Agent 接入 CLI；完整接入文档在构建期嵌入可执行文件，通过 `skill` 命令输出，杜绝篡改。

### 三、第 7 条补充：Core 化落点
- `main.exe` 与 `cli.exe` 为零业务逻辑的纯壳层。
- `MainWindow` 仅负责 WinUI 渲染（XAML 控件、动画、对话框、窗口消息）。
- `CliService` 仅负责控制台 I/O（参数解析、输出格式化、颜色）。

### 四、第 8 条补充：文本管理
- 面向用户的文本统一走 `text.cs`，文本资源存储于 `text.yaml`。

### 五、编译流
- 本仓库本地不编译发行包，发行包在推送代码后由Github编译。本地一般只编debug包。

### 六、工程文件偏好
- 能用csproj就绝不要用sln，sln被视为不稳定的过程文件。

### 七、系统动画偏好
- 所有应用自定义动画（Storyboard、RenderTransform、计时驱动的透明度变化及显式 XAML 转场）开始前，读取 `UISettings.AnimationsEnabled`。
- 系统动画关闭时，不启动动画，直接设置目标视觉状态，并照常执行完成后的清理、状态更新和计时逻辑。
- 无需订阅动画设置变更事件；只在每次启动动画的入口检查当前值。
