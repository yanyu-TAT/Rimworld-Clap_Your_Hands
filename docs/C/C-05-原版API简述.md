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

`ThoughtDef` 关键字段：`stages`(`ThoughtStage` 列表，含 `baseMoodEffect` / `opinionOffset`)、`durationDays`、`stackLimit`、`stackLimitForSameOtherPawn`、`stackedEffectMultiplier`(默认 0.75)、`maxCumulatedOpinionOffset`、`lerpOpinionToZeroAfterDurationPct`(默认 0.7)、`effectMultiplyingStat`、`neverNullifyIfAnyTrait`、`nullifyingTraits`、`taleDef`、`showBubble`。

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

`StatDefOf.WorkSpeedGlobal`（工作速度全局）、`StatDefOf.MoveSpeed`（移速）、`StatDefOf.PlantWorkSpeed`、`StatDefOf.WorkTableWorkSpeedFactor`、`StatDefOf.SocialImpact`（社交影响力，剪发想法/好感）。

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
