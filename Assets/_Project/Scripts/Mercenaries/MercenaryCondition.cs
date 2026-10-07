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
    /// 용병 피로·사기와 주급. 매일 자정(GameClock.OnDayAdvanced):
    /// 파견 중이면 피로 +12, 마을에서 잤으면 -25. 지친(피로 80↑) 용병은 사기 -5.
    /// 7일마다(8·15·22…일차) 주급(고용비의 20%)을 파티 순서대로 낸다. 받으면 사기 +5, 골드가 모자라 못 받으면 -30.
    /// 마을에 있는 사기 0 용병은 길드를 떠난다. 전투·습격·훈련의 피로·사기 변화도 여기 함수로 처리한다.
    /// 능력치 영향(피로 50↑ -10%, 80↑ -25%, 사기 30↓ -10%)은 Mercenary.ConditionMultiplier.
    /// </summary>
    public static class MercenaryCondition
    {
        public const float WageRatio = 0.2f;
        public const int PayIntervalDays = 7;

        private const int ExpeditionFatiguePerDay = 12;
        private const int RestFatiguePerNight = 25;
        private const int ExhaustedMoralePerDay = 5;
        private const int BattleFatigue = 20;
        private const int AmbushFatigue = 10;
        private const int VictoryMorale = 10;
        private const int DefeatMorale = 15;
        private const int FallenComradeMorale = 10;
        private const int PaidMorale = 5;
        private const int UnpaidMorale = 30;
        public const int TrainingFatiguePerHour = 2;

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다(InnRest와 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnDayAdvanced += HandleNewDay;

        /// <summary>이 용병의 주급(고용비의 20%, 최소 1G).</summary>
        public static int WeeklyWage(Mercenary merc) => Mathf.Max(1, Mathf.RoundToInt(Pricing.HireCost(merc) * WageRatio));

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
                if (ExpeditionLog.Instance.IsOnExpedition(merc)) merc.AddFatigue(ExpeditionFatiguePerDay);
                else merc.AddFatigue(-RestFatiguePerNight);
                if (merc.IsExhausted) merc.AddMorale(-ExhaustedMoralePerDay);
            }

            int day = GameClock.CurrentDay;
            if (day > 1 && (day - 1) % PayIntervalDays == 0) PayWages(members);

            Desert();
        }

        private static void PayWages(List<Mercenary> members)
        {
            if (members.Count == 0) return;
            int paid = 0, spent = 0;
            var unpaid = new List<string>();
            foreach (var merc in members)
            {
                int wage = WeeklyWage(merc);
                if (Wallet.TrySpend(wage))
                {
                    paid++;
                    spent += wage;
                    merc.AddMorale(PaidMorale);
                }
                else
                {
                    unpaid.Add(merc.Name);
                    merc.AddMorale(-UnpaidMorale);
                }
            }

            string line = $"주급 지급: {paid}명 -{spent}G";
            if (unpaid.Count > 0) line += $" (골드 부족으로 {unpaid.Count}명 미지급: {string.Join(", ", unpaid)} · 사기 -{UnpaidMorale})";
            ToastLog.Show(line);
            DailyLog.Add(line);
        }

        /// <summary>마을에 있는 사기 0 용병은 길드를 떠난다(파견 중이면 돌아온 뒤 다음 자정에).</summary>
        private static void Desert()
        {
            var leaving = PlayerParty.Instance.Members
                .Where(m => m.IsAlive && m.Morale <= 0 && !ExpeditionLog.Instance.IsOnExpedition(m)).ToList();
            foreach (var merc in leaving)
            {
                PlayerParty.Instance.Remove(merc);
                string line = $"{merc.Name}이(가) 사기가 바닥나 길드를 떠났다.";
                ToastLog.Show(line);
                DailyLog.Add(line);
            }
        }

        /// <summary>파견 전투가 끝났을 때(살아남은 사람만): 피로 +20, 승리 사기 +10 / 패배 -15, 전사자 1명마다 -10.</summary>
        public static void AfterBattle(IEnumerable<Mercenary> survivors, bool victory, int fallenCount)
        {
            foreach (var merc in survivors)
            {
                merc.AddFatigue(BattleFatigue);
                merc.AddMorale((victory ? VictoryMorale : -DefeatMorale) - FallenComradeMorale * fallenCount);
            }
        }

        /// <summary>이동 중 습격에서 살아남았을 때: 피로 +10, 전사자 1명마다 사기 -10.</summary>
        public static void AfterAmbush(IEnumerable<Mercenary> survivors, int fallenCount)
        {
            foreach (var merc in survivors)
            {
                merc.AddFatigue(AmbushFatigue);
                if (fallenCount > 0) merc.AddMorale(-FallenComradeMorale * fallenCount);
            }
        }

        /// <summary>UI용: "피로 34 · 사기 70" + 능력치 감소가 있으면 "(공격·방어 -10%)".</summary>
        public static string Describe(Mercenary merc)
        {
            string text = $"피로 {merc.Fatigue} · 사기 {merc.Morale}";
            float m = merc.ConditionMultiplier;
            if (m < 1f) text += $" (공격·방어 -{Mathf.RoundToInt((1f - m) * 100f)}%)";
            if (merc.IsExhausted) text += " · 지쳐서 파견 불가";
            return text;
        }
    }
}
