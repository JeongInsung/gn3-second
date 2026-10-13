using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GN3.Mercenaries;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Random = System.Random;
#endif

namespace GN3.EditorTools
{
    /// <summary>
    /// 용병 1명의 피로·사기·체력이 파견 → 마을 회복을 오가며 어떻게 변하는지 시간 단위로 굴려 표로 뽑는다(에디터 전용).
    /// 메뉴: GN3/Balance/용병 상태 시뮬레이션 → 콘솔 + Logs/condition_sim.txt.
    /// 수치는 ConditionTuning만 읽는다. 씬·싱글턴을 쓰지 않는 모형이라 아래 가정은 실제 게임과 다를 수 있다.
    ///  - 파견 중에는 자정마다 가는 길/귀환길 피로(마지막 귀환 밤도 길 위로 친다), 전투는 도착한 날 바로.
    ///    (게임에서는 파견 피로가 시간마다 나눠 붙고 이동도 출발 시각부터 24시간 단위로 센다. 하루 총량은 같아 결과는 같다.)
    ///  - 마을 밤에는 쉬는 날을 세어 IdleGraceDays를 넘기면 사기가 준다(입원은 넣지 않음).
    ///  - 귀환 일수 = max(1, (가는 일수 + 1) / 2) (Expedition.BeginReturn과 같음). 퀘스트 일수는 계층 범위의 최댓값.
    ///  - 전투 뒤 체력 BattleHealthLeft, 습격 1회마다 AmbushHealthLoss를 잃는다고 둔다(전투 결과는 모형 밖).
    ///  - 마을에서는 9시부터 쉼터(온천·도박장, 무작위 순서)·훈련을 굴린다. 실시간은 VenueRollHours·TrainingRollHours마다 한 번(VillagePartyPresenter),
    ///    '진행'으로 건너뛰면 1시간마다 그 간격에 비례한 확률로(VillageSkipVisits).
    ///  - 동료 관계 사기·질병·전사자·주급 미지급은 넣지 않는다(이기고 주급을 받는 평범한 경우).
    /// </summary>
    public static class ConditionBalanceSim
    {
        private const int Seed = 12345;
        private const int AverageRuns = 300;
        private const float BattleHealthLeft = 0.6f;
        private const float AmbushHealthLoss = 0.15f;
        private const float VenueRollHours = ConditionTuning.VenueRollHours;
        private const float TrainingRollHours = ConditionTuning.TrainingRollHours;
        private const float TrainingChance = ConditionTuning.TrainingVisitChance;
        private const float TrainingMinHours = 1f, TrainingMaxHours = 3f; // TrainingHall
        private const int VillageOpenHour = (int)ConditionTuning.VillageOutHour, VillageCloseHour = 22; // GameClock.NightStartHour
        private static readonly (string name, int outbound)[] Tiers = { ("1단계(약탈 세력)", 2), ("2단계(지형 동물)", 3), ("3단계(보스)", 5) };

        public enum VillageMode { Realtime, Skip }

        /// <summary>사기 규칙(기본은 ConditionTuning 그대로). 조정 후보를 나란히 비교할 때만 바꿔 쓴다(F 시나리오).</summary>
        private sealed class Rules
        {
            public string Name = "지금 값";
            public int Victory = ConditionTuning.VictoryMorale;
            public int Defeat = ConditionTuning.DefeatMorale;
            public int GamblingCap = ConditionTuning.GamblingMoraleCap;
            public int DriftPerDay = ConditionTuning.MoraleDriftPerDay;
            public float GamblingPerHour = ConditionTuning.GamblingMoralePerHour;
            public bool Win = true;   // 파견 결과
            public bool Paid = true;  // 주급을 받는지
        }

        private sealed class Merc
        {
            public float Fatigue;
            public float Morale = ConditionTuning.StartingMorale;
            public float Health = 1f;
            public string Venue;       // 지금 들어가 있는 곳(온천/도박장/훈련), 없으면 null
            public float VenueHoursLeft;
            public float PendingFatigue, PendingMorale; // RestVenue처럼 소수점은 쌓았다가 정수만

            public bool Exhausted => Fatigue >= ConditionTuning.ExhaustedFatigue;
            public bool Tired => Fatigue >= ConditionTuning.TiredFatigue;
            public void AddFatigue(float v) => Fatigue = Clamp(Fatigue + v);
            public void AddMorale(float v) => Morale = Clamp(Morale + v);
            private static float Clamp(float v) => Math.Max(0f, Math.Min(ConditionTuning.MaxCondition, v));
        }

