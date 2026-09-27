# 中心方环 · Game0

Godot .NET 4.7.2 + .NET 8 的 13×13 中心式四连方原型。启动后直接进入新玩法。

## 试玩

在 Godot .NET 中导入 `project.godot`，构建 C# 项目后运行；或使用：

```sh
./tools/play
```

macOS 如未把 Godot .NET 安装在 `/Applications/Godot_mono.app`，可传入 `--godot /path/to/Godot`；Windows 可运行 `python tools/play.py`。

`./tools/play --demo` 会展示已投下一块和库存一块猫猫的局面。`./tools/play --demo -- --lock` 可检查红色 Lock 状态。

## 规则

- 13×13 棋盘中央有不可移动的白色核心。每 Hand 各有 `I/J/L/O/S/T/Z` 一块，自由选择使用顺序。
- 从 Hand 拖出普通四连方，在棋盘任意横向位置松手即可选定顶部入口；棋盘上沿的箭头和横线显示入口。按 `R`、`↑`、空格或拖拽时右键旋转。松手后方块快速下落，撞到结构时停在碰撞前的位置；若入口被堵住则返回原槽。一路没有碰到结构而穿出棋盘的块也返回原槽。
- 完整的中心方环同时消除；单次直接完成两环及以上可获得一块随机猫猫，库存最多四块。猫猫可从四边进入，拖动到希望的入口一侧即可投放。
- 消环后，剩余格子组成的连通块整体向中心移动。不能整体移动的块原位悬浮，仍可作为之后的连接点。
- 每四次成功投放旋转棋盘 90°，顺时针与逆时针交替，方向和剩余步数提前显示。
- 每 Hand 共用 75 秒绿色时间。耗尽后红条保持满格，只能按白框顺序使用剩余普通块；猫猫始终可以插入使用。
- 当前可用普通块没有合法落点时，仍能用猫猫救场；普通块和猫猫均无合法投放时结束。
- 每次完成 1/2/3/4 个环分别得 100/300/600/1000 分。颜色只用于区分形状。

暂停按钮或 `Esc` 暂停；拖拽中按 `Esc` 取消；`F5` 重新开始。

## 开发

```sh
dotnet run --project Tests/ChromaDrop.Tests.csproj
```

- `Core/Game0/Game0Session.cs`：确定性的棋盘、投放、消环、收缩、Hand、Lock 和猫猫规则。
- `Game/Game0Controller.cs` 与 `Scenes/Game0.tscn`：Godot 输入及画面。
- `Assets/PixelUI/`：项目现有 Kenney 像素界面资源；`Assets/Cats/`：经用户选择的黑猫头素材及 CC BY 4.0 来源记录。
- `Tests/Game0Tests.cs`：Game0 规则测试。

旧版 Web、AI 和回放实验仍保存在分支中，尚未迁移到新玩法。
