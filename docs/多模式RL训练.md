# 多模式 RL 训练

目标是一个策略检查点适配自由模式的十二种组合：三种颜色配置 × 固定色块开关 × 闭合填充开关。
规则在每局内保持不变，不在游戏中途切换。第一轮仍优化存活，得分只作为评估指标。

## 观察版本

原有九通道、73 维元数据保持为版本 1，旧 PPO、行为克隆、候选排序实验仍可加载。
版本 2 使用 11 个棋盘通道与 189 维元数据：

- 原有七种颜色和两个整块连接通道；任意大小的填充连通块直接使用 cells 与连接关系编码。
- 新增固定格通道，区别受重力与不受重力的块。
- 新增闭合区域历史通道，包含已记录的闭合格；仅看当前棋盘无法判断旧洞是否会填充。
- 保留活动块、三个普通落块、颜色概率、稀有奖励、目标和剩余预算。
- 新增两个规则开关、最多七项按真实顺序排列的 forecast，以及两个随机结算待定标记。

JSON state 新增 known_enclosed_cells，行优先排序，不消耗随机数。预览复制独立历史，避免泄露或修改真实对局。
固定块的未来位置依旧未知。版本 2 拒绝缺少历史字段的旧引擎，避免静默使用不完整观察。

网络仍为小型 CNN actor-critic，共 795,049 个参数。旧策略升级时扩展第一层卷积和元数据层，新增输入权重置零，
保留原有输出；随后混合模式 PPO 学习新增输入。旧观察版本不静默转换为新模式。

## 混合采样

每轮随机打乱十二种模式，逐局轮换；完整周期每种模式出现一次。游戏种子与模式顺序使用独立随机流。
这保证局数接近平衡，不保证放置步数相等，因为不同模式的存活长度不同。日志报告各模式完成局数。
每个训练步执行一个合法普通块落点，使用外部 tick 完成结算，奖励为存活一块的 1 分。

本次从本地已有 survival warm-42 策略开始，继续训练 16,384 个放置步、随机种子 45，预算每局 300 块。
训练初始策略及训练后策略分别使用相同的二十个独立种子 10000000–10000019，逐模式评估，不用这些种子选择检查点。
初始策略已经迁移到版本 2，但新输入权重尚未学习；因此对照衡量同一策略经过混合训练后的变化。
只训练一个随机种子的策略；结果是首轮实验，不代表所有模式已解决。

## 命令

```sh
# 先构建当前 JSON 引擎
./tools/play --headless --prepare-only

.local/rl/bin/python -m AI.ppo train --mixed-features \
  --init-model build/ai/imitation/warm-42.pt \
  --model build/ai/multi-mode-20260928/policy.pt \
  --objective survival --max-pieces 300 --steps 16384 --seed 45

# 默认评估检查点内的全部模式，episodes 是每种模式的局数
.local/rl/bin/python -m AI.ppo evaluate \
  --model build/ai/multi-mode-20260928/policy.pt \
  --seed 10000000 --episodes 20 --report build/ai/multi-mode-20260928/after-20.json

# 同一检查点直接运行两项规则同时开启的七色模式，并验证回放
.local/rl/bin/python -m AI.ppo play \
  --model build/ai/multi-mode-20260928/policy.pt \
  --game-mode rare_seven_anchored_fill --seed 10000000 \
  --trace build/ai/multi-mode-20260928/combined-replay.jsonl
```

模式键为 classic / rare_six / rare_seven，可依次追加 _anchored、_fill。
--game-mode 可用于单模式训练、选择一个模式评估或指定回放模式；未指定回放模式时采用检查点的首个模式。
旧检查点不能选择训练范围外的规则组合。二进制权重留在本地 build 目录，不提交 Git。
复现本次迁移需要已有 warm-42；省略 init-model 可从头训练同样的混合模式网络。

参考：[PyTorch 官方 PPO 教程](https://docs.pytorch.org/tutorials/intermediate/reinforcement_ppo.html)。

## 首轮结果

每种模式相同的 20 个独立种子，共 240 局/策略。训练前平均存活 38.63 块，训练后 46.51 块，提升 20.4%；
平均得分从 38156.67 提高到 47416.25。十二种模式的平均存活都提高，没有一局达到 300 块上限。

| 模式 | 训练前平均块数 | 训练后平均块数 |
| --- | ---: | ---: |
| classic | 32.85 | 36.15 |
| classic_fill | 54.35 | 78.25 |
| classic_anchored | 29.80 | 32.95 |
| classic_anchored_fill | 45.65 | 62.75 |
| rare_six | 35.10 | 44.40 |
| rare_six_fill | 58.20 | 64.05 |
| rare_six_anchored | 28.95 | 38.35 |
| rare_six_anchored_fill | 38.10 | 46.55 |
| rare_seven | 32.40 | 36.55 |
| rare_seven_fill | 40.65 | 45.15 |
| rare_seven_anchored | 28.90 | 30.35 |
| rare_seven_anchored_fill | 38.65 | 42.60 |

完整种子、模式分布、哈希及逐局结果：[实验报告](../AI/reports/multi-mode-ppo.json)。
两个新规则同时启用的对局已生成 combined-replay.jsonl，并用当前引擎逐步验证通过。
仅一个训练随机种子，不能由这次平均提升推断长期稳定性；后续优先增加训练预算并检查稀有七色固定块模式。