        private sealed class Row
        {
            public int Day;
            public string Where;
            public float Fatigue, Morale, Health;
            public string Note;
        }

        private sealed class Sim
        {
            public readonly Merc M = new Merc();
            public readonly List<Row> Rows = new List<Row>();
            public int Day = 1;
            public int ExhaustedDays;
            public int IdleDays;
            public readonly List<float> UnpaidMorales = new List<float>(); // 주급을 못 받은 직후 사기(떠날지 굴리는 순간, MercenaryCondition.ConsiderLeaving)
            public readonly Random Rng;
            public readonly VillageMode Mode;
            public readonly Rules R;
            private readonly List<string> _notes = new List<string>();

            public Sim(int seed, VillageMode mode, Rules rules = null)
            {
                Rng = new Random(seed);
                Mode = mode;
                R = rules ?? new Rules();
            }

            /// <summary>day일차(그날 자정 뒤) 사기. 아직 그날까지 안 갔으면 마지막 값.</summary>
            public float MoraleOn(int day)
            {
                var row = Rows.FirstOrDefault(r => r.Day >= day);
                return row != null ? row.Morale : M.Morale;
            }

            public void Note(string text) => _notes.Add(text);

            /// <summary>파견 하루(자정 하나). ambush면 그날 습격.</summary>
            public void ExpeditionNight(string where, bool ambush)
            {
                if (ambush)
                {
                    M.AddFatigue(ConditionTuning.AmbushFatigue);
                    M.Health = Math.Max(0.05f, M.Health - AmbushHealthLoss);
                    Note("습격");
                }
                Midnight(where, onExpedition: true);
            }

            public void Battle(bool victory)
            {
                M.AddFatigue(ConditionTuning.BattleFatigue);
                M.AddMorale(victory ? R.Victory : -R.Defeat);
                M.Health = Math.Min(M.Health, BattleHealthLeft);
                Note(victory ? "전투 승리" : "전투 패배");
            }

            /// <summary>마을에서 하루(6시~다음 자정).</summary>
            public void VillageDay()
            {
                M.Venue = null;
                float nextVenueRoll = VillageOpenHour + (float)Rng.NextDouble() * VenueRollHours;
                float nextTrainingRoll = VillageOpenHour + (float)Rng.NextDouble() * TrainingRollHours;
                var visits = new List<string>();
                for (int hour = 6; hour < 24; hour++)
                {
                    if (M.Venue != null) StayHour();
                    if (M.Venue != null || hour < VillageOpenHour) continue;
                    bool trainingRoll = Mode == VillageMode.Skip ? Rng.NextDouble() < 1f / TrainingRollHours : hour >= nextTrainingRoll;
                    bool venueRoll = Mode == VillageMode.Skip ? Rng.NextDouble() < 1f / VenueRollHours : hour >= nextVenueRoll;
                    if (trainingRoll)
                    {
                        nextTrainingRoll += TrainingRollHours;
                        if (hour < VillageCloseHour && !M.Exhausted && Rng.NextDouble() < TrainingChance)
                        {
                            Enter("훈련", TrainingMinHours, TrainingMaxHours);
                            visits.Add("훈련");
                            continue;
                        }
                    }
                    if (venueRoll)
                    {
                        nextVenueRoll += VenueRollHours;
                        foreach (string venue in Rng.Next(2) == 0 ? new[] { "온천", "도박장" } : new[] { "도박장", "온천" })
                        {
                            bool open = venue == "도박장" || hour < VillageCloseHour;
                            float chance = venue == "온천"
                                ? ConditionTuning.HotSpringVisitChance((int)M.Fatigue)
                                : ConditionTuning.GamblingVisitChance((int)M.Fatigue, (int)M.Morale);
                            if (!open || Rng.NextDouble() >= chance) continue;
                            if (venue == "온천") Enter(venue, ConditionTuning.HotSpringMinHours, ConditionTuning.HotSpringMaxHours);
                            else Enter(venue, ConditionTuning.GamblingMinHours, ConditionTuning.GamblingMaxHours);
                            visits.Add(venue);
                            break;
                        }
                    }
                }
                if (visits.Count > 0) Note(string.Join("·", visits));
                M.Venue = null;
                Midnight("마을", onExpedition: false);
            }

