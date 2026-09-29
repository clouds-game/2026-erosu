# CNN 与 PPO：整块棋盘观察与放置策略

## 架构

C# 继续执行唯一的游戏规则；Python/PyTorch 使用 JSON 观察并选择动作。
没有复制消除、重力或计分；没有直接读取隐藏颜色袋、未来 RNG 或测试种子答案。

- `AI/neural.py`：九通道棋盘编码、73 维元信息、787,337 参数 actor-critic。
- `AI/rl_env.py`：一次整块放置作为一步；通过外部 tick 完成消除与重力。
- `AI/ppo.py`：PPO 训练、独立种子评估、轨迹录制和精确回放验证。
- `GameSession.Placement/Place`：与手动旋转共用踢墙算法，验证并执行真实可达路径。

棋盘为 `9 × 18 × 10`：前七个通道表示颜色，空格均为零；后两个通道表示该格与
右侧/下侧是否属于同一完整块。原始块 ID 不进入网络，相邻同色的不同块仍可区分。

元信息包含当前块及下三个预告块（每块形状 7 维、颜色 7 维）、颜色袋频率 7 维、
每整块额外奖励 7 维、任务类型 2 维、剩余预算比例 1 维。元信息只针对自由模式。
观察发生在新块刚生成时，不支持任意旋转/移动后的中间姿态作为训练输入。

网络：三层 3×3 卷积（16/32/32 通道，padding=1，ReLU），保留绝对位置后展平到
128 维；元信息 MLP 到 64 维；拼接后经过 128 维共享层，输出 40 个动作 logits 和一个状态价值。

这是带隐藏袋状态的部分可观察任务。当前网络没有记忆，也不跟踪历史供给；
名义袋频率不等于此刻袋内的条件概率。CNN 并不自动解决这一限制。

## 动作与时间

`action = rotation_count × 10 + target_left_column`，共 40 个槽位。
先旋转 0–3 次，再水平移到目标列，最后硬降。每一步的碰撞、踢墙均由 C# 检查。
O 块非零旋转无效；其他等价朝向可能有重复动作。本版本不搜索先平移再旋转或滑入悬空区的路径。

`placements` 返回所有槽位的实际落点或 null；只读，不推进时间和 RNG。
PPO 将无效槽位概率屏蔽为零。`place` 先完整验证，再提交；被阻挡时不会留下半途移动。
宏动作只锁定一次，之后 Python 逐 tick 等待消除和重力结束，不消耗下一块的思考时间。

## 目标和 PPO

两个模型分开训练：

- survival：每锁定一个完整块奖励 1，无额外分数奖励。
- score：每步真实游戏分数增量 / 1000，无生存奖励。

两者都采用最多 300 块的有限任务；预算比例进入观察。死亡与预算结束均停止该任务的
价值 bootstrap，但报告区分 `terminated` 和 `capped`。rollout 中途截断则 bootstrap
下一观察的价值，GAE 不越过 episode 边界。

PPO 参数：gamma=0.99，lambda=0.95，clip=0.2，Adam lr=0.0003，
value coefficient=0.5，entropy coefficient=0.01，梯度范数上限 0.5。
默认 rollout=256、每次更新 4 epochs、minibatch=64，标准化优势。
因此实际优化的是折扣回报，并非严格的无折扣最终得分/总生存长度。

单个持久 C# 子进程串行采样，每局重新抽训练种子（0–999999），预算中不使用固定测试种子。
检查点保存网络、任务配置、训练种子、步数和指纹；不是包含优化器及 RNG 状态的可续训存档。
评估默认贪心动作，可用 `--sample-actions` 复现按概率采样的评估；两者需分别报告。

## 运行

```sh
python3 tools/play.py --headless --prepare-only
python3 -m venv .local/rl
.local/rl/bin/pip install -r AI/requirements-rl.txt

.local/rl/bin/python -m AI.ppo train --objective survival \
  --model build/ai/ppo-survival.pt --steps 8192 --seed 42
.local/rl/bin/python -m AI.ppo train --objective score \
  --model build/ai/ppo-score.pt --steps 8192 --seed 43

.local/rl/bin/python -m AI.ppo evaluate --model build/ai/ppo-survival.pt \
  --seed 4000000 --episodes 30 --report build/ai/ppo-survival-eval.json
.local/rl/bin/python -m AI.ppo evaluate --model build/ai/ppo-score.pt \
  --seed 4000000 --episodes 30 --report build/ai/ppo-score-eval.json

.local/rl/bin/python -m AI.ppo play --model build/ai/ppo-survival.pt \
  --seed 4000000 --trace build/ai/ppo-survival-replay.jsonl
```

