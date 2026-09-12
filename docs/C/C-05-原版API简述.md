# 原版 API 简述（反编译证据库）

> 本文件按系统分类收录 RimWorld 1.6 的**已核实** API 事实（来源：`ilspycmd` 反编译）。
> 每条结论标注来源类型，未经验证的内容必须显式标注「无证据」。持续扩充。

环境基准：RimWorld 1.6，程序集 `Assembly-CSharp.dll`（Steam 本地安装）。

***

## 一、社交互动系统

### 1.1 触发调用链（来源：`RimWorld/Pawn_InteractionsTracker.cs`）

```
Pawn.interactions.InteractionsTrackerTickInterval(delta)   // 每 60 tick 检查一次
  └─ 按 RandomSocialMode 取 MTB：Quiet=22000 / Normal=6600 / SuperActive=550
  └─ Rand.MTBEventOccurs(...) 命中 → TryInteractRandomly()
       └─ 遍历 pawn.Map.mapPawns.SpawnedPawnsInFaction(pawn.Faction)（Shuffle 后）
            └─ 过滤：p == pawn / !CanInteractNowWith(p) / !CanReceiveRandomInteraction(p) / pawn.HostileTo(p)
            └─ allDefsListForReading.TryRandomElementByWeight(x => CanInteractNowWith(p, x) ? x.Worker.RandomSelectionWeight(pawn, p) : 0f, out result)
                 └─ TryInteractWith(p, result)
```

**结论 1**：新增 `InteractionDef` + 自定义 `InteractionWorker` 会被原版**自动纳入随机社交池**，无需任何补丁。
**结论 2**：候选人抽取时**原版已排除敌对**（`pawn.HostileTo(p)`）与不可互动者；「仇视」（非敌对但好感度极低）必须在 `RandomSelectionWeight` 中返回 0 自行排除。

### 1.2 候选门槛（来源：`RimWorld/SocialInteractionUtility.cs`）

| 方法 | 门禁 |
|---|---|
| `CanInitiateInteraction(pawn, def)` | 有 `interactions`、`Talking` 能力、`Awake()`、未燃烧、非禁止社交的 mutant、`!IsInteractionBlocked` |
| `CanInitiateRandomInteraction(p)` | 上述 + Humanlike、非 Downed、非 AggroMentalState、`Faction != null`、生命阶段允许、`!Inhumanized()` |
| `CanReceiveInteraction(pawn, def)` | `Awake()`、未燃烧、非禁止社交 mutant、`!IsInteractionBlocked` |
| `CanReceiveRandomInteraction(p)` | 上述 + Humanlike、非 Downed、非 AggroMentalState |
| `IsGoodPositionForInteraction` | 距离 ≤ **6f** 且有视线 |
| `Pawn_InteractionsTracker.CanInteractNowWith(recipient, def)` | 非「过近互动」+ 位置合适 + 双方门禁 |

常量：`SocialInteractionUtility.MaxInteractRange = 6f`；`Pawn_InteractionsTracker.RandomInteractIntervalMin = 320`、`InteractIntervalAbsoluteMin = 120`、`DirectTalkInteractInterval = 320`。

### 1.3 互动定义字段（来源：`RimWorld/InteractionDef.cs`）

```csharp
public class InteractionDef : Def {
    private Type workerClass = typeof(InteractionWorker);   // XML <workerClass>，原版惯例写简名
    public ThingDef interactionMote;                        // 缺省 → ThingDefOf.Mote_Speech
    public float socialFightBaseChance;
    public ThoughtDef initiatorThought;                     // 固定想法（我们做随机结果时不用它）
    public SkillDef initiatorXpGainSkill; public int initiatorXpGainAmount;
    public ThoughtDef recipientThought;
    public SkillDef recipientXpGainSkill; public int recipientXpGainAmount;
    public bool ignoreTimeSinceLastInteraction;             // 跳过 120 tick 硬下限检查
    public InteractionSymbolSource symbolSource;
    public RulePack logRulesInitiator;                      // 日志文本规则（rulesStrings）
    public RulePack logRulesRecipient;
    public InteractionWorker Worker { get; }                // 懒加载单例，自动注入 interaction
}
```

### 1.4 工作器扩展点（来源：`RimWorld/InteractionWorker.cs`、`InteractionWorker_Chitchat.cs`、`InteractionWorker_DeepTalk.cs`）

```csharp
public class InteractionWorker {
    public InteractionDef interaction;
    public virtual float RandomSelectionWeight(Pawn initiator, Pawn recipient);  // 默认 0f = 不抽取
    public virtual void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
        out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets);
}
```

权重基准（**重要**）：
- `InteractionWorker_Chitchat`：常数 `1f`（闲聊是绝对主力）
- `InteractionWorker_DeepTalk`：`0.075f × CompatibilityFactorCurve.Evaluate(initiator.relations.CompatibilityWith(recipient))`，
  曲线点位 `(-1.5f,0f) (-0.5f,0.1f) (0.5f,1f) (1f,1.8f) (2f,3f)`
- `InteractionWorker_KindWords`：有 Kind 特质时 `0.01f`

**结论 3**：**原版范式 = `SimpleCurve` 做关系映射**，击掌的「好感度影响概率」应照此实现，而非手写阶梯 if。

### 1.4.1 原版加权抽取 API（来源：`Verse/GenCollection.cs`）

```csharp
public static T RandomElementByWeight<T>(this IEnumerable<T> source, Func<T, float> weightSelector);
public static T RandomElementByWeightWithFallback<T>(this IEnumerable<T> source, Func<T, float> weightSelector, T fallback = default(T));
public static bool TryRandomElementByWeight<T>(this IEnumerable<T> source, Func<T, float> weightSelector, out T result);
public static T RandomElementByWeightWithDefault<T>(this IEnumerable<T> source, Func<T, float> weightSelector, float defaultValueWeight);
```

`TryRandomElementByWeight` 实现要点：
- `source` 为 **`IList<T>` 时走数组快速路径**（两次遍历 + 减法抽取），否则退化为枚举器路径
- **权重总和为 0 时返回 `false`** —— 不是死循环、也不是异常，而是内置的「权重耗尽」保护
- 负权重会 `Log.Error` 并当作 0 处理
- 单元素且权重 > 0 时直接返回该元素

**结论 3.1**：**不要手写累积权重抽取**。`RandomElementByWeightWithFallback` 已经提供了「权重全 0 时回退到指定值」的语义，且对数组输入自动走快速路径 —— 把候选集合声明为**数组**即可白拿性能，无需任何额外保护代码。

**结论 3.2**：当权重下标与枚举值一一对应时（如 `ClapOutcome`），可直接用
`AllOutcomes.RandomElementByWeightWithFallback(o => weights[(int)o], fallback)`，无需自建索引映射。
> 代价是 `weightSelector` 为闭包（每次调用分配一个小对象）；**非热路径**（互动结算等低频入口）完全可接受，原版自身大量如此使用（如 `TryInteractRandomly`）。

