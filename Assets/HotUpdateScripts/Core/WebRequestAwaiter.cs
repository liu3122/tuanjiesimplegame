#if UNITY_WEBGL
using System;
using System.Runtime.CompilerServices;
using UnityEngine.Networking;

namespace Game.HotUpdate
{
    /// <summary>
    /// 热更侧自研 awaiter：AOT 裁剪后 mscorlib 缺 YieldAwaitable / TaskCompletionSource /
    /// DelayPromise——热更 DLL 一引用它们就在 Assembly.Load 阶段 TYPEREF 崩
    /// （ExecutionEngineException: ReadTypeFromResolutionScope，2026-11-08 ffc27a4 实测，
    /// 热更程序集整个加载失败、模拟器卡死）。Task 生态里没有可用的"等一帧"原语，
    /// 故用热更程序集内自定义 awaiter 挂 UnityWebRequestAsyncOperation.completed 事件。
    /// 仅依赖 AOT 中已证存活的类型：INotifyCompletion / Action / UnityWebRequestAsyncOperation。
    /// </summary>
    public struct WebRequestAwaiter : INotifyCompletion
    {
        private readonly UnityWebRequestAsyncOperation _op;

        public WebRequestAwaiter(UnityWebRequestAsyncOperation op) { _op = op; }

        public bool IsCompleted => _op == null || _op.isDone;
        public void GetResult() { }

        public void OnCompleted(Action continuation)
        {
            if (continuation == null) return;
            if (_op == null || _op.isDone) { continuation(); return; }
            _op.completed += _ => continuation();
        }
    }
}
#endif
