using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GN3.UI
{
    /// <summary>
    /// ESC로 창을 닫는 곳을 한 군데로 모은다. 창마다 따로 ESC를 받으면 겹친 창이 한 번에 다 닫히므로,
    /// 등록된 창 중 열려 있는 맨 위 창(루트 Canvas sortingOrder가 높은 쪽, 같으면 나중에 열린 쪽) 하나만 닫는다.
    /// 반드시 골라야 하는 창은 tryClose가 false를 돌려줘 ESC를 먹기만 한다(뒤 창이 대신 닫히지 않게).
    /// </summary>
    public class EscapeCloser : MonoBehaviour
    {
        private class Entry
        {
            public GameObject Panel;
            public System.Func<bool> TryClose;
            public bool WasOpen;
            public int OpenedAt;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static int _openCounter;

        // Domain Reload가 꺼져 있어도 Play 진입마다 등록을 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _entries.Clear();
            _openCounter = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRunner()
        {
            var go = new GameObject(nameof(EscapeCloser), typeof(EscapeCloser));
            DontDestroyOnLoad(go);
        }

        /// <summary>ESC로 닫을 창을 등록한다. close는 창의 닫기 버튼과 같은 동작이면 된다.</summary>
        public static void Register(GameObject panel, System.Action close)
        {
            Register(panel, () => { close(); return true; });
        }

        /// <summary>ESC를 받을지 창이 정한다. false면 닫지 않고 ESC만 먹는다.</summary>
        public static void Register(GameObject panel, System.Func<bool> tryClose)
        {
            if (panel == null || tryClose == null) return;
            _entries.Add(new Entry { Panel = panel, TryClose = tryClose });
        }

        private void Update()
        {
            _entries.RemoveAll(e => e.Panel == null);
            foreach (var e in _entries)
            {
                bool open = e.Panel.activeInHierarchy;
                if (open && !e.WasOpen) e.OpenedAt = ++_openCounter;
                e.WasOpen = open;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

            Entry top = null;
            int topOrder = int.MinValue;
            foreach (var e in _entries)
            {
                if (!e.WasOpen) continue;
                var canvas = e.Panel.GetComponentInParent<Canvas>();
                int order = canvas != null ? canvas.rootCanvas.sortingOrder : 0;
                if (top == null || order > topOrder || (order == topOrder && e.OpenedAt > top.OpenedAt))
                {
                    top = e;
                    topOrder = order;
                }
            }
            top?.TryClose();
        }
    }
}