### 1.5 互动结算顺序（来源：`RimWorld/Pawn_InteractionsTracker.TryInteractWith`）

```
CanInteractNowWith 校验 → 120 tick 硬下限校验（除 ignoreTimeSinceLastInteraction）
→ initiatorThought → recipientThought（经 AddInteractionThought，需 recipient.needs.mood != null）
→ 双方技能经验（recipient 需 RaceProps.Humanlike）
→ CheckSocialFightStart（可能转为打架，转打架则不调用 Worker.Interacted）
→ Worker.Interacted(...)
→ MoteMaker.MakeInteractionBubble(...)     // 互动气泡，不会因结果不同而跳过
→ 双方 lastInteractionTime / lastInteraction / lastInteractionDef 更新
→ Find.PlayLog.Add(new PlayLogEntry_Interaction(...))
→ letterDef 非空则 Find.LetterStack.ReceiveLetter(...)
```

### 1.6 情绪与好感度（来源：`RimWorld/Pawn_InteractionsTracker.AddInteractionThought`、`RimWorld/ThoughtDef.cs`）

```csharp
// public static —— 可被模组直接调用
public static void AddInteractionThought(Pawn pawn, Pawn otherPawn, ThoughtDef thoughtDef) {
    if (pawn.needs.mood == null) return;
    float statValue = otherPawn.GetStatValue(StatDefOf.SocialImpact);
    Thought_Memory m = (Thought_Memory)ThoughtMaker.MakeThought(thoughtDef);
    m.moodPowerFactor = statValue;
    if (m is Thought_MemorySocial social) social.opinionOffset *= statValue;
    pawn.needs.mood.thoughts.memories.TryGainMemory(m, otherPawn);
}
```

**结论 4**：**没有「直接增加好感度」的 API**。opinion 由记忆想法派生，标准做法是定义 `ThoughtDef`（`thoughtClass` 为 Social 型 → `IsSocial == true`，`stages[].opinionOffset` 提供好感度），再经上述方法写入。注意**好感度会被 `SocialImpact` 缩放**（不是裸 +3/+5）。

`ThoughtDef` 关键字段：`stages`(`ThoughtStage` 列表)、`durationDays`、`stackLimit`、`stackLimitForSameOtherPawn`、`stackedEffectMultiplier`(默认 0.75)、`maxCumulatedOpinionOffset`、`lerpOpinionToZeroAfterDurationPct`(默认 0.7)、`effectMultiplyingStat`、`neverNullifyIfAnyTrait`、`nullifyingTraits`、`taleDef`、`showBubble`、`thoughtClass`、`developmentalStageFilter`、`socialTargetDevelopmentalStageFilter`。

`ThoughtStage` 字段（来源：`RimWorld/ThoughtStage.cs`）：

| 字段 | 说明 |
| --- | --- |
| `label` / `labelSocial` / `labelAbstract` | 想法文本（`labelSocial` 用于社交场景，与 `label` 相同时原版会报 ConfigError） |
| `description` | 描述 |
| **`baseMoodEffect`** | **心情效果（XML 里就是这个名字，不是 `moodOffset`）** |
| **`baseOpinionOffset`** | **好感度效果（XML 里就是这个名字，注意不是 `opinionOffset`）** |
| `visible` | 是否可见（默认 true） |

**结论 4.1（易错）**：`ThoughtDef` 的 XML 里心情/好感字段是 **`baseMoodEffect` / `baseOpinionOffset`**；
而运行时 `Thought_MemorySocial.opinionOffset` 是**另一个东西**（读取时按 `SocialImpact` 缩放后的值）。
**结论 4.2（会报错）**：`ThoughtStage.ConfigErrors()` 规定 —— **`baseMoodEffect != 0` 时必须有 `description`**，否则加载期报错。

**结论 4.3**：原版社交想法有两种写法，按用途选：
- `Thought_MemorySocial`（如 `DeepTalk`）：配 `durationDays` / `stackLimitForSameOtherPawn` / `stackedEffectMultiplier`，适合「一次性强效果 + 可递减堆叠」
- `Thought_MemorySocialCumulative`（如 `Chitchat`）：配 `maxCumulatedOpinionOffset`，适合「小效果高频累积并封顶」

原版实例（`Data/Core/Defs/ThoughtDefs/Thoughts_Memory_Social.xml`）：

```xml
<ThoughtDef>
  <defName>DeepTalk</defName>
  <thoughtClass>Thought_MemorySocial</thoughtClass>
  <durationDays>20</durationDays>
  <stackLimit>300</stackLimit>
  <stackLimitForSameOtherPawn>10</stackLimitForSameOtherPawn>
  <stackedEffectMultiplier>0.9</stackedEffectMultiplier>
  <developmentalStageFilter>Baby, Child, Adult</developmentalStageFilter>
  <socialTargetDevelopmentalStageFilter>Baby, Child, Adult</socialTargetDevelopmentalStageFilter>
  <nullifyingTraits><li>Psychopath</li></nullifyingTraits>
  <stages>
    <li>
      <label>deep talk</label>
      <baseOpinionOffset>15</baseOpinionOffset>
    </li>
  </stages>
</ThoughtDef>
```

> 原版所有社交想法都带 `nullifyingTraits: Psychopath` 与 `nullifyingHediffs: Inhumanized`（Anomaly）——「心理变态者不受社交影响」，自建社交想法应沿用。

### 1.7 冷却（来源：`Pawn_InteractionsTracker.cs`）

`lastInteractionTime` / `lastInteraction` / `lastInteractionDef` 均为 **private**，仅 `LastInteractionDef` 是公开属性。
`InteractedTooRecentlyToInteract()` = `TicksGame < lastInteractionTime + 120`（对**任意**互动生效）。

**结论 5**：**自定义互动的 24h 冷却无法从外部读取/写入原版字段**，必须自建载体（推荐随 Pawn 的隐藏 `Hediff`，天然 Scribe 且自动过期）。

### 1.8 日志文本（来源：`RimWorld/InteractionDef.cs` + `Data/Core/Defs/InteractionDefs/Interactions_Social.xml`）

`logRulesInitiator` / `logRulesRecipient` 用 `RulePack`，内含 `rulesStrings` 的 `r_logentry->...` 规则，占位符如 `[INITIATOR_nameDef]` `[RECIPIENT_nameDef]`，可用 `(p=0.8)` 设权重、用 `<include><li MayRequire="...">` 引入其他规则包。

XML 参考格式（原版 `Chitchat`）：

```xml
<InteractionDef>
  <defName>Chitchat</defName>
  <label>chitchat</label>
  <workerClass>InteractionWorker_Chitchat</workerClass>
  <symbol>Things/Mote/SpeechSymbols/Chitchat</symbol>
  <initiatorThought>Chitchat</initiatorThought>
  <initiatorXpGainSkill>Social</initiatorXpGainSkill>
  <initiatorXpGainAmount>4</initiatorXpGainAmount>
  <recipientThought>Chitchat</recipientThought>
  <logRulesInitiator><rulesStrings><li>r_logentry->...</li></rulesStrings></logRulesInitiator>
</InteractionDef>
```

