# 色块连落（暂定名）

Godot + C# 原生下落益智游戏。进入游戏后直接显示棋盘，不设网页式标题、品牌栏、宣传文案或装饰面板。

**一个方块就是一个完整的四连方形状。每块只有一种颜色；三个及以上同色方块通过边接触连成一组时，整组消除。消除后其他形状保持完整地下落，并可能触发连锁。**

## 环境与启动

当前使用 **Godot .NET 4.7.2 + .NET SDK 8**。必须使用支持 C# 的 Godot .NET 版本，普通 Godot 版本无法运行此项目。

1. 在 Godot .NET 中导入根目录的 `project.godot`。
2. 构建 C# 项目，按 F5 运行主场景。
3. 默认进入三关挑战组；侧栏可直接选择各项挑战、三个学习关卡或自由模式。

本机命令：

```sh
dotnet build ChromaDrop.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

直接载入示例：

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . -- --demo
```

独立运行规则测试，不需要启动 Godot，也没有第三方测试包依赖：

```sh
dotnet run --project Tests/ChromaDrop.Tests.csproj
```

## 操作

| 按键 | 功能 |
| --- | --- |
| ← / → 或 A / D | 左右移动；支持长按连续移动 |
| ↑ 或 W | 顺时针旋转 |
| ↓ 或 S | 加速下落 |
| Space | 直接落地 |
| P / Esc | 暂停或恢复 |
| R | 重新开始 |
| F2 | 解谜模式显示提示；自由模式载入三消示例 |
| M | 开关音效 |
| Enter | 暂停时恢复，失败时重试，通关后进入下一关 |

失去窗口焦点时自动暂停。通关记录、语言、自由模式最高分和声音设置保存在 Godot 的 `user://progress.cfg`。

## 结构

- `Core/`：纯 C# 规则、方块生成、结算状态机，与 Godot 解耦。
- `Game/`：Godot 输入、棋盘绘制、必要 HUD、音效和本地保存。
- `Scenes/Game.tscn`：主场景，棋盘与 HUD 可以分别修改。
- `Assets/`：项目资源；当前几何图形由 Godot 绘制。
- `Tests/`：规则与状态机测试，失败时返回非零退出码。
- `Web/`：共享 C# 规则的 .NET WebAssembly 网页版。
- `.github/workflows/`：Windows / macOS 打包、Web 构建、GitHub Pages 和标签发布。
- `tools/`：校验 Godot 下载、导出桌面包和准备 Pages 路径的脚本。
- `docs/`：中文玩法、美术、开发与验证文档。
- `archive/web-prototype/`：保留早期网页实验，仅供对照，不属于当前 Godot 构建。

[挑战关卡](docs/挑战关卡.md) · [学习关卡](docs/关卡设计.md) · [玩法设计](docs/玩法设计.md) · [美术与资源](docs/美术与资源.md) · [开发与试玩](docs/开发与试玩.md) · [验证记录](docs/验证记录.md)

讨论使用英文，项目文档使用中文。界面支持英文、简体中文和日语。

## 自动构建与网页发布

主分支和 PR 自动运行测试并生成 Windows x64、macOS Universal 和 WASM 构建产物。`v*` 标签发布桌面 ZIP 到 GitHub Releases。网页使用同一份 C# 核心，界面维持棋盘与必要 HUD。

[下载最新 Nightly](https://github.com/clouds-game/2026-erosu/releases/tag/nightly)：每次成功的主分支构建及每天北京时间 02:23 更新同一个滚动预发布，包含 Windows / macOS 包、校验文件和构建提交信息。

详见 [持续集成与发布](docs/持续集成与发布.md)。仓库已按用户要求公开并启用 Pages。在线地址：[开始游戏](https://clouds-game.github.io/2026-erosu/)。

界面支持 English / 简体中文 / 日本語，可在游戏侧栏切换并记住偏好。详见 [界面国际化](docs/界面国际化.md)。