            private void Enter(string venue, float min, float max)
            {
                M.Venue = venue;
                M.VenueHoursLeft = min + (float)Rng.NextDouble() * (max - min);
            }

            private void StayHour()
            {
                float hours = Math.Min(1f, M.VenueHoursLeft);
                M.VenueHoursLeft -= 1f;
                float fatigue, morale;
                switch (M.Venue)
                {
                    case "온천": fatigue = -ConditionTuning.HotSpringFatigueReliefPerHour; morale = ConditionTuning.HotSpringMoralePerHour; break;
                    case "도박장": fatigue = -ConditionTuning.GamblingFatigueReliefPerHour; morale = M.Morale >= R.GamblingCap ? 0f : R.GamblingPerHour; break;
                    default: fatigue = ConditionTuning.TrainingFatiguePerHour; morale = 0f; break;
                }
                M.PendingFatigue += fatigue * hours;
                M.PendingMorale += morale * hours;
                int wholeF = (int)M.PendingFatigue, wholeM = (int)M.PendingMorale;
                M.PendingFatigue -= wholeF;
                M.PendingMorale -= wholeM;
                M.AddFatigue(wholeF);
                M.AddMorale(wholeM);
                if (M.VenueHoursLeft <= 0f) M.Venue = null;
            }

            private void Midnight(string where, bool onExpedition)
            {
                if (onExpedition)
                {
                    M.AddFatigue(where == "귀환길" ? ConditionTuning.ReturnFatiguePerDay : ConditionTuning.ExpeditionFatiguePerDay);
                    IdleDays = 0;
                }
                else
                {
                    if (++IdleDays > ConditionTuning.IdleGraceDays) M.AddMorale(-ConditionTuning.IdleMoralePerDay);
                    M.AddFatigue(-ConditionTuning.RestFatiguePerNight);
                    M.Health = Math.Min(1f, M.Health + ConditionTuning.InnHealRatio);
                }
                if (M.Exhausted)
                {
                    M.AddMorale(-ConditionTuning.ExhaustedMoralePerDay);
                    ExhaustedDays++;
                }
                if (R.DriftPerDay > 0 && (int)M.Morale != ConditionTuning.MoraleBaseline) // MercenaryCondition.Drift
                {
                    float gap = ConditionTuning.MoraleBaseline - M.Morale;
                    M.AddMorale(Math.Sign(gap) * Math.Min(R.DriftPerDay, Math.Abs(gap)));
                }
                Day++;
                if (Day > 1 && (Day - 1) % ConditionTuning.PayIntervalDays == 0)
                {
                    M.AddMorale(R.Paid ? ConditionTuning.PaidMorale : -ConditionTuning.UnpaidMorale);
                    Note(R.Paid ? "주급" : "주급 미지급");
                    if (!R.Paid) UnpaidMorales.Add(M.Morale);
                }
                Rows.Add(new Row { Day = Day, Where = where, Fatigue = M.Fatigue, Morale = M.Morale, Health = M.Health, Note = string.Join(", ", _notes) });
                _notes.Clear();
            }

            /// <summary>파견 한 번(가는 길 → 전투 → 귀환). 습격은 ambushDays에 든 날(가는 길 0부터, 귀환은 outbound부터 이어서 센다).</summary>
            public void Expedition(int outbound, Func<int, bool> ambushOn)
            {
                for (int d = 0; d < outbound; d++) ExpeditionNight("가는 길", ambushOn(d));
                Battle(R.Win);
                int back = Math.Max(1, (outbound + 1) / 2);
                for (int d = 0; d < back; d++) ExpeditionNight("귀환길", ambushOn(outbound + d));
            }