***

## 二、Hediff（增益载体）

### 2.1 HediffStage 字段（来源：`Verse/HediffStage.cs`）

`minSeverity`、`label`、`overrideLabel`、`painFactor`、`painOffset`、`capMods`、**`statOffsets`**（`List<StatModifier>`）、**`statFactors`**（`List<StatModifier>`）、`statOffsetsBySeverity`、`statFactorsBySeverity`、`multiplyStatChangesBySeverity`、`statOffsetEffectMultiplier`、`statFactorEffectMultiplier`、`hungerRateFactor(Offset)`、`restFallFactor(Offset)`、`socialFightChanceFactor`、`mentalBreakMtbDays`、`blocksMentalBreaks`、`blocksInspirations`、`overrideMoodBase`、`hediffGivers`、`disabledWorkTags`、`opinionOfOthersFactor` 等。

**结论 6**：工作速度用 `statFactors`（`WorkSpeedGlobal`），移速用 `statOffsets`（`MoveSpeed`），直接写进 `HediffDef.stages[].li` 即可，无需代码。

### 2.2 相关 StatDef（来源：`RimWorld/StatDefOf.cs`）

`StatDefOf.WorkSpeedGlobal`（全局工作速度）、`StatDefOf.MoveSpeed`（移速）、`StatDefOf.PlantWorkSpeed`、`StatDefOf.WorkTableWorkSpeedFactor`、`StatDefOf.SocialImpact`（社交影响力，参与想法/好感度缩放）。

### 2.3 HediffDef 的 XML 写法要点（来源：原版 `Hediffs_Psycasts.xml` 等实例）

**结论 6.1（易错）**：`stages[].statFactors` / `statOffsets` 支持 **`<StatDefName>value</StatDefName>` 简写**，**不需要** `<li Class="StatModifier">`：

```xml
<li>
  <minSeverity>0</minSeverity>
  <statFactors>
    <WorkSpeedGlobal>1.10</WorkSpeedGlobal>
  </statFactors>
  <statOffsets>
    <MoveSpeed>0.1</MoveSpeed>
  </statOffsets>
</li>
```

**结论 6.2**：`statFactors` 的 value 是**乘数**（`1.10` = ×1.10 = +10%，原版实例 `<PsychicEntropyMax>1.3334</PsychicEntropyMax>` 即 +33.34%）；`statOffsets` 的 value 是**加值**。
**结论 6.3**：`stages` 按 `minSeverity` 升序匹配，`StageAtSeverity(severity)` 取 `minSeverity <= severity` 的最后一个 stage；`severity` 同时表达「等级」时把各 stage 的 `minSeverity` 设为 0/1/2、severity 用 1/2/3 即可。
**结论 6.4**：隐藏 Hediff —— `Verse.Hediff.Visible` 是 **`public virtual bool`**（`Verse/Hediff.cs`），自定义 `hediffClass` 中 `public override bool Visible => false;` 即可不在健康列表出现（原版 `Hediff.ShouldRemove` 同样是 virtual）。
**结论 6.5**：`Verse.Hediff.Tick()` / `TickInterval(int delta)` / `ExposeData()` / `PostAdd` / `PostRemoved` 均为 `public virtual`，可在自定义 Hediff 中重写以自维护过期逻辑，无需依赖 `HediffComp_Disappears`。

***

## 三、灵感（来源：`RimWorld/InspirationHandler.cs`）

```csharp
public class InspirationHandler : IExposable {
    public bool Inspired { get; }
    public Inspiration CurState { get; }
    public InspirationDef CurStateDef { get; }
    public bool TryStartInspiration(InspirationDef def, string reason = null, bool sendLetter = true);
    public void EndInspiration(Inspiration inspiration);
    public void EndInspiration(InspirationDef inspirationDef);
    public void Reset();
    public InspirationDef GetRandomAvailableInspirationDef();
}
```

**结论 7**：「随机灵感」直接 `GetRandomAvailableInspirationDef()` → `TryStartInspiration(def)`；返回 null 表示当前不可用（已有灵感 / 被 hediff 阻断等），必须判空。

### 3.1 灵感信件的产生与自定义（来源：`RimWorld/Inspiration.cs`）

```csharp
public class Inspiration : IExposable
{
    public Pawn pawn;
    public InspirationDef def;
    public string reason;                        // 由 TryStartInspiration(def, reason) 传入
    public virtual string LetterText { get; }    // 信件正文
    public virtual void PostStart(bool sendLetter = true);
    protected virtual void SendBeginLetter();
    protected virtual void AddEndMessage();
    protected void End();
}
```

```csharp
// 正文 = reason（若非空） + def.beginLetter（语法解析后）
public virtual string LetterText
{
    get
    {
        string text = def.beginLetter.Formatted(pawn.LabelCap, pawn.Named("PAWN")).AdjustedFor(pawn).Resolve();
        if (!string.IsNullOrWhiteSpace(reason))
        {
            text = reason.Formatted(pawn.LabelCap, pawn.Named("PAWN")).AdjustedFor(pawn).Resolve() + "\n\n" + text;
        }
        return text;
    }
}

// 标题 = def.beginLetterLabel（缺省用 def.LabelCap） + ": " + pawn.LabelShortCap
protected virtual void SendBeginLetter()
{
    if (!def.beginLetter.NullOrEmpty() && PawnUtility.ShouldSendNotificationAbout(pawn))
    {
        string text = (def.beginLetterLabel ?? ((string)def.LabelCap)).CapitalizeFirst() + ": " + pawn.LabelShortCap;
        Find.LetterStack.ReceiveLetter(text, LetterText, def.beginLetterDef, pawn);
    }
}
```

**结论 7.1（「通过该方式触发灵感时，信件内容能自定义吗？」）**：**可以**，按侵入性从小到大有三条路径：

| 路径 | 做法 | 影响范围 |
| --- | --- | --- |
| ① 追加一段说明 | `TryStartInspiration(def, reason)` 的 `reason` 会被拼在正文**最前面**（`reason + "\n\n" + 原正文`） | 仅本次触发 |
| ② 不发信 | `TryStartInspiration(def, reason, sendLetter: false)` | 仅本次触发 |
| ③ 完全自定义标题与正文 | 让 `InspirationDef.inspirationClass` 指向自定义 `Inspiration` 子类，重写 `LetterText`（virtual 属性）与 `SendBeginLetter()`（protected virtual） | **该 Def 的所有触发场合** |

⚠️ **路径 ③ 的注意点**：`InspirationDef` 是**共享 Def**，修改 `inspirationClass` 会同时影响原版「高心情随机灵感」等所有触发该灵感的场合。
若要只影响击掌触发的灵感，应**新建一个专属 `InspirationDef`**（复用原版的 `workerClass` / `baseDurationDays` 等），而不是改原版 Def。

