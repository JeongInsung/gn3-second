using System.Collections.Generic;
using System.Linq;
using GN3.Combat;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.UI;

namespace GN3.Quests
{
    /// <summary>
    /// 목적지에 도착한 파견대의 전투 계산과 결과 처리. 파티 패널의 "전투 시작"(로그 재생)과
    /// 도착 알림창의 "자동 진행"(바로 결과)이 같은 계산·처리를 쓴다.
    /// </summary>
    public static class ExpeditionBattle
    {
        public class Battle
        {
            public BattleResult Result;
            public List<Mercenary> Fighters;
            public List<Combatant> Combatants;
            public bool Victory => Result.Outcome == BattleOutcome.TeamAVictory;
        }

        private static readonly System.Random Rng = new System.Random();

        public static Battle Simulate(Expedition expedition)
        {
            var fighters = expedition.Members.Where(m => m.IsAlive).ToList();
            var combatants = fighters.Select(m => m.ToCombatant()).ToList();
            var quest = expedition.Quest;
            var enemies = EnemySquadGenerator.Generate(quest.Region, quest.EnemyCount, quest.Difficulty, Rng);
            var result = new AutoBattleSimulator().Simulate(combatants, enemies);
            return new Battle { Result = result, Fighters = fighters, Combatants = combatants };
        }

        /// <summary>
        /// 전투 결과를 반영하고 파견을 끝낸다: 체력 반영(0이면 전사 → 파티에서 제거), 승리 시 보상, 알림·하루 보고서 기록,
        /// ExpeditionLog.Complete(살아남은 용병은 마을 출구에서 걸어 들어온다). 한 줄 요약을 돌려준다.
        /// </summary>
        public static string Conclude(Expedition expedition, Battle battle)
        {
            var quest = expedition.Quest;
            var fallen = new List<string>();
            for (int i = 0; i < battle.Fighters.Count; i++)
            {
                var merc = battle.Fighters[i];
                merc.SetHealth(battle.Combatants[i].CurrentHealth);
                if (!merc.IsAlive)
                {
                    fallen.Add(merc.Name);
                    PlayerParty.Instance.Remove(merc);
                }
            }

            var questGrade = QuestDifficulty.Grade(quest);
            string summary;
            if (battle.Victory)
            {
                int reward = Pricing.QuestReward(quest);
                Wallet.Add(reward);
                int rep = Guild.VictoryReputation(questGrade);
                summary = $"[{quest.Title}] 임무 완료! 보상 +{reward}G · 명성 +{rep}";
                Guild.Add(rep);
            }
            else
            {
                summary = $"[{quest.Title}] 임무 실패... 명성 -{Guild.FailPenalty}";
                Guild.Add(-Guild.FailPenalty);
            }

            // 살아남은 파견원 경험치: 승리 40+20×등급, 실패는 절반.
            int xp = 40 + 20 * (int)questGrade;
            if (!battle.Victory) xp /= 2;
            var levelUps = GrantExperience(expedition.Members.Where(m => m.IsAlive), xp);
            summary += $" · 경험치 +{xp}";

            string survivors = string.Join(", ", expedition.Members.Where(m => m.IsAlive).Select(m => m.Name));
            if (survivors.Length > 0) summary += $" · 귀환: {survivors}";
            if (fallen.Count > 0) summary += $" · 전사: {string.Join(", ", fallen)}";

            ToastLog.Show(summary);
            DailyLog.Add(summary);
            foreach (var line in levelUps)
            {
                ToastLog.Show(line);
                DailyLog.Add(line);
            }
            ExpeditionLog.Instance.Complete(expedition);
            return summary;
        }

        /// <summary>경험치를 나눠 주고 "아린 레벨 업! Lv.3" 문구 목록을 돌려준다.</summary>
        public static List<string> GrantExperience(IEnumerable<Mercenary> members, int xp)
        {
            var lines = new List<string>();
            foreach (var merc in members)
                if (merc.AddExperience(xp) > 0)
                    lines.Add($"{merc.Name} 레벨 업! Lv.{merc.Level}");
            return lines;
        }
    }
}
