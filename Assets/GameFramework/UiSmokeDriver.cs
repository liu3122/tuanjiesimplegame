using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// UI 链路冒烟（batchmode 用，正常游戏不受影响）：
/// 等待 YooAsset 初始化 → 检查 UIManager 是否成功加载 LoginScreen prefab。
/// 用法：Unity.exe -batchmode -projectPath . -executeMethod ProjectSetup.SetupAndSmokeTest -uismoke
/// </summary>
public class UiSmokeDriver : MonoBehaviour
{
    public static bool IsRequested()
    {
        foreach (var arg in System.Environment.GetCommandLineArgs())
        {
            if (arg == "-uismoke") return true;
        }
        return false;
    }

    private IEnumerator Start()
    {
        Debug.Log("[UISMOKE] 等待 YooAsset 初始化与登录屏加载…");
        float wait = 0;
        object name = null;
        var uiType = System.Type.GetType("Game.HotUpdate.ChestGame.UIManager, Game.HotUpdate");
        while (wait < 25f)
        {
            yield return new WaitForSeconds(1f);
            wait += 1f;
            if (uiType != null)
            {
                name = uiType.GetProperty("CurrentName", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (name != null) break;
            }
        }

        var current = uiType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        bool pass = current != null && (string)name == "LoginScreen";
        Debug.Log($"[UISMOKE] UIManager.Current={name ?? "null"} -> {(pass ? "PASS ✅ 界面 prefab 经 YooAsset 加载成功" : "FAIL ❌")}");
        yield return null;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(pass ? 0 : 3);
#endif
    }
}
