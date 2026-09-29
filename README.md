# SimpleProject — 宝箱商人（YooAsset + HybridCLR + Luban）

基于三大框架的开箱即用 Unity 工程模板，当前承载玩法：**宝箱商人**（排除博弈 + 角色技能 + 拍卖行）。

| 组件 | 版本 | 用途 |
|---|---|---|
| Unity | **2019.4.40f1** | 编辑器（HybridCLR 官方推荐版本） |
| [YooAsset](https://github.com/tuyoogame/YooAsset) | 3.0.5 | 资源管理 / 热更资源管线（本地嵌入包） |
| [HybridCLR](https://github.com/focus-creative-games/hybridclr) | v8.14.1 | C# 热更新（本地嵌入包） |
| [Luban](https://github.com/focus-creative-games/luban) | v5.1.0 | 配置表（xlsx 数据源，工具自编译） |

## 玩法说明（联机版，服务端权威）

启动服务器后，Unity Play 流程：

1. **登录**：输入玩家名（首次自动注册并发放初始金币，SQLite 持久化，重启不丢）
2. **主大厅**：商城 / 背包 / 拍卖行 / 排行榜 四个入口
3. **商城**：购买三档宝箱（木/银/金，20 格金额池，服务端洗牌）
4. **背包 → 开宝箱**：20 格面板点格子排除（前 5 次免费，之后递增收费）；
   随时出售给系统（收购价 = 期望 × 系数 0.80→1.00）；或赌到最后一格全额开箱
5. **开箱时九选一技能**（不再绑定角色，技能面板选择）：
   - 洞察：自动排除一个低于剩余平均值的选项
   - 抬价：本宝箱下次系统收购价 +15%
   - 幸运手：本宝箱接下来 3 次排除免费
   - 窥视：偷看任意一个未排除选项的金额（金色持续显示）
   - 探测：随机 3 个未排除选项中标记金额最高的一格（蓝色 ▲）
   - 保底：系统结算所得低于购买价 60% 时补足
   - 锁价：锁定当前收购价为底价，之后只涨不跌
   - 鉴定：挂拍后买家可见完整剩余金额分布（拍品溢价 +4%）
   - 减免：本宝箱接下来 5 次排除费用减半
   规则：宝箱自带技能额度 5 次（最多 5 个玩家各用一次），每个玩家对每个宝箱限一次，
   额度耗尽后易主也无法再用；剩余额度影响拍卖价值（机器人出价每点约 +5% EV，已鉴定再 +4%）
   **技能冷却**：同一技能使用后进入冷却，**跨宝箱生效**（服务端权威 + SQLite 持久化，重启不丢），
   冷却期间即使买新宝箱也不能重复使用同一技能，防止反复刷强力技能；时长配于 `tb_character.xlsx`
   的 `SkillCooldownSec` 列（洞察/保底 300s、锁价 240s、抬价/幸运手/减免 180s、鉴定 120s、探测 90s、窥视 60s）；
   技能面板实时显示倒计时并禁用冷却中的技能，到点自动解锁
6. **拍卖行**：挂拍（起拍/一口价）→ 出价（+5% 起，预扣、被超自动退回、有人出价顺延）→
   一口价/到期成交（卖家抽成 5%），流拍退回；机器人参与竞价
7. **排行榜（四榜 × 地区）**：现金总榜/现金周榜/开箱总榜/开箱周榜 × 全球榜/地区榜；
   地区来源：CDN 国家码头（CF-IPCountry/X-Country-Code，部署到 CDN 后自动生效）> 客户端上报（接 SDK 后换定位/自选）> GeoIP 扩展点（代码留 TODO）
8. **抢宝房（即时玩法）**：多档房间配置于 `TbRushRoom`（新手/豪华 + **盲抢**两种各一档），
   门票入池 → 机器人补满 6 人 → 3 秒倒计时 → 30 秒实时抢 20 格（点格即得，全房广播）→ 结算入账；
   **盲抢房金额隐藏、抢到才揭示，机器人同样不可见**（公平推理博弈，`Blind` 列配置）；
   原生 WebSocket（`/ws/rush`）双向推送；奖池 = 门票×人数×倍率（金字塔分布）
8b. **成长与赛季**：等级 1~10（`tb_level.xlsx` 配置经验门槛），经验来源 = 开箱结算 +10 / 对决胜利 +15（平 +5）/ 抢宝参与 +3；
   升级解锁：Lv2 窥视、Lv3 银宝箱+对赌房、Lv4 探测、Lv5 金宝箱、Lv6 保底（服务端权威裁决，客户端置灰提示）；
   **周赛季**：后台服务每周一按上周现金周榜自动发奖（前 10 名 400/250/150/50…，直接入账 + `season_reward` 流水，state 可查）；
   **对决天梯**：Elo 计分（K=32，初始 1000，双真人标准对局；对机器人 ±4/2 防刷），排行榜新增「对决天梯」页签
9. **镜像对决（即时 1v1 对抗）**：入场金 100 → 3 秒倒计时 → 双方各开一个「同金额池、不同格位」的镜像宝箱，
   并行排除（对决内免费）+ 技能（沿用技能冷却，跨玩法共享；保底兜底 60% 入场金）+ 银行家报价；随时「卖给银行」锁定分数给对手施压，
   双方锁定或 120 秒超时后比分数：高分赢奖池（入场金×2 扣 5% 抽成），平局退入场金；
   **勇气加成**：撑到最后一格开箱得分 ×1.2（鼓励风险路线，避免"到价就卖"单一策略）；
   对手排除的格号与金额实时公开（同池不同位 → 只泄露"池面"不泄露格位，信息战核心）；
   断线给 8 秒重连宽限，过期/中途退出按当前收购价锁定（逃跑惩罚）；机器人补位（目标 1.0~2.2 倍入场金，会排除/用洞察/接受报价）；
   原生 WebSocket（`/ws/duel`）；入口在大厅「对决」按钮（纯代码 UI，无 prefab 依赖）
10. **对赌房（赢家通吃）**：与镜像对决同规则同设施（镜像箱/技能冷却/报价/勇气加成/8s 宽限/机器人补位），
    但入场金 = 宝箱售价（默认木箱 75），**赢家独得两个宝箱的结算总价值**（扣 5% 抽成），
    输家一无所有，平局各自落袋自己箱子的价值——风险远高于普通对决，适合搏命翻盘；
    入口：大厅「对决」→ 模式选择；独立开关 `potduel`；流水 reason `potduel_win`
11. **新手教学箱**：0 次开箱的新玩家登录后自动弹「领取教学宝箱」（固定种子免费木箱，一次），
    开箱界面三步横幅教学（排除与期望 → 费用与落袋 → 报价抉择），结算即毕业（`POST /api/tutorial/chest`）
12. **邀请双奖（传播裂变）**：每玩家一个邀请码（玩家 Id），新人（未开过箱）绑定后，
    **首箱结算时双方各得 100 金币**（邀请人每日最多 5 人防刷，复用频控记账）；
    大厅「邀请」面板：我的邀请码 / 复制邀请文案 / 新人绑定入口；`POST /api/referral/bind`，流水 `referral`；开关 `referral`
13. **图片战报（炫耀分享）**：开箱结算（终局/落袋）与对决结算（胜负/对赌）弹出战报卡，
    「保存战报图」截屏生成 PNG 走 `ShareBridge.ShareImage`（SDK 接入后自动走平台图片消息；默认剪贴板+日志）
14. **道具商店（金币下水道，tb_item 落地）**：买即用消耗品，把金币变成便利——
    冷却重置券 200（清全部技能冷却）/ 补签卡 300（补回断签档位）/ 挑战券 150（每日挑战 +1 次，日限 3 张）/
    改名卡 500（含重名校验）；无效使用拒绝且不扣费；流水 `item_buy`（经济报表可监控下水道效果）；开关 `itemshop`
15. **每日签到（D1~D7 留存）**：7 天循环递增奖励（30/40/50/60/80/100/150，`tb_sign.xlsx` 配置），
    连续签到档位递进、断签本轮回第 1 天（**累计天数终身不清零**）；**看广告双倍当日奖励**（每日 1 次，复用广告频控）；
    面板含 7 天进度格 + 今日可得；`POST /api/sign`，流水 `sign_reward`；开关 `sign`
13. **每日任务（D1 留存核心）**：任务池配置于 `tb_quest.xlsx`（开箱/排除/对决/对决胜利/抢宝/广告/拍卖出价 7 种），
    **按天窗口轮换取 3 个**（全服同任务、确定性、重启不变），进度内存权威计数（事件埋点，无需 SQL）；
    领奖 `POST /api/quest/claim`（流水 `quest_reward`）；**看广告双倍领取**（每日 3 次，复用广告频控记账，与广告层打通）；
    状态随 `/api/state` 下发；入口：大厅「任务」按钮；开关 `quest`
17. **广告变现（前期唯一变现，无内购）**：激励视频三广告位配置于 `tb_ad.xlsx`
    （领金币 50×5 次/日、免费木箱 ×2 次/日、清全部技能冷却 ×1 次/日），频控全部服务端权威（日限+冷却+防重放），
    流水 reason `ad_reward`/`ad_chest`（经济报表可单独监控广告水龙头）；客户端 `AdBridge` 接口层
    （开发模式即插即用，接穿山甲/优量汇/AdMob 时实现 `IAdProvider` 并 SetProvider）；入口：大厅「福利」按钮；开关 `ad`
18. **分享**：`ShareBridge`（接口层）+ 默认剪贴板实现；服务端 `/api/share/content` 生成文案、`/api/share/report` 上报；
   接微信/抖音 SDK：实现 `IShareProvider` 并启动时 `ShareBridge.SetProvider(...)` 即可

> **玩法开关（tb_feature.xlsx）**：每个玩法一行（rush/auction/daily/duel/potduel），`Enabled=false` 时服务端拦截
> 对应路由与 WebSocket（返回 `FEATURE_DISABLED`），`/api/state` 的 `featureFlags` 同步下发、客户端自动隐藏
> 大厅入口；改表 → 双击 gen.bat → 重启服务端即生效；缺行默认开启（删行即恢复）。

> 防作弊：洗牌随机、未揭示格金额、钱包、技能裁决全部在服务端；客户端只拿聚合数值。
> **并发架构**：按实体细粒度锁（Player/Chest/Lot 各自 Sync，全局锁序 Room/Lot → Chest → Player，多玩家按 Id 排序），
> 鉴权走令牌索引 O(1)，仓储为并发字典，Id 分配 Interlocked，共享随机源线程安全；
> 写路径不再阻塞游戏逻辑（入队微秒级，后台单写者事务合批落库，停机自动排空）；
> 压测（本机，aiohttp keep-alive）：旧架构（全局锁+同步写）100 并发 3622 RPS / p95 45.7ms / max 243ms，
> 新架构 **4844 RPS / p95 23.6ms / max 44ms**（吞吐 +34%，尾延迟减半，最大延迟 5.6 倍改善）；300 并发 4400+ RPS 零错误。
> **运维保障**：抢宝门票/对决入场金入房即记 `active_stake`，服务端异常重启后自动退款（`stake_refund` 流水）；
> 经济报表 `GET /api/admin/economy`（`X-Admin-Token` 头，token 配置 `AdminToken`，默认 `dev-admin`）：
> 按流水 reason 聚合真人/机器人收支 + 近 7 日真人净流入 + 存量，用于监控机器人注水与通胀。

## 快速开始

1. 启动服务器：`cd Server && dotnet run --urls http://127.0.0.1:5188`
2. 用 Unity 2019.4.40f1 打开工程，打开 `Assets/Scenes/Boot.unity`，Play → 登录。

## 宝箱金额生成算法（Rarity Template + EV 归一化）

`Chest.GenMode`：0=固定金额池（洗牌，结构恒定）；1=算法生成（每箱结构随机）。

算法流程（服务端 `ChestFactory`）：
1. 按权重 Roll 每格稀有度档（白/绿/蓝/紫/金，权重配于 `TbChestGen`）
2. 档位基准金额 × 对数正态扰动（`exp(jitter·N(0,1))`，Box-Muller）
3. **EV 归一化**：整体缩放至固定池总池（期望严格恒定，庄家边际不变），取整误差由最大格修正

当前：木箱固定池（新手教学），银/金箱算法生成。参数在 `tb_chestgen.xlsx`（权重/基准/扰动），
基准应使加权均值≈固定池期望以减小归一化形变。实测：多箱总池严格恒定、金额连续分布、结构每箱不同。

## 配置（LubanWorkspace/Data/*.xlsx，改完双击 gen.bat）

| 文件 | 内容 |
|---|---|
| `tb_chest.xlsx` | 宝箱档位：售价 + 20 格金额池 + GenMode（0 固定/1 算法生成） |
| `tb_chestgen.xlsx` | 生成器参数：稀有度权重 / 档位基准 / 扰动强度 |
| `tb_banker.xlsx` | 系统收购系数曲线（0.80→1.00） |
| `tb_character.xlsx` | 角色与技能（SkillType: 1洞察/2抬价/3免排…9减免 + 每技能冷却秒数 `SkillCooldownSec`） |
| `tb_global.xlsx` | 初始金币 2000、免排 5 次、排除费、拍卖时长 60s、加价 5%、抽成 5%、宝箱技能额度 5 次、镜像对决（入场金 `DuelStake`/档位 `DuelChestId`/时限 `DuelMatchSec`） |
| `tb_feature.xlsx` | **玩法开关**：rush/auction/daily/duel/potduel/ad/quest/sign/itemshop/referral 每行一个 Enabled 布尔 |
| `tb_ad.xlsx` | 广告位：日限/冷却/奖励（金币或宝箱档位；均为 0 = 清技能冷却） |
| `tb_quest.xlsx` | 每日任务池：事件类型/目标/奖励（按天窗口轮换取 3 个） |
| `tb_sign.xlsx` | 签到奖励：7 天循环（Day → 金币） |
| `tb_level.xlsx` | 玩家等级：经验门槛（XpNeed）与解锁说明（UnlockText），1~10 级 |
| `tb_item.xlsx` | 道具商店：冷却重置/补签/挑战券/改名卡（Kind 决定使用效果，买即用） |

xlsx 约定：第 1 行字段名（A 列 `##` 勿动）、第 3 行起数据；列表用 `|` 分隔；枚举填名称；引用错误生成时即报错。

## UI Prefab 工作流（烘焙 → 手摆的过渡护栏）

- **代码契约**：prefab 与热更代码的唯一契约是**节点路径 + 组件类型**（`UIManager.Find` 的键）；
  权威表在 `Assets/Editor/ValidateUIPrefabs.cs`
- **校验**：菜单 `Build/UI/Validate UI Prefabs` —— 逐 prefab 校验契约节点/组件，缺失即 FAIL；
  并反扫源码 Find 字面量，契约表未收录的新键会警告（防表过期）
- **契约文档**：菜单 `Build/UI/Export UI Contract` —— 导出 `Docs/UIContract.md`（手摆对账单）
- **防误覆盖**：`Build/UI/Bake UI Prefabs` 已加确认弹窗——手摆过的 prefab 会被烘焙整体重置，慎用
- **手摆纪律**：保留节点名/父子结构（装饰节点随意加）；静态文本写中文原文（运行时 L10n 按中文 Key 自动翻译）；
  改完跑校验 + 重打 YooAsset 包

## 上线换库（SQLite → MySQL，老数据完整平移）

`Server/migrate_to_mysql.py`（迁移 + 对账一体，源库只读，绝不修改；simplegame.db 原文件即回滚保险）：

```bash
cd Server
python migrate_to_mysql.py --check-only                 # 1) 迁移前自检：金币守恒（流水净额==存量，含预扣修正）
python migrate_to_mysql.py --host <h> --user <u> --password <p> --db simplegame   # 2) 停服后迁移（目标非空会拒绝；--fresh 清空重迁）
python migrate_to_mysql.py --host <h> ... --check-only-target                       # 3) 迁移后对账：9 表行数+顺序无关逐行校验和全比对
```

要点：JSON TEXT 列原样平移、gold_tx 保留原自增 id、MySQL 库需 utf8mb4；对账 FAIL 则勿切换。
SQL 方言差异（UPSERT 语法/参数前缀/PRAGMA）改 `Db.cs` 即可，业务代码零改动。
实测对账样例：2598 玩家 / 7300 宝箱 / 10085 流水，金币守恒零误差。

## 多语言

`tb_text.xlsx`（Key = 中文原文，En 列英文，当前 199 条）：双端共用。客户端 `L10n`（`UI/Common/L10n.cs`）：
- 中文即 Key —— zh 环境零查找；en 查表回退原文
- prefab 静态文本：`UIManager.ShowAsync` 实例化后 `L10n.LocalizeTransform` 自动翻译
- 动态拼接文案：代码中 `L10n.Get("...")`（已替换 88 处）
- 语言切换：登录屏/主大厅右上角 EN/中文 按钮（PlayerPrefs 持久化）

## UI 代码结构（按模块分文件夹）

```
Assets/HotUpdateScripts/
├── Core/            # ApiClient / OnlineState / GameConfig（通信与全局状态）
├── HotUpdateEntry.cs / GameHub.cs
└── UI/              # 全部界面类
    ├── Common/      # UIManager / UIRoot / UITheme / TDUIHelper / L10n / ShareBridge / ScreenCommon(ErrText)
    ├── Login/ Lobby/ Shop/ Bag/ Chest/ Auction/ Rank/ Daily/ Rush/
    │                # Screens 按 partial 拆分到各模块（LoginScreen.cs 等）+ 各界面组件
```

## UI 主题（秘宝暗金）

一套程序化生成的完整 UI 皮肤：`Assets/AssetRaw/UI/`（19 张 PNG + 规范文档 `UI_THEME.md`）。

- **内容**：九宫格面板（暗金/暗色）、四种状态按钮、开箱格子、三档宝箱图标、金币、三个技能图标、主大厅四入口图标
- **主题系统**：`UITheme.cs` 统一加载 Sprite（自动九宫格）+ 组件皮肤 API（`ThemedButton` / `CreatePanel` / `IconCard`）
- **已应用**：全部界面（皮肤在 prefab 内）
- **界面 prefab 化**：7 个界面静态框架烘焙为 prefab（`Assets/AssetRaw/Prefab/UI/`，收集进 YooAsset 资源包），
  `UIManager.ShowAsync(name)` 加载实例化、`CloseCurrent(unloadUnused:true)` 销毁并回收界面专属图集；
  改布局后重跑菜单 **Build/UI/Bake UI Prefabs** 即重新烘焙（界面成为可热更资源）
- **控件绑定约定**：prefab 节点名 = 代码 `UIManager.Find(root, path)` 的键（如 `btn_login` / `slot_0..19` / `btn_skill_1..3`）
- **替换为美术成品**：同名覆盖 PNG、保持 `UI_THEME.md` 中的九宫格参数即可，代码零改动
- **再生成工具**：`python Tools/gen_ui_assets.py`（Pillow）

## 目录结构

```
Assets/
├── AssetRaw/            # YooAsset 收集根（Config 配置 json / 热更 dll 等）
├── GameFramework/       # AOT 启动引导（GameApp，不热更）
├── HotUpdateScripts/    # ★ 热更代码（Game.HotUpdate 程序集）
│   ├── ChestGame/       # 宝箱玩法（Chest 规则 / 主界面 / 拍卖行 / UI 工具）
│   ├── Gen/             # Luban 生成代码
│   ├── GameHub.cs       # 主菜单（角色选择）
│   └── HotUpdateEntry.cs
├── Editor/              # 工程初始化 / 构建辅助
LubanWorkspace/          # ★ 配置工作区（Defines 结构 xml + Data xlsx + gen.bat）
Packages/                # manifest + 三个内嵌包
```

## 开发流程

- **改玩法代码**：`Assets/HotUpdateScripts/` 下直接改 → Play（编辑器模拟模式自动使用编译好的热更程序集）。
- **改数值**：改 xlsx → 双击 `LubanWorkspace/gen.bat` → Play。
- **测试真实热更链路**：菜单 Build/HybridCLR/Copy HotUpdate Dlls 后 Play。
- **真机/出包**：HybridCLR/Installer 安装 → HybridCLR/Generate/All → YooAsset Collector 构建 → 资源上 CDN（改 `GameApp.RemoteHostURL`）→ IL2CPP 打包。详见各官方文档。

## 常见问题

- HybridCLR 安装需 git + Gitee 可达（默认源已指向 Gitee）。
- IL2CPP 构建需 VS「C++ 游戏开发」工作负载。
- Luban 生成需 .NET 8 SDK；改工具源码后重新 `dotnet publish Luban/src/Luban -c Release -o LubanWorkspace/Tools/Luban`。

## 自建服务端（SimpleGameServer）

服务端权威架构已就绪：钱包、宝箱生成/揭示、技能额度与按玩家限用、拍卖撮合、机器人竞价全部在服务端，
未揭示格的金额**绝不下发客户端**（防作弊核心）。

```
Server/                        # ASP.NET Core 8（与客户端共用 Luban 配置与 json 数据）
├── Program.cs                 # 路由：登录/选角/买箱/排除/技能/出售/拍卖 + SignalR 推送
├── Config/GameTables.cs       # 加载 Assets/AssetRaw/Config/*.json（与客户端同一份数据）
├── Game/Db.cs                 # 持久层：后台单写者合批落库（Channel+事务），读走短连接（WAL 单写多读）
├── Game/Models.cs             # 服务端权威实体（Chest 技能额度/SkillUsedBy 集合等）
├── Game/GameStore.cs          # 内存仓储（lock 保护；接 MySQL 时以仓储接口替换）
├── Game/GameService.cs        # 玩法逻辑 + 拍卖结算 + 机器人竞价
└── Hubs/AuctionHub.cs         # SignalR：竞价/结算实时推送
```

### 启动与联调

```bash
cd Server && dotnet run --urls http://127.0.0.1:5188
# 冒烟自测（登录→买箱→排除→技能→限用校验→出售→拍卖→出价）
python server_smoke_test.py    # 或按 README 协议用 curl
# 镜像对决回归（需 pip install websockets）
python duel_e2e_test.py        # 双人镜像对决全流程
python duel_bot_test.py        # 单人 + 机器人补位 + 金币一致性
python duel_courage_test.py    # 勇气加成（最后一格开箱 ×1.2）
python duel_grace_test.py      # 断线重连宽限 + 宽限到期锁定
python duel_pot_test.py        # 对赌房赢家通吃（两箱总价值扣 5%）
python admin_tutorial_test.py  # 经济报表鉴权 + 教学箱一次性发放
python ad_test.py            # 广告发奖/日限/冷却/清冷却/无效广告位
python quest_test.py         # 每日任务轮换/进度/领取/双倍
python sign_test.py         # 签到/断签档位/广告双倍/重复拒绝
python item_test.py         # 道具购买/效果/防重名/经济报表下水道
python referral_test.py      # 邀请绑定/双发奖/重复绑定/邀请计数
python levels_test.py         # 等级/经验/解锁门槛（宝箱/技能/对赌房）
python rush_blind_test.py     # 盲抢房金额隐藏 + 抢到揭示
python duel_rating_test.py    # 双真人 Elo 天梯 + 胜利经验
# 经济报表（运维）
curl -H "X-Admin-Token: dev-admin" http://127.0.0.1:5188/api/admin/economy
```

接口速览（Bearer token 鉴权，JSON）：
`POST /api/login`、`POST /api/character`、`GET /api/state`、`POST /api/chest/buy`、`POST /api/tutorial/chest`、
`POST /api/chest/{uid}/eliminate`、`POST /api/chest/{uid}/skill`、`POST /api/chest/{uid}/sell`、
`GET /api/auction`、`POST /api/auction/list`、`POST /api/auction/{lotId}/bid`、`POST /api/auction/{lotId}/buyout`、
`GET /api/admin/economy`（`X-Admin-Token`）；
实时事件：SignalR `/hubs/auction`（`LotChanged` / `LotSettled`）。

配置双端：客户端跑 `gen.bat`，服务端跑 `gen_server.bat`（同一份 xlsx → 两份代码 + 同一份 json 数据）。

客户端接入：`Assets/HotUpdateScripts/ChestGame/ApiClient.cs` 已封装全部接口
（`UseServer = false` 本地模拟 / `true` 走服务器）；下一步将 ChestGame 的操作改为调 API 并由 `/api/state` 驱动 UI。

存储：当前内存版（重启清档），MySQL/Redis 与账号体系按需接入。

### 本地部署（SQLite 持久化已内置）

```bash
# 1. 启动（首次自动建库 Server/simplegame.db，之后重启数据自动恢复）
cd Server && dotnet run --urls http://127.0.0.1:5188

# 2. 回归测试（登录→买→排除→技能限用→出售→拍卖→出价）
python server_smoke_test.py
```

- **数据文件**：`Server/simplegame.db`（备份 = 复制该文件；清档 = 删除后重启）
- **持久化范围**：玩家（金币/预扣/角色）、宝箱（20 格金额/揭示进度/技能额度/增益）、进行中拍品、金币流水（`gold_tx` 表，对账用：`sqlite3 simplegame.db "SELECT * FROM gold_tx"`）
- **换库路径**：`Db.cs` 是独立持久层（建表/CRUD/恢复），上 MySQL 时替换该实现即可，玩法代码不动
- 自定义路径：`dotnet run --urls ... --GameDb path/to/db --GameDataDir path/to/config`