**结论 7.2**：`Inspiration.reason` 参与存档（`Scribe_Values.Look(ref reason, "reason")`），自定义 reason 会随存档保留。
**结论 7.3**：发信还受 `PawnUtility.ShouldSendNotificationAbout(pawn)` 约束（非玩家派系/动物等不会发信）。

***

## 四、位置与特效

- `SocialInteractionUtility.BestInteractableCell(actor, targetPawn)` — 取目标周围最近的可站立互动格
- `SocialInteractionUtility.TryGetAdjacentInteractionCell(pawn, entityThing, forced, out cell)`
- `MoteMaker.MakeInteractionBubble(initiator, recipient, moteDef, symbolTex, symbolColor)` — 互动气泡（原版自动调用）
- `SocialInteractionUtility.ImitateSocialInteractionWithManyPawns(initiator, targets, intDef)` / `ImitateInteractionWithNoPawn(initiator, intDef)` — 无实际互动但产生日志的表现
- `MoteMaker.ThrowText(Vector3 loc, Map map, string text, float timeBeforeStartFadeout = -1f)` / `ThrowText(Vector3 loc, Map map, string text, Color color, float timeBeforeStartFadeout = -1f)` — **彩色飘字**（另见「六、视觉特效系统」）

***

## 五、音效系统

来源：`Verse.Sound/SoundStarter.cs`、`Verse/SoundDef.cs`、`Verse.Sound/AudioGrain_Clip.cs`、`Verse.Sound/AudioGrain_Folder.cs`、`Data/Core/Defs/SoundDefs/Ritual_SoundDefs.xml`

```csharp
public static class SoundStarter {
    public static void PlayOneShotOnCamera(this SoundDef soundDef, Map onlyThisMap = null);
    public static void PlayOneShot(this SoundDef soundDef, SoundInfo info);
    public static Sustainer TrySpawnSustainer(this SoundDef soundDef, SoundInfo info);
}

public class SoundDef : Def {
    public bool sustain;
    public static SoundDef Named(string defName);
}

public class AudioGrain_Clip : AudioGrain { public string clipPath = ""; }      // 单音频文件
public class AudioGrain_Folder : AudioGrain { /* clipFolderPath */ }           // 文件夹随机取
```

**结论 8**：播放一次性音效用扩展方法 `soundDef.PlayOneShotOnCamera(map)`（无需 SoundInfo）。
`clipPath` / `clipFolderPath` **不含 `Sounds/` 前缀，也不含扩展名**（内部走 `ContentFinder<AudioClip>.Get`）；
音频资源目录 `Sounds/` **不是原版 XML 的一部分**（原版音频打包在 Unity 资源内），mod 需自备 `Sounds/` 目录。

SoundDef XML 模板（原版 `RitualConclusion_Positive`）：

```xml
<SoundDef>
  <defName>RitualConclusion_Positive</defName>
  <context>MapOnly</context>
  <maxSimultaneous>1</maxSimultaneous>
  <subSounds>
    <li>
      <onCamera>True</onCamera>
      <grains>
        <li Class="AudioGrain_Folder">
          <clipFolderPath>Misc/Rituals/ConclusionPositive</clipFolderPath>
        </li>
      </grains>
      <volumeRange>30</volumeRange>
      <distRange>15~30</distRange>
    </li>
  </subSounds>
</SoundDef>
```

**结论 9**：`AudioGrain_Clip` 指向单个文件，`AudioGrain_Folder` 指向文件夹（随机取一个，适合多音效变体避免听腻）；`onCamera` 控制是否只在镜头内播放。

***

## 六、视觉特效系统

来源：`RimWorld/MoteMaker.cs`、`RimWorld/FleckMaker.cs`、`RimWorld/FleckDefOf.cs`、`Verse/CameraDriver.cs`、`Verse/CameraShaker.cs`

### 6.1 MoteMaker（Mote = 有生命周期/可附着的地图特效）

| 方法 | 用途 |
| --- | --- |
| `MakeStaticMote(IntVec3 cell, Map, ThingDef moteDef, float scale = 1f)` / `(Vector3 loc, Map, ThingDef, float scale, bool makeOffscreen, float exactRot)` | 静态特效 |
| `ThrowText(Vector3 loc, Map map, string text, Color color, float timeBeforeStartFadeout = -1f)` | **彩色飘字** |
| `MakeAttachedOverlay(Thing thing, ThingDef moteDef, Vector3 offset, float scale, float solidTimeOverride)` | 附着于 Thing |
| `MakeInteractionBubble(...)` / `MakeThoughtBubble(...)` | 气泡 |
| `ThrowExplosionCell(IntVec3 cell, Map, ThingDef moteDef, Color color)` | 爆炸格特效（带颜色） |
| `MakeConnectingLine(Vector3 start, Vector3 end, ThingDef moteType, Map, float width = 1f)` | 两点连线 |

### 6.2 FleckMaker（Fleck = 轻量粒子，性能优于 Mote，推荐首选）

| 方法 | 用途 |
| --- | --- |
| `Static(Vector3 loc, Map, FleckDef, float scale = 1f)` | 静态粒子 |
| `ThrowLightningGlow(Vector3 loc, Map, float size)` | **闪电光晕**（复用 `FleckDefOf.LightningGlow`） |
| `ThrowExplosionCell(IntVec3 cell, Map, FleckDef, Color color)` | 爆炸格（可染色） |
| `ThrowMicroSparks(Vector3 loc, Map)` / `ThrowMicroSparksFast(...)` | 火花 |
| `ThrowSmoke(Vector3 loc, Map, float size)` / `ThrowFireGlow(...)` / `ThrowHeatGlow(...)` | 烟 / 火光 / 热浪 |
| `ConnectingLine(Vector3 start, Vector3 end, FleckDef, Map, float width = 1f)` | 两点连线 |
| `AttachedOverlay(Thing thing, FleckDef, Vector3 offset, float scale, float solidTimeOverride)` | 附着于 Thing |
| `ThrowDustPuff(Vector3 loc, Map, float scale)` / `ThrowMetaPuff(...)` | 尘 / 涟漪 |

### 6.3 可直接复用的原版 FleckDef（`FleckDefOf`）

`LightningGlow`、`ExplosionFlash`、`ShotFlash`、`MicroSparks`、`MicroSparksFast`、`LineEMP`、`EntropyPulse`、`FlashHollow`、`BroadshieldActivation`、`MetaPuff`、`Smoke`、`FireGlow`、`HeatGlow`、`DustPuff`、`DustPuffThick`、`AncientVentHeatGlow`、`AncientVentHeatShimmer` 等。

**结论 10**：**不绘制任何贴图也能做出成规模的特效** —— 复用原版 Fleck + `instanceColor` 染色即可（如把 `LightningGlow` 染成暗红黑紫 = 黑闪感）。

