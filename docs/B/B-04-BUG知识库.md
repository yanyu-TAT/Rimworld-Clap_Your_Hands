# B-04 BUG 知识库

> 记录本模组开发中遇到的典型 Bug、根因与修复。**根因属于规范缺失时，必须反哺 `docs/B/B-01-开发规范.md` 与根目录 `AGENTS.md`。**

## 收录格式

```
### BUG-00X 标题
- 现象：
- 现场（版本/复现步骤/日志原文）：
- 根因：
- 修复：
- 规范反哺：（无 / 已补充到 B-01 第 X 节 + AGENTS.md 第 X 章）
```

## 已收录

### BUG-001 `Tried to add health diff to missing part` —— 原版 `defaultInstallPart` 不过滤缺失部位

- **现象**：击掌触发增益时，`HediffSet.AddDirect` 报
  `Tried to add health diff to missing part BodyPartRecord(Hand parts.Count=5)`，增益施加失败。
- **现场**：**单臂小人**（一只手被截肢）触发完美击掌。堆栈：
  `ApplyPerfectReward → ApplyBuff → GetOrAddHediff → AddHediff → AddDirect → Log.Error`
- **根因**：`Clap_Buff` 当时使用 `<defaultInstallPart>Hand</defaultInstallPart>`，而原版 `Pawn_HealthTracker.AddHediff(Hediff, BodyPartRecord, ...)` 的自动挑选逻辑是：

  ```csharp
  if (part == null && hediff.def.defaultInstallPart != null)
      part = pawn.RaceProps.body.AllParts
          .Where((BodyPartRecord x) => x.def == hediff.def.defaultInstallPart)
          .RandomElement();     // ← body.AllParts 含【已缺失】部位，且此处无 PartIsMissing 过滤
  ```

  `body.AllParts` 是**身体定义的全部部位**（不随截肢变化），因此单臂小人有一半概率被挑中那只已截肢的手。
  又因为权重前置检查用的是「**至少一只手**」口径（单臂本就允许击掌），所以必然会踩中。
- **修复**：① 撤销 `defaultInstallPart`；② 新增 `ClapUtility.TryGetHand(Pawn, out BodyPartRecord)` —— 自行筛选**可用**的手（复用静态 `List` 以免热路径分配），把结果作为 `part` 传给 `GetOrAddHediff`。

  ```csharp
  TryGetHand(pawn, out BodyPartRecord hand);
  var buff = pawn?.health?.GetOrAddHediff(ClapDefOf.Clap_Buff, hand) as Hediff_ClapBuff;
  ```

  「可用手」的判定随后按用户决策扩展为三态（见 `plans` 附录 C.9）：自然手 / 手位植入体 / **祖先被植入体替换的「机械手」** 均可用；真正被截肢的手不可用。

- **规范反哺**：已补充到 `AGENTS.md`「八、AI 高频错误防犯清单」第 2 条 —— **复用原版的「自动挑选/自动填充」逻辑前，必须先反编译核对其筛选条件**。

#### 教训

「原版有现成的 X」不等于「原版的 X 适用于我的场景」。判定链条要完整走通：
**原版做了什么筛选 → 我的数据是否满足这些前置 → 不满足时会怎样**。
本次的 `body.AllParts`（含缺失部位）这一事实，人家在写 `HasHands` 时**已经用到**了（正因如此才要配 `PartIsMissing` 判断），却没有把它与 `defaultInstallPart` 的 `RandomElement()` 联系起来。

---

### BUG-002 同一报错复发：判定「机械手可用」却把 Hediff 挂到了缺失部位

- **现象**：与 BUG-001 **相同的报错文本** `Tried to add health diff to missing part BodyPartRecord(Hand parts.Count=5)`，但触发路径不同。
- **现场**：**装了仿生臂**的小人触发完美击掌。堆栈同样为 `ApplyBuff → GetOrAddHediff → AddHediff → AddDirect`。
- **根因**：BUG-001 修复后，`TryGetHand` 用 `PartIsMissing(part) && !AncestorHasDirectlyAddedParts(part)` 判定可用性 ——
  装仿生臂时 `Hand` **既**是 MissingPart（递归标记，见 `C-05` 结论 33）、**又**有祖先 AddedPart，于是被判为「可用」，
  但返回的是**那只缺失的 `Hand` 部位本身**。而 `HediffSet.AddDirect` 有硬约束：

  ```csharp
  if (hediff.Part != null && !GetNotMissingParts().Contains(hediff.Part))
  {
      Log.Error("Tried to add health diff to missing part " + hediff.Part);
      return;      // ← 被拒绝，Hediff 根本没挂上
  }
  ```

- **修复**（用户指出方向：挂到执行替换的父部位）：新增 `ClapUtility.ResolveHandAnchor(hediffSet, hand)` ——
  手本身存在 → 返回手；手因祖先被植入体替换而缺失 → 沿 `part.parent` 上溯，返回第一个 `HasDirectlyAddedPartFor(part)` 为真的部位
  （如仿生臂所在的 `Shoulder`）；真·截肢 → 返回 `null`（不可用）。

  ```csharp
  private static BodyPartRecord ResolveHandAnchor(HediffSet hediffSet, BodyPartRecord hand)
  {
      if (!hediffSet.PartIsMissing(hand))
          return hand;

      for (BodyPartRecord part = hand.parent; part is not null; part = part.parent)
      {
          if (hediffSet.HasDirectlyAddedPartFor(part))
              return part;
      }

      return null;
  }
  ```

- **规范反哺**：`AGENTS.md` 第 2 条已覆盖「核对筛选条件」，本条补充其**推论** —— 筛选正确还不够，
  **返回的目标也必须是原版允许挂载的实体**。

#### 教训

**「判定可用」与「能挂上去」是两个不同的条件**：
- 可用性看的是**语义**（这只手算不算一只手 → 机械手算）
- 挂载看的是**原版的硬约束**（`GetNotMissingParts().Contains(part)` → 缺失部位一律不行）

两者必须分别满足，不能用一个判据同时充任。

---

## 待验证的高风险点（来自前期架构侦察）

以下为**尚未在本项目中实际发生**的预判风险，实现时重点验证，命中后转为正式条目：

| 预判风险 | 依据 | 验证方式 |
| --- | --- | --- |
| ~~用 `SetDuration` 等不存在的方法设置 Hediff 时长~~ **已证伪** | ~~同类项目经验（API 在 1.6 不存在）~~ | **核实结论：1.6 的 `HediffComp_Disappears.SetDuration(int)` 确实存在且可随时调用**（见 `C-05` §7.3）。原先的规避属过度保守 —— 教训：跨版本的 API 印象不可直接沿用，必须以当前版本反编译为准 |
| Hediff `severity = 0` 被立即移除 | RimWorld 通用行为 | 游戏内观察增益是否瞬间消失 |
| 隐藏 Hediff 出现在健康列表 | UI 默认行为 | 游戏内检查健康标签页 |
| 好感度写入数值与设计值不符 | `AddInteractionThought` 会乘 `SocialImpact` | 游戏内对比 before/after opinion |
| 互动权重过高导致击掌刷屏 | 原版权重基准（Chitchat 1.0 / DeepTalk 0.075） | 游戏内观察社交日志密度 |
| 冷却状态存读档后丢失 | 若误用裸 static | 存档 → 读档 → 检查冷却 |
