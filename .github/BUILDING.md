# 自动构建与发布

发布标签以小写 `v` 开头的 Release 时，`post_release.yml` 首先将正文设为“正在构建，稍后将在此显示更新日志和软件包”，再调用 `build_release.yml`，在 Windows 2025 上检出该标签并直接构建 `EricGameLauncher.csproj`。附件上传成功后，最后从对应标签的 `.releasenote.md` 写入正式正文。GitHub 不运行测试套件；普通提交、PR、仅推送标签和非 `v` 标签的 Release 均不自动发布附件。

等待提示、构建、附件上传或正式正文更新任一阶段失败时，流程将本次触发的 Release 转回草稿，保留当前说明、附件、Git 标签和 Actions 运行记录。通过 Release ID、标签名和发布时间核对，避免旧失败任务影响后来重新发布的版本；已删除的 Release 不会重建。修复后可重新发布该草稿。转换失败会明确报错。手动构建不改变已有 Release 状态。

标签格式为 `vX.Y.Z` 或 `vX.Y.Z.W`，必须与该标签内的 `Version.cs` 完全一致。项目及其辅助组件的构建设置来自项目文件，不读取解决方案中的配置映射。

本地与 CI 均从 `global.json` 选择 .NET SDK，由主项目决定 win-x64 以及外部 .NET 10 Runtime 和 Windows App SDK 1.8 运行时依赖，不再要求 Desktop Runtime。Windows App SDK 原生运行时和多语言资源不随包分发。Release 打包策略与版本在项目中定义；CI 只指定 Release 配置、审查要求和隔离产物目录。构建警告及 NuGet 审查警告作为失败处理。

本地测试需要匹配的 Windows App Runtime；必要时手动使用 `Test/Scripts/prepare-runtime.ps1` 安装官方签名运行时。运行时不进入发布 ZIP。

CI 将兼容 ZIP 与 Setup EXE 直接生成到同一个运行器临时目录，分别检查包内必要文件和私有数据后存为当前运行的同一个 Artifact。附件工作流只取这次构建的 Artifact，上传其中的原始 ZIP 与 EXE 到对应 Release；两个附件均上传成功后才写入同一标签的 `.releasenote.md`。不读取仓库中的 `Releasepack/`，也不再删除或提交主分支上的文件。

需要单独测试构建时，在 Actions 中手动运行 `EricGameLauncher-BuildRelease`，输入一个已存在的 `v` 标签；该入口只生成保留 7 天的 Artifact，不上传 Release 附件。

本地普通 Release 构建默认生成 `Releasepack/EricGameLauncher_<版本>.zip` 和 `Releasepack/EricGameLauncher_Setup_<版本>.exe`，两者使用相同应用载荷。该目录仅用于本地构建，已排除在版本控制和源码扫描之外。也可显式指定空目录：

```powershell
pwsh.exe -File .github/scripts/build-release.ps1 -Tag v1.2.4 -PackageDirectory C:\Temp\EGL-CI-Test
```

自动构建不会改变用户下载时 Windows 添加互联网来源标记的行为。

版本统一从 `Version.cs` 派生，主程序、CLI、更新器及生成的清单使用同一版本。Release 采用目录式发行，程序集和依赖 DLL 保持外置；主程序与 CLI 的 deps/runtimeconfig JSON 只在 obj 中生成，再分别嵌入对应 EXE，不向发行目录输出，不在运行时释放。Debug 保留外部启动 JSON 供调试和测试宿主使用。XBF 在 obj 中编译并直接嵌入主程序集，由 WinUI 的 ResourceNotFound 回调返回二进制资源；关闭 PRI 生成，不分发或释放 PRI。构建在复制输出项之前排除已内嵌的 XBF 与源 XAML，不使用构建后删除。Debug 与 Release 都不向输出目录复制这些散装资源。仍依赖系统安装的 x64 .NET Runtime 和 Windows App Runtime，不捆绑系统运行时。便携数据、存储模式、快捷方式和更新目标均以 EXE 所在目录为准。

