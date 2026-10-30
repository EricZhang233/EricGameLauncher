# 待实现需求汇总

本文档用于持续收集需求，待需求汇总后统一实现。文档中的已确认内容作为实现依据；待明确事项不视为已确认需求。

## 发行方式

- 保留当前散包发行方式，不迁移至 MSIX。
- 保留便携模式和现有自更新能力。

## 主界面交互与显示

- 默认即可拖动项目调整顺序，取消编辑模式入口和退出按钮；松开后通过现有排序服务保存，单击、双击启动仍遵循设置。
- 拖动期间不显示项目名称提示，防止遮挡落点。
- 启动层与窗口根布局使用透明背景，保留亚克力背景及渐入渐出，动画仍遵循系统动画设置。
- 主界面在后台完成布局与加载，splash 淡出到时间轴的 2/3 时开始淡入，在剩余 1/3 时间内完成；淡出与主界面淡入共用时间轴，动画结束前不可操作。动画默认开启，仅遵循 Windows 系统动画开关，与快速启动无关；系统关闭动画时直接无缝切换，后台预热及动画异常路径同样恢复显示。
- 删除快速启动下的动画开关及对应配置项读写、CLI 设置与帮助；已有配置中的旧动画字段被忽略，不影响系统动画判断。
- 设置浮层移除独立问号按钮，将原有帮助的悬停提示与点击弹层绑定到快速启动、更新通道、GitHub Token、存储位置各项的图标和标题区域，保留键盘访问与本地化。
- 手动检查更新立即打开更新窗口，在窗口内显示加载状态，覆盖网络请求及更新说明内容准备，再原位替换为结果或错误说明，不显示独立加载提示；阻止重复检查，关闭窗口取消等待，完成或取消后恢复菜单，不影响静默检查。
- 更新窗口在打开前设置原有宽高限制，加载内容与结果共用同一组尺寸，确保嵌入的更新说明按正确视口宽度显示及换行。
- 属性面板的平台标签在配置为通用 `User` 时优先识别启动协议，Google Play Games 项目显示正确品牌；显式自定义平台名称继续保留，不改写已有配置。
- 验证：GPG 属性标签已在界面确认；手动拖动的两次提交日志及配置顺序均已核对。405 项功能检查通过；该次测试整体因界面测试进程占用构建目录，收尾清理失败。

## Jump List

状态：已进入实现与验证阶段。

当前纯任务区方案：394 项集成检查通过，覆盖配置 Pin 持久化、固定按主面板顺序、常用排序去重、未固定平台隐藏及空段处理。以下早期验证记录不代表仍保留历史系统固定同步方案。

本次验证：Debug 严格编译通过；核心测试 36 项通过；集成测试 389 个检查通过，包含平台识别、显式 false 保留、平台取消固定持久化、GPG 记录解析与截断数据拒绝。属性框位置已实机检查。Jump List 系统提交成功（查询额度 10，含任务区共提交 15 项）；实际最终可见数量仍由 Windows 决定。笔记本运行生产 CLI 的 GPG 扫描成功，仅返回部落冲突并复用含 lid、pid 的完整快捷方式 URI。尚未执行游戏卸载对照或跨版本数据验证。

### 已确认需求

