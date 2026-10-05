using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을 용병(VillageWanderer) 머리 위를 따라다니는 이름표. 화면 UI(Overlay Canvas)로 그려서
    /// 캐릭터가 건물·나무 뒤로 가려져도 이름은 항상 보인다. 대상이 사라지면 같이 사라지고, 꺼지면(밤) 같이 숨는다.
    /// </summary>
    public class VillageNameTag : MonoBehaviour
    {
        // 발(루트)에서 머리 끝까지 약 0.58유닛(파츠 캔버스 머리 y 74px ~ 발 y 198px, 124px × 0.3 / 64) + 여백
        private const float HeadOffset = 0.66f;

        private Transform _target;
        private Text _text;

        public static VillageNameTag Create(Transform layer, Transform target, string name)
        {
            var go = new GameObject($"NameTag_{name}", typeof(RectTransform), typeof(Text), typeof(Outline), typeof(VillageNameTag));
            go.transform.SetParent(layer, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200f, 24f);
            rect.pivot = new Vector2(0.5f, 0f); // 아래 가운데를 머리 위 점에 맞춘다

            var text = go.GetComponent<Text>();
            text.text = name;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.LowerCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;

            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var tag = go.GetComponent<VillageNameTag>();
            tag._target = target;
            tag._text = text;
            tag.LateUpdate();
            return tag;
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                Destroy(gameObject);
                return;
            }

            var cam = Camera.main;
            bool visible = cam != null && _target.gameObject.activeInHierarchy;
            _text.enabled = visible;
            if (!visible) return;

            transform.position = cam.WorldToScreenPoint(_target.position + Vector3.up * HeadOffset);
        }
    }
}
