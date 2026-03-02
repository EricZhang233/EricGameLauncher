# 永久守则

## 文件修改规则（全链路强制）

**禁止使用任何编辑器工具以外的手段修改项目文件。**

具体约束：

- ❌ 禁止通过 `run_in_terminal` 执行 PowerShell / CMD / bash 等 Shell 命令来写入、覆盖或删除文件
- ❌ 禁止使用任何终端命令间接修改文件内容（包括 `Set-Content`、`Out-File`、`tee`、重定向符 `>`、`sed`、`awk` 等）
- ✅ 所有文件创建、编辑必须且只能通过以下编辑器工具完成：
  - `create_file`
  - `replace_string_in_file`
  - `multi_replace_string_in_file`
  - `edit_notebook_file`

**理由：** 终端命令修改文件绕过了逐行可审查性，无法在编辑器中追踪差异，不符合代码审查要求。
