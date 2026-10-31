# 自动构建与发布

发布标签以小写 `v` 开头的 Release 时，`post_release.yml` 首先将正文设为“正在构建，稍后将在此显示更新日志和软件包”，再调用 `build_release.yml`，在 Windows 2025 上检出该标签并直接构建 `EricGameLauncher.csproj`。附件上传成功后，最后从对应标签的 `.releasenote.md` 写入正式正文。GitHub 不运行测试套件；普通提交、PR、仅推送标签和非 `v` 标签的 Release 均不自动发布附件。

等待提示、构建、附件上传或正式正文更新任一阶段失败时，流程将本次触发的 Release 转回草稿，保留当前说明、附件、Git 标签和 Actions 运行记录。通过 Release ID、标签名和发布时间核对，避免旧失败任务影响后来重新发布的版本；已删除的 Release 不会重建。修复后可重新发布该草稿。转换失败会明确报错。手动构建不改变已有 Release 状态。

标签格式为 `vX.Y.Z` 或 `vX.Y.Z.W`，必须与该标签内的 `Version.cs` 完全一致。项目及其辅助组件的构建设置来自项目文件，不读取解决方案中的配置映射。

本地与 CI 均从 `global.json` 选择 .NET SDK，由主项目决定 win-x64 以及外部 .NET 10 Desktop Runtime 和 Windows App SDK 1.8 运行时依赖，与 v1.2.4 的部署方式一致。Windows App SDK 原生运行时和多语言资源不随包分发。Release 打包策略与版本在项目中定义；CI 只指定 Release 配置、审查要求和隔离产物目录。构建警告及 NuGet 审查警告作为失败处理。

本地测试需要匹配的 Windows App Runtime；必要时手动使用 `Test/Scripts/prepare-runtime.ps1` 安装官方签名运行时。运行时不进入发布 ZIP。

CI 将 ZIP 直接生成到运行器临时目录，检查必要文件和私有数据后存为当前运行的 Artifact。附件工作流只取这次构建的 Artifact，上传内部原始 ZIP 到对应 Release；发布说明读取同一标签的 `.releasenote.md`。不读取仓库中的 `Releasepack/`，也不再删除或提交主分支上的文件。

需要单独测试构建时，在 Actions 中手动运行 `EricGameLauncher-BuildRelease`，输入一个已存在的 `v` 标签；该入口只生成保留 7 天的 Artifact，不上传 Release 附件。

本地普通 Release 构建默认生成 `Releasepack/EricGameLauncher_<版本>.zip`，该目录仅用于本地构建，已排除在版本控制和源码扫描之外。也可显式指定空目录：

```powershell
pwsh.exe -File .github/scripts/build-release.ps1 -Tag v1.2.4 -PackageDirectory C:\Temp\EGL-CI-Test
```

自动构建不会改变用户下载时 Windows 添加互联网来源标记的行为。

版本统一从 `Version.cs` 派生，主程序、CLI、更新器及生成的清单使用同一版本。`Build/Build.psm1` 提供统一的目录/ZIP 发布检查；本地打包、正式发布和测试拒绝相同的配置、缓存与日志条目。`Build/Build.targets` 的公共清理目标供 clean、Release 收尾和测试失败收尾共用。

已移除 Validate 工作流及其手动、可复用入口。自动晋升只接受目标提交最新的 Release 全流程已完成且成功；失败、排队、运行中或失败重跑均不回退到更早成功记录，本地测试成功也不能代替发行构建和上传成功。

test 是独立流程，唯一入口是在仓库根目录运行 `dotnet test`，一次执行依赖审查、Core、集成、发布契约、隔离 Release 包和窗口启停检查。不需要分区参数或其他命令。普通 build/run 不自动触发测试，测试成功不自动提交或推送。推送前单独完成测试，失败则停止后续推送，内容变化后重新测试。测试包仅存于临时目录，不作为正式发行包。详见 [Test](../Test/README.md)。
