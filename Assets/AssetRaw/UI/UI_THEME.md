# UI 主题规范 — 「秘宝暗金」主题

全部 UI 素材位于 `Assets/AssetRaw/UI/`（PNG，程序化生成，可整体替换为美术成品）。
运行时由 `UITheme.cs` 统一加载与应用；替换贴图无需改代码。

## 色板

| 用途 | 色值 | 说明 |
|---|---|---|
| 主背景 | `#12151D` | 深蓝黑，全局底色 |
| 面板底 | `#1C2230` | 卡片/弹窗底 |
| 面板描边 | `#E8C35A` | 暗金描边（核心品牌色） |
| 强调金 | `#F5D061` | 金币、标题、高亮 |
| 次级文本 | `#9AA3B2` | 说明文字 |
| 成功/可买 | `#4DD0A5` | 确认按钮、余额充足 |
| 危险/失败 | `#E05D5D` | 出售、警示 |
| 冰蓝/技能 | `#6EC6FF` | 冰霜、信息提示 |

## 字体层级（fontSize @ 1920×1080 参考分辨率）

- 大标题 56 / 页面标题 40 / 卡片标题 34
- 正文 24~26 / 按钮 26~30 / 注释 20

## 九宫格参数（Unity Sprite Editor Border，L,T,R,B）

| 素材 | 尺寸 | Border |
|---|---|---|
| panel_gold.png | 256×256 | 32,32,32,32 |
| panel_dark.png | 256×256 | 32,32,32,32 |
| btn_gold.png / btn_green.png / btn_red.png / btn_blue.png | 192×64 | 24,16,24,16 |
| slot_locked.png / slot_open.png | 128×128 | 20,20,20,20 |

## 图标（128×128，居中留白 8px）

coin（金币）、chest_wood / chest_silver / chest_gold（三档宝箱）、
skill_insight（洞察·眼）、skill_price（抬价·向上箭头）、skill_free（免排·闪电）、
icon_shop / icon_bag / icon_auction / icon_rank（主大厅四入口）

## 图集拆分（spritePackingTag，与 UITheme._atlasOf 一致）

| 图集 | 内容 | 使用界面 |
|---|---|---|
| **common** | 双面板、四按钮、金币 | 全部（常驻） |
| **main** | 主大厅四入口图标 | 主大厅、拍卖行行图标（暂复用） |
| **shop** | 三档宝箱 | 商城 |
| **open** | 开箱格子×2、技能×3 | 背包、开箱 |

界面关闭后其专属图集可被  整体回收；common 常驻。
新增素材：在  表中登记归属，并同步 。

## 约定

- 图片导入 Unity 后：Texture Type = Sprite (2D and UI)，勾选九宫格的按上表填 Border
- `UITheme.Apply()` 幂等，可在任何界面构建前调用
- 换主题 = 换 `Assets/AssetRaw/UI/` 下的 PNG + 本文件色板