### 6.4 自定义 FleckDef 的写法（参考原版与同类项目）

```xml
<FleckDef>
  <defName>Clap_BlackFlashLine</defName>
  <fleckSystemClass>Verse.FleckSystemThrown</fleckSystemClass>
  <altitudeLayer>MoteOverhead</altitudeLayer>
  <graphicData>
    <graphicClass>Graphic_Fleck</graphicClass>
    <texPath>ClapYourHands/BlackFlash</texPath>
    <shaderType>MoteGlow</shaderType>
    <color>(1, 1, 1, 1)</color>
    <drawSize>(0.2, 0.2)</drawSize>
  </graphicData>
  <fadeInTime>0.05</fadeInTime>
  <solidTime>0.05</solidTime>
  <fadeOutTime>0.4</fadeOutTime>
</FleckDef>
```

**结论 11（踩坑点）**：需要 `velocity` 迸溅方向的特效**必须用 `Verse.FleckSystemThrown`**；`FleckSystemStatic` 下 `data.velocity` 不生效。
可选 `fleckSystemClass`：`Verse.FleckSystemStatic` / `FleckSystemThrown` / `FleckSystemSplash`；常用 `graphicClass`：`Graphic_Fleck` / `Graphic_FleckPulse` / `Graphic_FleckSplash`。

### 6.5 屏幕震动

```csharp
public class CameraDriver : MonoBehaviour {
    public CameraShaker shaker = new CameraShaker();     // public 字段，可直接访问
    public bool InViewOf(Thing thing);
}

public class CameraShaker {
    private const float ShakeDecayRate = 0.5f;
    private const float ShakeFrequency = 24f;
    private const float MaxShakeMag = 0.2f;
    public float CurShakeMag { get; }
    public Vector3 ShakeOffset { get; }
    public void DoShake(float mag);
    public void DoShake(float mag, int durationTicks);
    public float GetMaxShakeMag();
    public void SetMinShake(float mag);
    public void StopAllShaking();
}
```

**结论 12**：震屏为 `Find.CameraDriver.shaker.DoShake(mag)`（`shaker` 是 public 字段，无需反射/补丁）；`mag` 上限为 `MaxShakeMag = 0.2f`。
`CameraDriver.InViewOf(Thing)` 可用于先判断目标是否在镜头内，再决定是否震屏；访问 `Find.CameraDriver` 前应判空（非地图场景/初始化期可能为 null）。

***

## 七、实现落地期补充核实

来源：`RimWorld/DefOfHelper.cs`、`Verse/HediffMaker.cs`、`Verse/Pawn_HealthTracker.cs`（本项目用 `ilspycmd` 反编译，存于 `decompiledFiles/`）

### 7.1 DefOf 模式

```csharp
namespace RimWorld;   // ← 注意：DefOfHelper 在 RimWorld 命名空间，不是 Verse

public static class DefOfHelper
{
    public static void RebindAllDefOfs(bool earlyTryMode);
    public static void EnsureInitializedInCtor(Type defOf);
}
```

**结论 13**：标准写法 = `[DefOf]` 属性 + `public static` 字段（**字段名必须等于 defName**）+ 静态构造中调用 `DefOfHelper.EnsureInitializedInCtor(typeof(X))`。
绑定由 `GenTypes.AllTypesWithAttribute<DefOf>()` 反射完成，并支持 `[MayRequire]` / `[MayRequireAnyOf]`（条件绑定）与 `[DefAlias]`（字段名与 defName 不一致时）。
⚠️ `EnsureInitializedInCtor` 只会**警告**：在 defs 加载完成前访问 DefOf 字段会静默得到 null，不要在 Def 的默认字段值里使用 DefOf（应在 `ResolveReferences()` 里解析）。

### 7.2 Hediff 的创建与增删

```csharp
public static class HediffMaker
{
    public static Hediff MakeHediff(HediffDef def, Pawn pawn, BodyPartRecord partRecord = null);
}

public class Pawn_HealthTracker
{
    public void AddHediff(Hediff hediff, BodyPartRecord part = null, DamageInfo? dinfo = null, DamageWorker.DamageResult result = null);
    public void RemoveHediff(Hediff hediff);
}
```

**结论 14**：创建 = `HediffMaker.MakeHediff(def, pawn)`（会自动采用 `initialSeverity`），再 `pawn.health.AddHediff(hediff)`。
**结论 15**：查找用 `HediffSet.GetFirstHediff<T>()` 泛型（比 `GetFirstHediffOfDef(def)` 更类型安全，无需强转）。
**结论 16**：Hediff 的生命周期钩子（`Tick` / `TickInterval` / `PostTick` / `ExposeData` / `PostAdd` / `PostRemoved` / `ShouldRemove` / `Visible` / `Severity`）均为 `public virtual`，可自由重写。
但**仅需「定时消失」时不必自己写任何生命周期代码** —— 用原版 `HediffComp_Disappears`（见 §7.3）；自行重写 `ShouldRemove` 只适用于更复杂的条件性移除。
`Hediff.Tick()` / `TickInterval(int)` / `ExposeData()` / `PostAdd` / `PostRemoved` / `Severity` 均为 `public virtual`，可自由重写。

### 7.3 `HediffComp_Disappears`（定时消失组件）

来源：`Verse/HediffComp_Disappears.cs`、`Verse/HediffCompProperties_Disappears.cs`（本项目 1.6 反编译，存于 `decompiledFiles/Verse/`）

```csharp
public class HediffComp_Disappears : HediffComp
{
    public int ticksToDisappear;          // public 字段，可随时读写
    public int disappearsAfterTicks;      // public 字段
    public int seed;

    public override bool CompShouldRemove { get; }   // ticksToDisappear > 0 时不移除
    public virtual int TicksLostPerTick => 1;        // 每 tick 递减量（可重写）
    public float Progress { get; }                   // 已过比例
    public int EffectiveTicksToDisappear { get; }

    public override void CompPostMake();             // disappearsAfterTicks = Props.disappearsAfterTicks.RandomInRange，并赋给 ticksToDisappear
    public void ResetElapsedTicks();                 // ticksToDisappear = disappearsAfterTicks
    public void SetDuration(int ticks);              // ★ 同时设置 disappearsAfterTicks 与 ticksToDisappear
    public override void CompPostTick(ref float severityAdjustment);  // ticksToDisappear -= TicksLostPerTick
    public override void CompPostMerged(Hediff other);   // 合并时取两者中较大的 ticksToDisappear
    public override void CompExposeData();           // 上述三个字段全部 Scribe
    public override string CompLabelInBracketsExtra; // Props.showRemainingTime 时在括号里显示剩余时间
}

public class HediffCompProperties_Disappears : HediffCompProperties
{
    public IntRange disappearsAfterTicks;
    public bool showRemainingTime;
    public bool canUseDecimalsShortForm;
    public MentalStateDef requiredMentalState;
    public string messageOnDisappear;          // [MustTranslate]
    public string letterTextOnDisappear;       // [MustTranslate]
    public string letterLabelOnDisappear;      // [MustTranslate]
    public bool sendLetterOnDisappearIfDead = true;
    public bool leaveFreshWounds = true;
}
```

