#if UNITY_WEBGL || WEIXINMINIGAME
using System;
using AOT;
using HybridCLR;
using UnityEngine;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// HybridCLR ReversePInvokeWrapper 预留桥（JS→C# 反向回调专用）。
    ///
    /// 背景：小游戏底包（wasm）不含热更代码，原生侧反向回调包装在导出底包时固化进
    /// HybridCLR 生成的 methodbridge.cpp；热更程序集里的 [MonoPInvokeCallback] 方法只有
    /// 在底包里预留过同签名槽位（[ReversePInvokeWrapperGeneration(N)] 的 N = 槽位数，同签名
    /// 所有回调共用），运行时才能拿到函数指针。某次热更若新增无槽位签名的回调 → 取指针失败，
    /// 且热更侧无法自救，必须重导 wasm 底包（= 发版过审），"分钟级热更迭代"失效。
    ///
    /// 铁律：
    /// 1. 热更新增 JS→C# 回调只允许复用这里的签名；新增签名/扩槽位 = 重导底包（改本文件）。
    /// 2. 预留入口的【签名绝不可改】（改了 = 与已发底包槽位错配），只允许改方法体。
    /// 3. 签名只支持基元/IntPtr/结构体——HybridCLR 分析器禁 string/数组/引用类型
    ///    （MetaUtil.IsSupportedPInvokeTypeSig）；字符串一律 IntPtr 传指针、接收侧自行 Marshal
    ///    （Marshal.PtrToStringUTF8 属 BCL 冷门方法，AOT 可能被裁，用前先确认 link.xml，
    ///    参见构建指南"热更代码引用被裁剪的 BCL 类型"坑位）。
    ///
    /// 未来接入方式（届时实现，本文件只负责占位与分发）：热更侧
    /// Marshal.GetFunctionPointerForDelegate(new JsEventCallback(JsCallbackBridge.OnJsEvent))
    /// 取 wasm 函数表下标传给 JS（自定义 jslib 或 WX.CallJSFunction），JS 侧经 wasm 表回调进来。
    /// 生效条件：下次导出底包时 HybridCLR/Generate/All 扫描热更 DLL，Console 出现
    /// "[ReversePInvokeWrap.Generator] ... wrapperCount:N" 日志即槽位已进 methodbridge.cpp。
    /// 注意：切换 Development 选项后必须重新 Generate/All 再出包（HybridCLR 严重崩溃项）。
    /// </summary>
    public static class JsCallbackBridge
    {
        // ---- 预留签名（delegate 供 [MonoPInvokeCallback] 与取函数指针共用；勿改成泛型 Action/Func）----

        /// <summary>通用事件：eventId + UTF8 JSON/字符串指针</summary>
        public delegate void JsEventCallback(int eventId, IntPtr payloadPtr);

        /// <summary>同步请求：eventId + 指针，返回 int</summary>
        public delegate int JsRequestCallback(int eventId, IntPtr payloadPtr);

        /// <summary>纯 int 事件：eventId + value</summary>
        public delegate void JsIntEventCallback(int eventId, int value);

        /// <summary>数值事件：eventId + float</summary>
        public delegate void JsFloatEventCallback(int eventId, float value);

        /// <summary>无参通知</summary>
        public delegate void JsNotifyCallback();

        /// <summary>JS 通用事件（payload 为 UTF8 指针，订阅方自行 Marshal）。最常用签名，槽位最多</summary>
        public static event Action<int, IntPtr> JsEvent;

        /// <summary>JS 同步请求（返回 -1=无订阅者，0=成功，>0=业务错误码，约定见接入实现）</summary>
        public static event Func<int, IntPtr, int> JsRequest;

        /// <summary>JS 纯 int 事件（进度/状态码/简单选择）</summary>
        public static event Action<int, int> JsIntEvent;

        /// <summary>JS 数值事件（音量/传感器/滑动增量）</summary>
        public static event Action<int, float> JsFloatEvent;

        /// <summary>JS 无参通知（生命周期/一次性触发）</summary>
        public static event Action JsNotify;

        // ---- 预留入口：签名与槽位随底包固化，只能改方法体 ----

        [MonoPInvokeCallback(typeof(JsEventCallback))]
        [ReversePInvokeWrapperGeneration(8)]
        internal static void OnJsEvent(int eventId, IntPtr payloadPtr)
        {
            var h = JsEvent;
            if (h != null) h(eventId, payloadPtr);
            else Debug.Log($"[JsCallbackBridge] JS 事件 {eventId} 无订阅者（payloadPtr={payloadPtr}）");
        }

        [MonoPInvokeCallback(typeof(JsRequestCallback))]
        [ReversePInvokeWrapperGeneration(4)]
        internal static int OnJsRequest(int eventId, IntPtr payloadPtr)
        {
            var h = JsRequest;
            if (h != null) return h(eventId, payloadPtr);
            Debug.Log($"[JsCallbackBridge] JS 请求 {eventId} 无订阅者，返回 -1");
            return -1;
        }

        [MonoPInvokeCallback(typeof(JsIntEventCallback))]
        [ReversePInvokeWrapperGeneration(4)]
        internal static void OnJsIntEvent(int eventId, int value)
        {
            var h = JsIntEvent;
            if (h != null) h(eventId, value);
            else Debug.Log($"[JsCallbackBridge] JS int 事件 {eventId}={value} 无订阅者");
        }

        [MonoPInvokeCallback(typeof(JsFloatEventCallback))]
        [ReversePInvokeWrapperGeneration(2)]
        internal static void OnJsFloatEvent(int eventId, float value)
        {
            var h = JsFloatEvent;
            if (h != null) h(eventId, value);
        }

        [MonoPInvokeCallback(typeof(JsNotifyCallback))]
        [ReversePInvokeWrapperGeneration(2)]
        internal static void OnJsNotify()
        {
            var h = JsNotify;
            if (h != null) h();
        }
    }
}
#endif