- 在散包下实现任务栏 Jump List。
- 全部使用 Windows Tasks 任务区，不提交任何目的地分组，不提供系统固定入口。
- 任务区仅分为游戏平台、常用两段；固定项目置于常用段顶部，与其他常用项目之间不加分隔线。固定项目按主面板顺序，其后普通常用项目按成功启动次数排序。仅在两段均非空时插入一条系统分隔线。
- `pin` 完全由应用配置管理，不读取或同步 Windows 系统固定缓存。
- 平台项目默认勾选 `pin`，但允许用户取消固定并保存；普通项目默认 `pin = false`。
- 新建平台项目或项目首次设置为平台时默认勾选 Pin；用户可在本次编辑中取消，保存、重新打开或重新加载不得再次强制勾选。
- 旧配置补齐时，若项目缺少 `pin` 字段，则根据补齐后的 `isplatform` 设置默认值：平台为 `true`，普通项目为 `false`；已有显式 `pin` 值一律保留。
- 平台项仅在 `pin = true` 时进入游戏平台任务区；取消 Pin 后从整个 Jump List 隐藏，不进入已固定、常用或任务区。
- 已固定与游戏平台两段均沿用主界面顺序。
- 带 Pin 图标的切换按钮位于属性框底部，删除与保存按钮之间，按面板宽度居中。
- 所有项目都可直接使用属性 Pin 按钮和 CLI `--pin true|false`，按现有属性保存流程落盘。取消临时展示入口和手动固定提示。
- 此前已由用户固定在 Windows 系统区的遗留项可能继续存在；不自动篡改系统缓存，用户可自行取消其固定。
- 常用分组不包含已固定的项目，同一项目不能同时出现在已固定和常用分组中。
- 各区域互斥：`isplatform = true` 且 `pin = true` 的项目仅进入游戏平台任务区；`isplatform = true` 且 `pin = false` 的项目不进入任何区域；非平台且 `pin = true` 的项目仅进入已固定；常用仅从非平台且未固定的项目中选取。
- 游戏平台任务区展示所有 `isplatform = true` 且 `pin = true` 的项目，直接沿用主界面顺序。
- 常用分组按累计成功启动次数降序排列，同次数按主界面顺序排列。
- 常用允许零次成功启动的项目参与补位；有余量时按主界面顺序补齐，仍排除平台与已固定项目。
- 每次成功触发项目主启动链路计一次累计成功启动次数，GUI、CLI 与 Jump List 共用同一计数逻辑；管理器、伴随程序和自定义菜单不单独计数。
- 启动次数仅以项目 ID 与次数的映射存放于独立缓存，不写入 `items.yaml`；这是当前版本的新功能，不迁移旧字段或历史数据，缓存从零开始。
- 次数缓存位于应用缓存目录，以配置目录区分不同资料库；内存快照按文件变化刷新，写入使用跨进程互斥与原子替换。删除缓存会重置次数，不影响项目配置。
- 成功启动只更新次数缓存；任务栏重新排序复用内存中的项目列表，不为计数再读写完整项目配置。
- 独立次数缓存方案已通过 402 项集成检查，包含并发累计、配置文件内容与修改时间不变、属性编辑保留缓存次数、删除与损坏缓存重建。
- 不再查询或使用 `BeginList` 的目的地额度来限制任务项，最终显示仍受 Windows 可用屏幕空间限制，不能承诺全部提交项均可见。
- 全部任务项通过启动器加项目 ID 参数执行，复用项目启动逻辑；用户不能在系统 Jump List 中固定、取消固定或移除这些任务项。
- 数据层新增项目级 `isplatform` 布尔字段，表示该项目本身是游戏平台。
- `isplatform` 及自动识别结果必须保存在该项目自己的配置记录中，与项目其他属性一起读写；不能只存在界面状态、临时缓存或全局平台配置中。
- 属性框“这是个游戏平台”复选框直接绑定当前项目配置中的 `isplatform`；Jump List 的游戏平台分组读取各项目已保存的该字段，不在生成列表时重新识别覆盖。
- 在项目属性框的管理器区域、输入框下方增加“这是个游戏平台”复选框，用于读取和设置该项目的 `isplatform`。
- 根据项目启动目标的可执行文件名自动识别游戏平台。本版本即内置下方调研清单，不将扩充平台留作后续版本任务。
- 自动识别到游戏平台时自动勾选“这是个游戏平台”，但允许用户取消勾选并保存。
- 新增项目时应用自动识别；用户手动选择后，以用户选择为准，不因保存、重新打开属性框或重新加载配置而覆盖。
- 加载旧配置时，逐项目检查 `isplatform` 字段是否存在：仅字段缺失时自动判断并补齐，识别为平台则写入 `true`，否则写入 `false`，并持久化结果。
- 必须区分字段缺失和字段显式为 `false`；已有 `true` 或 `false` 均保留，不重复自动推断，确保用户取消勾选后不会被下次加载重新勾选。

