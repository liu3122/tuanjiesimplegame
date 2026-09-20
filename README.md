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
6. **拍卖行**：挂拍（起拍/一口价）→ 出价（+5% 起，预扣、被超自动退回、有人出价顺延）→
   一口价/到期成交（卖家抽成 5%），流拍退回；机器人参与竞价
7. **排行榜**：金币排行（机器人填充展示数据，自己高亮）

> 防作弊：洗牌随机、未揭示格金额、钱包、技能裁决全部在服务端；客户端只拿聚合数值。

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
| `tb_character.xlsx` | 角色与技能（SkillType: 1洞察/2抬价/3免排） |
| `tb_global.xlsx` | 初始金币 2000、免排 5 次、排除费、拍卖时长 60s、加价 5%、抽成 5%、宝箱技能额度 3 次 |
| `tb_item.xlsx` | 道具表（预留） |

xlsx 约定：第 1 行字段名（A 列 `##` 勿动）、第 3 行起数据；列表用 `|` 分隔；枚举填名称；引用错误生成时即报错。

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
```

接口速览（Bearer token 鉴权，JSON）：
`POST /api/login`、`POST /api/character`、`GET /api/state`、`POST /api/chest/buy`、
`POST /api/chest/{uid}/eliminate`、`POST /api/chest/{uid}/skill`、`POST /api/chest/{uid}/sell`、
`GET /api/auction`、`POST /api/auction/list`、`POST /api/auction/{lotId}/bid`、`POST /api/auction/{lotId}/buyout`；
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
