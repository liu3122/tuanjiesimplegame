#if UNITY_WEBGL
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WeChatWASM;

namespace Game.HotUpdate
{
    /// <summary>
    /// 微信小游戏键盘桥：WASM 环境无键盘事件，且 SDK 已把 WebGLInput.mobileKeyboardSupport
    /// 关闭（DisableKeyboardInput.cs），InputField 必须手动接 wx.showKeyboard。
    /// 用法：WXKeyboard.Bind(inputField)——选中弹微信键盘，输入回填 text，确认/失焦收起。
    /// 仅 WebGL（微信小游戏）编译；编辑器/单机平台走 Unity 原生输入。
    /// </summary>
    public static class WXKeyboard
    {
        private static InputField _current;
        private static bool _hooked;

        public static void Bind(InputField field)
        {
            if (field == null) return;
            var trigger = field.gameObject.GetComponent<EventTrigger>();
            if (trigger == null) trigger = field.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.Select, () => Show(field));
            AddTrigger(trigger, EventTriggerType.Deselect, Hide);
            // 提交/点选其他 UI 导致结束时收起微信键盘（isFocused 仍真说明只是回车换行）
            field.onEndEdit.AddListener(_ => { if (!field.isFocused) Hide(); });
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        private static void Show(InputField field)
        {
            _current = field;
            Hook();
            WX.ShowKeyboard(new ShowKeyboardOption
            {
                defaultValue = field.text,
                maxLength = field.characterLimit > 0 ? field.characterLimit : 20,
                multiple = false,
                confirmType = "done",
                confirmHold = false,
            });
        }

        private static void Hide()
        {
            if (_current == null) return;
            _current = null;
            WX.HideKeyboard(new HideKeyboardOption());
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            WX.OnKeyboardInput(OnInput);
            WX.OnKeyboardConfirm(OnConfirm);
            WX.OnKeyboardComplete(OnComplete);
        }

        private static void OnInput(OnKeyboardInputListenerResult result)
        {
            if (_current != null && result.value != null)
                _current.text = result.value;
        }

        private static void OnConfirm(OnKeyboardInputListenerResult result)
        {
            if (_current == null) return;
            if (result.value != null) _current.text = result.value;
            var field = _current;
            _current = null; // 先清再收起，避免 Deselect→Hide 重复调用
            WX.HideKeyboard(new HideKeyboardOption());
            field.DeactivateInputField();
        }

        private static void OnComplete(OnKeyboardInputListenerResult result)
        {
            if (_current != null && result.value != null)
                _current.text = result.value;
            _current = null;
        }
    }
}
#endif
