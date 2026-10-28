using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 용병 피로·사기와 주급. 파견 중에는 게임 시간이 흐르는 대로(GameClock.OnHoursPassed) 하루 +8(귀환길 +6)을 시간에 나눠 붙인다.
    /// 매일 자정(GameClock.OnDayAdvanced): 마을에서 잤으면 피로 -20. 지친(피로 80↑) 용병은 사기 -5.
    /// 파견 없이 마을에서 2일을 넘기면(입원 중 제외) 하루 사기 -2(지루함). 모든 용병은 자정마다 사기가 60 쪽으로 1씩 끌린다. 수치는 ConditionTuning.
    /// 7일마다(8·15·22…일차) 주급(고용비의 20%)을 파티 순서대로 낸다. 받으면 사기 +5, 골드가 모자라 못 받으면 -30.
    /// 마을에 있는 사기 0 용병은 길드를 떠난다. 전투·습격·훈련의 피로·사기 변화도 여기 함수로 처리한다.
    /// 능력치 영향(피로 50↑ -10%, 80↑ -25%, 사기 30↓ -10%)은 Mercenary.ConditionMultiplier.
    /// </summary>
    public static class MercenaryCondition
    {
        // 수치는 ConditionTuning
        public const float WageRatio = ConditionTuning.WageRatio;
        public const int PayIntervalDays = ConditionTuning.PayIntervalDays;

        private const int ExpeditionFatiguePerDay = ConditionTuning.ExpeditionFatiguePerDay;
        private const int ReturnFatiguePerDay = ConditionTuning.ReturnFatiguePerDay;
        private const int RestFatiguePerNight = ConditionTuning.RestFatiguePerNight;
        private const int ExhaustedMoralePerDay = ConditionTuning.ExhaustedMoralePerDay;
        private const int BattleFatigue = ConditionTuning.BattleFatigue;
        private const int AmbushFatigue = ConditionTuning.AmbushFatigue;
        private const int VictoryMorale = ConditionTuning.VictoryMorale;
        private const int DefeatMorale = ConditionTuning.DefeatMorale;
        private const int FallenComradeMorale = ConditionTuning.FallenComradeMorale;
        private const int PaidMorale = ConditionTuning.PaidMorale;
        private const int UnpaidMorale = ConditionTuning.UnpaidMorale;
        public const int TrainingFatiguePerHour = ConditionTuning.TrainingFatiguePerHour;

        // 파견 없이 마을에서 보낸 날(용병 id별). 저장하지 않으므로 불러오면 0부터 다시 센다.
        private static readonly Dictionary<string, int> IdleDays = new Dictionary<string, int>();
        // 파견 피로의 소수점(실시간은 프레임마다 아주 조금씩 흘러서 쌓았다가 정수만 더한다, TrainingHall과 같은 방식).
        private static readonly Dictionary<string, float> PendingTravelFatigue = new Dictionary<string, float>();
        // 파견 중에 길드를 떠나기로 결심한 용병 id → 떠나는 이유. 마을로 돌아온 뒤 자정(Desert)에 떠난다. 저장한다(SaveData.leaving).
        private static readonly Dictionary<string, string> LeavingIds = new Dictionary<string, string>();
        private static readonly System.Random LeaveRng = new System.Random();

        /// <summary>불러오기: 저장하지 않는 셈(한가한 날·파견 피로 소수점)을 0부터 다시 센다(떠날 용병은 저장에서 따로 복원).</summary>
        public static void ResetTransient()
        {
            IdleDays.Clear();
            PendingTravelFatigue.Clear();
        }

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다(InnRest와 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe()
        {
            IdleDays.Clear();
            PendingTravelFatigue.Clear();
            LeavingIds.Clear();
            GameClock.OnDayAdvanced += HandleNewDay;
            GameClock.OnHoursPassed += HandleHoursPassed;
        }

        /// <summary>파견 중인 용병(가는 길·도착 대기·귀환길)에게 흐른 시간만큼 파견 피로를 붙인다.</summary>
        private static void HandleHoursPassed(float hours)
        {
            foreach (var merc in PlayerParty.Instance.Members)
            {
                if (!merc.IsAlive || !ExpeditionLog.Instance.IsOnExpedition(merc)) continue;
                int perDay = ExpeditionLog.Instance.IsReturning(merc) ? ReturnFatiguePerDay : ExpeditionFatiguePerDay;
                float pending = (PendingTravelFatigue.TryGetValue(merc.Id, out float p) ? p : 0f) + hours * perDay / 24f;
                int whole = Mathf.FloorToInt(pending);
                PendingTravelFatigue[merc.Id] = pending - whole;
                if (whole > 0) merc.AddFatigue(whole);
            }
        }

        /// <summary>이 용병의 주급: 계약서 금액(계약이 없던 예전 저장은 고용비의 20%로 기본 계약을 만든다).</summary>
        public static int WeeklyWage(Mercenary merc) => Contracts.Ensure(merc).WeeklyWage;

        public static int TotalWeeklyWage() => PlayerParty.Instance.Members.Where(m => m.IsAlive).Sum(WeeklyWage);

        /// <summary>다음 주급날(일차). 8, 15, 22… 일차 자정에 낸다.</summary>
        public static int NextPayday
        {
            get
            {
                int day = GameClock.CurrentDay;
                int passed = (day - 1) / PayIntervalDays;
                return 1 + (passed + 1) * PayIntervalDays;
            }
        }

        private static void HandleNewDay()
        {
            var members = PlayerParty.Instance.Members.Where(m => m.IsAlive).ToList();
            foreach (var merc in members)
            {
                if (ExpeditionLog.Instance.IsOnExpedition(merc)) IdleDays[merc.Id] = 0; // 파견 피로는 HandleHoursPassed
                else
                {
                    merc.AddFatigue(-RestFatiguePerNight);
                    if (!Hospital.IsAdmitted(merc)) Idle(merc);
                }
                if (merc.IsExhausted) merc.AddMorale(-ExhaustedMoralePerDay);
                Drift(merc);
                // 아름다운 마을(장식 매력 단계)에서 지내면 사기가 조금씩 오른다 — 파견 중이면 없음
                if (!ExpeditionLog.Instance.IsOnExpedition(merc) && VillageCharm.MoralePerDay > 0) merc.AddMorale(VillageCharm.MoralePerDay);
            }

            int day = GameClock.CurrentDay;
            if (day > 1 && (day - 1) % PayIntervalDays == 0) PayWages(members);

            Contracts.DailyCheck(); // 계약 만료 알림·만료(마을에 있으면 바로, 파견 중이면 돌아온 뒤 떠난다)
            Desert();
        }

        /// <summary>사기 끌림: 기준값(MoraleBaseline)보다 높으면 조금 내리고 낮으면 조금 올린다(MoraleDriftPerDay가 0이면 끔).</summary>
        private static void Drift(Mercenary merc)
        {
            int drift = ConditionTuning.MoraleDriftPerDay;
            if (drift <= 0 || merc.Morale == ConditionTuning.MoraleBaseline) return;
            int gap = ConditionTuning.MoraleBaseline - merc.Morale;
            merc.AddMorale(System.Math.Sign(gap) * System.Math.Min(drift, System.Math.Abs(gap)));
        }

        /// <summary>마을에서 하루를 보냈다. 쉬는 날이 IdleGraceDays를 넘으면 사기가 조금씩 준다.</summary>
        private static void Idle(Mercenary merc)
        {
            int days = (IdleDays.TryGetValue(merc.Id, out int d) ? d : 0) + 1;
            IdleDays[merc.Id] = days;
            if (days > ConditionTuning.IdleGraceDays) merc.AddMorale(-ConditionTuning.IdleMoralePerDay);
        }

        private static void PayWages(List<Mercenary> members)
        {
            if (members.Count == 0) return;
            int paid = 0, spent = 0;
            var unpaid = new List<string>();
            var unpaidMercs = new List<Mercenary>();
            foreach (var merc in members)
            {
                int wage = WeeklyWage(merc);
                if (Wallet.TrySpend(wage, "임금"))
                {
                    paid++;
                    spent += wage;
                    merc.AddMorale(PaidMorale);
                }
                else
                {
                    unpaid.Add(merc.Name);
                    unpaidMercs.Add(merc);
                    merc.AddMorale(-UnpaidMorale);
                }
            }

            string line = $"주급 지급: {paid}명 -{spent}G";
            if (unpaid.Count > 0) line += $" (골드 부족으로 {unpaid.Count}명 미지급: {string.Join(", ", unpaid)} · 사기 -{UnpaidMorale})";
            DailyLog.Add(line);
            if (unpaid.Count == 0)
            {
                ToastLog.Show(line);
                return;
            }
            // 미지급은 중요: 게임을 멈추고 알림창
            ToastLog.Show(line, false);
            Mailbox.Post(MailKind.Alert, "주급을 다 주지 못했습니다",
                $"골드가 부족해 {unpaid.Count}명에게 주급을 주지 못했습니다.\n미지급: {string.Join(", ", unpaid)}\n사기 -{UnpaidMorale}\n\n" +
                $"지급: {paid}명 -{spent}G", important: true);
            // 미지급 알림 뒤에: 사기가 꺾인 사람은 떠날 수 있다.
            foreach (var merc in unpaidMercs) ConsiderLeaving(merc, "주급을 받지 못해");
        }

        /// <summary>
        /// 자정: 마을에 있는 사기 0 용병, 그리고 파견 중에 떠나기로 결심했다가 돌아온 용병은 길드를 떠난다
        /// (파견 중이면 돌아온 뒤 다음 자정에).
        /// </summary>
        private static void Desert()
        {
            var leaving = PlayerParty.Instance.Members
                .Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m) && (m.Morale <= 0 || LeavingIds.ContainsKey(m.Id))).ToList();
            foreach (var merc in leaving)
                Leave(merc, LeavingIds.TryGetValue(merc.Id, out string reason) ? reason : "사기가 바닥나");
            // 죽었거나 해고돼 파티에 없는 사람의 결심은 지운다.
            foreach (var id in LeavingIds.Keys.Where(id => PlayerParty.Instance.Members.All(m => m.Id != id)).ToList())
                LeavingIds.Remove(id);
        }

        /// <summary>
        /// 친한 동료의 죽음·주급 미지급으로 사기가 낮은 사기 기준(30) 아래로 떨어졌을 때 떠날지 굴린다(ConditionTuning.LeaveChance — 낮을수록 높음).
        /// 마을에 있으면 바로 떠나고, 파견 중이면 결심만 해 두었다가 돌아온 뒤 자정에 떠난다. reason은 "주급을 받지 못해"처럼 "길드를 떠났다" 앞에 붙는 말.
        /// </summary>
        private static void ConsiderLeaving(Mercenary merc, string reason)
        {
            if (merc == null || !merc.IsAlive || LeavingIds.ContainsKey(merc.Id)) return;
            if (LeaveRng.NextDouble() >= ConditionTuning.LeaveChance(merc.Morale)) return;
            if (!ExpeditionLog.Instance.IsOnExpedition(merc))
            {
                Leave(merc, reason);
                return;
            }
            LeavingIds[merc.Id] = reason;
            DailyLog.Add($"{Josa.IGa(merc.Name)} {reason} 돌아가면 길드를 떠나겠다고 한다.");
        }

        /// <summary>
        /// 계약이 끝났다(Contracts.DailyCheck): 마을에 있으면 바로 떠나고, 파견 중이면 돌아온 뒤 자정에 떠난다.
        /// 이미 떠나기로 한 사람은 그 이유를 그대로 둔다.
        /// </summary>
        public static void EndContract(Mercenary merc)
        {
            if (merc == null || !merc.IsAlive) return;
            const string reason = "계약이 끝나";
            if (!ExpeditionLog.Instance.IsOnExpedition(merc))
            {
                Leave(merc, LeavingIds.TryGetValue(merc.Id, out string r) ? r : reason);
                return;
            }
            if (LeavingIds.ContainsKey(merc.Id)) return;
            LeavingIds[merc.Id] = reason;
            DailyLog.Add($"{Josa.IGa(merc.Name)} 계약이 끝나 돌아가면 길드를 떠난다.");
        }

        /// <summary>재계약했다: 계약이 끝나 떠나려던 결심을 거둔다(다른 이유로 떠나려는 사람은 그대로).</summary>
        public static void CancelContractLeaving(Mercenary merc)
        {
            if (merc != null && LeavingIds.TryGetValue(merc.Id, out string reason) && reason == "계약이 끝나") LeavingIds.Remove(merc.Id);
        }

        /// <summary>길드를 떠난다: 파티에서 빼고 중요 알림(게임을 멈추고 알림창, 시간 넘기기도 여기서 멈춘다).</summary>
        private static void Leave(Mercenary merc, string reason)
        {
            if (Loans.TryReturnLeaving(merc)) { LeavingIds.Remove(merc.Id); return; } // 빌려 온 손님은 원래 길드로 돌아간다
            LeavingIds.Remove(merc.Id);
            PlayerParty.Instance.Remove(merc);
            Contracts.Remove(merc);
            GN3.Economy.RivalGuilds.OnPlayerReleased(merc); // 근처 길드가 데려갈 수 있다
            string line = $"{Josa.IGa(merc.Name)} {reason} 길드를 떠났다.";
            ToastLog.Show(line, false);
            DailyLog.Add(line);
            Mailbox.Post(MailKind.Alert, "용병 이탈", line, important: true);
        }

        /// <summary>파견 중에 떠나기로 결심했는지(정보창 표시용).</summary>
        public static bool IsLeaving(Mercenary merc) => merc != null && LeavingIds.ContainsKey(merc.Id);

        /// <summary>저장용: 떠나기로 결심한 용병 id와 이유("id|이유").</summary>
        public static List<string> SaveLeaving() => LeavingIds.Select(p => p.Key + "|" + p.Value).ToList();

        /// <summary>저장 파일에서 불러올 때(파티 복원 뒤).</summary>
        public static void RestoreLeaving(IEnumerable<string> entries)
        {
            LeavingIds.Clear();
            if (entries == null) return;
            foreach (var entry in entries)
            {
                int bar = entry.IndexOf('|');
                string id = bar >= 0 ? entry.Substring(0, bar) : entry;
                if (PlayerParty.Instance.Members.Any(m => m.Id == id)) LeavingIds[id] = bar >= 0 ? entry.Substring(bar + 1) : "사기가 꺾여";
            }
        }

        /// <summary>마을에서 병 등으로 죽은 용병: 파티에 남은 동료가 관계 단계만큼 슬퍼하고(떠날 수도 있다). 파티에서 빼기 전에 부른다(친밀도를 읽는다).</summary>
        public static void AfterDeath(Mercenary dead)
        {
            var others = PlayerParty.Instance.Members.Where(m => m != dead && m.IsAlive).ToList();
            MournFriends(others, new[] { dead });
        }

        private const int MaxAffinityBattleMorale = ConditionTuning.MaxAffinityBattleMorale; // 동료 관계로 오르내리는 사기 한도(±)

        /// <summary>
        /// 파견 전투가 끝났을 때(살아남은 사람만): 피로 +20, 승리 사기 +5 / 패배 -25, 전사자 1명마다 -10.
        /// 친밀도 보정: 같이 싸운 동료(전사자 포함)마다 관계 단계의 사기(앙숙 -3 ~ 절친 +3)를 더해 ±9로 자르고,
        /// 전사자와의 관계 단계에 따라 추가로 잃는다(지인 -3 · 친구 -10 · 절친 -15).
        /// </summary>
        public static void AfterBattle(IReadOnlyList<Mercenary> survivors, bool victory, IReadOnlyList<Mercenary> fallen)
        {
            var fighters = survivors.Concat(fallen).ToList();
            foreach (var merc in survivors)
            {
                merc.AddFatigue(BattleFatigue);
                merc.AddMorale((victory ? VictoryMorale : -DefeatMorale) - FallenComradeMorale * fallen.Count);
                int bonus = 0;
                foreach (var other in fighters)
                {
                    if (other == merc) continue;
                    var tier = Affinity.TierBetween(merc, other);
                    bonus += tier.BattleMorale;
                    if (tier == Affinity.Tiers[0]) DailyLog.Add($"{Josa.EunNeun(merc.Name)} 앙숙 {Josa.WaGwa(other.Name)} 같이 싸우는 게 불편했다.");
                }
                merc.AddMorale(Mathf.Clamp(bonus, -MaxAffinityBattleMorale, MaxAffinityBattleMorale));
            }
            MournFriends(survivors, fallen);
            Ailments.AfterFight(survivors, AilmentTrigger.AfterBattle); // 체력이 낮게 남은 사람은 다칠 수 있다(부상·질병 에셋의 발생 조건)
        }

        /// <summary>이동 중 습격에서 살아남았을 때: 피로 +10, 전사자 1명마다 사기 -10(전사자가 친구·절친이면 추가로 더).</summary>
        public static void AfterAmbush(IReadOnlyList<Mercenary> survivors, IReadOnlyList<Mercenary> fallen)
        {
            foreach (var merc in survivors)
            {
                merc.AddFatigue(AmbushFatigue);
                if (fallen.Count > 0) merc.AddMorale(-FallenComradeMorale * fallen.Count);
            }
            MournFriends(survivors, fallen);
            Ailments.AfterFight(survivors, AilmentTrigger.AfterAmbush);
        }

        /// <summary>지인 이상을 잃은 사람은 관계 단계만큼 사기가 더 떨어진다. 친밀도는 전사자가 파티에서 빠지기 전 값으로 본다.</summary>
        private static void MournFriends(IReadOnlyList<Mercenary> survivors, IReadOnlyList<Mercenary> fallen)
        {
            foreach (var merc in survivors)
                foreach (var dead in fallen)
                {
                    var tier = Affinity.TierBetween(merc, dead);
                    if (tier.MournMorale == 0) continue;
                    merc.AddMorale(tier.MournMorale);
                    DailyLog.Add($"{Josa.IGa(merc.Name)} {tier.Name} {Josa.EulReul(dead.Name)} 잃은 슬픔에 잠겼다. (사기 {tier.MournMorale})");
                    ConsiderLeaving(merc, $"{tier.Name} {Josa.EulReul(dead.Name)} 잃은 슬픔에");
                }
        }

        /// <summary>UI용: "피로 34 · 사기 70" + 능력치 감소가 있으면 "(공격·방어 -10%)".</summary>
        public static string Describe(Mercenary merc)
        {
            string text = $"피로 {merc.Fatigue} · 사기 {merc.Morale}";
            float m = merc.ConditionMultiplier;
            if (m < 1f) text += $" (공격·방어 -{Mathf.RoundToInt((1f - m) * 100f)}%)";
            if (merc.IsExhausted) text += " · 지쳐서 파견 불가";
            if (merc.HasAilment) text += $" · {merc.AilmentSummary()}" + (merc.IsSeverelyAiling ? " (파견 불가)" : "");
            if (IsLeaving(merc)) text += " · 귀환하면 떠날 생각";
            return text;
        }
    }
}
