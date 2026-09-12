# Languages — 本地化目录约定

本目录存放玩家可见文本，**中英必须逐键对称**。

```
Languages/
├── ChineseSimplified/
│   ├── DefInjected/     Def 字段文本（文件名与 Defs/ 一一对应）
│   └── Keyed/           代码内文本（"Key".Translate()）
└── English/
    ├── DefInjected/
    └── Keyed/
```

## 四条文本路径（不可混用）

| 文本来源 | 存放位置 | 键格式 | 示例 |
| --- | --- | --- | --- |
| Def 字段（`label` / `description` / `stages[].label`） | `DefInjected/<Def类型>/<与 Defs 对应文件名>.xml` | `<DefName>.<字段>` | `<Clap.label>击掌</Clap.label>` |
| `rulesStrings` 日志规则 | 同上 | `<DefName>.<规则包字段>` | 整表替换 |
| 代码内文本 | `Keyed/<域>.xml` | `ClapYourHands.<域>.<语义>` | `<ClapYourHands.Clap.Perfect>完美的击掌</ClapYourHands.Clap.Perfect>` |
| 任务节点 SlateRef 文本 | Def 元素加 `TKey` + 语言文件 | `<DefName>.<引用名>.slateRef` | （本项目暂未使用） |

## 硬性规则

```
✅ Def 内文本一律保留英文默认值作兜底，由 DefInjected 覆盖
✅ 新增术语先登记到 docs/C/C-06-本地化术语表.md
❌ 把 Keyed 键填进 Def 字段 —— Def 字段不会自动 Translate，只会原样显示键名
❌ 清空 Def 内文本 —— 语言键缺失时会退化成 defName
❌ 将纯展示文本硬编码在逻辑代码里（例外：运行时动态拼接）
```

> ⚠️ `DefInjected` 中引用的 Def 必须真实存在，否则游戏加载会报 `Could not find def`。
> 因此语言文件应与 `Defs/` 同步创建，先有 Def 再加语言条目。
