<div align="center">
  <img src="ico.ico" />
</div>
<div align="center">

# Eric Game Launcher

**让游戏启动回归纯粹与极速**

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/UI-WinUI%203-0078D4?logo=windows)](https://github.com/microsoft/microsoft-ui-xaml)
[![Windows 11 Ready](https://img.shields.io/badge/Style-Windows%2011-blue?logo=windows11)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)

[功能特性](#功能特性) • [技术亮点](#技术亮点) • [快速开始](#快速开始) • [开发指南](#开发指南)

<div align="center">
  <img src="readme_res\readmescrsoot1.png" width="100%" />
  <img src="readme_res\readmescrsoot2.png" width="100%" />
</div>

</div>

---

## 简介

**Eric Game Launcher** 是一款专为极简主义者打造的下一代游戏启动器。它摒弃了传统平台的臃肿，利用 **WinUI 3** 和 **.NET 8** 的强大性能，为您提供毫秒级的启动体验和原生的 Windows 11 视觉享受。

无论您的游戏来自 Steam、Epic，还是独立的 `.exe`，这里都是它们统一的家。

## 功能特性

### 专为玩家打造
*   **极致轻量**：启动即用，用完即走。无后台服务，零广告打扰，系统资源占用几近于零。
*   **拼音秒搜**：专为中文环境优化。想玩《英雄联盟》？只需输入"YXLM"，结果即刻呈现。
*   **专业级属性控制**：这不是一个简陋的快捷方式。
    *   **以管理员身份运行**：可以分别为“游戏主程序”和“游戏管理器”（如 Steam/Epic）设置是否提权，解决权限不足无法启动的痛点。
    *   **运行管理器**：支持配置管理器路径，确保游戏不仅能启动，还能正确拉起平台服务。
    *   **全面参数支持**：主程序、管理器、替代启动及伴随程序的所有路径均支持携带**命令行参数**或使用 **URL Protocol**。
    *   **代替主程序启动**：勾选此项即可使用自定义命令（如 `starward://`）完全替代原始 EXE 的启动逻辑。
        > 💡 **实用技巧**：保留 EGL 中的游戏封面，但实际调用外部工具（如 Starward）启动游戏并统计时长。
    *   **启动时同时执行**：玩游戏时想自动打开翻译器、计时器或性能监控？配置此项，一键双开。
    *   **更换图标**：支持手动上传图片作为封面，拯救强迫症。
    *   **图标大小**：从紧凑列表到沉浸大图，随心所欲。

### 专为极客设计
*   **完全便携 (Portable Mode)**：支持将所有数据（配置、缓存图标）存储在程序目录下。放入 U 盘，您的游戏库随身携带，插在任何电脑上即可运行。
*   **数据无缝迁移**：想从系统安装模式切换到便携模式？一键迁移，所有配置和图标自动搬家，无需重新设置。
*   **智能图标提取**：自动解析 `.exe`、`.lnk` 甚至 Steam/Epic URL 协议，提取并缓存高清图标。

## 技术亮点

对于开发者而言，Eric Game Launcher 展示了如何用最精简的代码构建现代化的 Windows 桌面应用：

*   **前沿技术栈**：基于最新的 **Windows App SDK (1.6+)** 和 **.NET 8** 构建，展示了 WinUI 3 在桌面应用中的强大潜力。
*   **原生性能**：
    *   利用 `P/Invoke` (User32.dll) 进行高效的系统级图标提取。
    *   使用 `System.Text.Json` 实现高性能、低内存占用的数据序列化。
*   **简洁架构**：
    *   **高效的数据绑定**：通过 `ObservableCollection` 和 XAML 绑定实现流畅的 UI 交互，代码逻辑清晰直观。
    *   **静态服务设计**：`ConfigService`、`I18n` 等核心模块采用静态类设计，零开销调用，极简高效。
*   **现代化构建**：
    *   **Unpackaged 发布**：抛弃繁琐的 MSIX 打包，直接生成绿色纯净的 `.exe` 可执行文件。
    *   **完全自包含 (Self-Contained)**：内置 .NET 8 核心组件与 Windows App SDK，无论系统环境如何，下载解压即开即用。

## 快速开始

### 用户
1.  前往 [Releases](../../releases) 页面下载最新版本。
2.  解压并运行 `EricGameLauncher.exe`。
3.  点击右上角 **“更多” -> “添加”**，选择游戏快捷方式或可执行文件。
4.  *(推荐)* 在设置中开启“便携模式”，体验绿色软件的快乐。

## 贡献与支持

我们欢迎任何形式的贡献！无论是提交 Bug、改进文档，还是提交代码 (PR)。

如果您觉得这个项目对您有帮助，请给它由衷的一颗 ⭐️ **Star**！


## 特别致谢 / 友情链接
*   [**Starward**](https://github.c/Scighost/Starward): 一个功能强大的米哈游游戏启动器。我们最初的对此项目的构想只是一个本地游戏的集合启动器，没计划支持分发平台。一开始之所以支持url-scheme协议，就是因为需要支持他的协议来支持记录米家游戏游戏时长。后来想了想既然都支持了url-scheme了，那就支持一下诸如Steam/Epic等平台的协议吧，结果就有了现在这个支持分发平台启动器的功能。Starward在米哈游游戏启动器领域的创新和贡献是不可磨灭的，我们希望通过支持其协议来向其致敬，并为玩家提供更多选择。借由这个契机，我们的启动器应该能支持绝大部分的启动器url-scheme协议和带参执行。
*   [**胡桃工具箱 (Snap Hutao)**](https://github.com/DGP-Studio/Snap.Hutao)：感谢其社区曾经的卓越贡献。它证明了玩家社区的力量能创造出无限可能。虽已谢幕，但其开源精神将薪火相传，激励后来者前行。**R.I.P**

---
<div align="center">
  Made with ❤️ by EricZhang233
</div>