            /// <summary>마을에서 쉬며 조건을 만족할 때까지 걸린 날(최대 limit).</summary>
            public int RestUntil(Func<Merc, bool> ready, int limit = 10)
            {
                int days = 0;
                while (!ready(M) && days < limit)
                {
                    VillageDay();
                    days++;
                }
                return days;
            }
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 용병 상태 시뮬레이션 ===");
            sb.AppendLine($"기준: 피곤 {ConditionTuning.TiredFatigue}↑ / 지침 {ConditionTuning.ExhaustedFatigue}↑(파견 불가) / 낮은 사기 {ConditionTuning.LowMorale}↓, 시작 사기 {ConditionTuning.StartingMorale}");
            sb.AppendLine($"피로: 가는 길 하루 +{ConditionTuning.ExpeditionFatiguePerDay}, 귀환길 +{ConditionTuning.ReturnFatiguePerDay}, 마을 밤 -{ConditionTuning.RestFatiguePerNight}, 전투 +{ConditionTuning.BattleFatigue}, 습격 +{ConditionTuning.AmbushFatigue}, 온천 시간당 -{ConditionTuning.HotSpringFatigueReliefPerHour:0}");
            sb.AppendLine($"사기: 승리 +{ConditionTuning.VictoryMorale}, 주급 +{ConditionTuning.PaidMorale}, 지친 날 -{ConditionTuning.ExhaustedMoralePerDay}, 마을에서 {ConditionTuning.IdleGraceDays}일 넘게 쉬면 하루 -{ConditionTuning.IdleMoralePerDay}, " +
                          $"도박장 사기는 {ConditionTuning.GamblingMoraleCap} 미만일 때만, 하루 끌림 {ConditionTuning.MoraleDriftPerDay}(기준 {ConditionTuning.MoraleBaseline})");
            sb.AppendLine();

            // A. 계층별 1회 왕복(습격 1회, 가는 길 첫날) → 마을 회복
            sb.AppendLine("## A. 계층별 1회 왕복 (승리, 습격 1회) 후 마을 회복");
            foreach (var mode in new[] { VillageMode.Realtime, VillageMode.Skip })
            {
                sb.AppendLine($"### 마을: {(mode == VillageMode.Realtime ? "실시간으로 둠" : "'진행'으로 건너뜀")}");
                foreach (var tier in Tiers)
                {
                    var sim = new Sim(Seed, mode);
                    sim.Expedition(tier.outbound, d => d == 0);
                    var back = sim.M;
                    float fatigueBack = back.Fatigue, moraleBack = back.Morale;
                    sim.RestUntil(m => m.Fatigue < ConditionTuning.TiredFatigue && m.Health >= 0.99f, 6);
                    sim.RestUntil(m => false, 1);
                    sb.AppendLine($"- {tier.name}: 가는 {tier.outbound}일 + 귀환 {Math.Max(1, (tier.outbound + 1) / 2)}일 → 귀환 시 피로 {fatigueBack:0} · 사기 {moraleBack:0}, 지친 날 {sim.ExhaustedDays}일");
                    AppendTable(sb, sim.Rows);
                }
                sb.AppendLine();
            }