**结论 17（重要纠正）**：**`SetDuration(int ticks)` 在 RimWorld 1.6 中确实存在**，可**随时**修改 Hediff 的持续时间（同时更新 `disappearsAfterTicks` 与 `ticksToDisappear`）；另可用 `ResetElapsedTicks()` 只重置剩余时间、或直接赋值 public 字段 `ticksToDisappear`。
> ⚠️ 此前基于旧技术栈记忆认为「`SetDuration` 不存在」，该说法**在 1.6 上不成立**，已由反编译证伪（见 §7.3）。**教训：某 API 是否存在，一律以当前版本的反编译结果为准，不沿用跨版本的印象。**

**结论 18（易踩坑）**：使用该组件时 **必须提供 `<disappearsAfterTicks>`**（`IntRange`，可写单值如 `30000`）——`CompPostMake()` 用它初始化 `ticksToDisappear`；**若省略，`IntRange` 默认为 0，Hediff 会在生成后被立即移除**。

**结论 19**：`HediffWithComps.ShouldRemove` 会聚合所有 comp 的 `CompShouldRemove`（任一为 true 即移除），而 `HediffComp.CompShouldRemove` 的基类实现是 `false`（**不会**递归回 parent）。因此「定时消失」用本组件即可，无需自己重写 `Hediff.ShouldRemove`。

**结论 20**：`HediffWithComps.Visible` 会聚合各 comp 的 `CompDisallowVisible()`（任一为 true 即隐藏）；隐藏 Hediff 也可以直接重写 `Hediff.Visible => false`（更简单，本项目采用）。

**结论 21**：`Props.showRemainingTime = true` 时，剩余时间会经 `CompLabelInBracketsExtra` 显示在 Hediff 名称的括号中（换算时考虑 `TicksLostPerTick`），适合「有明确倒计时的增益」。

***

## 八、开发者菜单（Debug）扩展

来源：`LudeonTK/DebugActionAttribute.cs`、`DebugActionType.cs`、`DebugMenuOption.cs`、`Dialog_DebugOptionListLister.cs`（本项目 1.6 反编译）

```csharp
namespace LudeonTK;   // ← DebugAction 系列在 LudeonTK 命名空间

[AttributeUsage(AttributeTargets.Method)]
public class DebugActionAttribute : Attribute
{
    public string name;
    public string category = "General";
    public AllowedGameStates allowedGameStates = AllowedGameStates.Playing;
    public DebugActionType actionType;
    public bool requiresRoyalty / requiresIdeology / requiresBiotech / requiresAnomaly / requiresOdyssey;
    public int displayPriority;
    public bool hideInSubMenu;
    public bool IsAllowedInCurrentGameState { get; }
}

public enum DebugActionType { Action, ToolMap, ToolMapForPawns, ToolWorld }

public struct DebugMenuOption(string label, DebugMenuOptionMode mode, Action method);
public enum DebugMenuOptionMode { Action, Tool }
```

**结论 22**：注册调试项 = `public static` 方法 + `[DebugAction(category, name, actionType = ..., allowedGameStates = ...)]`（需 `using LudeonTK;`）。
`Action` 点击即执行；`ToolMapForPawns` 会传入被点选小人的 `Pawn` 参数；`allowedGameStates` 决定何时可见；`requiresXXX` 可与 DLC 绑定。

**结论 23（已修正）**：`DebugActionType` 只有 4 个值、`DebugMenuOption` 没有勾选字段 —— 但这些**不是**「开关式调试项」的实现途径。
原版真正的开关式调试项走 **`DebugTabMenuDef` + `DebugActionNode.settingsField`**，见结论 26/27（此前的「原版没有勾选机制」判断是**错的**，仅查了 `DebugAction` 系列就下了结论）。

**结论 24**：`Messages.Message(string text, MessageTypeDef def, bool historical = true)`；
`MessageTypeDefOf` 可用值：`ThreatBig` / `ThreatSmall` / `PawnDeath` / `NegativeHealthEvent` / `NegativeEvent` / `NeutralEvent` / `TaskCompletion` / `PositiveEvent` / `SituationResolved` / `RejectInput` / `CautionInput` / `SilentInput`。

**结论 25**：`Pawn_HealthTracker.GetOrAddHediff(HediffDef def, BodyPartRecord part = null, DamageInfo? dinfo = null, DamageWorker.DamageResult result = null)` 是**原版 API**（`Pawn_HealthTracker.cs:217`），一次调用即可「取已有或新建并添加」，比 `GetFirstHediff` + `HediffMaker.MakeHediff` + `AddHediff` 三步更简洁。
原版用法示例（`JobDriver_UnnaturalCorpseAttack.cs:40`）：
```csharp
Victim.health.GetOrAddHediff(HediffDefOf.AwakenHypnosis).TryGetComp<HediffComp_Disappears>().ticksToDisappear++;
```

### 8.1 开关式调试项的正解：`DebugTabMenuDef`（**mod 可扩展**）

来源：`RimWorld/DebugTabMenuDef.cs`、`LudeonTK/DebugTabMenu_Settings.cs`、`LudeonTK/DebugTabMenu.cs`、`LudeonTK/DebugActionNode.cs`、`LudeonTK/Dialog_Debug.cs`、`Data/Core/Defs/DebugTabMenuDefs/DebugTabMenuDefs.xml`

```csharp
namespace RimWorld;
public class DebugTabMenuDef : Def
{
    public Type menuClass;              // 必须派生自 LudeonTK.DebugTabMenu
    public int displayOrder = 99999;
}

namespace LudeonTK;
public abstract class DebugTabMenu
{
    protected DebugActionNode myRoot;
    public DebugTabMenu(DebugTabMenuDef def, Dialog_Debug dialog, DebugActionNode rootNode);
    public abstract DebugActionNode InitActions(DebugActionNode root);
    public static DebugTabMenu CreateMenu(DebugTabMenuDef def, Dialog_Debug dialog, DebugActionNode root);
}

public class DebugActionNode
{
    public string label;
    public DebugActionType actionType;
    public Action action;
    public Action<Pawn> pawnAction;
    public string category;
    public int displayPriority;
    public FieldInfo settingsField;     // ★ 非空 → UI 画成复选框
    public bool On => settingsField == null ? false : (bool)settingsField.GetValue(null);

    public DebugActionNode(string label, DebugActionType actionType = DebugActionType.Action,
                           Action action = null, Action<Pawn> pawnAction = null);
    public void AddChild(DebugActionNode child);
}
```

**扩展链（每一环都已核实）**：

