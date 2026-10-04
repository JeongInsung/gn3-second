using GN3.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 클릭한 건물(SelectableBuilding)의 이름·설명을 보여주는 가운데 패널. 지금은 상품 목록 칸만 있는 틀이다.
    /// 닫기 버튼, Esc, 바깥 어두운 배경 클릭으로 닫힌다. 패널이 열려 있는 동안 마우스가 UI 위에 있으므로 건물은 눌리지 않는다.
    /// 화면 구성은 DesignSceneBootstrapper가 코드로 만든다.
    /// </summary>
    public class BuildingPanel : MonoBehaviour
    {
        public Text Title;
        public Text Description;
        public RectTransform ItemListContent;
        public Image Portrait;

        public SelectableBuilding Current { get; private set; }

        public void Open(SelectableBuilding building)
        {
            Current = building;
            Title.text = building.DisplayName;
            Description.text = building.Description;
            if (Portrait != null)
            {
                Portrait.sprite = building.Portrait;
                Portrait.gameObject.SetActive(building.Portrait != null);
            }
            building.SetHover(false);
            gameObject.SetActive(true);
        }

        public void Close()
        {
            Current = null;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
        }
    }
}
