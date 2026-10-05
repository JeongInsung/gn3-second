using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 마을 여관 찾기와 클릭 가능하게 만들기. 용병이 드나드는 문(VillagePartyPresenter)과
    /// 여관 클릭 → 파티 구성 패널(MainMenuBootstrapper)이 같은 여관을 쓴다.
    /// </summary>
    public static class VillageInn
    {
        private const string SpritePrefix = "여관";
        private const float OutlineOffset = 0.0185f; // 화면 약 2px(1080p, 1유닛 ≈ 108px)

        private static readonly Vector2[] OutlineDirections =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.7071f, 0.7071f), new Vector2(0.7071f, -0.7071f), new Vector2(-0.7071f, 0.7071f), new Vector2(-0.7071f, -0.7071f),
        };

        /// <summary>
        /// 여관 그림(원본 "여관" 또는 구운 "여관@...")의 렌더러. 창문 불빛 마스크·그림자·외곽선도 같은 그림 이름을 쓰므로 뺀다.
        /// </summary>
        public static SpriteRenderer FindRenderer()
        {
            foreach (var renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                if (IsInnSprite(renderer)) return renderer;
            return null;
        }

        public static bool IsInn(SelectableBuilding building) =>
            building != null && IsInnSprite(building.GetComponent<SpriteRenderer>());

        private static bool IsInnSprite(SpriteRenderer renderer) =>
            renderer != null && renderer.sprite != null && renderer.sprite.name.StartsWith(SpritePrefix)
            && renderer.GetComponent<WindowGlow>() == null && renderer.GetComponent<ProjectedShadow>() == null
            && renderer.name != "Outline" && renderer.transform.parent?.name != "Outline";

        /// <summary>
        /// 여관을 마우스로 고를 수 있게 한다. 에디터 메뉴("선택한 건물을 클릭 가능하게")로 이미 만들었으면 그대로 쓰고,
        /// 아니면 실행 중에 실루엣 콜라이더 + SelectableBuilding + 흰 테두리(실루엣 8방향 사본)를 붙인다.
        /// </summary>
        public static SelectableBuilding EnsureClickable(SpriteRenderer inn)
        {
            var existing = inn.GetComponent<SelectableBuilding>();
            if (existing != null) return existing;

            if (inn.GetComponent<Collider2D>() == null) inn.gameObject.AddComponent<PolygonCollider2D>(); // 스프라이트 실루엣으로 자동 생성
            var selectable = inn.gameObject.AddComponent<SelectableBuilding>();
            var group = CreateOutlineGroup(inn);
            if (group != null) selectable.SetOutlineGroup(group);
            return selectable;
        }

        /// <summary>흰 실루엣 8장을 사방으로 살짝 밀어 여관 뒤에 둔다 → 바깥 테두리만 보인다.</summary>
        private static GameObject CreateOutlineGroup(SpriteRenderer inn)
        {
            var shader = Resources.Load<Shader>("SpriteSilhouette");
            if (shader == null) return null;
            var material = new Material(shader);

            var group = new GameObject("Outline");
            group.transform.SetParent(inn.transform, false);
            foreach (var direction in OutlineDirections)
            {
                var go = new GameObject("OutlineCopy", typeof(SpriteRenderer));
                go.transform.SetParent(group.transform, false);
                go.transform.localPosition = direction * OutlineOffset;
                var copy = go.GetComponent<SpriteRenderer>();
                copy.sprite = inn.sprite;
                copy.flipX = inn.flipX;
                copy.sharedMaterial = material;
                copy.sortingLayerID = inn.sortingLayerID;
                copy.sortingOrder = inn.sortingOrder - 1;
            }
            group.SetActive(false);
            return group;
        }
    }
}
