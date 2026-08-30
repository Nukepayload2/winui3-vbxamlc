# InternalDevDocs — WinUI3 VB XAML 编译器维护账本

本目录是 WinUI3VBXaml 维护者（MaterialBox `MagicTools/WinUI3VBXaml/main.md`）的长期任务状态与源码地图。目标：接手并做完微软半成品的 WinUI3 VB XAML 编译器。

## 结构

| 路径 | 用途 |
|------|------|
| `xaml-compiler-index.md` | XAML 编译器源码地图（轻量索引），引用编译器源码的唯一起点 |
| `decisions.md` | 维护决策账本（编号决策，唯一权威）——首个决策落地时建立 |
| `proposals\` | 特性提案（`proposal-<slug>.md`，六节模板） |
| `meetings\` | 提案评审会议纪要（`meeting-<slug>.md`，与提案 1:1） |
| `tasks\<slug>\` | 任务计划（README + design-overview + design-detailed + test-plan） |
| `spec\` | 已定案能力的产品 spec |
| `upstream-merge.md` | 上游 microsoft-ui-xaml 合并账本与原则 |

## 约定

- **证据纪律**：所有决策标注证据等级（阶梯：未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑，见 MaterialBox `manifest.md`「证据纪律」）；引用源码用 `文件:行号` 锚点，不凭记忆。
- **状态随仓库走**：本目录内容可提交、随仓库分发；临时工作材料放 `tmp\`（git-ignored，不入库）。
- **上游合并**：见 `upstream-merge.md`。
- **无副作用测试**：单元测试禁止网络请求 / 文件写入 / 启动进程 / 注册表写入；写不了就测不了的部分向用户说明并询问，不静默跳过。

## 入口

维护者开工先读：

1. `xaml-compiler-index.md`（源码地图）
2. `decisions.md`（若存在）

## 接手起点

VB 编译器半成品现状基线见 `xaml-compiler-index.md`「VB 现状基线」一节。首个任务 = 摸底：把基线扩展成完整的 VB 能力矩阵（每特性：生成是否通过 / vbc 能否编译 / 是否可运行），作为后续提案与任务计划的依据。
