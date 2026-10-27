using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 지은 마을 건물과 그 자리. 키는 Village 자식 이름 + "#같은 이름 중 몇 번째"(같은 집 그림이 여러 채라서).
    /// 씬이 아니라 여기 값이 원본이라 전투에 다녀오거나 건물을 지어 MainScene을 다시 불러도 그대로 남는다.
    /// 씬에 맞추는 일은 VillageLayoutApplier가 한다(아직 정해지지 않았으면 그때 씬을 보고 처음 목록을 만든다).
    /// </summary>
    public static class VillageLayout
    {
        private static readonly Dictionary<string, Vector2> _built = new Dictionary<string, Vector2>();

        /// <summary>false면 아직 목록이 없다 → 씬에 처음 맞출 때 새 게임(StartBuilt 건물만 — 지금은 빈 숲) 또는 예전 저장(전부)으로 채운다.</summary>
        public static bool IsInitialized { get; private set; }
        /// <summary>예전 저장(버전 9 이하)을 불러왔다: 건물이 처음부터 다 있던 시절이라 프리팹 자리 그대로 전부 지은 것으로 친다.</summary>
        public static bool PendingFullVillage { get; private set; }
        /// <summary>목록이 바뀔 때마다 1씩 오른다. 씬이 맞춘 값과 다르면 다시 불러야 한다.</summary>
        public static int Stamp { get; private set; }

        public static event Action OnChanged;

        public static IReadOnlyDictionary<string, Vector2> Built => _built;

        public static bool IsBuilt(string key) => _built.ContainsKey(key);

        /// <summary>그 종류 건물이 하나라도 지어져 있는가(BuildingCatalog id).</summary>
        public static bool HasBuilding(string id)
        {
            var entry = BuildingCatalog.Get(id);
            return entry != null && _built.Keys.Any(k => entry.Matches(k));
        }

        /// <summary>새 게임에서 처음부터 있던 건물(StartBuilt)을 뺀, 길드장이 지은 건물 수.</summary>
        public static int PlayerBuiltCount =>
            _built.Keys.Count(k => BuildingCatalog.ForObject(k) is { StartBuilt: false });

        // 목록을 손대면(짓기·치우기) 그 목록이 정본이다 — IsInitialized가 false로 남아 있으면 전투에서 돌아와 씬을 다시 맞출 때
        // VillageLayoutApplier가 Initialize(StartBuilt만)로 덮어써 지은 건물이 모두 사라졌다(2026-10-26, 건물 없는 저장을 불러온 뒤 지은 경우).
        public static void Add(string key, Vector2 position)
        {
            _built[key] = position;
            IsInitialized = true;
            Changed();
        }

        /// <summary>그 자리 하나를 뺀다(놓은 장식품 치우기).</summary>
        public static void Remove(string key)
        {
            IsInitialized = true;
            if (_built.Remove(key)) Changed();
        }

        public static void RemoveBuilding(string id)
        {
            var entry = BuildingCatalog.Get(id);
            if (entry == null) return;
            foreach (var key in _built.Keys.Where(entry.Matches).ToList()) _built.Remove(key);
            Changed();
        }

        /// <summary>씬을 보고 처음 목록을 만든다(VillageLayoutApplier). 바뀐 것으로 치지 않는다(씬이 이미 이 모습이 된다).</summary>
        public static void Initialize(IEnumerable<KeyValuePair<string, Vector2>> buildings)
        {
            _built.Clear();
            foreach (var kv in buildings) _built[kv.Key] = kv.Value;
            IsInitialized = true;
            PendingFullVillage = false;
            GN3.Economy.Guild.RefreshRank(false); // 길드 홀이 있으면 용병단 → 길드
        }

        /// <param name="announce">불러오기(Restore)면 false — 단계가 올라도 등급 상승 알림을 띄우지 않는다.</param>
        private static void Changed(bool announce = true)
        {
            Stamp++;
            OnChanged?.Invoke();
            GN3.Economy.Guild.RefreshRank(announce); // 길드 홀을 짓거나 잃으면 단계 이름·막힌 단계가 바뀐다
        }

        // ---------- 저장 ----------

        /// <summary>"키|x|y"</summary>
        public static List<string> SaveEntries() =>
            _built.Select(kv => $"{kv.Key}|{kv.Value.x.ToString("0.###", CultureInfo.InvariantCulture)}|{kv.Value.y.ToString("0.###", CultureInfo.InvariantCulture)}").ToList();

        /// <summary>fullVillage: 예전 저장이라 건물 목록이 없다 → 다음에 씬에 맞출 때 전부 지은 마을로 만든다.</summary>
        public static void Restore(IEnumerable<string> entries, bool fullVillage)
        {
            _built.Clear();
            if (fullVillage)
            {
                IsInitialized = false;
                PendingFullVillage = true;
            }
            else
            {
                if (entries != null)
                    foreach (var e in entries)
                    {
                        var parts = e.Split('|');
                        if (parts.Length == 3
                            && !BuildingCatalog.RetiredPrefixes.Any(p => parts[0].StartsWith(p, StringComparison.Ordinal)) // 없앤 건물(예전 용병 숙소)은 버린다
                            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                            _built[MigrateKey(parts[0])] = new Vector2(x, y);
                    }
                IsInitialized = true; // 새 저장의 건물 목록은 비어 있어도 정답이다(아무것도 안 지음) — 새 게임처럼 다시 만들지 않는다
                PendingFullVillage = false;
            }
            Changed(false);
        }

        private const string OldTentName = "천막"; // 2026-10-24 직접 그린 천막 → 하얀 텐트 그림(길드 천막)

        private static string MigrateKey(string key) =>
            key.StartsWith(OldTentName + "#") ? BuildingCatalog.TentPrefix + key.Substring(OldTentName.Length) : key;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _built.Clear();
            IsInitialized = false;
            PendingFullVillage = false;
            Stamp = 0;
            OnChanged = null;
        }
    }
}
