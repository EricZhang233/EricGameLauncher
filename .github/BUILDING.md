# 自动构建与发布

发布标签以小写 `v` 开头的 Release 时，`post_release.yml` 首先将正文设为“正在自动化测试和构建，稍后将在此显示更新日志和软件包”，再调用 `validate.yml` 对该标签执行完整测试，通过后调用 `build_release.yml`，在 Windows 2025 上检出该标签并直接构建 `EricGameLauncher.csproj`。附件上传成功后，最后从对应标签的 `.releasenote.md` 写入正式正文。普通提交、PR、仅推送标签和非 `v` 标签的 Release 均不自动测试或发布附件。

等待提示、发版测试、构建、附件上传或正式正文更新任一阶段失败时，流程将本次触发的 Release 转回草稿，从公开发行列表隐藏，保留当前说明、附件、Git 标签和 Actions 运行记录，测试报告附件保留 14 天。通过 Release ID、标签名和发布时间核对，避免旧失败任务影响后来重新发布的版本；已删除的 Release 不会重建。修复后可重新发布该草稿。转换失败会明确报错。手动验证和手动构建不改变已有 Release 状态。

标签格式为 `vX.Y.Z` 或 `vX.Y.Z.W`，必须与该标签内的 `Version.cs` 完全一致。项目及其辅助组件的构建设置来自项目文件，不读取解决方案中的配置映射。

本地与 CI 均从 `global.json` 选择 .NET SDK，由主项目决定 win-x64 以及外部 .NET 10 Desktop Runtime 和 Windows App SDK 1.8 运行时依赖，与 v1.2.4 的部署方式一致。Windows App SDK 原生运行时和多语言资源不随包分发。Release 打包策略与版本在项目中定义；CI 只指定 Release 配置、审查要求和隔离产物目录。构建警告及 NuGet 审查警告作为失败处理。

Validate 在测试前通过 `Test/Scripts/prepare-runtime.ps1` 安装与还原依赖匹配的官方 x64 Windows App Runtime，校验安装器的 Microsoft 签名，并记录安装输出。该运行时仅安装到测试机，不进入发布 ZIP；未安装时 SDK 默认安装提示可能让无交互 CLI 等待到超时。

CI 将 ZIP 直接生成到运行器临时目录，检查必要文件和私有数据后存为当前运行的 Artifact。附件工作流只取这次构建的 Artifact，上传内部原始 ZIP 到对应 Release；发布说明读取同一标签的 `.releasenote.md`。不读取仓库中的 `output/`，也不再删除或提交主分支上的文件。

需要单独测试构建时，在 Actions 中手动运行 `EricGameLauncher-BuildRelease`，输入一个已存在的 `v` 标签；该入口只生成保留 7 天的 Artifact，不上传 Release 附件。

本地普通 Release 构建仍默认生成 `output/EricGameLauncher_<版本>.zip`。也可显式指定空目录：

```powershell
pwsh.exe -File .github/scripts/build-release.ps1 -Tag v1.2.4 -PackageDirectory C:\Temp\EGL-CI-Test
```

自动构建不会改变用户下载时 Windows 添加互联网来源标记的行为。

版本统一从 `Version.cs` 派生，主程序、CLI、更新器及生成的清单使用同一版本。`Build/Build.psm1` 提供统一的目录/ZIP 发布检查；本地打包、正式发布和测试拒绝相同的配置、缓存与日志条目。`Build/Build.targets` 的公共清理目标供 clean、Release 收尾和测试失败收尾共用。

Validate 由发版流程调用，也保留手动指定已有标签的入口。自动晋升只接受目标提交最新的 Release 全流程已完成且成功；失败、排队、运行中或失败重跑均不回退到更早成功记录，手动验证成功也不能代替发版全流程成功。

项目测试统一位于 [Test](../Test/README.md)，在项目根目录执行 `dotnet test` 即可全量运行。Validate 工作流调用同一项目入口，测试逻辑和报告均归 Test 管理。
