# SimpleProject — YooAsset + HybridCLR + Luban 塔防示例工程

一个开箱即用的 Unity 热更工程模板，含一个完整的 **Q 版塔防小游戏**，演示三大框架的标准集成方式：

| 组件 | 版本 | 用途 |
|---|---|---|
| Unity | **2019.4.40f1** | 编辑器（HybridCLR 官方推荐版本，本机已装） |
| [YooAsset](https://github.com/tuyoogame/YooAsset) | 3.0.5 | 资源管理 / 热更资源管线（本地嵌入包） |
| [HybridCLR](https://github.com/focus-creative-games/hybridclr) | v8.14.1 | C# 热更新（本地嵌入包） |
| [Luban](https://github.com/focus-creative-games/luban) | v5.1.0 | 配置表（工具源码 + 自编译） |

三个包全部内嵌在 `Packages/` 下（离线可复现），HybridCLR 安装源指向 Gitee（无需科学上网）。

---

## 目录结构

```
SimpleProject/
├── Assets/
│   ├── AssetRaw/                  # YooAsset 收集根目录
│   │   ├── Config/                # Luban 生成的 json 配置（生成物）
│   │   ├── Prefab/                # 游戏 prefab（首次打开自动生成示例）
│   │   ├── HotUpdateDlls/         # 热更 dll（菜单生成，.bytes）
│   │   └── AOTMetadataAssemblies/ # AOT 补充元数据 dll（菜单生成，.bytes）
│   ├── GameFramework/GameApp.cs   # AOT 启动引导（不热更）
│   ├── HotUpdateScripts/          # ★ 热更代码（Game.HotUpdate 程序集）
│   │   ├── TowerDefense/          # 塔防玩法（热更示例）
│   │   └── Gen/                   # Luban 生成的 C# 配置代码（生成物）
│   ├── Scenes/Boot.unity          # 启动场景（首次打开自动生成）
│   └── Editor/                    # 工程初始化 / 构建辅助脚本
├── Luban/                         # Luban 工具源码（v5.1.0）
├── LubanWorkspace/                # ★ 配置表工作区
│   ├── Defines/                   # 表结构定义（xml）
│   ├── Data/                      # 表数据（xlsx，Excel 直接编辑）
│   ├── Tools/Luban/               # 自编译的生成工具
│   └── gen.bat                    # 双击生成
├── Packages/                      # manifest + 三个内嵌包
├── ProjectSettings/
└── HybridCLRData/                 # HybridCLR 安装/构建产物（打开工程后生成）
```

带 ★ 的是日常开发最常打交道的目录。

---

## 快速开始

1. 用 **Unity 2019.4.40f1** 打开本工程（Unity Hub → Add → 选择本目录）。
2. 首次打开会自动完成：生成 YooAsset 收集器配置、Boot 场景、示例 prefab（Console 可见 `[ProjectSetup]` 日志）。
3. 打开 `Assets/Scenes/Boot.unity`，点 **Play**，即可进入塔防游戏。

> 首次 Play 前请确保 `LubanWorkspace/gen.bat` 已经跑过（本仓库已提交生成产物，开箱即玩）。

## 塔防玩法说明

- 初始金币/生命来自配置表（默认 150 金币 / 10 生命），共 5 波敌人。
- 底部按钮选择塔类型（箭塔/炮塔/冰霜塔），再点击地面上的**绿色地块**建造。
- 波间 8 秒倒计时后自动出兵（也可点「出兵」提前）；全部波次守住即胜利，生命归零失败（可重开）。
- 敌人不会攻击塔，走到终点即扣生命（塔防常规设计）。
- 所有数值（塔属性、怪物血量/移速/奖励、波次编成）都在 `LubanWorkspace/Data/*.xlsx`，改完跑 `gen.bat` 再 Play 即生效 —— **这就是配置热更**。

### 自动化冒烟测试（可选）
无头验证整条链路（初始化 → 自动建塔 → 出兵 → 断言塔打出伤害）：
```
Unity.exe -batchmode -projectPath . -executeMethod ProjectSetup.SetupAndSmokeTest -smoketest -logFile smoke.log
```
退出码 0 = 通过（日志搜 `[SMOKE] ... PASS`）。

## 日常开发流程

### 改热更代码（塔防玩法在 `Assets/HotUpdateScripts/`）
直接改代码 → Play。编辑器模拟模式下 Unity 直接使用编译好的热更程序集。

### 改配置表（Excel）
1. 用 Excel/WPS 打开 `LubanWorkspace/Data/*.xlsx` 直接改数值（第 1 行字段名、第 3 行起数据，A 列为标签列请留空）；
2. 双击 `LubanWorkspace/gen.bat`，生成物自动落到 `Assets/HotUpdateScripts/Gen/` 与 `Assets/AssetRaw/Config/`；
3. Play 验证。

xlsx 填写约定（Luban Excel 规范）：
- 列表字段用 `|` 分隔（如怪物表 DropList 填 `1003|1003`）；
- 嵌套结构列表（如波次表 Items 列）填 JSON：`[{"MonsterId":10001,"Count":8,"Interval":1.2}]`
 （该列标题声明为 `Items#format=json`）；
- 枚举填枚举名（White/Green/…），id 引用错误会在生成时报错。

### 测试“真实热更流程”（编辑器内）
菜单 **Build/HybridCLR/Copy HotUpdate Dlls**：编译热更 dll 并拷贝进收集目录，
此后 Play 走「从资源包加载 Game.HotUpdate」的真实链路（Console 会打印加载字节数）。

### 真机 / 出包流程
1. **HybridCLR/Installer** → 点 Install（从 Gitee 拉取，约 30 秒；只需一次）。
2. **HybridCLR/Generate/All**（生成 link.xml、桥接函数、AOT 泛型补充等）。
3. **Build/HybridCLR/Copy HotUpdate Dlls**（热更 dll + AOT 元数据进收集目录）。
4. **YooAsset/AssetBundle Collector** 面板 → Build（产物在 `Bundles/<平台>/DefaultPackage/<版本>`）。
5. 首包：把 Build 产物按 `EBundledCopyOption` 拷入 StreamingAssets（或手动拷 `PackageOutput` 目录）。
6. 更新：把产物目录放到 HTTP 服务器，改 `GameApp.RemoteHostURL`，客户端 HostPlayMode 自动比对下载。
7. 打 APK/EXE（IL2CPP）。注意 `GameApp.DevicePlayMode` 默认 HostPlayMode，
   无资源服务器时可临时切 `EPlayMode.OfflinePlayMode`（全内置，免服务器）。

---

## 关键机制说明

- **启动链路**：`Boot.unity → GameApp(AOT)` → YooAsset 初始化（编辑器 EditorSimulate / 真机 HostPlay）
  → AOT 补充元数据（真机）→ `Assembly.Load(Game.HotUpdate)` → 反射 `HotUpdateEntry.StartGame()`。
- **配置加载**：热更代码通过 YooAsset 按文件名加载 json（如 `tbitem`）→ `cfg.Tables`。
  配置**代码**在热更程序集内、**数据**在资源包内 —— 改表结构与数值都不需要重发 APP。
- **AOT 与热更隔离**：`Game.HotUpdate.asmdef` 的 `autoReferenced=false`，AOT 侧零引用，
  Editor 脚本通过反射挂载热更组件（`Rotator`），保证打包关系干净。
- **收集器**：首次打开由 `ProjectSetup` 自动生成（DefaultPackage，Addressable 模式，
  收集 Config / Prefab / HotUpdateDlls / AOTMetadataAssemblies 四组）。

## 常见问题

- **HybridCLR 安装失败**：确认 git 可用、Gitee 可访问（默认源 `gitee.com/focus-creative-games/...`）。
- **IL2CPP 构建报错**：需要 Visual Studio 2019+ 的「使用 C++ 的游戏开发」工作负载。
- **Luban 生成失败**：需 .NET SDK 8.0+（`dotnet --version` 检查）；改了工具源码后重新
  `dotnet publish Luban/src/Luban -c Release -o LubanWorkspace/Tools/Luban`。
- **升级 Unity**：HybridCLR 官方支持 2019.4~6000.x LTS；本工程锁 2019.4.40f1。
