using System.Collections.Generic;
using System.Linq;
using GN3.Combat;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 부상·질병이 생기고, 옮고, 저절로 낫고, 매일 효과를 내는 규칙. 종류별 값은 전부 AilmentSO 에셋(Resources/Ailments)에서 읽는다.
    /// - 발생: 전투·습격에서 살아남았을 때 / 파견 중 매일 / 마을에서 매일, 그 트리거를 가진 에셋을 중한 것 먼저 하나씩 굴려
    ///   갈래(부상·질병)마다 처음 걸린 하나만 건다. 한 사람은 부상 하나·질병 하나까지.
    /// - 전염: 매일 자정, 입원하지 않은 마을 환자가 입원하지 않은 마을 동료에게 contagionPerDay 확률로 옮긴다.
    /// - 매일 효과: 사기·체력 감소, 입원하지 않은 날 수 세기(deathAfterDays를 넘으면 사망).
    /// - 자연 회복: 마을에 있고 입원하지 않았으면 시간당 1% × naturalSpeed. 파견 중엔 낫지 않는다.
    /// </summary>
    public static class Ailments
    {
        public const float NaturalProgressPerHour = 0.01f;

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다(InnRest와 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe()
        {
            GameClock.OnDayAdvanced += HandleNewDay;
            GameClock.OnHoursPassed += RecoverNaturally;
        }

        /// <summary>전투·습격에서 살아남은 사람에게 부상·질병을 굴린다(MercenaryCondition.AfterBattle/AfterAmbush가 부른다).</summary>
        public static void AfterFight(IEnumerable<Mercenary> survivors, AilmentTrigger trigger)
        {
            foreach (var merc in survivors)
            {
                if (merc == null || !merc.IsAlive) continue;
                float ratio = merc.CurrentHealth / (float)Mathf.Max(1, merc.CurrentStats.MaxHealth);
                RollAll(merc, trigger, def => FightChance(def, ratio), "");
            }
        }

        /// <summary>전투·습격 뒤 남은 체력 비율이 healthRatio일 때 이 에셋에 걸릴 확률(편집 창 미리보기도 쓴다).</summary>
        public static float FightChance(AilmentSO def, float healthRatio)
        {
            if (healthRatio >= def.healthBelow && def.healthBelow < 1f) return 0f;
            if (!def.scaleWithMissingHealth || def.healthBelow <= 0f) return def.chance;
            return def.chance * Mathf.Clamp01((def.healthBelow - healthRatio) / def.healthBelow);
        }

        /// <summary>매일 판정(파견 중·마을)에서 이 에셋에 걸릴 확률(편집 창 미리보기도 쓴다).</summary>
        public static float DailyChance(AilmentSO def, bool rain, bool snow, bool exhausted) =>
            Mathf.Clamp01(def.chance + (rain ? def.rainBonus : 0f) + (snow ? def.snowBonus : 0f) + (exhausted ? def.exhaustedBonus : 0f));

        /// <summary>상태이상을 걸고 알린다. 같은 갈래가 이미 있으면 아무 일도 없다.</summary>
        public static bool Inflict(Mercenary merc, AilmentSO def, string verb)
        {
            if (!merc.AddAilment(def)) return false;
            string line = $"{merc.Name}이(가) {verb}: {def.displayName}"; // 효과는 마우스를 올리면 툴팁으로(EffectTooltip)
            ToastLog.Show(line);
            DailyLog.Add(line);
            return true;
        }

        /// <summary>
        /// 마우스를 올렸을 때 보여 줄 능력치 하락 툴팁. 앓는 게 없으면 null.
        /// "부상·질병으로 떨어진 능력치 / 공격 24 → 18 · 방어 10 → 8 / 골절 45%: 공격 -25% · …" (바뀐 능력치만 적는다)
        /// </summary>
        public static string EffectTooltip(Mercenary merc)
        {
            if (merc == null || !merc.HasAilment) return null;
            var lines = new List<string> { "부상·질병으로 떨어진 능력치", StatDrop(merc.StatsWithoutAilments, merc.CurrentStats) };
            foreach (var ailment in merc.Ailments) lines.Add(AilmentLine(ailment));
            return string.Join("\n", lines);
        }

        /// <summary>상태이상 한 건만의 툴팁: 그 한 건이 깎은 능력치(다른 상태이상은 그대로 둔 채로 비교).</summary>
        public static string EffectTooltip(Mercenary merc, Ailment ailment)
        {
            if (merc == null || ailment == null) return null;
            var others = merc.Ailments.Where(a => a != ailment).ToList();
            var before = merc.ComputeStats(others);
            var after = merc.ComputeStats(others.Append(ailment));
            return $"{AilmentLine(ailment)}\n{StatDrop(before, after)}";
        }

        private static string AilmentLine(Ailment ailment) =>
            $"{ailment.Def.displayName} {Mathf.RoundToInt(ailment.Progress * 100f)}%: {ailment.Def.EffectText()}";

        private static string StatDrop(CombatStats before, CombatStats after)
        {
            var parts = new List<string>();
            if (after.Attack != before.Attack) parts.Add($"공격 {before.Attack} → {after.Attack}");
            if (after.Defense != before.Defense) parts.Add($"방어 {before.Defense} → {after.Defense}");
            if (after.MoveSpeed != before.MoveSpeed) parts.Add($"속도 {before.MoveSpeed} → {after.MoveSpeed}");
            return parts.Count > 0 ? string.Join(" · ", parts) : "능력치 변화 없음";
        }

        /// <summary>진행도를 더하고, 다 나으면 지우고 알린다. 나았으면 true.</summary>
        public static bool Heal(Mercenary merc, Ailment ailment, float amount, string place)
        {
            ailment.Progress += amount;
            if (ailment.Progress < 1f) return false;
            merc.RemoveAilment(ailment);
            string line = $"{merc.Name}의 {ailment.Def.displayName}이(가) {place} 나았다.";
            ToastLog.Show(line);
            DailyLog.Add(line);
            return true;
        }

        /// <summary>그 트리거를 가진 에셋을 순서대로 굴려, 아직 비어 있는 갈래마다 처음 걸린 하나만 건다. where = "파견 중 " 같은 알림 앞말.</summary>
        private static void RollAll(Mercenary merc, AilmentTrigger trigger, System.Func<AilmentSO, float> chanceOf, string where)
        {
            foreach (var def in AilmentCatalog.All)
            {
                if (!def.Has(trigger)) continue;
                if (def.IsInjury ? merc.HasInjury : merc.HasIllness) continue;
                if (Random.value < chanceOf(def)) Inflict(merc, def, where + (def.IsInjury ? "다쳤다" : "병에 걸렸다"));
            }
        }

        private static bool InVillage(Mercenary merc) =>
            merc.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(merc);

        private static void HandleNewDay()
        {
            var members = PlayerParty.Instance.Members.Where(m => m.IsAlive).ToList();
            var weather = Weather.Today.Kind;
            bool rain = Weather.IsRain(weather), snow = Weather.IsSnow(weather);

            // 오늘 앓고 있던 사람만 매일 효과를 받는다(오늘 새로 걸린 사람은 내일부터).
            ApplyDailyEffects(members);

            var free = members.Where(m => m.IsAlive && InVillage(m) && !Hospital.IsAdmitted(m)).ToList();
            var carriers = free.Where(m => m.HasIllness).ToList();

            foreach (var merc in members.Where(m => m.IsAlive))
            {
                if (ExpeditionLog.Instance.IsOnExpedition(merc))
                    RollAll(merc, AilmentTrigger.ExpeditionDaily, def => DailyChance(def, rain, snow, merc.IsExhausted), "파견 중 ");
                else if (!Hospital.IsAdmitted(merc))
                    RollAll(merc, AilmentTrigger.VillageDaily, def => DailyChance(def, rain, snow, merc.IsExhausted), "마을에서 ");
            }

            // 마을 전염: 오늘 새로 걸린 사람은 내일부터 옮긴다.
            foreach (var carrier in carriers)
            {
                var illness = carrier.Ailments.FirstOrDefault(a => !a.Def.IsInjury);
                if (illness == null || illness.Def.contagionPerDay <= 0f) continue;
                foreach (var other in free)
                {
                    if (other == carrier || other.HasIllness || carriers.Contains(other)) continue;
                    if (Random.value >= illness.Def.contagionPerDay) continue;
                    Inflict(other, illness.Def, $"{carrier.Name}에게서 병이 옮았다");
                }
            }
        }

        /// <summary>사기·체력 감소와, 입원하지 않은 날 수(넘으면 사망). 죽은 사람은 파티에서 빠진다.</summary>
        private static void ApplyDailyEffects(List<Mercenary> members)
        {
            foreach (var merc in members.Where(m => m.HasAilment).ToList())
            {
                bool admitted = Hospital.IsAdmitted(merc);
                AilmentSO fatal = null;
                foreach (var ailment in merc.Ailments)
                {
                    var def = ailment.Def;
                    if (def.moraleLossPerDay > 0) merc.AddMorale(-def.moraleLossPerDay);
                    if (def.healthLossPerDay > 0f)
                        merc.SetHealth(Mathf.Max(1, merc.CurrentHealth - Mathf.CeilToInt(merc.CurrentStats.MaxHealth * def.healthLossPerDay)));
                    if (!admitted) ailment.DaysUntreated++;
                    if (def.deathAfterDays > 0 && ailment.DaysUntreated >= def.deathAfterDays) fatal ??= def;
                }
                if (fatal != null) Die(merc, fatal);
            }
        }

        private static void Die(Mercenary merc, AilmentSO cause)
        {
            merc.SetHealth(0);
            PlayerParty.Instance.Remove(merc);
            string line = $"{merc.Name}이(가) {cause.displayName}을(를) 치료받지 못해 세상을 떠났다.";
            ToastLog.Show(line, false);
            DailyLog.Add(line);
            Mailbox.Post(MailKind.Alert, "용병 사망", line, important: true);
        }

        private static void RecoverNaturally(float hours)
        {
            foreach (var merc in PlayerParty.Instance.Members.Where(m => InVillage(m) && m.HasAilment && !Hospital.IsAdmitted(m)).ToList())
                foreach (var ailment in merc.Ailments.ToList())
                {
                    if (ailment.Def.noNaturalRecovery || ailment.Def.naturalSpeed <= 0f) continue;
                    Heal(merc, ailment, hours * NaturalProgressPerHour * ailment.Def.naturalSpeed, "저절로");
                }
        }
    }
}