            // B. 평균(무작위 습격 25%/일) — 귀환 상태와 회복 일수
            sb.AppendLine($"## B. {AverageRuns}회 평균 (습격 하루 25%)");
            sb.AppendLine("| 계층 | 마을 | 귀환 피로 | 귀환 사기 | 지친 날 | 재파견 가능(지침 해소)까지 | 피곤 해소(<50)까지 |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var tier in Tiers)
                foreach (var mode in new[] { VillageMode.Realtime, VillageMode.Skip })
                {
                    float f = 0, m = 0, ex = 0, ready = 0, fresh = 0;
                    for (int i = 0; i < AverageRuns; i++)
                    {
                        var sim = new Sim(Seed + i, mode);
                        sim.Expedition(tier.outbound, _ => sim.Rng.NextDouble() < 0.25);
                        f += sim.M.Fatigue;
                        m += sim.M.Morale;
                        int toReady = sim.RestUntil(x => !x.Exhausted);
                        ready += toReady;
                        fresh += toReady + sim.RestUntil(x => !x.Tired);
                        ex += sim.ExhaustedDays;
                    }
                    sb.AppendLine($"| {tier.name} | {(mode == VillageMode.Realtime ? "실시간" : "건너뜀")} | {f / AverageRuns:0.0} | {m / AverageRuns:0.0} | {ex / AverageRuns:0.0}일 | {ready / AverageRuns:0.0}일 | {fresh / AverageRuns:0.0}일 |");
                }
            sb.AppendLine();

            // C. 28일 반복: 지치지 않으면(피로 < 피곤) 바로 같은 계층 재파견
            sb.AppendLine("## C. 28일 반복: 피곤(50) 아래로 내려오면 바로 같은 계층 재파견 (습격 25%/일)");
            sb.AppendLine("| 계층 | 마을 | 파견 횟수 | 지친 날 | 28일 뒤 사기 | 사기 최저 |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var tier in Tiers)
                foreach (var mode in new[] { VillageMode.Realtime, VillageMode.Skip })
                {
                    float trips = 0, ex = 0, morale = 0, low = 0;
                    for (int i = 0; i < AverageRuns; i++)
                    {
                        var sim = new Sim(Seed + i, mode);
                        float min = sim.M.Morale;
                        int count = 0;
                        while (sim.Day <= 28)
                        {
                            if (sim.M.Tired) sim.VillageDay();
                            else
                            {
                                sim.Expedition(tier.outbound, _ => sim.Rng.NextDouble() < 0.25);
                                count++;
                            }
                            min = Math.Min(min, sim.Rows.Min(r => r.Morale));
                        }
                        trips += count;
                        ex += sim.ExhaustedDays;
                        morale += sim.M.Morale;
                        low += min;
                    }
                    sb.AppendLine($"| {tier.name} | {(mode == VillageMode.Realtime ? "실시간" : "건너뜀")} | {trips / AverageRuns:0.0} | {ex / AverageRuns:0.0}일 | {morale / AverageRuns:0.0} | {low / AverageRuns:0.0} |");
                }
            sb.AppendLine();

            // D. 쉬는 날이 긴 용병: 2단계 파견 뒤 마을에서 N일 쉬기를 28일 반복
            sb.AppendLine("## D. 28일 반복: 2단계 파견 뒤 마을에서 N일 쉬기 (실시간, 습격 25%/일)");
            sb.AppendLine("| 쉬는 날 | 파견 횟수 | 28일 뒤 사기 | 사기 최저 |");
            sb.AppendLine("|---|---|---|---|");
            foreach (int restDays in new[] { 2, 4, 7, 28 })
            {
                float trips = 0, morale = 0, low = 0;
                for (int i = 0; i < AverageRuns; i++)
                {
                    var sim = new Sim(Seed + i, VillageMode.Realtime);
                    int count = 0;
                    while (sim.Day <= 28)
                    {
                        if (count == 0 || restDays < 28)
                        {
                            sim.Expedition(Tiers[1].outbound, _ => sim.Rng.NextDouble() < 0.25);
                            count++;
                        }
                        for (int d = 0; d < restDays && sim.Day <= 28; d++) sim.VillageDay();
                    }
                    trips += count;
                    morale += sim.M.Morale;
                    low += sim.Rows.Min(r => r.Morale);
                }
                sb.AppendLine($"| {restDays}일 | {trips / AverageRuns:0.0} | {morale / AverageRuns:0.0} | {low / AverageRuns:0.0} |");
            }
            sb.AppendLine();
            AppendMoraleComparison(sb);
            return sb.ToString();
        }

        /// <summary>
        /// F. 사기 조정 후보 비교(2단계, 실시간, 습격 25%/일). 이기는 파티는 피곤이 풀리면 바로 재파견하며 7·14·21·28일차 사기를,
        /// 연패·주급 미지급 파티는 사기가 처음 낮은 사기(30) 아래로 내려간 날을, 마을에서만 지내는 용병은 28일 뒤 사기를 본다.
        /// </summary>
        private static void AppendMoraleComparison(StringBuilder sb)
        {
            // 사기를 바꿀 때 여기에 후보를 더해 나란히 본다. "조정 전"은 2026-10-12 조정 전 값(승리 10·패배 15·도박장 시간당 4·제한 없음·끌림 없음).
            var variants = new List<Rules>
            {
                new Rules(),
                new Rules { Name = "조정 전", Victory = 10, Defeat = 15, GamblingPerHour = 4, GamblingCap = 100, DriftPerDay = 0 },
            };
            sb.AppendLine($"## F. 사기 비교 (2단계, 실시간, 습격 25%/일, {AverageRuns}회 평균)");
            sb.AppendLine("| 후보 | 이기는 파티 7·14·21·28일차 | 28일 최저 | 연패: 30 아래까지 | 주급 미지급: 30 아래까지 | 미지급: 28일 안에 떠날 확률 | 마을에만 28일 |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var v in variants)
            {
                float[] weeks = new float[4];
                float low = 0f, lose = 0f, unpaid = 0f, idle = 0f, leave = 0f;
                for (int i = 0; i < AverageRuns; i++)
                {
                    var win = RunPolicy(Seed + i, v, win: true, paid: true);
                    for (int w = 0; w < 4; w++) weeks[w] += win.MoraleOn(7 * (w + 1));
                    low += win.Rows.Where(r => r.Day <= 28).Min(r => r.Morale);
                    lose += DayBelowLow(RunPolicy(Seed + i, v, win: false, paid: true));
                    var unpaidRun = RunPolicy(Seed + i, v, win: true, paid: false);
                    unpaid += DayBelowLow(unpaidRun);
                    // 28일 안의 주급날(8·15·22일차)마다 그 직후 사기로 떠날지 굴린 확률을 누적(사기 0이면 자정에 무조건 떠남)
                    float stay28 = 1f;
                    foreach (float m in unpaidRun.UnpaidMorales.Take(3)) stay28 *= 1f - ConditionTuning.LeaveChance((int)m);
                    leave += 1f - stay28;
                    var stay = new Sim(Seed + i, VillageMode.Realtime, v);
                    while (stay.Day <= 28) stay.VillageDay();
                    idle += stay.MoraleOn(28);
                }
                string curve = string.Join(" → ", weeks.Select(x => (x / AverageRuns).ToString("0")));
                sb.AppendLine($"| {v.Name} | {curve} | {low / AverageRuns:0} | {Days(lose)} | {Days(unpaid)} | {leave / AverageRuns * 100f:0}% | {idle / AverageRuns:0} |");
            }

            string Days(float total) => total / AverageRuns > 40f ? "안 내려감" : $"{total / AverageRuns:0.0}일";
            sb.AppendLine($"떠날 확률(친한 동료의 죽음·주급 미지급으로 사기가 {ConditionTuning.LowMorale} 아래로 떨어진 순간): " +
                          string.Join(" · ", new[] { 29, 20, 15, 10, 5, 0 }.Select(m => $"사기 {m} → {ConditionTuning.LeaveChance(m) * 100f:0}%")));
        }

        /// <summary>피곤이 풀리면 바로 2단계 재파견을 42일 반복(28일 이후는 '30 아래까지'를 재기 위해 더 굴림).</summary>
        private static Sim RunPolicy(int seed, Rules baseRules, bool win, bool paid)
        {
            var rules = new Rules { Name = baseRules.Name, Victory = baseRules.Victory, Defeat = baseRules.Defeat, GamblingCap = baseRules.GamblingCap, DriftPerDay = baseRules.DriftPerDay,
                GamblingPerHour = baseRules.GamblingPerHour, Win = win, Paid = paid };
            var sim = new Sim(seed, VillageMode.Realtime, rules);
            while (sim.Day <= 42)
            {
                if (sim.M.Tired) sim.VillageDay();
                else sim.Expedition(Tiers[1].outbound, _ => sim.Rng.NextDouble() < 0.25);
            }
            return sim;
        }

        /// <summary>사기가 처음 낮은 사기 기준 아래로 내려간 일차(42일 안에 안 내려가면 99).</summary>
        private static int DayBelowLow(Sim sim)
        {
            var row = sim.Rows.FirstOrDefault(r => r.Morale < ConditionTuning.LowMorale);
            return row != null ? row.Day : 99;
        }

        private static void AppendTable(StringBuilder sb, List<Row> rows)
        {
            sb.AppendLine("  | 일차(자정 뒤) | 위치 | 피로 | 사기 | 체력 | 상태 | 그날 |");
            sb.AppendLine("  |---|---|---|---|---|---|---|");
            foreach (var r in rows)
            {
                string state = r.Fatigue >= ConditionTuning.ExhaustedFatigue ? "지침" : r.Fatigue >= ConditionTuning.TiredFatigue ? "피곤" : "-";
                if (r.Morale < ConditionTuning.LowMorale) state += " 사기낮음";
                sb.AppendLine($"  | {r.Day} | {r.Where} | {r.Fatigue:0} | {r.Morale:0} | {r.Health * 100f:0}% | {state} | {r.Note} |");
            }
        }

#if UNITY_EDITOR
        [MenuItem("GN3/Balance/용병 상태 시뮬레이션")]
        private static void RunFromMenu()
        {
            string report = Run();
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs", "condition_sim.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report, Encoding.UTF8);
            Debug.Log($"[ConditionBalanceSim] {path}\n{report}");
        }
#endif
    }
}