| 环 | 代码 | 位置 |
| --- | --- | --- |
| ① 标签页来自 **Def 数据库** | `menuDefsSorted.AddRange(DefDatabase<DebugTabMenuDef>.AllDefs.ToList())` | `Dialog_Debug.cs:153` |
| ② 从 Def 实例化菜单类 | `Activator.CreateInstance(def.menuClass, def, dialog, root)` | `DebugTabMenu.CreateMenu` |
| ③ 有 `settingsField` 就画复选框 | `if (node.settingsField != null) DoCheckbox(...) else DoButton(...)` | `Dialog_Debug.DrawNode` |
| ④ 勾选状态**直接读写静态字段** | `settingsField.SetValue(null, checkOn)`，并调用可选的 `"<字段名>Toggled"` 静态方法 | `Dialog_Debug.DoCheckbox` |

**结论 26（mod 注册复选框式调试开关的完整做法）**：

1. XML 定义 `<DebugTabMenuDef>`：`defName` / `label` / `menuClass`（**自定义类需写完整命名空间**）/ `displayOrder`
2. 继承 `LudeonTK.DebugTabMenu`，重写 `InitActions(DebugActionNode absRoot)`：
   ```csharp
   myRoot = new DebugActionNode("Clap");
   absRoot.AddChild(myRoot);
   var node = new DebugActionNode("My Toggle") { settingsField = typeof(MyDebug).GetField(nameof(MyDebug.Flag), BindingFlags.Public | BindingFlags.Static) };
   node.category = "General";
   myRoot.AddChild(node);
   ```
3. **不需要自己写切换逻辑** —— 复选框直接读写该静态字段（字段必须是 `public static bool`）。

原版实例：`DebugTabMenu_Settings.InitActions` 反射 `DebugSettings` / `DebugViewSettings` / `DebugGenerationSettings` 三个类的字段逐个建节点并设 `settingsField`。

⚠️ **注意**：`DebugTabMenu_Settings` 只扫描那三个**硬编码**的类，mod **无法**往 `DebugSettings` 添加字段；要加自己的开关必须**自建标签页**（上述做法）。
⚠️ `displayOrder` 原版取值：`Actions = 0` / `Settings = 100` / `Output = 200`。

**结论 27.1（把选项并入原版分页 —— 零补丁可行）**：`Dialog_Debug.TrySetupNodeGraph` 为**所有** `DebugTabMenuDef` 传入**同一个** rootNode：

```csharp
rootNode = new DebugActionNode("Root");
foreach (DebugTabMenuDef allDef in DefDatabase<DebugTabMenuDef>.AllDefs)
{
    roots.Add(allDef, DebugTabMenu.CreateMenu(allDef, null, rootNode).InitActions(rootNode));
    //                                                        ^^^^^^^^ 共享同一个根节点
}
```

各分页在 `InitActions(absRoot)` 里以 `absRoot.AddChild(myRoot)` 把自己挂上去。
→ 因此可以在自己的 `InitActions` 中从 `absRoot.children` **找到原版分页的节点**（如 `label == "Settings"`），
直接向其中 `AddChild` —— **选项会真正出现在原版分页内部**，只归入自己的 `category`（如 `"Clap"`），无需补丁。

⚠️ **顺序依赖**：`DefDatabase.AllDefs` 按 def 加载顺序（Core → DLC → mod），原版 `"Settings"` 节点必先于 mod 建立；
仍建议保留「找不到就自建」的兜底分支。

⚠️ **零补丁的固有限制**：`DrawTabs` 无条件遍历 `menuDefsSorted`（即 `DefDatabase<DebugTabMenuDef>.AllDefs`），
所以**只要注册了 `DebugTabMenuDef`，分页栏就必然多出一个 tab** —— 而「被调用以注入节点」与「不出现在分页栏」绑定在一起，无法两全。
可行缓解：让该 tab 复用同一个 `DebugActionNode`（即 `InitActions` 返回原版 Settings 的节点），使其内容与原版 Settings **完全一致**（本项目采用）。

***

## 九、身体部位系统

来源：`Data/Core/Defs/Bodies/Bodies_Humanlike.xml`、`Verse/HediffSet.cs`、`Verse/BodyPartRecord.cs`、`Verse/BodyPartDef.cs`、`RimWorld/BodyPartDefOf.cs`、`Verse/Pawn_HealthTracker.cs`、`Verse/HediffMaker.cs`、`Verse/Hediff.cs`

### 9.1 左右手如何表示

原版人类 BodyDef 的原文：

```xml
<li><def>Hand</def><customLabel>left hand</customLabel>  <coverage>0.14</coverage> ...</li>
<li><def>Hand</def><customLabel>right hand</customLabel> <coverage>0.14</coverage> ...</li>
```

**结论 28**：**左右手共享同一个 `BodyPartDef`（`Hand`）**；`BodyPartRecord` 上**没有** left/right 字段，只有 `customLabel` / `untranslatedCustomLabel`（`customLabel` 是 Def 内的原文，不随语言变化）。
→ 因此「随机取一只手」**根本不需要判断左右**，只需在 `def == BodyPartDefOf.Hand` 的部位集合里随机取一个即可。

### 9.2 部位查询 API

| 方法 | 行为 |
| --- | --- |
| `HediffSet.GetBodyPartRecord(BodyPartDef partDef)` | 遍历 `GetNotMissingParts()`，返回**第一个** def 匹配的部位；无则 `null` |
| `HediffSet.TryGetBodyPartRecord(BodyPartDef, out BodyPartRecord)` | 上述的 `bool` 包装 |
| `HediffSet.GetNotMissingParts(height, depth, tag, parent)` | 遍历 `pawn.def.race.body.AllParts`，过滤 `PartIsMissing` 及可选条件（**iterator，有分配**） |
| `HediffSet.PartIsMissing(BodyPartRecord part)` | 遍历 hediffs 找挂在同一部位的 `Hediff_MissingPart`（成本低） |
| `HediffSet.GetRandomNotMissingPart(...)` | 按 `coverageAbs × hitChance` **加权**随机（用于受伤判定，**不是**均匀随机） |
| `HediffSet.IsBionicOrImplant(BodyPartDef)` | 该部位是否有 `countsAsAddedPartOrImplant` 的 hediff |

- `BodyDef.AllParts` 是 **`List<BodyPartRecord>`**（可直接索引遍历，避开 iterator 分配 —— 热路径上有意义）
- `RimWorld.BodyPartDefOf` 提供：`Leg` / `Eye` / `Shoulder` / `Arm` / **`Hand`** / `Head` / `Lung` / `Torso` / `Heart` / `Neck`
- `BodyPartRecord` 字段：`body` / `def` / `customLabel` / `untranslatedCustomLabel` / `parts` / `height` / `depth` / `coverage` / `groups` / `woundAnchorTag` / `flipGraphic` / `visibleHediffRots` / `parent`

### 9.3 Hediff 挂载到指定部位

