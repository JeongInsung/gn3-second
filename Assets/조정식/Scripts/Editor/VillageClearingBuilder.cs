using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// 가운데 칸(처음 화면)의 공터를 둥글게(타원) 만든다(Village 프리팹):
    /// - "숲" 자식(나무·덤불·바위 — 그림자가 있어 길·건설을 막는다) 중 발밑이 공터 타원 안인 것을 타원 밖 테두리로 옮긴다.
    /// - 지우지 않고 옮기기만 한다: 벌목 기록(ForestLayout) 키가 "이름#같은 이름 중 몇 번째"라 개수·순서가 바뀌면 안 된다.
    /// - 새 자리는 타원 경계에 가까운 곳부터 채워 둥근 테두리가 또렷하게 보이게 한다(주로 칸의 네 모서리).
    /// - 그래도 테두리에 빈 곳이 남으면 가운데 칸 숲을 복제해 채운다. 복제본은 그룹 맨 뒤에 RingSuffix를 붙여 둔다
    ///   (원래 나무의 "#몇 번째"를 밀지 않는다). 다시 돌리면 지난 복제본을 지우고 새로 채운다.
    /// "숲 바닥"(풀·들꽃)은 건설을 막지 않으므로 그대로 둔다.
    /// </summary>
    public static class VillageClearingBuilder
    {
        private const string PrefabPath = "Assets/조정식/Prefabs/Village.prefab";
        private const string ForestGroupName = "숲";
        public const string RingSuffix = " (공터)";

        // 가운데 칸 = VillageExpandBuilder와 같은 값
        private static readonly Vector2 CellCenter = new Vector2(0.19f, 0.54f);
        private static readonly Vector2 CellSize = new Vector2(17.78f, 10f);
        // 공터 타원(발밑 기준). 나무 밑동·그림자가 공터로 들어오지 않게 KeepOut만큼 더 넓게 비운다.
        private static readonly Vector2 ClearingCenter = new Vector2(0.25f, 0.45f);
        private static readonly Vector2 ClearingRadius = new Vector2(6.4f, 3.2f);
        private static readonly Vector2 KeepOut = new Vector2(0.6f, 0.45f);
        private const float EdgeInset = 0.3f;   // 칸 가장자리에서 이만큼 안쪽에 둔다
        private const float OuterBand = 1.6f;   // 칸 안에 자리가 모자라면 칸 바로 밖 이 폭 안에 둔다
        private const float Spacing = 0.55f;    // 숲 물체 발밑끼리 이보다 가까우면 버린다
        private const float Jitter = 0.15f;
        private const int Candidates = 6000;
        private const int Seed = 20261025;
        // 테두리 채우기: 비울 타원의 정규화 반지름 RingInner~RingOuter 띠에서 숲이 RingGap보다 멀리 떨어진 빈 곳을 채운다
        private const float RingInner = 1.0f;
        private const float RingOuter = 1.35f;
        private const float RingGap = 0.85f;
        private const float RingCellOverhang = 0.6f; // 칸 끝을 이만큼 넘어서까지 채운다(위·아래 가운데 끝)

        [MenuItem("GN3/Village/가운데 공터 둥글게")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var group = root.transform.Find(ForestGroupName);
                if (group == null)
                {
                    Debug.LogWarning("[VillageClearingBuilder] \"숲\" 그룹이 없습니다.");
                    return;
                }
                var rng = new System.Random(Seed);
                foreach (var old in group.Cast<Transform>().Where(t => t.name.EndsWith(RingSuffix)).ToList())
                    Object.DestroyImmediate(old.gameObject);
                var all = group.Cast<Transform>().ToList();
                var movers = all.Where(t => IsInClearing(t.position)).ToList();
                var taken = all.Except(movers).Select(t => (Vector2)t.position).ToList();

                var cell = new Rect(CellCenter - CellSize * 0.5f, CellSize);
                var inner = Rect.MinMaxRect(cell.xMin + EdgeInset, cell.yMin + EdgeInset, cell.xMax - EdgeInset, cell.yMax - EdgeInset);
                var outer = Rect.MinMaxRect(cell.xMin - OuterBand, cell.yMin - OuterBand, cell.xMax + OuterBand, cell.yMax + OuterBand);
                var spots = PickSpots(inner, movers.Count, taken, rng, p => true);
                if (spots.Count < movers.Count)
                    spots.AddRange(PickSpots(outer, movers.Count - spots.Count, taken, rng, p => !cell.Contains(p)));

                int moved = 0;
                foreach (var mover in movers)
                {
                    if (moved >= spots.Count) break;
                    var pos = spots[moved++];
                    mover.position = new Vector3(pos.x, pos.y, mover.position.z);
                    var renderer = mover.GetComponent<SpriteRenderer>();
                    if (renderer != null && rng.NextDouble() < 0.5) renderer.flipX = !renderer.flipX; // 그림자는 원본 flip을 따라간다(ProjectedShadow)
                }

                int ring = FillRing(group, cell, rng);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[VillageClearingBuilder] 공터 안 숲 {moved}/{movers.Count}개를 테두리로 옮김 · 테두리 빈 곳 {ring}그루 채움");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>타원 테두리 띠의 빈 곳에 가운데 칸 숲(바깥 복제본 제외)을 복제해 둔다.</summary>
        private static int FillRing(Transform group, Rect cell, System.Random rng)
        {
            var all = group.Cast<Transform>().ToList();
            var sources = all.Where(t => cell.Contains(t.position) && !t.name.EndsWith(VillageExpandBuilder.OuterSuffix)).ToList();
            if (sources.Count == 0) return 0;
            var taken = all.Select(t => (Vector2)t.position).ToList();
            var rect = Rect.MinMaxRect(cell.xMin + EdgeInset, cell.yMin - RingCellOverhang, cell.xMax - EdgeInset, cell.yMax + RingCellOverhang);
            var candidates = new List<Vector2>(Candidates);
            for (int i = 0; i < Candidates; i++)
            {
                var p = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, (float)rng.NextDouble()), Mathf.Lerp(rect.yMin, rect.yMax, (float)rng.NextDouble()));
                float r = NormalizedRadius(p, KeepOut);
                if (r >= RingInner && r <= RingOuter) candidates.Add(p);
            }
            int added = 0;
            foreach (var p in candidates.OrderBy(c => NormalizedRadius(c, KeepOut)))
            {
                if (taken.Any(t => (t - p).sqrMagnitude < RingGap * RingGap)) continue;
                var source = sources[rng.Next(sources.Count)];
                var copy = Object.Instantiate(source.gameObject, group); // 맨 뒤에 붙는다
                copy.name = source.name + RingSuffix;
                copy.transform.position = new Vector3(p.x, p.y, source.position.z);
                var renderer = copy.GetComponent<SpriteRenderer>();
                if (renderer != null && rng.NextDouble() < 0.5) renderer.flipX = !renderer.flipX;
                taken.Add(p);
                added++;
            }
            return added;
        }

        /// <summary>발밑이 비워 둘 타원(공터 + KeepOut) 안인가.</summary>
        public static bool IsInClearing(Vector2 foot) => NormalizedRadius(foot, KeepOut) < 1f;

        private static float NormalizedRadius(Vector2 p, Vector2 grow)
        {
            var d = p - ClearingCenter;
            float rx = ClearingRadius.x + grow.x, ry = ClearingRadius.y + grow.y;
            return Mathf.Sqrt(d.x * d.x / (rx * rx) + d.y * d.y / (ry * ry));
        }

        /// <summary>rect 안·비울 타원 밖에서 후보를 뽑아 타원 경계에 가까운 순서로 count개까지 고른다(이미 있는 숲과 Spacing 이상).</summary>
        private static List<Vector2> PickSpots(Rect rect, int count, List<Vector2> taken, System.Random rng, System.Func<Vector2, bool> allowed)
        {
            var candidates = new List<Vector2>(Candidates);
            for (int i = 0; i < Candidates; i++)
            {
                var p = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, (float)rng.NextDouble()), Mathf.Lerp(rect.yMin, rect.yMax, (float)rng.NextDouble()));
                if (IsInClearing(p) || !allowed(p)) continue;
                candidates.Add(p);
            }
            var picked = new List<Vector2>();
            foreach (var c in candidates.OrderBy(p => NormalizedRadius(p, KeepOut)))
            {
                if (picked.Count >= count) break;
                var p = c + new Vector2((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)) * Jitter;
                if (IsInClearing(p) || !rect.Contains(p)) continue;
                if (taken.Any(t => (t - p).sqrMagnitude < Spacing * Spacing)) continue;
                taken.Add(p);
                picked.Add(p);
            }
            return picked;
        }
    }
}
