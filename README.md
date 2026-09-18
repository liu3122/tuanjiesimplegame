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
5. **开箱时三选一技能**（不再绑定角色）：
   - 洞察：自动排除一个低于剩余平均值的选项
   - 抬价：本宝箱下次系统收购价 +15%
   - 幸运手：本宝箱接下来 3 次排除免费
   规则：宝箱自带技能额度 3 次（最多 3 个玩家各用一次），每个玩家对每个宝箱限一次，
   额度耗尽后易主也无法再用；剩余额度影响拍卖价值（机器人出价每点约 +5% EV）
6. **拍卖行**：挂拍（起拍/一口价）→ 出价（+5% 起，预扣、被超自动退回、有人出价顺延）→
   一口价/到期成交（卖家抽成 5%），流拍退回；机器人参与竞价
7. **排行榜**：金币排行（机器人填充展示数据，自己高亮）

> 防作弊：洗牌随机、未揭示格金额、钱包、技能裁决全部在服务端；客户端只拿聚合数值。

## 快速开始

1. 启动服务器：`cd Server && dotnet run --urls http://127.0.0.1:5188`
2. 用 Unity 2019.4.40f1 打开工程，打开 `Assets/Scenes/Boot.unity`，Play → 登录。

## 配置（LubanWorkspace/Data/*.xlsx，改完双击 gen.bat）

| 文件 | 内容 |
|---|---|
| `tb_chest.xlsx` | 宝箱档位：售价 + 20 格金额池 |
| `tb_banker.xlsx` | 系统收购系数曲线（0.80→1.00） |
| `tb_character.xlsx` | 角色与技能（SkillType: 1洞察/2抬价/3免排） |
| `tb_global.xlsx` | 初始金币 2000、免排 5 次、排除费、拍卖时长 60s、加价 5%、抽成 5%、宝箱技能额度 3 次 |
| `tb_item.xlsx` | 道具表（预留） |

xlsx 约定：第 1 行字段名（A 列 `##` 勿动）、第 3 行起数据；列表用 `|` 分隔；枚举填名称；引用错误生成时即报错。

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
