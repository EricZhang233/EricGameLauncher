# 项目测试

测试工程与脚本全部在 `Test/`，测试编译输出和临时夹具放在系统 `%TEMP%\EricGameLauncher.Tests`，报告保存在 `Test/artifacts/`。主 `EricGameLauncher.csproj` 导入 `Test/Test.targets`，直接调用两个测试工程，因此在项目根目录运行 `dotnet test` 即可，不依赖解决方案的工程登记或配置映射。根目录没有手动测试脚本；GitHub 的 Validate 工作流调用同一入口。`.github/scripts/build-release.ps1` 是正式发布构建工具，不是测试入口。

在项目根目录打开 PowerShell 7，安装 .NET 10 SDK。完整集成与发布包测试还需要 Windows 11 24H2 或更高版本、Windows App SDK 1.8 运行时、Node.js、Windows SDK 和可用的桌面会话。测试用的 xUnit 依赖首次运行时由 NuGet 还原。先关闭正在运行的启动器，测试发现已有窗口会报错退出，不会替你关闭窗口。

## 常用命令

全量检查：

```powershell
dotnet test
```

主项目在 `dotnet test` 的 Restore 阶段还原测试工程，再由 VSTest 目标执行测试。普通 Build/Restore 仅处理生产依赖，不还原或运行测试。测试结束会删除测试工程编译输出和还原中间文件，下次直接运行 `dotnet test` 重新生成；`--no-restore`、`--no-build` 仅适合已单独准备输出且尚未执行清理的情况。也可以显式执行 `dotnet test EricGameLauncher.csproj`。仓库只使用项目文件，不保留解决方案文件。

日常只检查核心规则，不编译或启动主窗口：

```powershell
dotnet test Test/Core/Core.Tests.csproj
```

按功能筛选，例如只检查时间持久化：

```powershell
dotnet test Test/Core/Core.Tests.csproj --filter FullyQualifiedName~TimestampTests
```

只检查业务与 CLI 集成，包括更新器本地模拟和日志压力，不启动主窗口：

```powershell
dotnet test Test/System/System.Tests.csproj --filter Category=Integration
```

只检查发布工作流与执行器契约：

```powershell
dotnet test Test/System/System.Tests.csproj --filter Category=Contracts
```

只检查真正构建出的 Release 包，包括一次主窗口启动和正常关闭：

```powershell
dotnet test Test/System/System.Tests.csproj --filter Category=Packaging
```

需要 IDE 时，直接打开 `Test/Core/Core.Tests.csproj` 或 `Test/System/System.Tests.csproj`。Core 测试逐条显示；现有子进程回归在 System 工程中按 Integration、Contracts、Packaging 三组显示，组内每条断言写入对应日志。不能用 `--filter` 单独选取子进程内某一条断言。

## 测试实际做了什么

每条断言先安排已知输入，再调用生产代码，最后核对结果。例如创建两条回收站记录，把其中一条时间设为 73 小时前，清理后必须只删除这一条；刚标记的另一条必须还在。断言不成立会使 `dotnet test` 返回非零退出码。

异常场景也要测试通过。例如故意篡改 ZIP 时，期望是校验失败、安装目录不变；若程序接受篡改包，测试才会失败。不是把“程序没崩”当成通过。

Core 工程链接纯业务生产源码，没有另写一份业务实现；当前业务程序集包含 WinUI，所以另用 Regression 子进程加载真正编译出的主程序集并调用真实 CLI，覆盖持久化、跨进程修改和 UI/CLI 共享业务。UpdateIntegrity 使用本机回环 HTTP 和命名管道模拟下载，不上传数据、不安装真实更新。压力测试写入自己的日志目录。

所有测试业务数据、测试工程编译输出和 Release 测试包放在系统 `%TEMP%\EricGameLauncher.Tests` 下，CLI 使用隔离模式，不修改日常游戏库或开机启动注册，不覆盖 `Releasepack/` 中的本地构建包。无论成功或失败，都在结束后清理临时夹具和编译文件；失败原因、命令输出和断言结果写入报告。测试宿主的程序集在 VSTest 返回后才清理。生产程序仍按正常构建路径生成，由机测结束时的构建清理目标回收。手工临时编译或手测也使用系统临时目录，退出后清理。

## 看结果

- `Test/artifacts/results/*.trx`：标准测试结果，可在 IDE 中查看。执行 `dotnet test --logger trx --results-directory Test/artifacts/results` 时生成。
- `Test/artifacts/machine-tests/<时间与编号>/summary.md`：本次组测试的进度、耗时与日志入口。
- 同目录 `summary.json`：程序可读的结果、临时目录路径与清理状态。`passed` 才表示该组执行完成，`failed` 是失败，`not_run` 表示前面失败后未执行，不能当作通过。
- 同目录 `00.log` 等：完整命令、stdout、stderr 和每条 `PASS` 断言。
- Packaging 的 `lifecycle.json`：一次启停的启动耗时、私有内存和句柄数采样。

各组报告是独立的，不要把旧报告或另一组的通过状态当成当前失败组已经通过。每次运行有自己的编号；完整结果还应以最外层 `dotnet test` 的退出码为准。

发布包默认只启动、退出一次，检查能否显示窗口、正常退出以及有无残留子进程。需要排查偶发启动失败时，可显式运行内部测试入口：

```powershell
pwsh.exe -NoProfile -File Test/Scripts/validate.ps1 -Area Packaging -LifecycleCycles 6
```

这不是六条新功能测试，也不能证明长期不泄漏；每轮都是新进程。长期泄漏需要在同一个窗口会话里反复操作，再观察是否持续增长。日常不必重复启停。

## 覆盖范围

逐项覆盖和仍需实机执行的测试点见 [COVERAGE.md](COVERAGE.md)。矩阵对照当前代码、README，以及 Git 历史中 `.releasenote.md` 的功能记录建立；已经移除的八语言选项、旧 Mica 和旧版 JSON 配置 UI 不作为当前功能验收，但遗留数据兼容仍应测试。

云端完整测试仅在发布 v 标签的 Release 时自动运行，测试通过后才构建并上传附件；失败会将本次 Release 转回草稿，保留说明、附件、标签、Actions 运行记录和测试报告（报告附件保留 14 天）。旧失败任务不会改变后来重新发布的版本状态。普通提交、PR 和标签推送不自动运行测试。需要单独排查时，可手动运行 Validate 并指定已有标签；该手动入口不会改变已有 Release 状态。

发版流程先将公开正文设为等待测试与构建的提示，附件上传成功后才写入标签内的正式更新日志。正文准备或最终写入失败同样触发转草稿；相关 API 状态与错误传播由 `Scripts/test-release-body.mjs` 模拟检查。

云端测试先执行 `Scripts/prepare-runtime.ps1`，从已还原的依赖中读取 Windows App Runtime 版本并安装官方签名运行时，安装日志位于 `artifacts/runtime-setup`。本地已有对应运行时无需执行此安装脚本。运行时只准备在测试机，不会加入发布包。发布包 CLI 超时会附带窗口标题和输出，以便区分运行时安装提示与业务等待。

可执行 `pwsh.exe -File Test/Scripts/prepare-runtime.ps1 -ValidateOnly` 检查真实依赖恢复、运行时版本及下载链接，不下载或安装运行时。运行时包使用三段 NuGet 版本号，和已安装框架的四段版本号分别处理。

本地桌面通过不等于 GitHub 云端已经通过。视觉对齐、动画、真实 UAC、真实 Store 游戏、多屏 DPI、登录后台宿主以及长时间单会话资源变化需要按矩阵进行实机验收。