Windows 使用 `.local/rl/Scripts/python` 和对应 pip。代码默认 CPU，限制线程数为 2；
无 GPU/MPS 依赖。PyTorch 是可选依赖，不影响普通规则引擎、Godot、Web 或原 CEM AI。
录制拒绝覆盖已有轨迹；结束后自动逐响应回放验证。模型和大轨迹保存在忽略的 `build/` 下。

评估读取检查点的目标、颜色配置和预算，拒绝训练种子重叠。
不要用同一测试集反复选择训练时长/参数，否则应另留最终测试集。

## 验证

```sh
.local/rl/bin/python -m unittest discover -s tools -p 'test_*.py'
dotnet run --project Tests/ChromaDrop.Tests.csproj -c Release
```

测试覆盖颜色相同但块身份不同的观察、块 ID 重命名不变性、非法动作概率为零、实际 PPO
权重更新、GAE episode 边界、得分奖励结算、预算结束、放置查询只读性，以及宏动作与
手动旋转/移动/硬降的逐状态等价。C# 测试额外验证几何落点存在但水平路径被挡时禁止放置。
CI 有独立 PyTorch CPU 测试任务；未安装依赖的普通 Python 环境跳过神经网络测试。

## 参考

- [PyTorch PPO 教程](https://docs.pytorch.org/tutorials/intermediate/reinforcement_ppo.html)
- [Invalid Action Masking 研究](https://arxiv.org/abs/2006.14171)

本实现直接使用 PyTorch，并未引入 TorchRL/Godot RL 插件：当前共享规则已提供外部 tick、
JSON 和轨迹，插件会增加第二套环境桥接。此前已评估 Godot AI/Gym 接入方向。

## 2026-09-27 首次短训练结果

两个独立模型各训练 8,192 次放置，survival 种子 42、score 种子 43。
固定测试集 4000000–4000029，每局预算 300；未用测试结果选择检查点或调参。
测试随机种子和前一轮颜色压力测试不同，不能直接逐行与旧报告比较。

| 策略 | 动作选择 | 平均生存块数 | 平均得分 |
|---|---|---:|---:|
| 原随机落点策略 | 随机 | 19.10 | 1,393 |
| 原手写六特征策略 | 贪心 | 137.03 | 98,010 |
| 原五色 CEM 权重 | 贪心 | 107.80 | 75,383 |
| CNN survival 初始化 | 贪心 | 11.93 | 543 |
| CNN survival 训练后 | 贪心 | 25.87 | 3,250 |
| CNN survival 训练后 | 概率采样 | 24.00 | 2,417 |
| CNN score 初始化 | 贪心 | 20.67 | 1,497 |
| CNN score 训练后 | 贪心 | 12.20 | 387 |
| CNN score 训练后 | 概率采样 | 20.00 | 1,160 |

初始化模型分别使用对应训练种子重建，权重与训练前相同。后续训练命令自动保存
`*.initial.pt`，可用相同 evaluate 命令比较。初始化贪心并不等于随机策略，不能混为一谈。
所有神经策略测试均自然终止，无预算截断。原启发式和神经策略的动作候选/合法性处理
不同，因此它们是端到端基线对比，不是只替换打分器的严格消融实验。

结论：生存策略在这次短训练中改善，但离已有启发式仍很远；得分策略没有显示改善，
贪心部署尤其差，采样也没有证明优于随机。不能宣称模型学会稀有色规划或七色问题已解决。
每个目标只有一个训练种子，尚不足以判断方法的稳定性。下一步应增加训练预算并以多个训练
种子验证，或研究更有效探索；不能只挑选少数漂亮回放宣称成功。

原始训练曲线、每局结果、检查点与引擎指纹见
[实验 JSON](../AI/reports/ppo-initial-experiment.json)。本机检查点为
`build/ai/ppo-survival.pt`、`build/ai/ppo-score.pt`，未提交二进制权重到仓库。
生存策略 seed=4000000 的 22 块轨迹已逐响应验证通过；该局零分，保留作为真实结果。

后续固定架构的教师示范、克隆初始化与多种子 PPO 对照见[模仿学习实验](模仿学习实验.md)。

## 自由模式规则组合

十二种颜色和可选规则组合使用观察版本 2 与同一个策略网络，详见 [多模式 RL 训练](多模式RL训练.md)。原实验的观察版本 1 保持兼容。
