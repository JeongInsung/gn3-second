using GN3.CharacterAnim;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public enum PortraitCrop
    {
        /// <summary>머리~가슴. 시장 카드/파티 로우 같은 작은 슬롯용 프로필.</summary>
        Bust,
        /// <summary>여백을 포함한 전신.</summary>
        FullBody,
    }

    /// <summary>
    /// 파츠 조합 캐릭터(ComposedCharacter)를 uGUI 초상화 슬롯으로 그리는 공용 위젯.
    /// 시장 카드와 파티 로우가 같은 코드를 쓰므로 프리팹 역할을 한다.
    /// 파츠 시트가 256x256 캔버스에서 캐릭터를 작게 담고 있어(폭 ~54px, 높이 ~124px)
    /// 슬롯을 RectMask2D로 마스킹하고 레이어를 확대·이동시켜 원하는 영역만 크롭해 보여준다.
    /// </summary>
    public static class CharacterPortraitUI
    {
        private const float CanvasSize = 256f;

        // 크롭 영역(캔버스 픽셀 기준 정사각형): 중심 x, 중심 y(위에서 아래로), 한 변 길이.
        // 4캐릭터 idle 프레임 0의 합집합 영역이 x 104~158, y 74~198 인 것을 기준으로 잡음.
        private static (float cx, float cy, float size) GetCrop(PortraitCrop crop)
        {
            switch (crop)
            {
                case PortraitCrop.FullBody: return (131f, 136f, 136f);
                default: return (131f, 106f, 84f);
            }
        }

        public static GameObject Create(Transform parent, ComposedCharacter character, int size, PortraitCrop crop = PortraitCrop.Bust)
        {
            var slotGO = new GameObject("Portrait", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(RectMask2D));
            slotGO.transform.SetParent(parent, false);

            var layout = slotGO.GetComponent<LayoutElement>();
            layout.minWidth = size;
            layout.minHeight = size;
            layout.preferredWidth = size;
            layout.preferredHeight = size;
            layout.flexibleWidth = 0;

            var background = slotGO.GetComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.08f);
            background.raycastTarget = false;

            if (character == null) return slotGO;

            var (cx, cy, cropSize) = GetCrop(crop);
            float scale = size / cropSize;
            float layerSize = CanvasSize * scale;
            // UI는 y가 위로, 이미지 좌표는 y가 아래로 커지므로 y 부호를 뒤집는다.
            var offset = new Vector2((CanvasSize / 2f - cx) * scale, (cy - CanvasSize / 2f) * scale);

            foreach (var part in character.ZOrderBackToFront)
            {
                if (!character.Parts.TryGetValue(part, out var sprite) || sprite == null) continue;
                AddLayer(slotGO.transform, part, sprite, layerSize, offset);
            }

            return slotGO;
        }

        private static void AddLayer(Transform parent, string name, Sprite sprite, float layerSize, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(layerSize, layerSize);
            rect.anchoredPosition = offset;

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
        }
    }
}
