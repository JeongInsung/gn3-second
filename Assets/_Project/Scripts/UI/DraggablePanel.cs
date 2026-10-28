using UnityEngine;
using UnityEngine.EventSystems;

namespace GN3.UI
{
    /// <summary>
    /// 창(패널)의 빈 곳·제목을 잡고 끌어 옮긴다. 누르면 같은 Canvas 안에서 맨 앞으로 오고, 화면 밖으로는 나가지 않는다.
    /// 목록(ScrollRect) 위에서 끌면 목록이 먼저 드래그를 받으므로 스크롤은 그대로다.
    /// 옮긴 자리는 창마다(루트부터의 이름 경로) PlayerPrefs에 남아, 닫았다 열어도·게임을 다시 켜도 그 자리에 뜬다
    /// (화면 배치라 세이브가 아니라 이 컴퓨터 설정). persist: false인 창은 예전처럼 닫히면(OnDisable) 기본 위치로 돌아간다.
    /// 설정 창 [창 위치 초기화]가 ResetAll로 모두 지운다.
    /// </summary>
    public class DraggablePanel : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float MinVisible = 80f; // 창이 화면보다 클 때 최소한 보이게 둘 폭
        private const string KeyPrefix = "win.";
        private const string KeyList = "win.keys"; // 쓴 키 목록('|') — 초기화 때 지운다

        private RectTransform _rect;
        private Canvas _canvas;
        private Vector2 _home;  // 처음 옮기기 직전 위치 = 코드·씬이 정해 둔 기본 위치
        private bool _moved;
        private bool _keepOnClose; // 꺼져도 옮긴 자리 그대로(켜고 끄는 창·저장되는 창)
        private string _key;       // 자리를 저장할 PlayerPrefs 키(null이면 저장 안 함)

        /// <param name="movedTo">창을 열어 둔 채 다시 만들 때: 옛 창의 옮긴 자리(MovedPosition). 저장된 자리보다 우선한다.</param>
        /// <param name="keepOnClose">꺼질 때 기본 위치로 되돌리지 않는다(저장되는 창은 늘 그렇다).</param>
        /// <param name="persist">옮긴 자리를 창마다 저장해 다음에 열 때·다음 실행에도 그 자리에 띄운다.</param>
        public static void Attach(RectTransform panel, Vector2? movedTo = null, bool keepOnClose = false, bool persist = true)
        {
            if (panel == null) return;
            var drag = panel.GetComponent<DraggablePanel>() ?? panel.gameObject.AddComponent<DraggablePanel>();
            drag._rect = panel;
            drag._key = persist ? KeyPrefix + PathOf(panel) : null;
            drag._keepOnClose = keepOnClose || persist;

            Vector2? target = movedTo;
            if (!target.HasValue && drag._key != null && PlayerPrefs.HasKey(drag._key + ".x"))
                target = new Vector2(PlayerPrefs.GetFloat(drag._key + ".x"), PlayerPrefs.GetFloat(drag._key + ".y"));
            if (target.HasValue)
            {
                if (!drag._moved) drag._home = panel.anchoredPosition;
                drag._moved = true;
                panel.anchoredPosition = target.Value;
                drag._canvas = panel.GetComponentInParent<Canvas>()?.rootCanvas;
                drag.ClampToCanvas();
            }
        }

        /// <summary>끌어 옮긴 창이면 지금 자리, 아니면 null(다시 만들기 직전에 읽어 Attach의 movedTo로 넘긴다).</summary>
        public static Vector2? MovedPosition(RectTransform panel)
        {
            if (panel == null) return null;
            var drag = panel.GetComponent<DraggablePanel>();
            return drag != null && drag._moved ? panel.anchoredPosition : (Vector2?)null;
        }

        /// <summary>저장된 창 위치를 모두 지운다(이미 열린 창은 다음에 열 때부터 기본 자리).</summary>
        public static void ResetAll()
        {
            foreach (var key in PlayerPrefs.GetString(KeyList, "").Split('|'))
            {
                if (key.Length == 0) continue;
                PlayerPrefs.DeleteKey(key + ".x");
                PlayerPrefs.DeleteKey(key + ".y");
            }
            PlayerPrefs.DeleteKey(KeyList);
            PlayerPrefs.Save();
        }

        /// <summary>루트부터 이 창까지 이름을 '/'로 이은 것(창마다 다른 저장 키).</summary>
        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private void Awake() => _rect = (RectTransform)transform;

        public void OnPointerDown(PointerEventData eventData) => _rect.SetAsLastSibling();

        public void OnBeginDrag(PointerEventData eventData)
        {
            _canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            if (!_moved)
            {
                _home = _rect.anchoredPosition;
                _moved = true;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_key == null) return;
            PlayerPrefs.SetFloat(_key + ".x", _rect.anchoredPosition.x);
            PlayerPrefs.SetFloat(_key + ".y", _rect.anchoredPosition.y);
            string keys = PlayerPrefs.GetString(KeyList, "");
            if (("|" + keys + "|").IndexOf("|" + _key + "|", System.StringComparison.Ordinal) < 0)
                PlayerPrefs.SetString(KeyList, keys.Length == 0 ? _key : keys + "|" + _key);
            PlayerPrefs.Save();
        }

        // 저장하지 않는 창: 닫는 방법(닫기 버튼·ESC·부모 끄기)과 상관없이 꺼질 때 원래 자리로
        private void OnDisable()
        {
            if (!_moved || _keepOnClose) return;
            _rect.anchoredPosition = _home;
            _moved = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _rect.anchoredPosition += eventData.delta / scale;
            ClampToCanvas();
        }

        /// <summary>창 네 모서리를 Canvas 좌표로 바꿔, 밖으로 나간 만큼 되돌린다.</summary>
        private void ClampToCanvas()
        {
            if (_canvas == null) return;
            var canvasRect = (RectTransform)_canvas.transform;
            var corners = new Vector3[4];
            _rect.GetWorldCorners(corners);
            Vector2 min = canvasRect.InverseTransformPoint(corners[0]);
            Vector2 max = canvasRect.InverseTransformPoint(corners[2]);
            Rect bounds = canvasRect.rect;
            if (bounds.width <= 1f || bounds.height <= 1f) return; // 막 만든 캔버스는 아직 크기가 없을 수 있다

            Vector2 shift = Vector2.zero;
            shift.x = Push(min.x, max.x, bounds.xMin, bounds.xMax);
            shift.y = Push(min.y, max.y, bounds.yMin, bounds.yMax);
            _rect.anchoredPosition += shift;
        }

        private static float Push(float min, float max, float lo, float hi)
        {
            if (max - min <= hi - lo)
            {
                if (min < lo) return lo - min;
                if (max > hi) return hi - max;
                return 0f;
            }
            // 창이 화면보다 크면 MinVisible만큼은 화면 안에 남긴다.
            if (max < lo + MinVisible) return lo + MinVisible - max;
            if (min > hi - MinVisible) return hi - MinVisible - min;
            return 0f;
        }
    }
}
