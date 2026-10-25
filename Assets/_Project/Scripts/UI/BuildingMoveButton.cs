using System.Linq;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을 건물을 눌러 연 창(파티·퀘스트·길드·건물 패널)의 오른쪽 위 [옮기기]. 누르면 창을 닫고 그 건물을 든 채 옮기기 모드가 된다
    /// (BuildingMover). 패널이 닫히면 버튼도 숨어서, 같은 창을 메뉴로 열면 보이지 않는다 — 건물을 눌러 연 때만 MainMenuBootstrapper가 켠다.
    /// [건설] 창 머리의 [옮기기](모드만 켬)도 같은 모양(CreateButton)을 쓴다.
    /// </summary>
    public class BuildingMoveButton : MonoBehaviour
    {
        private const float Width = 90f;
        private const float Height = 34f;

        private GameObject _button;
        private SelectableBuilding _building;
        private System.Action _close;

        private static readonly Vector2 TopRight = new Vector2(1f, 1f);

        /// <summary>parent의 corner(기본 오른쪽 위)에서 offset만큼 떨어진 [옮기기] 버튼(label로 같은 모양의 다른 버튼도 — [건설] 창 [벌목]).</summary>
        public static Button CreateButton(Transform parent, Vector2 offset, UnityEngine.Events.UnityAction onClick, Vector2? corner = null, string label = "옮기기")
        {
            var button = ShopListUI.CreateButton(parent, label, onClick, Width);
            Object.Destroy(button.GetComponent<LayoutElement>()); // 레이아웃 줄이 아니라 자리를 직접 잡는다
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = corner ?? TopRight;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(Width, Height);
            return button;
        }

        /// <summary>건물을 눌러 panelRoot 창을 열었다 → 그 창에 [옮기기]를 보인다(처음이면 만든다). close: 그 창을 닫는 일.</summary>
        public static void ShowOn(GameObject panelRoot, Transform buttonParent, Vector2 offset, System.Action close, SelectableBuilding building, Vector2? corner = null)
        {
            if (panelRoot == null || building == null) return;
            var mover = panelRoot.GetComponent<BuildingMoveButton>();
            if (mover == null)
            {
                mover = panelRoot.AddComponent<BuildingMoveButton>();
                mover._button = CreateButton(buttonParent != null ? buttonParent : panelRoot.transform, offset, mover.Move, corner).gameObject;
                UIThemeApplier.ApplyNow(mover._button.transform);
            }
            mover._close = close;
            mover._building = building;
            mover._button.SetActive(true);
            mover._button.transform.SetAsLastSibling();
        }

        private void Move()
        {
            // 대장간처럼 누르는 그림(연기 애니메이션)이 건물 자식일 수도 있어 계층으로 찾는다
            var target = _building != null ? _building.transform : null;
            var slot = target == null ? null : VillageLayoutApplier.Slots.FirstOrDefault(s => s.Built && target.IsChildOf(s.Transform));
            _close?.Invoke();
            BuildingMover.Enter(slot);
        }

        // 창이 닫히면(메뉴로 다시 열 때 안 보이게) 숨긴다
        private void OnDisable()
        {
            if (_button != null) _button.SetActive(false);
            _building = null;
        }
    }
}
