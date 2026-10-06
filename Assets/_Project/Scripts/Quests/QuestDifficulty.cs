using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;

namespace GN3.Quests
{
    /// <summary>
    /// 퀘스트 권장 전투력과, 내 전투력과 비교한 체감 난이도(매우 쉬움 ~ 매우 어려움).
    /// 권장 전투력 = 적 수 × 적 한 마리 평균 전투력. EnemySquadGenerator의 평균 능력치
    /// (공 6+2d, 방 2+d, 체 20+8d, 속 4+d)에 용병과 같은 전투력 공식(GradeTable.CombatPower)을 쓰면 58 + 21d.
    /// 전투 계산·퀘스트 생성은 바꾸지 않고 표시·판단 기준만 준다.
    /// </summary>
    public static class QuestDifficulty
    {
        public enum Rating { VeryEasy, Easy, Normal, Hard, VeryHard }

        // 내 전투력 ÷ 권장 전투력이 이 값 이상이면 그 판정(위에서부터).
        private const float VeryEasyRatio = 1.6f;
        private const float EasyRatio = 1.2f;
        private const float NormalRatio = 0.9f;
        private const float HardRatio = 0.65f;

        // 전투력이 넉넉하면 파견 소요 일수를 줄인다(줄이기만 함, 최소 1일).
        private const int VeryEasyDaysSaved = 2;
        private const int EasyDaysSaved = 1;

        private static readonly string[] Labels = { "매우 쉬움", "쉬움", "중간", "어려움", "매우 어려움" };
        private static readonly string[] Hexes = { "6fbf5a", "a8cf5a", "e0c25a", "f08a3c", "e05050" };

        public static int PowerPerEnemy(int difficulty) => 58 + 21 * difficulty;

        // 퀘스트 등급(용병과 같은 F~S): 권장 전투력이 이 값 미만이면 그 등급(F, E, D, C, B, A), 그 이상은 S.
        // D급 Lv1 용병 한 명 ≈ 전투력 100 기준.
        private static readonly int[] GradeUpperBounds = { 130, 180, 240, 310, 390, 480 };

        public static MercenaryGrade Grade(Quest quest)
        {
            int power = RecommendedPower(quest);
            for (int i = 0; i < GradeUpperBounds.Length; i++)
                if (power < GradeUpperBounds[i]) return (MercenaryGrade)i;
            return MercenaryGrade.S;
        }

        /// <summary>rich text용 "[S] 퀘스트 이름"(등급 색).</summary>
        public static string RichTitle(Quest quest) => $"{GradeTable.RichLabel(Grade(quest))} {quest.Title}";

        public static int RecommendedPower(Quest quest) => quest.EnemyCount * PowerPerEnemy(quest.Difficulty);

        public static Rating Rate(int partyPower, Quest quest)
        {
            float ratio = (float)partyPower / System.Math.Max(1, RecommendedPower(quest));
            if (ratio >= VeryEasyRatio) return Rating.VeryEasy;
            if (ratio >= EasyRatio) return Rating.Easy;
            if (ratio >= NormalRatio) return Rating.Normal;
            if (ratio >= HardRatio) return Rating.Hard;
            return Rating.VeryHard;
        }

        public static int DaysSaved(Rating rating) =>
            rating == Rating.VeryEasy ? VeryEasyDaysSaved : rating == Rating.Easy ? EasyDaysSaved : 0;

        /// <summary>이 전투력으로 보내면 걸리는 일수(최소 1일).</summary>
        public static int ShortenedDays(Quest quest, int teamPower) =>
            System.Math.Max(1, quest.DurationDays - DaysSaved(Rate(teamPower, quest)));

        /// <summary>"소요 3일" 또는 "소요 3일 → 2일"</summary>
        public static string DurationText(Quest quest, int teamPower)
        {
            int days = ShortenedDays(quest, teamPower);
            return days < quest.DurationDays ? $"소요 {quest.DurationDays}일 → {days}일" : $"소요 {quest.DurationDays}일";
        }

        public static string Label(Rating rating) => Labels[(int)rating];
        public static string Hex(Rating rating) => Hexes[(int)rating];
        public static string RichLabel(Rating rating) => $"<color=#{Hex(rating)}>[{Label(rating)}]</color>";

        public static int TeamPower(IEnumerable<Mercenary> members) => members.Where(m => m.IsAlive).Sum(m => m.CombatPower);

        /// <summary>지금 보낼 수 있는 용병(살아 있고 파견 중 아님) 중 전투력 높은 순으로 최대 파견 인원만큼 더한 값.</summary>
        public static int BestTeamPower(Quest quest) =>
            PlayerParty.Instance.Members
                .Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m) && !TrainingHall.IsTraining(m) && !m.IsExhausted)
                .Select(m => m.CombatPower)
                .OrderByDescending(p => p)
                .Take(quest.MaxDispatchSize)
                .Sum();

        /// <summary>게시판용: "권장 전투력 237 · 내 최선 412 [쉬움]" (보낼 용병이 없으면 "용병 없음").</summary>
        public static string DescribeForBoard(Quest quest)
        {
            int best = BestTeamPower(quest);
            string mine = best > 0 ? $"내 최선 {best} {RichLabel(Rate(best, quest))}" : "<color=#a3937c>[용병 없음]</color>";
            return $"권장 전투력 {RecommendedPower(quest)} · {mine}";
        }

        /// <summary>편성·파견용: "전투력 210 / 권장 237 → [중간]"</summary>
        public static string DescribeTeam(Quest quest, int teamPower) =>
            $"전투력 {teamPower} / 권장 {RecommendedPower(quest)} → {RichLabel(Rate(teamPower, quest))}";

        /// <summary>게시판용: "예상 소요 3일" 또는 최선 편성으로 줄 수 있으면 "예상 소요 3일 (최선 편성 2일)".</summary>
        public static string BoardDurationText(Quest quest)
        {
            int best = BestTeamPower(quest);
            int days = best > 0 ? ShortenedDays(quest, best) : quest.DurationDays;
            return days < quest.DurationDays
                ? $"예상 소요 {quest.DurationDays}일 (최선 편성 {days}일)"
                : $"예상 소요 {quest.DurationDays}일";
        }
    }
}
