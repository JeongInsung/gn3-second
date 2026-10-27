using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Combat;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 전투 한 판의 활약 평점: 0~100점 → 등급 S · A+ A A- · B+ B B- · C+ C C- · D+ D D-.
    /// 기여 = 준 피해 몫 + 치유 몫 + 0.5 × 받은 피해 몫(버틴 몫)을 팀 평균 기대치와 비교하고, 처치·생존·승패를 더한다.
    /// 수치는 Combatant의 이번 전투 카운터(CombatResolver·치유 스킬이 쌓는다). 기록은 MercenaryRecord.
    /// </summary>
    public static class BattleRating
    {
        private const float Base = 60f;
        private const float ShareWeight = 25f;   // 기대치의 2배 기여면 +25
        private const float ShareCap = 30f;
        private const float TakenWeight = 0.5f;  // 받은 피해(버틴 몫)는 절반만
        private const float KillPoints = 3f, KillCap = 12f;
        private const float SurvivedPoints = 5f, FallenPoints = -15f;
        private const float WinPoints = 5f, LossPoints = -5f;

        /// <summary>팀(같은 편 Combatant들) 안에서 한 사람의 점수. 승패는 이 편 기준.</summary>
        public static int Score(Combatant unit, IReadOnlyList<Combatant> team, bool victory)
        {
            if (unit == null || team == null || team.Count == 0) return 0;
            int dealt = team.Sum(c => c.DamageDealt), healed = team.Sum(c => c.HealingDone), taken = team.Sum(c => c.DamageTaken);
            float expected = 1f / team.Count;
            // 쓰인 항목만 기대치에 넣는다(치유가 없던 판에선 힐러가 아닌 사람이 손해 보지 않게)
            float weight = (dealt > 0 ? 1f : 0f) + (healed > 0 ? 1f : 0f) + (taken > 0 ? TakenWeight : 0f);
            float share = (dealt > 0 ? (float)unit.DamageDealt / dealt : 0f)
                        + (healed > 0 ? (float)unit.HealingDone / healed : 0f)
                        + (taken > 0 ? TakenWeight * unit.DamageTaken / taken : 0f);
            float ratio = weight > 0f ? share / (expected * weight) : 1f;

            float score = Base
                + Math.Clamp(ShareWeight * (ratio - 1f), -ShareCap, ShareCap)
                + Math.Min(KillCap, unit.Kills * KillPoints)
                + (unit.IsAlive ? SurvivedPoints : FallenPoints)
                + (victory ? WinPoints : LossPoints);
            return (int)Math.Round(Math.Clamp(score, 0f, 100f));
        }

        private static readonly (int min, string grade)[] Grades =
        {
            (90, "S"), (85, "A+"), (80, "A"), (75, "A-"), (70, "B+"), (65, "B"), (60, "B-"),
            (55, "C+"), (50, "C"), (45, "C-"), (40, "D+"), (35, "D"), (0, "D-"),
        };

        /// <summary>점수 → 등급("A-"). 평균 점수에도 같은 표를 쓴다.</summary>
        public static string Grade(float score)
        {
            foreach (var (min, grade) in Grades)
                if (score >= min) return grade;
            return "D-";
        }

        /// <summary>등급 색(16진): S 금색 · A 초록 · B 하늘 · C 흰색 · D 빨강.</summary>
        public static string Hex(string grade) => grade.Length == 0 ? "ffffff" : grade[0] switch
        {
            'S' => "ffd36b",
            'A' => "7fd36b",
            'B' => "6ab8ff",
            'C' => "e8e0d0",
            _ => "e05a4a",
        };

        public static string RichGrade(float score)
        {
            string grade = Grade(score);
            return $"<color=#{Hex(grade)}>{grade}</color>";
        }

        /// <summary>툴팁용 활약 한 줄: "피해 320 · 받은 피해 140 · 처치 3 · 치유 0".</summary>
        public static string Describe(Combatant unit) =>
            $"피해 {unit.DamageDealt} · 받은 피해 {unit.DamageTaken} · 처치 {unit.Kills} · 치유 {unit.HealingDone}";
    }
}
