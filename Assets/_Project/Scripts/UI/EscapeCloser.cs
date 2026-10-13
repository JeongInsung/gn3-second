using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GN3.UI
{
    /// <summary>창의 역할: 다른 창을 열 때 자동으로 닫히는지 정한다(EscapeCloser).</summary>
    public enum WindowRole
    {
        /// <summary>보통 창(시장·파티·퀘스트·길드·관계·건강·우편함·건물). 열리면 다른 Main·Linked 창을 닫는다.</summary>
        Main,
        /// <summary>다른 창에서 이어서 여는 창(캐릭터 정보·퀘스트 상세). 열려도 아무것도 닫지 않는다.</summary>
        Linked,
        /// <summary>알림·메뉴(중요 알림창·이어하기·진행 메뉴). 다른 창을 닫지도, 닫히지도 않는다.</summary>
        Popup,
    }

    /// <summary>
    /// 창 열고 닫기를 한 군데로 모은다.
    /// ESC: 창마다 따로 ESC를 받으면 겹친 창이 한 번에 다 닫히므로, 등록된 창 중 열려 있는 맨 위 창
    /// (루트 Canvas sortingOrder가 높은 쪽, 같으면 나중에 열린 쪽) 하나만 닫는다.
    /// 반드시 골라야 하는 창은 tryClose가 false를 돌려줘 ESC를 먹기만 한다(뒤 창이 대신 닫히지 않게).
    /// 자동 닫기: Main 창이 새로 열리면 열려 있던 다른 Main·Linked 창을 닫는다(창이 겹쳐 쌓이지 않게).
    /// Linked 창(또는 MarkNextOpenLinked로 이번만 연계로 연 창)은 부모 창 위에 뜨므로 아무것도 닫지 않는다.
    /// </summary>
    public class EscapeCloser : MonoBehaviour
    {
        private class Entry
        {
            public GameObject Panel;
            public System.Func<bool> TryClose;
            public WindowRole Role;
            public bool Seen;            // 등록 뒤 처음 본 프레임(이미 열려 있던 창을 "새로 열림"으로 보지 않게)
            public bool WasOpen;
            public int OpenedAt;
            public bool NextOpenLinked;  // 다음 한 번은 연계로 연 창(다른 창을 닫지 않음)
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
        public static void Register(GameObject panel, System.Action close, WindowRole role = WindowRole.Main)
        {
            Register(panel, () => { close(); return true; }, role);
        }

        /// <summary>ESC를 받을지 창이 정한다. false면 닫지 않고 ESC만 먹는다.</summary>
        public static void Register(GameObject panel, System.Func<bool> tryClose, WindowRole role = WindowRole.Main)
        {
            if (panel == null || tryClose == null) return;
            _entries.Add(new Entry { Panel = panel, TryClose = tryClose, Role = role });
        }

        /// <summary>panel을 다음에 열 때 한 번만 연계 창으로 본다(예: 캐릭터 정보창의 "관계" 버튼으로 연 관계 창).</summary>
        public static void MarkNextOpenLinked(GameObject panel)
        {
            foreach (var e in _entries)
                if (e.Panel == panel) e.NextOpenLinked = true;
        }

        private void Update()
        {
            _entries.RemoveAll(e => e.Panel == null);
            var openedMain = new List<Entry>();
            var openedNow = new HashSet<Entry>();
            foreach (var e in _entries)
            {
                bool open = e.Panel.activeInHierarchy;
                if (open && !e.WasOpen)
                {
                    e.OpenedAt = ++_openCounter;
                    if (e.Seen)
                    {
                        openedNow.Add(e);
                        if (e.Role == WindowRole.Main && !e.NextOpenLinked) openedMain.Add(e);
                    }
                    e.NextOpenLinked = false;
                }
                e.WasOpen = open;
                e.Seen = true;
            }

            if (openedMain.Count > 0) CloseOthers(openedNow);

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

        /// <summary>새 Main 창이 열렸다: 이번 프레임에 같이 열린 창을 빼고, 열려 있던 Main·Linked 창을 닫는다(Popup은 그대로).</summary>
        private static void CloseOthers(HashSet<Entry> openedNow)
        {
            foreach (var e in _entries.ToArray())
            {
                if (!e.WasOpen || openedNow.Contains(e) || e.Role == WindowRole.Popup) continue;
                if (e.TryClose()) e.WasOpen = e.Panel != null && e.Panel.activeInHierarchy;
            }
        }
    }
}
