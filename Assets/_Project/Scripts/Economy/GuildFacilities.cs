using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;
using UnityEngine;

namespace GN3.Economy
{
    public enum Facility { Training, Hospital, HotSpring, Dormitory, Storage }

    /// <summary>
    /// 길드 시설 증축(길드 창 → [시설]). 시설마다 0~3단계, 금화와 마정석을 들여 바로 올린다.
    ///  훈련소: 정원 +1 · 훈련 성장 +20%씩 / 병원: 정원 +1 · 치료 속도 +25%씩 / 온천: 정원 +1 · 피로 회복 +25%씩
    ///  숙소: 파티 정원 +1씩(Guild.ApplyPartySize) / 창고: 칸 +5씩(GuildStorage.Capacity)
    /// 효과는 TrainingHall·Hospital·RestVenues.HotSpring이 여기 값을 읽는다.
    /// 지금은 꺼 두었다(Enabled = false, 2026-10-20 — 나중에 비용을 다시 정해 켠다): 버튼이 숨고 단계가 있어도 효과가 없다.
    /// </summary>
    public static class GuildFacilities
    {
        public const int MaxLevel = 3;
        /// <summary>시설 기능을 켜는 스위치. false면 길드 창 [시설] 버튼이 숨고 모든 효과가 0단계와 같다(저장된 단계는 그대로 둔다).</summary>
        public const bool Enabled = false;

        public static readonly Facility[] All = { Facility.Training, Facility.Hospital, Facility.HotSpring, Facility.Dormitory, Facility.Storage };

        private static readonly Dictionary<Facility, int> _levels = new Dictionary<Facility, int>();

        public static event Action OnChanged;

        public static int Level(Facility f) => Enabled && _levels.TryGetValue(f, out int n) ? n : 0;

        // ---------- 효과 ----------

        public static int TrainingCapacityBonus => Level(Facility.Training);
        public static float TrainingGrowthMultiplier => 1f + 0.2f * Level(Facility.Training);
        public static int HospitalCapacityBonus => Level(Facility.Hospital);
        public static float HospitalSpeedMultiplier => 1f + 0.25f * Level(Facility.Hospital);
        public static int HotSpringCapacityBonus => Level(Facility.HotSpring);
        public static float HotSpringReliefMultiplier => 1f + 0.25f * Level(Facility.HotSpring);
        public static int DormitoryBonus => Level(Facility.Dormitory);
        public static int StorageBonus => 5 * Level(Facility.Storage);

        public static string Name(Facility f) => f switch
        {
            Facility.Training => "훈련소",
            Facility.Hospital => "병원",
            Facility.HotSpring => "온천",
            Facility.Dormitory => "숙소",
            _ => "창고",
        };

        /// <summary>그 단계의 효과(0단계는 기본).</summary>
        public static string Effect(Facility f, int level) => f switch
        {
            Facility.Training => $"정원 {TrainingHall.BaseCapacity + level}명 · 훈련 성장 ×{1f + 0.2f * level:0.0#}",
            Facility.Hospital => $"정원 {Hospital.BaseCapacity + level}명 · 치료 속도 ×{1f + 0.25f * level:0.0#}",
            Facility.HotSpring => $"정원 {ConditionTuning.HotSpringCapacity + level}명 · 피로 회복 ×{1f + 0.25f * level:0.0#}",
            Facility.Dormitory => $"파티 정원 +{level}명",
            _ => $"창고 +{5 * level}칸",
        };

        // ---------- 비용 ----------

        /// <summary>다음 단계(1~3) 비용: 1단계 300G, 2단계 800G + 보랏빛 마정석 2, 3단계 2000G + 황금빛 마정석 1 + 초록빛 마정석 2.</summary>
        public static (int gold, (int color, int count)[] stones) CostFor(int nextLevel) => nextLevel switch
        {
            1 => (300, new (int, int)[0]),
            2 => (800, new[] { (1, 2) }),
            _ => (2000, new[] { (3, 1), (2, 2) }),
        };

        /// <summary>"800G · 보랏빛 마정석 1/2" (보유/필요)</summary>
        public static string CostText(int nextLevel)
        {
            var (gold, stones) = CostFor(nextLevel);
            var parts = new List<string> { $"{gold}G" };
            parts.AddRange(stones.Select(s => $"{LootCatalog.ManaColorNames[s.color]} 마정석 {LootBag.CountManaColor(s.color)}/{s.count}"));
            return string.Join(" · ", parts);
        }

        /// <summary>올릴 수 없으면 이유, 되면 null.</summary>
        public static string Block(Facility f)
        {
            int next = Level(f) + 1;
            if (next > MaxLevel) return "이미 최고 단계입니다.";
            var (gold, stones) = CostFor(next);
            if (Wallet.Gold < gold) return $"금화 {gold}G가 필요합니다.";
            foreach (var (color, count) in stones)
                if (LootBag.CountManaColor(color) < count)
                    return $"{LootCatalog.ManaColorNames[color]} 마정석이 {count}개 필요합니다 (보유 {LootBag.CountManaColor(color)}).";
            return null;
        }

        public static bool TryUpgrade(Facility f)
        {
            if (Block(f) != null) return false;
            int next = Level(f) + 1;
            var (gold, stones) = CostFor(next);
            if (!Wallet.TrySpend(gold)) return false;
            foreach (var (color, count) in stones) LootBag.ConsumeManaColor(color, count);
            _levels[f] = next;
            if (f == Facility.Dormitory) Guild.ApplyPartySize();
            OnChanged?.Invoke();
            return true;
        }

        // ---------- 저장 ----------

        /// <summary>"훈련소|2" 꼴(0단계는 뺀다).</summary>
        public static List<string> SaveEntries() => // 꺼져 있어도 예전에 올린 단계는 그대로 저장한다
            _levels.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}|{kv.Value}").ToList();

        public static void Restore(IEnumerable<string> entries)
        {
            _levels.Clear();
            if (entries != null)
                foreach (var e in entries)
                {
                    int bar = e.IndexOf('|');
                    if (bar > 0 && Enum.TryParse(e.Substring(0, bar), out Facility f) && int.TryParse(e.Substring(bar + 1), out int level))
                        _levels[f] = Mathf.Clamp(level, 0, MaxLevel);
                }
            Guild.ApplyPartySize();
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _levels.Clear();
            OnChanged = null;
        }
    }
}
