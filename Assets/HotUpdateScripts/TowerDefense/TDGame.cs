using System.Collections;
using System.Collections.Generic;
using cfg;
using cfg.tower;
using cfg.wave;
using UnityEngine;

namespace Game.HotUpdate.TowerDefense
{
    public enum TDGameState
    {
        Ready,      // 等待出兵
        Running,    // 波次进行中
        Win,
        Lose,
    }

    /// <summary>
    /// 塔防主控：经济/生命/波次调度、建造、胜负判定。
    /// 所有数值来自 Luban 配置表（cfg.Tables）。
    /// </summary>
    public class TDGame : MonoBehaviour
    {
        public static TDGame Instance { get; private set; }

        public TDGameState State { get; private set; }
        public int Gold { get; private set; }
        public int Life { get; private set; }
        public int CurrentWave { get; private set; }       // 已开始的波数
        public int TotalWaves => GameConfig.Tables.TbWave.DataList.Count;
        public int SelectedTowerId { get; private set; }   // 0 = 未选择

        /// <summary>波间自动出兵倒计时剩余秒数（0 = 不在倒计时）</summary>
        public float AutoWaveCountdown { get; private set; }

        public readonly List<TDEnemy> Enemies = new List<TDEnemy>();
        private readonly List<TDTower> _towers = new List<TDTower>();
        private readonly List<TDBuildSlot> _slots = new List<TDBuildSlot>();
        private TDUI _ui;
        private bool _spawnFinished;
        private const float AutoWaveDelay = 8f;
        private float _readyTimer;

        private Transform _root;

        public static void StartNewGame()
        {
            if (Instance != null)
            {
                Destroy(Instance.gameObject);
            }
            var go = new GameObject("TDGame");
            go.AddComponent<TDGame>();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            BuildLevel();
            _ui = gameObject.AddComponent<TDUI>();
            Gold = GameConfig.Tables.TbGlobal.InitGold;
            Life = GameConfig.Tables.TbGlobal.InitLife;
            State = TDGameState.Ready;
            Debug.Log($"[TDGame] 塔防就绪：{TotalWaves} 波，初始金币 {Gold}，生命 {Life}。点下方按钮选塔，再点绿色地块建造；点“出兵”开始。");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>搭建关卡：相机、地面、路径、塔位、起终点</summary>
        private void BuildLevel()
        {
            _root = transform;

            // 相机俯视（按关卡包围盒自适应，保证任何屏幕比例下都能看全地图）
            Camera main = Camera.main;
            if (main != null)
            {
                main.clearFlags = CameraClearFlags.SolidColor;
                main.backgroundColor = new Color(0.36f, 0.58f, 0.78f);
                TDLevel.FitCamera();
            }

            // 地面
            var ground = TDUtil.Primitive(PrimitiveType.Cube, new Color(0.55f, 0.78f, 0.45f), "ground");
            ground.transform.SetParent(_root, false);
            ground.transform.localScale = new Vector3(34, 0.2f, 22);
            ground.transform.position = new Vector3(0, -0.1f, 0.5f);

            // 路径（土黄色窄条）
            var roadColor = new Color(0.82f, 0.71f, 0.48f);
            for (int i = 0; i < TDLevel.Waypoints.Length - 1; i++)
            {
                Vector3 a = TDLevel.Waypoints[i];
                Vector3 b = TDLevel.Waypoints[i + 1];
                Vector3 center = (a + b) / 2f;
                float length = Vector3.Distance(a, b);
                bool horizontal = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
                var road = TDUtil.Primitive(PrimitiveType.Cube, roadColor, $"road{i}");
                road.transform.SetParent(_root, false);
                road.transform.localScale = horizontal
                    ? new Vector3(length + 1.4f, 0.08f, 1.4f)
                    : new Vector3(1.4f, 0.08f, length + 1.4f);
                road.transform.position = new Vector3(center.x, 0.045f, center.z);
                Object.Destroy(road.GetComponent<Collider>());
            }

            // 起点（绿柱）与终点（红柱）
            MakeMark(TDLevel.Waypoints[0], new Color(0.3f, 0.85f, 0.4f), "start");
            MakeMark(TDLevel.Waypoints[TDLevel.Waypoints.Length - 1], new Color(0.9f, 0.3f, 0.3f), "end");

            // 塔位
            for (int i = 0; i < TDLevel.BuildSlots.Length; i++)
            {
                var slotGo = new GameObject($"slot_{i}");
                slotGo.transform.SetParent(_root, false);
                slotGo.transform.position = TDLevel.BuildSlots[i];
                var slot = slotGo.AddComponent<TDBuildSlot>();
                slot.Init(i);
                _slots.Add(slot);
            }
        }

        private void MakeMark(Vector3 pos, Color color, string name)
        {
            var mark = TDUtil.Primitive(PrimitiveType.Cylinder, color, name);
            mark.transform.SetParent(_root, false);
            mark.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
            mark.transform.position = pos + new Vector3(0, 1f, 0);
            Object.Destroy(mark.GetComponent<Collider>());
        }

        private void Update()
        {
            if (State != TDGameState.Ready && State != TDGameState.Running)
            {
                return;
            }

            // 波间自动出兵倒计时（也可手动提前）
            if (State == TDGameState.Ready && CurrentWave < TotalWaves)
            {
                AutoWaveCountdown = Mathf.Max(0f, AutoWaveDelay - _readyTimer);
                _readyTimer += Time.deltaTime;
                if (_readyTimer >= AutoWaveDelay)
                {
                    _readyTimer = 0f;
                    StartNextWave();
                }
            }
            else if (State != TDGameState.Ready)
            {
                _readyTimer = 0f;
                AutoWaveCountdown = 0f;
            }

            // 清理死亡敌人
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                if (Enemies[i] == null || Enemies[i].IsDead)
                {
                    Enemies.RemoveAt(i);
                }
            }

            // 波次结束判定：出兵完毕且场上无敌人
            if (State == TDGameState.Running && _spawnFinished && Enemies.Count == 0)
            {
                if (CurrentWave >= TotalWaves)
                {
                    FinishGame(true);
                }
                else
                {
                    State = TDGameState.Ready;
                    _ui.RefreshAll();
                }
            }
        }

