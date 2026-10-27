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

        /// <summary>적 1마리 몫. 초반(Lv1·2)은 적 체력·공격을 줄인 만큼(EnemySquadGenerator.EarlyStatMultiplier) 낮춘다.</summary>
        public static int PowerPerEnemy(int difficulty) =>
            (int)System.Math.Round((58 + 21 * difficulty) * GN3.Combat.EnemySquadGenerator.EarlyStatMultiplier(difficulty));

        // 퀘스트 등급(용병과 같은 F~S): 권장 전투력이 이 값 미만이면 그 등급(F, E, D, C, B, A), 그 이상은 S.
        // D급 Lv1 용병 한 명 ≈ 전투력 100 기준.
        // 2026-10-16 권장 전투력을 ×1.25(RecommendedScale 0.8 → 1.0) 올리며 경계도 같은 배율로 올려, 같은 퀘스트는 같은 등급 그대로.
        private static readonly int[] GradeUpperBounds = { 163, 225, 300, 388, 488, 600 };

        /// <summary>표시·게시판용 등급. 은월의 숲 퀘스트는 첫 지역이라 F급, 숲의 주인 앤트킹 토벌만 E급.
        /// 그 밖(예전 저장의 장소 없는 퀘스트 등)은 전투력 등급. 명성·성장 보상은 PowerGrade를 쓴다.</summary>
        public static MercenaryGrade Grade(Quest quest)
        {
            if (quest.LocationName != null && quest.LocationName.StartsWith("은월의 숲"))
                return quest.Waves.Any(w => w.IsBoss) ? MercenaryGrade.E : MercenaryGrade.F;
            return PowerGrade(quest);
        }

        /// <summary>권장 전투력으로 정한 등급(F~S). 승리 명성(Guild.VictoryReputation)·성장(GrowthTuning.BattleGrowth)이 이것을 따른다.</summary>
        public static MercenaryGrade PowerGrade(Quest quest)
        {
            int power = RecommendedPower(quest);
            for (int i = 0; i < GradeUpperBounds.Length; i++)
                if (power < GradeUpperBounds[i]) return (MercenaryGrade)i;
            return MercenaryGrade.S;
        }

        /// <summary>rich text용 "[S] 퀘스트 이름"(등급 색).</summary>
        public static string RichTitle(Quest quest) => $"{GradeTable.RichLabel(Grade(quest))} Lv{quest.Difficulty} {quest.Title}";

        /// <summary>좁은 퀘스트 정보창 제목용 핵심만: "[F] 고블린 토벌"(레벨·의뢰인은 본문에 나온다).</summary>
        public static string ShortRichTitle(Quest quest) => $"{GradeTable.RichLabel(Grade(quest))} {quest.EnemyName} 토벌";

        // 권장 전투력 = 적 1마리 몫 × 유효 적 수 × RecommendedScale.
        // 유효 적 수 = 가장 큰 라운드 + 나머지 라운드 적 × 0.5 + 보스 1 — 라운드는 한꺼번에가 아니라 차례로 나와서
        // 적 수 합만큼 무겁지 않다. 자동 전투 시뮬레이션(실제 직업 수치, 무작위 파티·퀘스트 6000판)으로 맞춤:
        // 2026-10-16 적 체력·공격 ×1.4(EnemySquadGenerator 1.82/1.61) 기준 Scale 1.0 → 판정 '중간' 승률 약 59%, '어려움' 약 29%.
        // (예전 Scale 0.8·적 1.3/1.15는 성장·연구·스킬이 붙으며 '중간' 82%·'어려움' 53%까지 쉬워져 있었다)
        private const float LaterWaveWeight = 0.5f;
        private const float BossWeight = 1f;
        private const float RecommendedScale = 1.0f;

        public static int RecommendedPower(Quest quest)
        {
            float effective = quest.MaxWaveSize + LaterWaveWeight * (quest.EnemyCount - quest.MaxWaveSize)
                + (quest.Waves.Any(w => w.IsBoss) ? BossWeight : 0f);
            // 필요 인원이 정해진 레벨(Lv3·4 4명, Lv5 6명)은 보통 파견 인원(가장 큰 라운드 + 1)보다 많이 가니 그만큼 권장도 올린다
            // — 정해진 인원을 다 채워도 '매우 쉬움'이 뜨지 않게.
            float crew = quest.RequiredDispatchSize > 0 ? System.Math.Max(1f, quest.RequiredDispatchSize / (quest.MaxWaveSize + 1f)) : 1f;
            return (int)System.Math.Round(effective * PowerPerEnemy(quest.Difficulty) * RecommendedScale * crew);
        }

        /// <summary>라운드 구성 한 줄: "1R 산적 ×2 → 2R 늑대 ×3 → 3R 보스 고목왕 ×1"(한 라운드면 "산적 ×3").</summary>
        public static string WavesText(Quest quest) => quest.WaveCount == 1
            ? $"{quest.Waves[0].EnemyName} ×{quest.Waves[0].Count}"
            : string.Join(" → ", quest.Waves.Select((w, i) => $"{i + 1}R {(w.IsBoss ? "보스 " : "")}{w.EnemyName} ×{w.Count}"));

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
                .Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m) && !TrainingHall.IsTraining(m) && !Hospital.IsAdmitted(m) && !RestVenues.IsResting(m) && !m.IsExhausted && !m.IsSeverelyAiling)
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
