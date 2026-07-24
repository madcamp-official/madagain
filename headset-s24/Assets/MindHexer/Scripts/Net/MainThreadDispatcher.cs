using System;
using System.Collections.Generic;
using UnityEngine;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// 소켓 수신 스레드/WebSocketSharp 콜백 스레드 → Unity 메인 스레드로 작업을 넘기는 큐. (SPEC 2.2)
    /// 씬에 하나만 두고, 백그라운드 스레드에서 <see cref="Enqueue"/>로 액션을 쌓으면
    /// 다음 Update에서 메인 스레드가 실행한다. Unity API는 반드시 이 경유로 호출할 것.
    /// </summary>
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private readonly Queue<Action> _queue = new Queue<Action>();

        public static MainThreadDispatcher Instance => _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        /// <summary>백그라운드 스레드에서 호출 가능. 스레드 안전.</summary>
        public void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queue) { _queue.Enqueue(action); }
        }

        private void Update()
        {
            // 프레임당 쌓인 작업 모두 소진.
            while (true)
            {
                Action next;
                lock (_queue)
                {
                    if (_queue.Count == 0) break;
                    next = _queue.Dequeue();
                }
                try { next(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
