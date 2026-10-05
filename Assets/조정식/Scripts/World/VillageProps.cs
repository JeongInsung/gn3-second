using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 마을의 특정 물체(여관, 퀘스트 게시판 등)를 그림 이름으로 찾고, 에디터 메뉴를 안 눌렀어도 실행 중에 클릭 가능하게 만든다.
    /// 클릭 가능 = 실루엣 콜라이더 + SelectableBuilding + 흰 테두리(흰 실루엣 8방향 사본을 물체 뒤에 깐 것).
    /// </summary>
    public static class VillageProps
    {
        private const float OutlineOffset = 0.0185f; // 화면 약 2px(1080p, 1유닛 ≈ 108px)

        private static readonly Vector2[] OutlineDirections =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.7071f, 0.7071f), new Vector2(0.7071f, -0.7071f), new Vector2(-0.7071f, 0.7071f), new Vector2(-0.7071f, -0.7071f),
        };

        /// <summary>
        /// 그림 이름이 spritePrefix로 시작하는 물체(원본 또는 구운 "이름@..."). 창문 불빛 마스크·그림자·외곽선도
        /// 같은 그림 이름을 쓰므로 뺀다.
        /// </summary>
        public static SpriteRenderer FindRenderer(string spritePrefix)
        {
            foreach (var renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                if (Matches(renderer, spritePrefix)) return renderer;
            return null;
        }

        public static bool IsProp(SelectableBuilding building, string spritePrefix) =>
            building != null && Matches(building.GetComponent<SpriteRenderer>(), spritePrefix);

        private static bool Matches(SpriteRenderer renderer, string spritePrefix) =>
            renderer != null && renderer.sprite != null && renderer.sprite.name.StartsWith(spritePrefix)
            && renderer.GetComponent<WindowGlow>() == null && renderer.GetComponent<ProjectedShadow>() == null
            && renderer.GetComponent<ForgeFire>() == null
            && renderer.name != "Outline" && (renderer.transform.parent == null || renderer.transform.parent.name != "Outline");

        /// <summary>이미 SelectableBuilding이 있으면(에디터 메뉴로 만든 경우) 그대로 쓰고, 없으면 실행 중에 붙인다.</summary>
        public static SelectableBuilding EnsureClickable(SpriteRenderer target)
        {
            var existing = target.GetComponent<SelectableBuilding>();
            if (existing != null) return existing;

            if (target.GetComponent<Collider2D>() == null) target.gameObject.AddComponent<PolygonCollider2D>(); // 스프라이트 실루엣으로 자동 생성
            var selectable = target.gameObject.AddComponent<SelectableBuilding>();
            var group = CreateOutlineGroup(target);
            if (group != null) selectable.SetOutlineGroup(group);
            return selectable;
        }

        /// <summary>흰 실루엣 8장을 사방으로 살짝 밀어 물체 뒤에 둔다 → 바깥 테두리만 보인다.</summary>
        private static GameObject CreateOutlineGroup(SpriteRenderer target)
        {
            var shader = Resources.Load<Shader>("SpriteSilhouette");
            if (shader == null) return null;
            var material = new Material(shader);

            var group = new GameObject("Outline");
            group.transform.SetParent(target.transform, false);
            foreach (var direction in OutlineDirections)
            {
                var go = new GameObject("OutlineCopy", typeof(SpriteRenderer));
                go.transform.SetParent(group.transform, false);
                go.transform.localPosition = direction * OutlineOffset;
                var copy = go.GetComponent<SpriteRenderer>();
                copy.sprite = target.sprite;
                copy.flipX = target.flipX;
                copy.sharedMaterial = material;
                copy.sortingLayerID = target.sortingLayerID;
                copy.sortingOrder = target.sortingOrder - 1;
            }
            group.SetActive(false);
            return group;
        }
    }
}