### 本版本内置游戏平台识别清单

调研日期：2026-10-30。以下为本版本实现范围；本表是需求与识别依据，尚未写入生产代码。

“全路径中含有”列为“—”时表示不附加路径条件；多个路径值为任一命中即可。

| 平台 | 可执行文件名 | 全路径中含有 | 说明与依据 |
| --- | --- | --- | --- |
| Steam | `steam.exe` | — | [Steam 官方支持](https://help.steampowered.com/en/faqs/view/3344-6439-1D50-5A9B) |
| Epic Games | `EpicGamesLauncher.exe` | — | [Epic 官方支持](https://www.epicgames.com/help/c-34254770/c-37371353/a13131646?lang=en-US) |
| Xbox | `XboxPcApp.exe` | — | [微软站点故障报告](https://learn.microsoft.com/en-us/answers/questions/4040077/windows-10-xbox-app-crashes-soon-after-running-it)。商店应用入口需解析包身份，不能要求用户取得 WindowsApps 内的 EXE 路径 |
| Google Play Games | `bootstrapper.exe`、`client.exe` | `\Google\Play Games\` | 不匹配后台 `Service.exe`。文件名来自 [Google 社区现场报告](https://support.google.com/googleplay/thread/343901570/solved-google-play-games-beta-crashing-on-startup-%E2%80%93-fix-for-invalid-namespace-wmi-errors?hl=en)，实现时须验证实际客户端样本，不视为厂商规范 |
| WeGame | `WeGame.exe` | — | 用户指定的基础支持项，实际客户端样本验证纳入实现验收 |
| EA app | `EADesktop.exe` | — | [EA 官方支持](https://help.ea.com/en/articles/platforms/ea-app-how-to-close-background-apps/) |
| Battle.net | `Battle.net.exe` | — | [Blizzard 官方支持检索](https://us.support.blizzard.com/en/search?f=post&page=1&q=Runtime+error) |
| GOG Galaxy | `GalaxyClient.exe` | — | [Remedy 官方支持](https://remedy.helpshift.com/hc/en/4-control-ultimate-edition/faq/156-how-to-clear-gog-galaxy-cache-or-reinstall-the-launcher/) |
| Ubisoft Connect | `UbisoftConnect.exe` | — | [Faugus 启动器源码中的启动路径](https://github.com/Faugus/faugus-launcher/blob/main/faugus/launcher.py) |
| Riot Client | `RiotClientServices.exe` | — | [Riot 官方支持](https://support.riotgames.com/en-us/riot/security/security-software-and-malware-scan/) |
| Rockstar Games Launcher | `Launcher.exe` | `\Rockstar Games\Launcher\` | [Rockstar 官方支持](https://support.rockstargames.com/articles/6lMu3vkdWoK5VzkOBjAnPw/repeatedly-being-prompted-by-user-account-control-when-attempting-to-launch-rockstar-games-launcher) |
| HoYoPlay / 米哈游启动器 | `launcher.exe` | `\HoYoPlay\` 或 `\米哈游启动器\` | 分别作为两条识别规则；路径特征为本项目采用的保守识别条件，不覆盖任意改名目录。不能误识别旧单游戏启动器；[HoYoverse 官方 FAQ（确认 EXE 名称）](https://honkaiimpact3.hoyoverse.com/global/en-us/news/126478) |
| Heroic Games Launcher | `Heroic.exe` | — | [Heroic 官方调试文档](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/wiki/Troubleshooting) |
| Playnite | `Playnite.DesktopApp.exe`、`Playnite.FullscreenApp.exe` | — | [Playnite 官方文档](https://api.playnite.link/docs/manual/gettingStarted/helpAndTroubleshooting/troubleshooting.html) |

### 自动识别规则

- 每条识别规则包含可执行文件名，并可选配置“全路径中含有”的值。
- 文件名比较忽略大小写，精确匹配完整文件名；文件名足以可靠判断时，不附加路径条件。
- 仅在文件名无法可靠判断时补充“全路径中含有”条件；此时必须同时满足文件名匹配与完整目标路径包含指定值，路径包含比较忽略大小写。
- “全路径中含有”检查解析并规范化后的完整目标 EXE 路径，不检查命令参数或快捷方式文件自身的路径；不写死盘符或默认安装根目录。
- 当前清单中需要路径补充的规则已明确为：Google Play Games 的 `\Google\Play Games\`、Rockstar 的 `\Rockstar Games\Launcher\`、HoYoPlay 的 `\HoYoPlay\` 或 `\米哈游启动器\`。其余清单项不附加路径包含条件。
- 上述路径值使用目录分隔符限定目录边界，避免仅名称片段相似就命中；用户改名或移动后不满足路径条件时，不自动勾选，可手动设置。
- 先解析带引号、参数和环境变量的命令，再提取目标 EXE；快捷方式先解析实际目标。
- 只判断项目本身的启动目标；游戏的管理器路径指向平台，不代表游戏项目本身是平台。
- 若启动参数明确指向某款游戏，不应只因宿主 EXE 属于平台便将该游戏项目自动标为平台。
- `launcher.exe`、`client.exe`、`bootstrapper.exe` 等通用文件名优先使用“全路径中含有”的平台特征值补充判断；必要时结合产品元数据。证据不足时不自动勾选，仍允许手动设置。
- 平台识别只负责 `isplatform`，不代表新增该平台的游戏扫描、账号接入或游戏导入能力。
- 全部内置规则共用同一识别服务，新建项目与旧配置补齐使用相同规则，且都尊重已保存的显式值。

### 待明确事项

- 已确定通过全部使用任务项消除新条目的系统固定入口，旧系统固定项由用户自行取消。

### 实现约束

- 使用支持非打包桌面应用的 Win32 Jump List 接口。
- 统一进程、快捷方式和 Jump List 的 AppUserModelID。
- 根据系统返回的可显示条目额度安排内容，不写死为每组固定额度。
- 处理便携目录移动后的启动路径更新。
- 遵守仓库现有 Core、CLI、日志和文本资源管理规范。

## 后续需求

后续确认的需求继续追加至本文档，再统一安排实现。

## Google Play Games 游戏发现调研

实现方案已确认：使用读取本地 SQLite 应用库、DPAPI 解开应用库密钥、AES-256-GCM 解密、解析记录的路径；不以图片缓存枚举作为主要发现来源。

实现范围已确认：Google Play Games 游戏发现纳入本次统一实现，同时提供 GUI 扫描和 CLI 扫描入口，复用 Core 层同一扫描逻辑，并同步 CLI 帮助与内置接入文档。

调研日期：2026-10-30。在 eric-laptop 的实际安装环境中完成只读检查，并将 AppLibrary 状态单独导出至本机临时目录验证解密，未读取账号凭据数据。

- 当前游戏扫描仅接入 Steam、Epic 和 Xbox，尚无 Google Play Games 专用扫描。
- 数据库位于 `%LOCALAPPDATA%\Google\Play Games\store.db`，为 SQLite；应用库记录位于 `AppLibrary.AppLibraryState`，类型为 BLOB。
- 对应密钥文件为同目录下的 `app_library_encryption_key`，由 Windows DPAPI 保护。在笔记本当前用户上下文中使用 `ProtectedData.Unprotect`、`CurrentUser`、空附加熵成功解开，得到 32 字节密钥。
- 本机实测 AES-256-GCM 解密成功：BLOB 前 12 字节为 nonce，末尾 16 字节为认证 tag，中间为密文，无附加认证数据；认证校验通过。
- 本次密文 1933 字节，解密结果 1905 字节，包含 `com.supercell.clashofclans`、版本 `18.647.5`、游戏名称“部落冲突 (Clash of Clans)”及图片 URL。
- 解密内容还包含 Play 商店、Google Play 服务等系统组件，不能把所有包记录直接当作游戏。下一步需解析二进制记录结构、确认安装状态字段，并验证安装与卸载后的变化。
- 当前仅证明该版本、该机器上的应用库可解密，尚未证明其为跨版本稳定的完整已安装游戏枚举接口；图片缓存与开始菜单快捷方式只能作为辅助证据，不能单独作为卸载失效判断依据。
- 后续实现应在运行用户本机解开 DPAPI 密钥并在内存中处理，不要求用户导出密钥，不将密钥、原始库数据或明文写入日志及项目配置。
- 读取数据库时使用只读连接，正确处理 SQLite 并发与 WAL；数据库不可读、解密失败或记录结构不兼容时报告扫描不可用，不据此认定已导入游戏被卸载。
- 本版本按包名前缀过滤：排除以 `com.google` 或 `com.android` 开头的记录，比较忽略大小写；其余记录进入游戏发现候选集。此规则按用户确认执行，即便前缀下可能存在游戏也同样排除，不改为仅排除已知系统包名。
- 当前实机样本验证结果：共 8 个包记录，按上述前缀排除 7 个系统或内部组件后，仅剩“部落冲突 (Clash of Clans)”（`com.supercell.clashofclans`，版本 `18.647.5`）。该前缀过滤作为本版本已确认规则，不列为待验证事项。

### 已解密样本的信息

应用库明文可按 Protobuf wire format 完整遍历，但尚未获得官方 schema。以下字段名是根据值推断的含义，不是厂商声明的字段名。

样本共含 8 个包记录：

| 包名 | 显示名称 | 版本字符串 |
| --- | --- | --- |
| `com.supercell.clashofclans` | 部落冲突 | `18.647.5` |
| `com.google.android.apps.play.battlestar.playclientservice` | PlayClientService | `0.397.994542841` |
| `com.android.vending` | Google Play 商店 | `53.4.34-29 [0] [PR] 991240293` |
| `com.google.android.gms` | Google Play 服务 | `26.37.37 (160800-994713346)` |
| `com.android.settings` | 设置 | `14` |
| `com.android.permissioncontroller` | 权限控制器 | `33 system image` |
| `com.google.android.webview` | Android System WebView | `149.0.7827.5` |
| `com.google.android.play.games` | Google Play 游戏 | `2023.01.40470 (501577610.501577610-000800)` |

部落冲突包记录的基础子消息字段：

| 字段号 | 原始值 | 当前解释 |
| --- | --- | --- |
| 1 | `com.supercell.clashofclans` | 包名 |
| 2 | `180647006` | 疑似 Android versionCode，待验证 |
| 3 | `18.647.5` | 版本字符串 |
| 4 | 部落冲突 | 应用显示名称 |
| 5 | `1792919732455` | 疑似毫秒时间戳，具体事件含义未知 |
| 6 | `1793205559897` | 疑似毫秒时间戳，具体事件含义未知 |
| 7 | `972855262455` | 字符串标识，含义未知 |
| 12 | `7` | 数字标记，含义未知 |
| 13 | 空字节串 | 含义未知 |
| 15 | `1` | 数字标记，不能直接认定为已安装状态 |

部落冲突还有单独的展示元数据子消息，包含完整标题“部落冲突 (Clash of Clans)”及三个图片 URL，其中一个明确以 `com.supercell.clashofclans_card_logo.png` 结尾；另外两个图片的用途未验证。

库中还包含全零 UUID、库级数字标记及疑似更新时间戳，含义尚未确认。当前样本未识别出 Windows EXE 路径、完整游戏启动 URI、明确命名的安装状态、游戏分类、游玩时长或启动次数。开始菜单中读到的 `googleplaygames://launch/?id=com.supercell.clashofclans&lid=1&pid=1` 来自快捷方式，不属于数据库已解析字段。

游戏与系统组件的初步筛选按上述包名前缀排除规则执行。验收前需通过安装、更新、卸载对照样本确认安装状态字段含义和记录生命周期；保留字段缺失、未知字段、多用户或实例以及版本变化的兼容处理。

## WeGame 清单扫描

2026-10-31 在 Eric-Laptop 只读检查，客户端安装于 `C:\Program Files (x86)\WeGame`。

已接入统一 Core 扫描服务，GUI 全部扫描与 CLI `scan --wegame`、`scan --all` 共用实现；CLI 帮助及内置接入文档已同步。GUI 沿用现有统一扫描提示，不新增平台专用加载文案；CLI 专用文本仅存放于 CLI 资源区域并通过 Text.Cli 读取。

### 本地数据入口

- `%APPDATA%\Tencent\WeGame\WeGameCfg.info` 为 SQLite，存在 `Setting`、`user_0` 和账号对应表。
- `Setting` 中的 `GameCfgMgr.InstalledGameList` 为 24 字节 BLOB，`CfgMgr.UserSysSetting.InstallPathMap` 为 72 字节 BLOB；现已通过独立只读原型解码，格式见下方。
- 安装目录的 `data\json_db\base_game_oss.local`、`public_game_oss.local` 等不是明文 JSON 或标准 SQLite；本次未解析，不作为现成扫描依据。
- 游戏目录下 `rail_files\rail_game_identify.json` 是明文 JSON，可读取 `game_id`；该文件只能验证已找到的候选目录，不能独立枚举全部安装位置。

### 开源实现核对

- [FrameGen Manager](https://github.com/XiaoQAQ10086/FrameGen-Manager/blob/520646e930ff2c39f7907779656ff834434c0cd8/crates/core/src/scan.rs)：枚举 HKLM 下腾讯注册表键，找目录及可执行文件。其开发文档明确说明 WeGame 扫描未做真实环境验证，没有解析上述数据库。
- [Digitrace](https://github.com/LINGYIIIIIIII/digitrace/blob/e4edc212d17d7ff2d4e992921bbcbad4b35578d1/crates/core/src/games/platform.rs)：收集常见 WeGame 根目录，扫描 games/apps 并按文件名挑选 EXE，没有解码 WeGame 清单；不能据此假定能覆盖本机独立的 WeGameApps 游戏库。
- [game-manager](https://github.com/JiangHongtao1019/game-manager/blob/95c97da037e0e06c5846c33b2a1bae1c64f05c44/scanner.py)：枚举系统卸载记录，InstallLocation 缺失时从 UninstallString 反推目录，可借鉴候选目录定位方式。其开发记录也观察到游戏入口为 WeGameLauncher/launcher.exe。
- [wegame-launcher](https://github.com/SHORiN-KiWATA/wegame-launcher)：用于在 Linux 上安装和运行 WeGame 的包装器，没有提供本机游戏清单解码实现。
- 本次查到并核对的项目没有提供可直接采用的 WeGameCfg.info 安装清单解码器；这不代表不存在其他实现。

### 清单解码结果与主方案

通过客户端 `wgcore.dll` 中 CfgDB 查询逻辑定位到 `common.dll` 的 `oi_symmetry_decrypt2`，确认使用 QQTEA：64 位分组、128 位固定密钥、16 轮 TEA、大端字节序及 QQ 的双重异或分组链。数据库按字段加密，SQLite 文件整体无需解密。客户端二进制仅做静态分析，没有运行复制的客户端或向运行进程注入代码。

固定字段密钥十六进制为 `21235321395f402540412560402b2d24`，属于客户端内置常量，不是用户登录凭据。现有根目录 wgcore.dll 与 `wgcore\6.5.6\1\wgcore.dll` 均发现相同常量，跨版本长期稳定性尚未保证。扫描实现不需要分发或加载客户端 DLL，复现算法即可。

解码时要求密文长度至少 16 字节且为 8 的倍数；解出后按首字节低三位确定填充长度，跳过填充及盐，检查尾部七个字节为零，再取有效内容。零尾检查不等同于带 MAC 的完整性认证，仍需验证数据格式、ID、路径及文件存在性。

| Setting.key | 已验证明文格式 | 实机结果 |
| --- | --- | --- |
| `GameCfgMgr.InstalledGameList` | 逗号分隔的游戏 ID 文本 | `26,2001918` |
| `CfgMgr.UserSysSetting.InstallPathMap` | JSON 数组，内容是游戏库根目录；不是逐游戏 ID 到目录的映射 | `C:\Users\eric\Storage\Program\Lib\WeGameApps` |

路径数组原始值存在重复反斜杠，JSON 解析后应使用 Windows 路径规范化处理，不能破坏 UNC 路径。数据库只需读取上述两个 Setting 项，无需读取账号表或登录数据。

主扫描路径已有实机依据：

1. 用现有 SQLite 只读能力读取两个字段，QQTEA 解码并校验格式。
2. 解析安装 ID 集合与游戏库根目录数组。
3. 只枚举这些根目录的一级子目录，读取各自的 `rail_files\rail_game_identify.json`。
4. 仅接受 game_id 存在于安装清单的目录，按规范化路径去重，并检查 `WeGameLauncher\launcher.exe` 等已验证入口存在；不遍历全部磁盘或系统安装记录。
5. 主方案无法匹配的已安装 ID 单独报告，卸载记录仅保留为备选。游戏名称可先从目录取得，规范名称的缓存来源尚未解析，不能把可执行文件描述当成可靠名称。

对实际数据库副本的独立 Node.js 原型及生产 C# 实现均已验证：两个字段解码后分别得到上述 ID 与根目录，尾部校验通过；修改密文末字节以及截断密文的样本均被拒绝。生产实现使用共享的只读 SQLite 读取器，不加载客户端 DLL。

本地 367 项核心回归检查通过，覆盖真实密文样本、空清单、无效 ID、只读访问、GPG 读取器兼容、未安装目录排除、重复根目录去重、现有项目使用其他 EXE 时的匹配、导入保留 WeGame 标签与管理器以及 CLI 帮助。生产 CLI 在笔记本隔离配置下执行 `scan --wegame --json` 返回两个游戏、退出码 0，客户端数据库前后 SHA-256 一致；没有导入结果或启动游戏。

扫描名称取目录名并去除匹配 ID 的数字后缀，因此本机三角洲结果标题为 `DeltaForce`。导入时保留 WeGame 标签；若能定位客户端，则保存其管理器路径。项目属性中的平台标签能识别有 Rail 标识的 WeGameLauncher 路径；分类与导入按游戏目录识别同一游戏的不同 EXE，避免重复入库。

研究原型位于项目仓库外的 `C:\Project\.research\wegame-data\decode-config.cjs` 与 `verify-config.cjs`，不属于产品依赖。`data\json_db` 中的 `.local` 数据、独立配置文件 `game_library.info` 尚未解码，不把它们误记为已解决。

### 实机验证的备选路径

只读枚举 HKLM 32/64 位与 HKCU 的系统卸载记录，优先使用 InstallLocation，缺失时从卸载程序路径反推目录；要求目录存在且能读出 Rail 游戏标识，按目录去重，再确认启动入口存在。

| 名称 | game_id | 本机目录 | 确认存在的入口 |
| --- | --- | --- | --- |
| 英雄联盟 | 26 | `C:\Users\eric\Storage\Program\Lib\WeGameApps\英雄联盟` | `WeGameLauncher\launcher.exe` |
| 三角洲行动 | 2001918 | `C:\Users\eric\Storage\Program\Lib\WeGameApps\DeltaForce(2001918)` | `WeGameLauncher\launcher.exe` |

本次原型无预设游戏目录，从卸载记录成功得到上述两个结果；名称来自 DisplayName，ID 来自 Rail JSON，路径由卸载记录反推。英雄联盟另有 `HKCU\Software\Tencent\LOL\InstallPath`，可作为候选来源补充。本次没有启动游戏，不将文件存在性等同于启动成功。

卸载记录方案仅保留为备选，不能保证覆盖无卸载记录、移动后未更新注册表、旧版目录布局或其他账号的游戏。清单主方案也需补充库外手动添加游戏、旧版无 Rail 标识、安装/卸载/搬移对照及更多客户端版本的验证。不应照搬按最大/首个 EXE 猜入口的规则；需要优先复用已有快捷方式或验证过的平台启动入口。扫描未命中不能直接作为卸载判据。
