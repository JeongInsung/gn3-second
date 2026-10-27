using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// MainScene이 로드될 때마다(MainMenuBootstrapper.Bootstrap 맨 앞, 마을 물체들의 Start보다 먼저) Village 프리팹의 건물을
    /// VillageLayout에 맞춘다: 짓지 않은 건물은 끄고, 지은 건물은 고른 자리로 옮긴다. 길찾기 격자·앞뒤 정렬·문 위치는
    /// 그 뒤 Start에서 계산된다. 건물을 지으면 씬을 다시 부르지 않고 ApplyLive로 그 자리만 고친 뒤 Rebuilt를 쏴서
    /// 격자·정렬·문·건물 클릭을 다시 맞추게 한다(씬을 다시 부르면 0.7초쯤 멈췄다). 불러오기는 씬을 다시 부른다.
    /// </summary>
    public static class VillageLayoutApplier
    {
        private const string VillageRootName = "Village";

        /// <summary>Village 안의 건물 자리 하나(지었든 안 지었든).</summary>
        public class Slot
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public string Key;
            public BuildingCatalog.Entry Entry;
            public bool Built => VillageLayout.IsBuilt(Key);
        }

        private static readonly List<Slot> _slots = new List<Slot>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            Rebuilt = null;
            _hiddenLog = null;
        }

        public static IReadOnlyList<Slot> Slots => _slots;

        /// <summary>씬이 맞춘 VillageLayout.Stamp. 다르면 씬이 저장 상태와 어긋난 것이다.</summary>
        public static int AppliedStamp { get; private set; } = -1;

        /// <summary>ApplyLive가 마을을 고친 뒤. 길찾기 격자·앞뒤 정렬·문(VillagePartyPresenter·VillageDogs)과 건물 클릭(MainMenuBootstrapper)이 다시 맞춘다.</summary>
        public static event Action Rebuilt;

        /// <summary>그 종류에서 아직 안 지은 첫 그림(주택 묶음은 프리팹 순서대로). 장식품·반복 건물(용병 텐트)은 남은 게 없으면 하나 더 복제한다.</summary>
        public static Slot FirstUnbuilt(BuildingCatalog.Entry entry)
        {
            var slot = _slots.FirstOrDefault(s => s.Entry == entry && !s.Built && s.Transform != null);
            if (slot == null && (entry.Decor || entry.Repeatable))
            {
                var village = GameObject.Find(VillageRootName);
                if (village != null)
                    slot = entry.Decor ? CreateDecor(village.transform, entry, NextDecorKey(entry), false)
                                       : CreateRepeat(village.transform, entry, NextRepeatKey(entry), false);
            }
            return slot;
        }

        // ---------- 반복 건물(용병 텐트) ----------

        /// <summary>
        /// 그 건물의 프리팹 그림(첫 자리)을 복제해 Village 바로 아래에 새 자리를 만든다(그림자는 자식이라 같이 온다). 이름은 원본과 같아
        /// 키는 "이름#n". built면 VillageLayout 자리로 옮기고 켠다. 원본 자리가 없으면 null.
        /// </summary>
        private static Slot CreateRepeat(Transform village, BuildingCatalog.Entry entry, string key, bool built)
        {
            var source = _slots.FirstOrDefault(s => s.Entry == entry && s.Transform != null);
            if (source == null) return null;
            var copy = UnityEngine.Object.Instantiate(source.Transform.gameObject, village);
            copy.name = source.Transform.name;
            var root = copy.transform;
            if (built && VillageLayout.Built.TryGetValue(key, out var position))
                root.position = new Vector3(position.x, position.y, source.Transform.position.z);
            var slot = new Slot { Transform = root, Renderer = copy.GetComponent<SpriteRenderer>(), Key = key, Entry = entry };
            if (slot.Renderer != null) slot.Renderer.color = Color.white; // 원본이 옮기는 중이라 흐려져 있었어도 새 것은 또렷하게
            _slots.Add(slot);
            copy.SetActive(built);
            return slot;
        }

        /// <summary>새 반복 건물 키: 원본 이름#(지금 쓰는 번호 중 가장 큰 것 + 1).</summary>
        private static string NextRepeatKey(BuildingCatalog.Entry entry)
        {
            var source = _slots.FirstOrDefault(s => s.Entry == entry && s.Transform != null);
            string name = source != null ? source.Transform.name : entry.Prefixes[0];
            int max = -1;
            foreach (var key in VillageLayout.Built.Keys.Concat(_slots.Select(s => s.Key)))
            {
                if (!key.StartsWith(name + "#")) continue;
                if (int.TryParse(key.Substring(name.Length + 1), out int n)) max = Mathf.Max(max, n);
            }
            return $"{name}#{max + 1}";
        }

        public static void Apply()
        {
            _slots.Clear();
            var village = GameObject.Find(VillageRootName);
            if (village == null) return;

            var seen = new Dictionary<string, int>();
            foreach (Transform child in village.transform)
            {
                // 없앤 건물(예전 용병 숙소 갈색 텐트)은 프리팹에 남아 있어도 늘 숨긴다(그림자는 자식이라 같이 꺼진다)
                if (BuildingCatalog.RetiredPrefixes.Any(p => child.name.StartsWith(p, StringComparison.Ordinal)))
                {
                    child.gameObject.SetActive(false);
                    continue;
                }
                var entry = BuildingCatalog.ForObject(child.name);
                if (entry == null) continue;
                seen.TryGetValue(child.name, out int n);
                seen[child.name] = n + 1;
                _slots.Add(new Slot { Transform = child, Renderer = child.GetComponent<SpriteRenderer>(), Key = $"{child.name}#{n}", Entry = entry });
            }

            if (!VillageLayout.IsInitialized)
            {
                // 예전 저장: 건물이 다 있던 마을 그대로(그때 없던 텐트·오두막은 뺀다). 새 게임: StartBuilt만(지금은 빈 숲).
                bool full = VillageLayout.PendingFullVillage;
                VillageLayout.Initialize(_slots
                    .Where(s => full ? !BuildingCatalog.OldVillageMissing.Any(s.Transform.name.StartsWith) : s.Entry.StartBuilt)
                    .Select(s => new KeyValuePair<string, Vector2>(s.Key, s.Transform.position)));
            }

            var builtAreas = new List<Rect>();
            foreach (var slot in _slots)
            {
                bool built = VillageLayout.Built.TryGetValue(slot.Key, out var position);
                if (built)
                {
                    slot.Transform.position = new Vector3(position.x, position.y, slot.Transform.position.z);
                    if (slot.Renderer != null) builtAreas.Add(BuildingArea(slot.Renderer));
                }
                slot.Transform.gameObject.SetActive(built);
            }

            // 놓은 장식품: 원본을 복제해 그 자리에 세운다
            foreach (var kv in VillageLayout.Built.ToList())
            {
                var entry = BuildingCatalog.DecorForKey(kv.Key);
                if (entry == null) continue;
                var slot = CreateDecor(village.transform, entry, kv.Key, true);
                if (slot?.Renderer != null) builtAreas.Add(BuildingArea(slot.Renderer));
            }

            // 반복 건물(용병 텐트)의 두 번째 채부터: 프리팹엔 한 자리뿐이라 그림을 복제해 세운다(번호 순서대로)
            foreach (var kv in VillageLayout.Built.OrderBy(k => k.Key, StringComparer.Ordinal).ToList())
            {
                if (_slots.Any(s => s.Key == kv.Key)) continue;
                var entry = BuildingCatalog.ForObject(kv.Key);
                if (entry == null || !entry.Repeatable) continue;
                var slot = CreateRepeat(village.transform, entry, kv.Key, true);
                if (slot?.Renderer != null) builtAreas.Add(BuildingArea(slot.Renderer));
            }

            IndexForest(village.transform);
            HideFelled(village.transform);
            ClearForestUnder(village.transform, builtAreas);
            AppliedStamp = VillageLayout.Stamp;
            RefreshLamps();
        }

        // ---------- 숲 벌목(ForestFelling) ----------

        // "숲" 그룹 자식 → 키(이름#같은 이름 중 몇 번째). 앞뒤 정렬이 부모를 바꾸기 전에(Apply 때) 프리팹 순서로 매긴다.
        private static readonly Dictionary<Transform, string> _forestKeys = new Dictionary<Transform, string>();

        private static void IndexForest(Transform village)
        {
            _forestKeys.Clear();
            var group = village.Find(ForestGroupName);
            if (group == null) return;
            var seen = new Dictionary<string, int>();
            foreach (Transform child in group)
            {
                seen.TryGetValue(child.name, out int n);
                seen[child.name] = n + 1;
                _forestKeys[child] = $"{child.name}#{n}";
            }
        }

        private static void HideFelled(Transform village)
        {
            if (ForestLayout.Felled.Count == 0) return;
            var shadows = village.GetComponentsInChildren<ProjectedShadow>();
            foreach (var kv in _forestKeys)
            {
                if (!ForestLayout.IsFelled(kv.Value)) continue;
                var renderer = kv.Key.GetComponent<SpriteRenderer>();
                if (renderer != null) Hide(renderer, shadows);
            }
        }

        /// <summary>지금 보이는 숲 물체(나무·덤불·바위)와 그 키 — 벌목 모드가 클릭으로 고른다.</summary>
        public static IEnumerable<(SpriteRenderer renderer, string key)> StandingForest()
        {
            foreach (var kv in _forestKeys)
            {
                if (kv.Key == null) continue;
                var renderer = kv.Key.GetComponent<SpriteRenderer>();
                if (renderer != null && renderer.gameObject.activeInHierarchy) yield return (renderer, kv.Value);
            }
        }

        /// <summary>숲 물체 하나를 벤다: 그림자째 숨기고 벤 목록(ForestLayout)에 적은 뒤 길찾기 격자를 다시 맞춘다.</summary>
        public static void Fell(SpriteRenderer renderer, string key)
        {
            var village = GameObject.Find(VillageRootName);
            if (village == null || renderer == null) return;
            Hide(renderer, village.GetComponentsInChildren<ProjectedShadow>());
            ForestLayout.Add(key);
            NotifyRebuilt();
        }

        // ---------- 장식품 ----------

        private const string DecorTemplateGroupName = "장식 원본"; // Village 아래 꺼 둔 원본들(DecorTemplateBuilder)

        /// <summary>
        /// 원본 "장식:id"를 복제해 Village 바로 아래에 둔다(이름은 원본과 같음, 꺼진 채로). built면 VillageLayout 자리로 옮기고 켠다.
        /// 루트가 놓는 자리이고 그림·그림자는 그 자식이라, 옮기면 정렬(SortAnchor)·그림자째 같이 간다. 원본이 없으면 null.
        /// </summary>
        private static Slot CreateDecor(Transform village, BuildingCatalog.Entry entry, string key, bool built)
        {
            var templates = village.Find(DecorTemplateGroupName);
            var template = templates != null ? templates.Find(BuildingCatalog.DecorPrefix + entry.Id) : null;
            if (template == null)
            {
                Debug.LogWarning($"[VillageLayoutApplier] 장식 원본이 없습니다: {entry.Id}");
                return null;
            }
            var copy = UnityEngine.Object.Instantiate(template.gameObject, village); // 원본이 꺼져 있어 복제본도 꺼진 채 나온다
            copy.name = template.name;
            var root = copy.transform;
            if (built && VillageLayout.Built.TryGetValue(key, out var position))
                root.position = new Vector3(position.x, position.y, root.position.z);
            var renderer = root.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r.GetComponent<ProjectedShadow>() == null);
            var slot = new Slot { Transform = root, Renderer = renderer, Key = key, Entry = entry };
            _slots.Add(slot);
            copy.SetActive(built);
            return slot;
        }

        /// <summary>장식품 원본의 그림([건설] 창 아이콘). 원본이 없으면 null.</summary>
        public static Sprite DecorIcon(BuildingCatalog.Entry entry)
        {
            var village = GameObject.Find(VillageRootName);
            var templates = village != null ? village.transform.Find(DecorTemplateGroupName) : null;
            var template = templates != null ? templates.Find(BuildingCatalog.DecorPrefix + entry.Id) : null;
            if (template == null) return null;
            var renderer = template.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r.GetComponent<ProjectedShadow>() == null);
            return renderer != null ? renderer.sprite : null;
        }

        /// <summary>새 장식품 키 "장식:id#n"(n = 지금 쓰는 번호 중 가장 큰 것 + 1, 종류와 상관없이).</summary>
        private static string NextDecorKey(BuildingCatalog.Entry entry)
        {
            int max = 0;
            foreach (var key in VillageLayout.Built.Keys.Concat(_slots.Select(s => s.Key)))
            {
                if (!key.StartsWith(BuildingCatalog.DecorPrefix)) continue;
                int hash = key.LastIndexOf('#');
                if (hash >= 0 && int.TryParse(key.Substring(hash + 1), out int n)) max = Mathf.Max(max, n);
            }
            return $"{BuildingCatalog.DecorPrefix}{entry.Id}#{max + 1}";
        }

        /// <summary>놓은 장식품을 치운다(옮기기 모드 [치우기]). 되살릴 수 있게 끄기만 한다 — RestoreDecor / DestroyDecor로 끝낸다.</summary>
        public static void RemoveDecor(Slot slot)
        {
            if (slot == null || !slot.Entry.Decor || slot.Transform == null) return;
            VillageLayout.Remove(slot.Key);
            slot.Transform.gameObject.SetActive(false);
            NotifyRebuilt();
        }

        /// <summary>치운 장식품을 그 자리에 되살린다([옮기기 취소]). 격자는 부르는 쪽이 NotifyRebuilt로 맞춘다.</summary>
        public static void RestoreDecor(Slot slot, Vector2 position)
        {
            if (slot == null || slot.Transform == null) return;
            VillageLayout.Add(slot.Key, position);
            slot.Transform.position = new Vector3(position.x, position.y, slot.Transform.position.z);
            slot.Transform.gameObject.SetActive(true);
        }

        /// <summary>치운 장식품을 아주 없앤다([완료]).</summary>
        public static void DestroyDecor(Slot slot)
        {
            if (slot == null) return;
            _slots.Remove(slot);
            if (slot.Transform != null) UnityEngine.Object.Destroy(slot.Transform.gameObject);
        }

        // 놓은 등불(랜턴 기둥·철등·모닥불)도 밤에 켜지게
        private static void RefreshLamps()
        {
            if (DayNightCycle.Instance != null) DayNightCycle.Instance.RefreshLamps();
        }

        /// <summary>
        /// 방금 VillageLayout에 넣은 건물 하나를 씬에 바로 세운다(BuildPlacement). 대신 들어선 건물(replacedId)은 그림자째 끄고,
        /// 새 건물 자리의 숲·풀꽃도 치운 뒤 Rebuilt를 쏜다.
        /// </summary>
        public static void ApplyLive(Slot built, string replacedId)
        {
            var village = GameObject.Find(VillageRootName);
            if (village == null || built == null || !VillageLayout.Built.TryGetValue(built.Key, out var position)) return;
            var shadows = village.GetComponentsInChildren<ProjectedShadow>();

            if (replacedId != null)
                foreach (var slot in _slots)
                    if (slot.Entry.Id == replacedId && slot.Renderer != null && slot.Renderer.gameObject.activeInHierarchy)
                        Hide(slot.Renderer, shadows);

            var target = built.Transform;
            var root = target.parent != null && target.parent.name == SortAnchorName ? target.parent : target; // 이미 정렬됐으면 SortAnchor째
            root.position += new Vector3(position.x, position.y, target.position.z) - target.position;
            target.gameObject.SetActive(true);
            if (built.Renderer != null) ClearForestUnder(village.transform, new List<Rect> { BuildingArea(built.Renderer) });
            NotifyRebuilt();
        }

        /// <summary>
        /// 지은 건물을 VillageLayout에 새로 적힌 자리로 옮긴다(BuildPlacement 옮기기). 앞뒤 정렬된 건물은 SortAnchor째 옮겨
        /// 테두리·콜라이더가 같이 가고, 밖으로 빠진 그림자는 sourceOverride로 따라온다. 새 자리의 숲·풀꽃을 치우고 Rebuilt를 쏜다.
        /// </summary>
        /// rebuild false면 Rebuilt를 미룬다(여러 채를 한꺼번에 되돌릴 때 — 끝나고 NotifyRebuilt 한 번).
        public static void MoveLive(Slot slot, bool rebuild = true)
        {
            var village = GameObject.Find(VillageRootName);
            if (village == null || slot == null || !VillageLayout.Built.TryGetValue(slot.Key, out var position)) return;
            var target = slot.Transform;
            Vector3 delta = new Vector3(position.x, position.y, target.position.z) - target.position;
            var root = target.parent != null && target.parent.name == SortAnchorName ? target.parent : target;
            root.position += delta;
            if (slot.Renderer != null) ClearForestUnder(village.transform, new List<Rect> { BuildingArea(slot.Renderer) });
            if (rebuild) NotifyRebuilt();
        }

        /// <summary>마을을 고친 뒤 격자·정렬·문·클릭을 다시 맞추게 한다(ApplyLive·MoveLive).</summary>
        public static void NotifyRebuilt()
        {
            AppliedStamp = VillageLayout.Stamp;
            Physics2D.SyncTransforms(); // 건물 클릭 콜라이더를 바로 새 자리로(시간을 멈춰 두면 물리가 돌지 않아 옛 자리에 남았다)
            VillageProps.InvalidateCache(); // 켜고 끈 건물이 바로 다시 찾아지게
            RefreshLamps();
            Rebuilt?.Invoke();
        }

        /// <summary>마을 물체를 끈다. 앞뒤 정렬된 물체는 SortAnchor째, 밖으로 빠진 그림자(sourceOverride)도 같이.</summary>
        private static void Hide(SpriteRenderer renderer, ProjectedShadow[] shadows)
        {
            var target = renderer.transform;
            if (target.parent != null && target.parent.name == SortAnchorName) target = target.parent;
            target.gameObject.SetActive(false);
            _hiddenLog?.Add(target.gameObject);
            foreach (var shadow in shadows)
            {
                if (shadow == null || shadow.sourceOverride != renderer) continue;
                shadow.gameObject.SetActive(false);
                _hiddenLog?.Add(shadow.gameObject);
            }
        }

        // 옮기기 모드(BuildingMover) 동안 치운 물체 — [옮기기 취소]면 다시 켠다
        private static List<GameObject> _hiddenLog;

        /// <summary>이제부터 치우는 나무·풀꽃(그림자 포함)을 적어 둔다.</summary>
        public static void BeginHiddenLog() => _hiddenLog = new List<GameObject>();

        /// <summary>적기를 그만둔다. restore면 그동안 치운 것을 다시 켠다(그 뒤 NotifyRebuilt로 격자에 다시 넣는다). 되살린 수.</summary>
        public static int EndHiddenLog(bool restore)
        {
            int restored = 0;
            if (restore && _hiddenLog != null)
                foreach (var go in _hiddenLog)
                    if (go != null && !go.activeSelf)
                    {
                        go.SetActive(true);
                        restored++;
                    }
            _hiddenLog = null;
            return restored;
        }

        /// <summary>건물 그림이 덮는 바닥 쪽(지붕 끝은 빼고 발밑은 조금 더). 이 안에 밑동이 있는 나무·풀은 건물을 지으며 치운 것으로 친다.</summary>
        private static Rect BuildingArea(SpriteRenderer renderer)
        {
            var b = renderer.bounds;
            return Rect.MinMaxRect(b.min.x + BuildingSideInset, b.min.y - BuildingFrontMargin, b.max.x - BuildingSideInset, b.min.y + b.size.y * BuildingCoverHeight);
        }

        private const string SortAnchorName = "SortAnchor";   // VillageDepthSorter
        private const string ForestGroupName = "숲";          // 나무·덤불·돌(그림자가 있어 길을 막음)
        private const string GroundCoverGroupName = "숲 바닥"; // 풀·들꽃·꽃무더기(그림자 없음, 길을 막지 않음)
        private const float BuildingSideInset = 0.1f;
        private const float BuildingFrontMargin = 0.4f; // 문 앞에 바짝 붙은 나무도 치운다
        private const float BuildingCoverHeight = 0.85f;

        /// <summary>
        /// 지은 건물 자리의 숲(나무·덤불·돌)과 바닥 풀꽃을 끈다. 새로 지을 땐 밑동끼리는 겹칠 수 없지만(BuildPlacement) 가지·풀은 덮이고,
        /// 예전 저장(건물이 다 있던 마을)은 둘레 숲과 겹치는 건물이 많아서다. 꺼 둔 나무는 길찾기 장애물에서도 빠진다.
        /// </summary>
        private static void ClearForestUnder(Transform village, List<Rect> builtAreas)
        {
            if (builtAreas.Count == 0) return;
            ProjectedShadow[] shadows = null;
            foreach (var groupName in new[] { ForestGroupName, GroundCoverGroupName })
            {
                var group = village.Find(groupName);
                if (group == null) continue;
                // 앞뒤 정렬 뒤(ApplyLive)엔 나무가 SortAnchor 아래, 그림자가 그 옆에 있어 자식 전체에서 그림만 고른다
                foreach (var renderer in group.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (renderer.GetComponent<ProjectedShadow>() != null || !renderer.gameObject.activeInHierarchy) continue;
                    var b = renderer.bounds;
                    var foot = new Vector2(b.center.x, b.min.y);
                    if (!builtAreas.Any(r => r.Contains(foot))) continue;
                    if (shadows == null) shadows = group.GetComponentsInChildren<ProjectedShadow>();
                    Hide(renderer, shadows);
                }
            }
        }
    }
}
