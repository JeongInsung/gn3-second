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
        [Tooltip("테두리 밝기가 숨쉬듯 변하는 속도")]
        [SerializeField] private float pulseSpeed = 3f;

        public UnityEvent<SelectableBuilding> onClicked = new UnityEvent<SelectableBuilding>();

        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Portrait => portrait;
        public bool IsHovered { get; private set; }

        private Collider2D _collider;

        public void SetOutline(SpriteRenderer renderer) => outline = renderer;

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
            SetHover(!overUI && ContainsPoint(world));

            if (IsHovered && mouse.leftButton.wasPressedThisFrame)
                Click();

            if (IsHovered && outline != null)
            {
                var c = outline.color;
                c.a = Mathf.Lerp(0.75f, 1f, (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f);
                outline.color = c;
            }
        }

        /// <summary>월드 좌표가 건물 실루엣 안인지.</summary>
        public bool ContainsPoint(Vector2 world) => _collider != null && _collider.OverlapPoint(world);

        public void Click() => onClicked.Invoke(this);

        public void SetHover(bool hovered)
        {
            IsHovered = hovered;
            if (outline != null) outline.enabled = hovered;
        }
    }
}