依赖使用 Windows App SDK 的 WinUI 与 Runtime 组件包，Foundation、InteractiveExperiences 和 WebView2 通过 WinUI 的依赖链保留，不引用整套 SDK、WinUIEx、AI、ML、Widgets 或 DWrite 包。拼音搜索使用 TinyPinyin，配置和更新器使用 YamlDotNet，图标处理直接引用 System.Drawing.Common，令牌与平台数据解密直接引用 System.Security.Cryptography.ProtectedData。任务栏跳转列表使用 Windows 原生 COM 接口，不引用 WPF；排序、分隔符、应用标识和图标引用维护均保留。不对反射与 COM 使用路径启用未经验证的 IL 裁剪，也不改变程序集身份。

主程序与 CLI 在 ResolvePackageAssets 之后、依赖清单生成与文件复制之前，统一排除当前没有引用的 17 个 Windows App SDK 托管投影及 UniversalBGTask 原生宿主。排除清单集中在 Directory.Build.targets，编译引用和运行时资产同步处理，不使用构建后删 DLL，也不改写第三方程序集。当前发行目录保留 19 个 DLL；Windows SDK、WinUI、InteractiveExperiences、MRT Core 投影、Bootstrap 引导库及 WebView2 的托管/原生组件仍必需。快速启动使用本程序的 Run 启动项和 -service 常驻进程、互斥量及命名管道，不使用 Windows App SDK 的 BackgroundTask 宿主。新增相关 SDK 功能时需重新审查排除清单。

默认 `ico.ico` 只作为源码中的编译输入，写入 EXE 原生图标及程序集资源，不复制到输出目录。窗口图标从内嵌资源生成独立 HICON；启动界面、标题栏及内置图标图片在内存中转换和加载，不查询安装目录中的同名 ICO，也不生成默认 ICO/PNG 缓存。用户显式配置的自定义图标仍按其设置加载。

`Directory.Build.props` 统一派生版本属性，`Directory.Build.targets` 供主程序与 CLI 共用 SDK 资产筛选及 Release 启动配置内嵌目标，其余构建、资源输出和公共清理目标集中在 `EricGameLauncher.csproj`，不另设 Build 目录。`.github/scripts/ReleaseTools.psm1` 提供统一的目录/ZIP 发布检查，供本地构建、正式发布及测试共用；检查必要的程序集，拒绝外部 JSON/PRI、私有配置、缓存、日志及散装 XBF/XAML。公共清理目标供 clean、Release 收尾和测试失败收尾共用。

已移除 Validate 工作流及其手动、可复用入口。自动晋升只接受目标提交最新的 Release 全流程已完成且成功；失败、排队、运行中或失败重跑均不回退到更早成功记录，本地测试成功也不能代替发行构建和上传成功。1.4.0 起还要求对应版本的 ZIP 与 Setup EXE 两个附件均存在且非空；旧版本保留 ZIP 单包检查，缺包、空包或其他版本的安装包均不能代替本版本所需附件。

test 是独立流程，唯一入口是在仓库根目录运行 `dotnet test`，一次执行依赖审查、Core、集成、发布契约、隔离 Release 包和窗口启停检查。不需要分区参数或其他命令。普通 build/run 不自动触发测试，测试成功不自动提交或推送。推送前单独完成测试，失败则停止后续推送，内容变化后重新测试。测试包仅存于临时目录，不作为正式发行包。详见 [Test](../Test/README.md)。

## 数据模式定位

`storage.mode` 是位于 EXE 同级目录的可选模式标记，不属于配置目录，也不是启动必需文件。普通启动与配置保存不创建它；实际切换数据模式或恢复相关迁移时写入。安装版及存储模式锁定的解压副本忽略此标记，固定使用系统数据。其他解压副本在没有标记时，优先识别 `Data` 中已有的配置，否则使用系统数据。
