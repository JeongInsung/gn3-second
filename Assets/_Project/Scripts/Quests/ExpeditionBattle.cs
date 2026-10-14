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
            /// <summary>다 잡은 라운드 수(승리면 WaveCount).</summary>
            public int WavesCleared;
            public int WaveCount = 1;
            public bool Victory => Result.Outcome == BattleOutcome.TeamAVictory;
        }

        private static readonly System.Random Rng = new System.Random();

        /// <summary>퀘스트 waveIndex번째 라운드의 적(이름은 라운드 적, 능력치는 퀘스트 난이도, 보스는 따로 세게).</summary>
        public static List<Combatant> SpawnWave(Quest quest, int waveIndex, System.Random rng)
        {
            var wave = quest.Waves[waveIndex];
            return EnemySquadGenerator.Generate(quest.Region, wave.Count, quest.Difficulty, rng, wave.Tier, wave.EnemyName);
        }

        /// <summary>라운드를 차례로 자동 전투한다. 체력은 라운드 사이에 그대로 이어지고, 한 라운드라도 지면(또는 비기면) 거기서 실패.</summary>
        public static Battle Simulate(Expedition expedition)
        {
            var fighters = expedition.Members.Where(m => m.IsAlive).ToList();
            var combatants = fighters.Select(m => m.ToCombatant()).ToList();
            var quest = expedition.Quest;
            var skills = ClassSkillFactory.ForTeam(fighters, combatants);

            var log = new List<BattleEvent>();
            var allEnemies = new List<Combatant>();
            int rounds = 0, cleared = 0;
            var outcome = BattleOutcome.TeamAVictory;
            for (int w = 0; w < quest.WaveCount; w++)
            {
                var enemies = SpawnWave(quest, w, Rng);
                allEnemies.AddRange(enemies);
                var result = new AutoBattleSimulator().Simulate(combatants, enemies, skills: skills);
                log.AddRange(result.Log);
                rounds += result.Rounds;
                outcome = result.Outcome;
                if (outcome != BattleOutcome.TeamAVictory) break;
                cleared++;
            }

            var total = new BattleResult { Outcome = outcome, Rounds = rounds, Log = log, TeamA = combatants, TeamB = allEnemies };
            return new Battle { Result = total, Fighters = fighters, Combatants = combatants, WavesCleared = cleared, WaveCount = quest.WaveCount };
        }

        /// <summary>
        /// 전투 결과를 반영하고 파견을 끝낸다: 체력 반영(0이면 전사 → 파티에서 제거), 승리 시 보상, 알림·하루 보고서 기록,
        /// 귀환 시작(ExpeditionLog.BeginReturn — 귀환 일수가 지나면 살아남은 용병이 마을 출구에서 걸어 들어온다). 한 줄 요약을 돌려준다.
        /// 우편함에 퀘스트 결과 편지(중요 → 게임을 멈추고 알림창)를 남기고 도착 편지를 처리 완료로 바꾼다.
        /// alertShown: 이미 결과 화면을 보여 준 곳(직접 진행 전투 씬·파티 패널 로그 재생)은 true — 알림창을 다시 띄우지 않는다.
        /// </summary>
        public static string Conclude(Expedition expedition, Battle battle, bool alertShown = false) =>
            ConcludeReport(expedition, battle, alertShown).Summary;

        /// <summary>결과 화면용 항목별 보고(Conclude와 같은 처리).</summary>
        public class Report
        {
            public bool Victory;
            public int Gold;          // 받은 보상(실패면 0)
            public int Reputation;    // 명성 변화(실패면 음수)
            public int WavesCleared;  // 다 잡은 라운드 수
            public int WaveCount = 1;
            public float ReturnHours; // 귀환까지(생존자가 없으면 0)
            public string Summary;
            public List<MemberReport> Members = new List<MemberReport>();
        }

        public class MemberReport
        {
            public Mercenary Merc;
            public int HealthBefore;
            public int HealthAfter;
            public int MaxHealth;
            public bool Fallen;
            public List<string> Gains = new List<string>(); // "공격 +3" 등
        }

        public static Report ConcludeReport(Expedition expedition, Battle battle, bool alertShown = false)
        {
            var report = new Report { Victory = battle.Victory };
            var quest = expedition.Quest;
            var fallen = new List<string>();
            var fallenMercs = new List<Mercenary>();
            var damage = new List<string>(); // 결과 창 "피해: 브렌 26 → 17 · …"
            for (int i = 0; i < battle.Fighters.Count; i++)
            {
                var merc = battle.Fighters[i];
                int before = merc.CurrentHealth;
                merc.SetHealth(battle.Combatants[i].CurrentHealth);
                report.Members.Add(new MemberReport
                {
                    Merc = merc,
                    HealthBefore = before,
                    HealthAfter = merc.CurrentHealth,
                    MaxHealth = battle.Combatants[i].Stats.MaxHealth,
                    Fallen = !merc.IsAlive,
                });
                if (!merc.IsAlive) damage.Add($"{merc.Name} 전사");
                else if (merc.CurrentHealth < before) damage.Add($"{merc.Name} {before} → {merc.CurrentHealth}");
                if (!merc.IsAlive)
                {
                    fallen.Add(merc.Name);
                    fallenMercs.Add(merc);
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
                summary = $"{quest.Title} — 임무 완료! 보상 +{reward}G · 명성 +{rep}";
                Guild.Add(rep);
                report.Gold = reward;
                report.Reputation = rep;
                QuestClearHistory.RecordClear(quest.Region, quest.Tier); // 이 지역·계층은 다음 게시판에서 덜 나오고, 안 깬 계층이 더 자주 나온다
            }
            else
            {
                string where = battle.WaveCount > 1 ? $" ({battle.WavesCleared + 1}/{battle.WaveCount}라운드에서 패배)" : "";
                summary = $"{quest.Title} — 임무 실패...{where} 명성 -{Guild.FailPenalty}";
                Guild.Add(-Guild.FailPenalty);
                report.Reputation = -Guild.FailPenalty;
            }

            // 살아남은 파견원 성장: 승리 1 + 0.5×퀘스트 등급, 실패는 절반(× 나이 배율, GrowthTuning.BattleGrowth).
            var levelUps = new List<string>();
            float growth = GrowthTuning.BattleGrowth((int)questGrade, battle.Victory);
            foreach (var merc in expedition.Members.Where(m => m.IsAlive))
            {
                var gains = merc.AddGrowth(growth);
                if (gains.Count == 0) continue;
                levelUps.Add($"{merc.Name} {string.Join(", ", gains)}");
                var member = report.Members.FirstOrDefault(r => r.Merc == merc);
                if (member != null) member.Gains.AddRange(gains);
            }
            var survivorsList = expedition.Members.Where(m => m.IsAlive).ToList();
            MercenaryCondition.AfterBattle(survivorsList, battle.Victory, fallenMercs); // 피로·사기(친밀도 보정 포함)
            // 함께 살아 돌아온 사람끼리 친밀도가 바뀐다(승리면 오르고, 실패면 성격이 안 맞는 사이는 내려간다).
            Affinity.AfterExpedition(survivorsList, battle.Victory);

            string survivors = string.Join(", ", expedition.Members.Where(m => m.IsAlive).Select(m => m.Name));
            if (survivors.Length > 0) summary += $" · 귀환길: {survivors} ({GN3.World.GameClock.FormatDuration(expedition.ReturnHoursFor)})";
            if (fallen.Count > 0) summary += $" · 전사: {string.Join(", ", fallen)}";

            ToastLog.Show(summary, false);
            DailyLog.Add(summary);
            foreach (var line in levelUps)
            {
                ToastLog.Show(line, false);
                DailyLog.Add(line);
            }

            // 우편함: 결과 편지(중요). 본문은 요약을 줄마다 나누고 피해(체력 변화)와 성장(오른 능력치)을 덧붙인다.
            // 제목이 이미 "퀘스트 — 임무 완료!"라 본문 첫 줄의 퀘스트 이름은 빼고 "임무 완료! 보상 …"부터.
            string prefix = quest.Title + " — ";
            string body = (summary.StartsWith(prefix) ? summary.Substring(prefix.Length) : summary).Replace(" · ", "\n");
            body += "\n\n" + (damage.Count > 0 ? "피해: " + string.Join(" · ", damage) : "피해 없음");
            if (levelUps.Count > 0) body += "\n\n" + string.Join("\n", levelUps);
            var mail = Mailbox.Post(MailKind.QuestResult,
                battle.Victory ? $"{quest.Title} — 임무 완료!" : $"{quest.Title} — 임무 실패...", body, important: true);
            if (alertShown) Mailbox.Acknowledge(mail);
            Mailbox.ResolveArrival(expedition);

            // 살아남은 사람이 있으면 귀환길에 오른다(며칠 뒤 마을 도착). 전멸이면 바로 끝낸다.
            if (survivorsList.Count > 0) ExpeditionLog.Instance.BeginReturn(expedition);
            else ExpeditionLog.Instance.Complete(expedition);
            report.ReturnHours = survivorsList.Count > 0 ? expedition.ReturnHoursFor : 0f;
            report.Summary = summary;
            report.WavesCleared = battle.WavesCleared;
            report.WaveCount = battle.WaveCount;
            return report;
        }

        /// <summary>살아남은 사람에게 성장량(나이 배율 전)을 준다. 능력치가 오른 사람마다 "카이런 공격 +3, 체력 +6" 한 줄.</summary>
        public static List<string> GrantGrowth(IEnumerable<Mercenary> members, float growth)
        {
            var lines = new List<string>();
            foreach (var merc in members)
            {
                var gains = merc.AddGrowth(growth);
                if (gains.Count > 0) lines.Add($"{merc.Name} {string.Join(", ", gains)}");
            }
            return lines;
        }
    }
}
