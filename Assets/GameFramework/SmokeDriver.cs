using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 塔防自动化冒烟测试（batchmode 用，正常游戏不受影响）：
/// 自动建塔 → 出兵 → 监测敌人血量与金币 → 判定攻击是否发生 → 退出并返回码。
/// 用法：Unity.exe -batchmode -projectPath . -executeMethod 无需 -quit（由本类 Exit）
/// </summary>
public class SmokeDriver : MonoBehaviour
{
    private const string TowerDefenseType = "Game.HotUpdate.TowerDefense.TDGame, Game.HotUpdate";
    private const string SlotType = "Game.HotUpdate.TowerDefense.TDBuildSlot, Game.HotUpdate";

    public static bool IsSmokeTestRequested()
    {
        foreach (string arg in System.Environment.GetCommandLineArgs())
        {
            if (arg == "-smoketest")
            {
                return true;
            }
        }
        return false;
    }

    private IEnumerator Start()
    {
        Debug.Log("[SMOKE] 冒烟测试启动，等待游戏初始化…");
        yield return new WaitForSeconds(3f);

        // 1. 找到 TDGame
        var tdType = System.Type.GetType(TowerDefenseType);
        object game = tdType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (game == null)
        {
            Fail("TDGame.Instance 为空，游戏未初始化");
            yield break;
        }
        int gold0 = (int)tdType.GetProperty("Gold").GetValue(game);
        Debug.Log($"[SMOKE] 游戏初始化 OK，初始金币 {gold0}");

        // 2. 自动建塔（金币不够的点位自动跳过）
        tdType.GetMethod("SelectTower").Invoke(game, new object[] { 2001 });
        var slotType = System.Type.GetType(SlotType);
        var tryBuild = slotType.GetMethod("TryBuild");
        int built = 0;
        foreach (var slot in FindObjectsOfType(slotType))
        {
            if ((bool)tryBuild.Invoke(slot, null))
            {
                built++;
            }
        }
        Debug.Log($"[SMOKE] 自动建造箭塔 {built} 座");

        // 3. 出兵
        tdType.GetMethod("StartNextWave").Invoke(game, null);
        Debug.Log("[SMOKE] 已出兵，开始监测战斗…");

        // 4. 监测 20 秒：敌人总血量 + 金币
        var enemiesField = tdType.GetField("Enemies");
        var hpProp = System.Type.GetType("Game.HotUpdate.TowerDefense.TDEnemy, Game.HotUpdate").GetProperty("Hp");
        long firstTotalHp = -1;
        long prevHp = long.MaxValue;
        int prevCount = -1;
        bool damaged = false;
        bool killed = false;

        for (int i = 0; i < 40; i++)
        {
            yield return new WaitForSeconds(0.5f);
            var enemies = enemiesField.GetValue(game) as System.Collections.IEnumerable;
            long totalHp = 0;
            int count = 0;
            foreach (var e in enemies)
            {
                totalHp += (long)hpProp.GetValue(e);
                count++;
            }
            int gold = (int)tdType.GetProperty("Gold").GetValue(game);

            if (firstTotalHp < 0 && count > 0)
            {
                firstTotalHp = totalHp;
            }
            // 判定伤害：与上一次采样相比，敌人数量不增而总血量下降
            if (prevCount == count && prevHp > totalHp)
            {
                damaged = true;
            }
            prevCount = count;
            prevHp = totalHp;
            // 判定击杀：金币回到初始值以上（击杀奖励）
            if (gold >= gold0)
            {
                killed = true;
            }
            if (i % 4 == 0)
            {
                Debug.Log($"[SMOKE] t={(i + 1) * 0.5f:F1}s 敌人={count} 总血量={totalHp} 金币={gold}");
            }
        }

        int finalGold = (int)tdType.GetProperty("Gold").GetValue(game);
        bool pass = damaged || killed;
        Debug.Log($"[SMOKE] 结果：受到伤害={damaged} 击杀获得金币={killed}（{gold0}→{finalGold}）→ {(pass ? "PASS ✅ 塔在攻击" : "FAIL ❌ 塔没有攻击")}");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(pass ? 0 : 3);
#endif
        yield break;
    }

    private void Update()
    {
        // 硬超时保护：3 分钟无论如何退出
        if (Time.time > 180f)
        {
#if UNITY_EDITOR
            Debug.LogError("[SMOKE] 硬超时");
            UnityEditor.EditorApplication.Exit(2);
#endif
        }
    }

    private static void Fail(string msg)
    {
        Debug.LogError($"[SMOKE] FAIL：{msg}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(3);
#endif
    }
}
