using System.Linq;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 해마다 생일(Mercenary.BirthdayIndex, 한 해 360일 중 몇째 날) 자정에 한 살 먹는다.
    /// 45세(GrowthTuning.DeclineStartAge)부터는 생일마다 성장 2회만큼 쇠퇴하고, 55세(RetireAge)가 되면 은퇴한다
    /// (파견 중이면 마을로 돌아온 뒤 자정에). 은퇴는 중요 알림(게임을 멈추고 알림창).
    /// 등급(전투력 구간)이 오르내리면 승급·강등 토스트도 여기서 낸다(Mercenary.GradeChanged).
    /// 매달 1일 자정에는 이번 달 기준값을 다시 찍는다(Mercenary.SnapshotMonth — 정보창 괄호 정산).
    /// </summary>
    public static class MercenaryAge
    {
        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다(InnRest와 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe()
        {
            GameClock.OnDayAdvanced += HandleNewDay;
            // Mercenary.GradeChanged는 Play마다 비워지지 않는 정적 이벤트라 빼고 다시 넣는다(두 번 구독되지 않게).
            Mercenary.GradeChanged -= AnnounceGrade;
            Mercenary.GradeChanged += AnnounceGrade;
        }

        /// <summary>승급·강등 알림(파티에 있는 용병만 — 시장 용병의 처음 경력은 조용히).</summary>
        private static void AnnounceGrade(Mercenary merc, MercenaryGrade from, MercenaryGrade to)
        {
            if (!PlayerParty.Instance.Members.Contains(merc)) return;
            string line = to > from
                ? $"{merc.Name} 승급! {GradeTable.Label(from)} → {GradeTable.Label(to)} (전투력 {merc.GradePower})"
                : $"{merc.Name} 강등… {GradeTable.Label(from)} → {GradeTable.Label(to)} (전투력 {merc.GradePower})";
            ToastLog.Show(line);
            DailyLog.Add(line);
        }

        /// <summary>오늘이 한 해의 몇째 날인지(0~359, 생일과 비교).</summary>
        public static int TodayIndex
        {
            get
            {
                var date = GameCalendar.Today;
                return (date.Month - 1) * GameCalendar.DaysPerMonth + (date.Day - 1);
            }
        }

        /// <summary>생일 표시: "5월 12일".</summary>
        public static string BirthdayText(Mercenary merc) =>
            $"{merc.BirthdayIndex / GameCalendar.DaysPerMonth + 1}월 {merc.BirthdayIndex % GameCalendar.DaysPerMonth + 1}일";

        private static void HandleNewDay()
        {
            // 매달 1일: 정보창 능력치 옆 괄호(이번 달 변화)를 원래 수치에 더해 정산한다. 생일 쇠퇴보다 먼저(그날 쇠퇴는 다음 달 괄호에 보이게).
            if (GameCalendar.Today.Day == 1)
            {
                foreach (var merc in PlayerParty.Instance.Members.Where(m => m.IsAlive)) merc.SnapshotMonth();
                DailyLog.Add($"{GameCalendar.Today.Month}월이 되어 지난달 능력치 변화를 정리했다.");
            }
            int today = TodayIndex;
            foreach (var merc in PlayerParty.Instance.Members.Where(m => m.IsAlive).ToList())
            {
                if (merc.BirthdayIndex == today) Birthday(merc);
                if (merc.Age >= GrowthTuning.RetireAge && !ExpeditionLog.Instance.IsOnExpedition(merc)) Retire(merc);
            }
        }

        private static void Birthday(Mercenary merc)
        {
            merc.GrowOlder();
            DailyLog.Add($"{merc.Name}의 생일 — {merc.Age}세가 되었다. ({GrowthTuning.RateLabel(merc.Age)})");
            // 은퇴 한 해 전: 미리 알린다(다음 생일에 바로 떠나므로 대신할 사람을 키울 시간)
            if (merc.Age == GrowthTuning.RetireAge - 1)
            {
                string notice = $"{Josa.IGa(merc.Name)} {merc.Age}세 — 다음 생일({BirthdayText(merc)})에 {GrowthTuning.RetireAge}세가 되어 은퇴한다.";
                ToastLog.Show(notice, true, ToastKind.Warn);
                DailyLog.Add(notice);
            }
            if (merc.Age < GrowthTuning.DeclineStartAge || merc.Age >= GrowthTuning.RetireAge) return;
            var losses = merc.Decline(GrowthTuning.DeclinePerYear);
            if (losses.Count == 0) return;
            string line = $"{Josa.IGa(merc.Name)} 나이가 들어 몸이 예전 같지 않다 ({string.Join(", ", losses)})";
            ToastLog.Show(line);
            DailyLog.Add(line);
        }

        private static void Retire(Mercenary merc)
        {
            PlayerParty.Instance.Remove(merc);
            string line = $"{Josa.IGa(merc.Name)} {merc.Age}세가 되어 용병 일을 그만두고 은퇴했다.";
            ToastLog.Show(line, false);
            DailyLog.Add(line);
            Mailbox.Post(MailKind.Alert, "용병 은퇴", line, important: true);
        }
    }
}
