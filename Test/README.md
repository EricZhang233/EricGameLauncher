# 项目测试

在仓库根目录执行：

```powershell
dotnet test
```

这是唯一对外测试入口，不需要参数、分区或额外命令。一次运行全部自动测试：生产与测试依赖审查、Core 单元测试、业务与 CLI 集成、更新器与日志压力、发布工作流契约、隔离 Release 包校验，以及一次窗口启动和正常退出检查。

需要 Windows 11 24H2 或以上、.NET 10 SDK、Windows App SDK 1.8 运行时、Windows SDK、Node.js 和 PowerShell 7。先关闭启动器；检测到已有进程会明确失败，不替用户关闭窗口。测试期间会启动隔离的测试窗口。

每阶段显示进度；依赖审查、构建、断言、超时或清理失败均使 dotnet test 返回非零退出码。失败后的 not_run 不算通过。测试通过不会自动提交、推送或发布，普通 build/run 和 GitHub 发布不运行测试套件。

## 结果

完整报告在 `Test/artifacts/machine-tests/<时间与编号>/`：

- `summary.md`：各阶段状态、耗时和日志入口。
- `summary.json`：机器可读状态及清理结果。
- `00.log` 等：完整命令、stdout、stderr 和断言结果。
- TRX：Core 单元测试结果。
- `lifecycle.json`：测试包一次启动和退出的资源采样。

测试数据、测试工程输出及测试包隔离在系统临时目录，完成后清理，不修改日常游戏库，不生成正式发行包。内部运行器和测试子工程由 dotnet test 自动调用，无需单独操作。

覆盖矩阵见 [COVERAGE.md](COVERAGE.md)。完整自动测试不能代替强制更新交互、真实设备、UAC、多屏和长期资源趋势的手动验收；实际 GitHub 发布仍需后续实际验证。