        #region 玩法操作（由 UI / 点击调用）

        /// <summary>选择要建造的塔类型（再次点击取消选择）</summary>
        public void SelectTower(int towerId)
        {
            SelectedTowerId = SelectedTowerId == towerId ? 0 : towerId;
            _ui.RefreshAll();
        }

        /// <summary>点击塔位（射线检测由 UpdateInput 转发）</summary>
        public void OnSlotClicked(TDBuildSlot slot)
        {
            if (State != TDGameState.Ready && State != TDGameState.Running)
            {
                return;
            }
            if (SelectedTowerId == 0)
            {
                _ui.ShowTip("先在下方选择要建造的塔！");
                return;
            }
            slot.TryBuild();
        }

        /// <summary>建造防御塔（金币不足返回 null）</summary>
        public TDTower BuildTower(Vector3 position)
        {
            Tower config = GameConfig.Tables.TbTower[SelectedTowerId];
            if (Gold < config.Cost)
            {
                _ui.ShowTip($"金币不足！{config.Name} 需要 {config.Cost} 金币");
                return null;
            }

            Gold -= config.Cost;
            var towerGo = new GameObject($"tower_{config.Name}");
            towerGo.transform.position = position;
            var tower = towerGo.AddComponent<TDTower>();
            tower.Init(config);
            _towers.Add(tower);
            _ui.RefreshAll();
            Debug.Log($"[TDGame] 建造 {config.Name}，剩余金币 {Gold}");
            return tower;
        }

        /// <summary>开始下一波</summary>
        public void StartNextWave()
        {
            if (State != TDGameState.Ready)
            {
                return;
            }
            _readyTimer = 0f;
            AutoWaveCountdown = 0f;
            CurrentWave++;
            StartCoroutine(RunWave(CurrentWave));
        }

        private IEnumerator RunWave(int waveId)
        {
            State = TDGameState.Running;
            _spawnFinished = false;
            _ui.RefreshAll();

            Wave wave = GameConfig.Tables.TbWave[waveId];
            foreach (WaveItem item in wave.Items)
            {
                for (int i = 0; i < item.Count; i++)
                {
                    SpawnEnemy(item.MonsterId);
                    yield return new WaitForSeconds(item.Interval);
                }
            }
            _spawnFinished = true;
        }

        private void SpawnEnemy(int monsterId)
        {
            var enemyGo = new GameObject($"enemy_{monsterId}");
            enemyGo.transform.SetParent(_root, false);
            enemyGo.transform.position = TDLevel.Waypoints[0];
            var enemy = enemyGo.AddComponent<TDEnemy>();
            enemy.Init(GameConfig.Tables.TbMonster[monsterId]);
            Enemies.Add(enemy);
        }

        #endregion

        #region 结算

        public void OnEnemyKilled(TDEnemy enemy)
        {
            if (State != TDGameState.Running)
            {
                return;
            }
            Gold += enemy.Config.Reward;
            _ui.RefreshAll();
        }

        public void OnEnemyReachEnd(TDEnemy enemy)
        {
            if (State != TDGameState.Running)
            {
                return;
            }
            Life--;
            _ui.ShowTip($"{enemy.Config.Name} 突破了防线！剩余生命 {Life}");
            _ui.RefreshAll();
            Destroy(enemy.gameObject);
            if (Life <= 0)
            {
                FinishGame(false);
            }
        }

        private void FinishGame(bool win)
        {
            State = win ? TDGameState.Win : TDGameState.Lose;
            _ui.ShowResult(win);
            Debug.Log(win ? "[TDGame] 胜利！防线守住了。" : "[TDGame] 失败…防线被突破了。");
        }

        public void Restart()
        {
            StartNewGame();
        }

        #endregion
    }
}