```csharp
// Pawn_HealthTracker
public Hediff AddHediff(HediffDef def, BodyPartRecord part = null, DamageInfo? dinfo = null, DamageWorker.DamageResult result = null);
public Hediff AddHediff(Hediff hediff, BodyPartRecord part = null, DamageInfo? dinfo = null, DamageWorker.DamageResult result = null);
public Hediff GetOrAddHediff(HediffDef def, BodyPartRecord part = null, DamageInfo? dinfo = null, DamageWorker.DamageResult result = null);

// HediffMaker
public static Hediff MakeHediff(HediffDef def, Pawn pawn, BodyPartRecord partRecord = null);   // 内部 obj.Part = partRecord

// HediffDef
public BodyPartDef defaultInstallPart;
```

`AddHediff(Hediff, BodyPartRecord, ...)` 的关键逻辑：

```csharp
if (part == null && hediff.def.defaultInstallPart != null)
{
    part = pawn.RaceProps.body.AllParts
        .Where((BodyPartRecord x) => x.def == hediff.def.defaultInstallPart)
        .RandomElement();          // ★ 原版自动随机挑一个
}
if (part != null) { hediff.Part = part; }
```

**结论 29（含重要警告）**：原版 `HediffDef.defaultInstallPart` 会让 `AddHediff(def)` / `GetOrAddHediff(def)` 在**不传 part** 时自动执行
`pawn.RaceProps.body.AllParts.Where(x => x.def == defaultInstallPart).RandomElement()`。
⚠️ **但 `body.AllParts` 包含已被截肢（缺失）的部位，此处没有任何 `PartIsMissing` 过滤** —— 若目标有部位缺失（如单臂小人），就有概率挑中缺失部位，导致 `HediffSet.AddDirect` 报
`Tried to add health diff to missing part`，Hediff 施加失败。
→ **不要依赖它实现「随机挂到某个部位」**；应自行筛选可用部位后再作为 `part` 传入（本项目做法见 `B-04` BUG-001）。
**结论 30**：`GetOrAddHediff(def, part)` **只按 def 查找**（`hediffSet.TryGetHediff(def, out ...)`），已存在时直接返回旧实例并**忽略传入的 part** —— 「每 Pawn 至多一个」的保证不受部位影响；效果是「首次创建时随机挂手，之后不再变动」。
**结论 31**：`Hediff.Part` 是 **public 可读写**属性（`Hediff.cs:271`，底层 private 字段 `part`），如需强制换部位可在创建后直接赋值。
**结论 32（原版隐患，值得规避）**：`HediffComp_Disappears.CompPostPostRemoved` 中有
```csharp
if (!Props.leaveFreshWounds)
    foreach (BodyPartRecord p in parent.Part.GetPartAndAllChildParts())   // ← Part 为 null 时 NRE
```
`leaveFreshWounds` **默认为 `true`**，所以默认安全；但若显式设为 `false` 且 Hediff 未挂部位就会 NRE。**挂载了部位的 Hediff 反而更安全**。

### 9.4 部位缺失的递归传播（判断部位可用性时必须知道）

```csharp
// ① Verse.Hediff_AddedPart.PostAdd —— 标记【直接】子部位
public override void PostAdd(DamageInfo? dinfo)
{
    base.PostAdd(dinfo);
    pawn.health.RestorePart(base.Part, this, checkStateChange: false);   // 递归清除该部位及所有后代上的 hediff
    for (int i = 0; i < base.Part.parts.Count; i++)
    {
        Hediff_MissingPart mp = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn);
        mp.IsFresh = true;
        mp.lastInjury = HediffDefOf.SurgicalCut;
        mp.Part = base.Part.parts[i];
        pawn.health.hediffSet.AddDirect(mp);        // AddDirect 内部会调用 mp.PostAdd(dinfo)
    }
}

// ② Verse.Hediff_MissingPart.PostAdd —— ★ 递归的关键
public override void PostAdd(DamageInfo? dinfo)
{
    base.PostAdd(dinfo);
    pawn.health.RestorePart(base.Part, this, checkStateChange: false);
    for (int i = 0; i < base.Part.parts.Count; i++)
    {
        Hediff_MissingPart mp = (Hediff_MissingPart)HediffMaker.MakeHediff(def, pawn);
        mp.Part = base.Part.parts[i];
        pawn.health.hediffSet.AddDirect(mp);        // 继续向下传播，直到叶子部位
    }
}
```

**结论 33**：两个 `PostAdd` 串联构成**递归传播** —— 往某部位装一个 `Hediff_AddedPart`（即任何 `ParentName="AddedBodyPartBase"` 的植入体），该部位的**整个后代分支**（子、孙……直到叶子）都会获得 `Hediff_MissingPart`。

实例（人体层级 `Shoulder → [Clavicle, Arm]`、`Arm → [..., Hand]`、`Hand → [Finger × 5]`）：

| 植入体 | RecipeDef 的 `appliedOnFixedBodyParts` | 结果 |
| --- | --- | --- |
| `BionicArm` | **`Shoulder`** | Clavicle / Arm / **Hand** / 所有 Finger **全部 missing** |
| `WoodenHand` / `PowerClaw` / `FieldHand` | **`Hand`** | 仅 Finger missing；**`Hand` 本身是 AddedPart（非 MissingPart）** |

**结论 34（推论与反向陷阱）**：`PartIsMissing` / `GetNotMissingParts()` 是**精确匹配**（只查挂在该 `BodyPartRecord` 上的 `Hediff_MissingPart`），但因递归传播，后代部位会被**正确**标记 —— 所以直接用它们判断「部位是否可用」是可靠的。
⚠️ **反向陷阱**：只在 `Hediff_AddedPart.PostAdd` 里看到「只遍历直接子部位」就断定「后代不受影响」是**错的** —— 必须继续追到 `Hediff_MissingPart.PostAdd` 才能看到完整递归。判断部位可用性时，**不要凭一层代码下结论**。

**结论 35（挂载硬约束 —— 把 Hediff 挂到「被替换的语义位置」时必须注意）**：`HediffSet.AddDirect` 会拒绝把 Hediff 挂到缺失部位：

```csharp
if (hediff.Part != null && !GetNotMissingParts().Contains(hediff.Part))
{
    Log.Error("Tried to add health diff to missing part " + hediff.Part);
    return;      // Hediff 不会被挂上
}
```

结合结论 33（被替换部位的后代分支**全部**是 MissingPart）可知：
**若想把 Hediff 挂到「某个被植入体替换掉的位置」（如机械手），实际挂载点必须是执行替换的那个祖先部位。**
查找方式：沿 `part.parent` 上溯，取第一个 `HediffSet.HasDirectlyAddedPartFor(part)` 为真的部位。
（`HasDirectlyAddedPartFor(BodyPartRecord)` → 该部位本身是否挂着 `Hediff_AddedPart`；对照 `AncestorHasDirectlyAddedParts(BodyPartRecord)` → 只查祖先、不含自身。）
→ **「语义上可用」与「原版允许挂载」是两个独立条件，必须分别满足**（见 `B-04` BUG-002）。
