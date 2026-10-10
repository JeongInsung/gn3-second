using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GN3.World
{
    /// <summary>
    /// 마우스를 올리면 건물 둘레에 흰 테두리가 숨쉬듯 빛나고, 왼쪽 클릭하면 onClicked를 부르는 건물.
    /// 판정은 건물 그림 실루엣으로 만든 PolygonCollider2D로 하고, UI 위에 마우스가 있으면 무시한다(패널이 열려 있으면 건물이 안 눌린다).
    /// 실루엣이 겹치면 앞(아래끝이 낮은) 건물만 hover·클릭된다(Pick).
    /// 테두리 그림(자식 "Outline")은 에디터 메뉴 "GN3/Interaction/선택한 건물을 클릭 가능하게"가 만든다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SelectableBuilding : MonoBehaviour
    {
        public static readonly List<SelectableBuilding> All = new List<SelectableBuilding>();

        [SerializeField] private string displayName = "의약품 상점";
        [TextArea] [SerializeField] private string description = "필요한 의약품을 구매할 수 있습니다.";
        [Tooltip("패널 옆에 서 있을 상인 그림(없으면 패널만 뜬다)")]
        [SerializeField] private Sprite portrait;
        [SerializeField] private SpriteRenderer outline;
        [Tooltip("미리 구운 테두리 대신 실루엣 사본 여러 장으로 만든 테두리 묶음(실행 중 VillageInn이 만든다)")]
        [SerializeField] private GameObject outlineGroup;
        [Tooltip("테두리 밝기가 숨쉬듯 변하는 속도")]
        [SerializeField] private float pulseSpeed = 3f;

        public UnityEvent<SelectableBuilding> onClicked = new UnityEvent<SelectableBuilding>();

        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Portrait => portrait;
        public bool IsHovered { get; private set; }

        private Collider2D _collider;

        public void SetOutline(SpriteRenderer renderer) => outline = renderer;

        /// <summary>실행 중에 붙인 건물(VillageProps.EnsureClickable)의 이름·설명을 정한다(기본값은 "의약품 상점").</summary>
        public void SetInfo(string newDisplayName, string newDescription)
        {
            displayName = newDisplayName;
            description = newDescription;
        }

        public void SetOutlineGroup(GameObject group)
        {
            outlineGroup = group;
            SetHover(IsHovered);
        }

        // GameClock과 같이 Domain Reload가 꺼져 있어도 Play마다 목록을 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => All.Clear();

        private void OnEnable()
        {
            _collider = GetComponent<Collider2D>();
            All.Add(this);
            SetHover(false);
        }

        private void OnDisable()
        {
            All.Remove(this);
            SetHover(false);
        }

        private void Update()
        {
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null) return;

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Vector3 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            // 건물 앞에 선 마을 캐릭터를 가리키고 있으면 캐릭터가 우선(건물 테두리·클릭은 무시).
            SetHover(!overUI && Pick(world) == this && VillageWanderer.Pick(world) == null);

            if (IsHovered && mouse.leftButton.wasPressedThisFrame)
                Click();

            if (IsHovered)
            {
                float alpha = Mathf.Lerp(0.75f, 1f, (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f);
                if (outline != null)
                {
                    var c = outline.color;
                    c.a = alpha;
                    outline.color = c;
                }
                if (outlineGroup != null)
                    foreach (var copy in outlineGroup.GetComponentsInChildren<SpriteRenderer>())
                        copy.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        /// <summary>월드 좌표가 건물 실루엣 안인지.</summary>
        public bool ContainsPoint(Vector2 world) => _collider != null && _collider.OverlapPoint(world);

        /// <summary>그 점을 덮는 건물 중 맨 앞(Y축 정렬이라 실루엣 아래끝이 가장 낮은) 건물. 겹친 곳에서 두 건물이 함께 눌리지 않게 한다.</summary>
        public static SelectableBuilding Pick(Vector2 world)
        {
            SelectableBuilding best = null;
            foreach (var building in All)
                if (building.ContainsPoint(world) && (best == null || building._collider.bounds.min.y < best._collider.bounds.min.y))
                    best = building;
            return best;
        }

        public void Click() => onClicked.Invoke(this);

        public void SetHover(bool hovered)
        {
            IsHovered = hovered;
            if (outline != null) outline.enabled = hovered;
            if (outlineGroup != null) outlineGroup.SetActive(hovered);
        }
    }
}
