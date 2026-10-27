using System;
using System.Collections.Generic;
using GN3.Combat;
using GN3.CharacterAnim;
using GN3.Economy;
using GN3.Traits;

namespace GN3.Mercenaries
{
    public class Mercenary
    {
        public string Id { get; }
        public string Name { get; }
        public MercenaryClassSO Class { get; }
        public ComposedCharacter Appearance { get; }
        public Personality Personality { get; }
        public bool HasRarePassive { get; }

        /// <summary>타고난 재능(F~S). 성장 속도 배율로만 쓴다(GrowthTuning.TalentRate). 시장에서 생길 때 정해진다.</summary>
        public MercenaryGrade Talent { get; private set; }

        /// <summary>등급(F~S) = 지금 전투력 구간(GradeTable.ForCombatPower). 키우면 오르고(승급) 늙으면 내려간다.</summary>
        public MercenaryGrade Grade => GradeTable.ForCombatPower(GradePower);

        /// <summary>등급을 정하는 전투력: 피로·사기·부상을 뺀 전투력(성장·성격·무기·수련). 지치거나 다쳐도 등급이 깜빡이지 않게.</summary>
        public int GradePower => GradeTable.CombatPower(ComputeStats(null, includeCondition: false));

        /// <summary>최종 능력치로 계산한 전투력(성장·성격·무기·피로·부상 모두 반영).</summary>
        public int CombatPower => GradeTable.CombatPower(CurrentStats);

        /// <summary>등급이 바뀌었을 때(성장·쇠퇴·무기). 승급·강등 알림용(MercenaryAge가 구독).</summary>
        public static event Action<Mercenary, MercenaryGrade, MercenaryGrade> GradeChanged;

        private void NotifyGrade(MercenaryGrade before)
        {
            var after = Grade;
            if (after != before) GradeChanged?.Invoke(this, before, after);
        }

        /// <summary>들고 있는 무기(없으면 맨손). 대장간에서 사서 쥐여 준다.</summary>
        public WeaponItem Weapon { get; private set; }

        /// <summary>메고 다니는 가방(전리품을 들고 올 칸 수 — BagCatalog). 처음엔 낡은 주머니, 의약품 상점에서 바꿔 준다.</summary>
        public GN3.Economy.BagItem Bag { get; private set; } = GN3.Economy.BagCatalog.Basic;

        /// <summary>가방을 바꾼다(들던 가방은 버린다). null이면 기본 가방.</summary>
        public void SetBag(GN3.Economy.BagItem bag) => Bag = bag ?? GN3.Economy.BagCatalog.Basic;

        /// <summary>직업 기본값 + 성장 → 성격 보정 → 무기 보너스(고정값) → 피로·사기 배율(공격·방어·신성력) → 부상·질병 순으로 계산한 현재 능력치.
        /// 마법사는 마지막에 공격을 마력으로 옮긴다(UsesMagic).</summary>
        public CombatStats CurrentStats => ComputeStats(_ailments);

        /// <summary>부상·질병이 없었다면의 능력치(툴팁에서 "공격 24 → 18"처럼 비교할 때).</summary>
        public CombatStats StatsWithoutAilments => ComputeStats(null);

        /// <summary>이 상태이상들만 적용한 능력치(null이면 부상·질병 없이). includeCondition이 false면 피로·사기 배율도 빼고(등급 전투력).
        /// legacyTalent면 예전 공식(재능 능력치 배율) — 예전 저장의 전투력을 잴 때만.</summary>
        public CombatStats ComputeStats(IEnumerable<Ailment> ailments, bool includeCondition = true, bool legacyTalent = false)
        {
            var raw = MercenaryStatCalculator.Calculate(Class, GrowthAttack, GrowthDefense, GrowthHealth, GrowthSpeed, GrowthHoly);
            if (legacyTalent) raw = GradeTable.ApplyLegacyTalent(raw, Talent);
            var stats = PersonalityTable.Apply(raw, Personality);
            if (Weapon != null)
            {
                stats.Attack += Weapon.AttackFor(Class.Kind);
                stats.Defense += Weapon.Defense;
                stats.MoveSpeed = Math.Max(0, stats.MoveSpeed + Weapon.Speed);
            }
            // 연구소·성당에서 쌓은 영구 보너스(직업별)
            var research = ResearchStatBonus();
            stats.Attack += research.attack;
            stats.Defense += research.defense;
            stats.MaxHealth += research.health;
            stats.Holy += research.holy;
            // 지치거나 사기가 낮으면 공격·방어·신성력이 깎인다(최대 체력은 그대로라 체력 계산이 꼬이지 않는다).
            float condition = includeCondition ? ConditionMultiplier : 1f;
            if (condition < 1f)
            {
                stats.Attack = (int)Math.Round(stats.Attack * condition);
                stats.Defense = (int)Math.Round(stats.Defense * condition);
                stats.Holy = (int)Math.Round(stats.Holy * condition);
            }
            // 부상·질병은 공격·방어·이동 속도를 깎는다(최대 체력은 그대로). 신성력은 공격과 같은 비율로 깎인다.
            if (ailments != null)
            {
                foreach (var ailment in ailments)
                {
                    var def = ailment.Def;
                    stats.Attack = (int)Math.Round(stats.Attack * (1f - def.attackPenalty));
                    stats.Defense = (int)Math.Round(stats.Defense * (1f - def.defensePenalty));
                    stats.MoveSpeed = (int)Math.Round(stats.MoveSpeed * (1f - def.moveSpeedPenalty));
                    stats.Holy = (int)Math.Round(stats.Holy * (1f - def.attackPenalty));
                }
            }
            // 마법사의 공격 능력치(기본값·성장·성격·무기·연구·피로·부상을 모두 거친 값)는 마력이다.
            if (UsesMagic)
            {
                stats.Magic += stats.Attack;
                stats.Attack = 0;
            }
            return stats;
        }

        // ---------- 피로·사기 (규칙·수치는 MercenaryCondition) ----------

        // 수치는 ConditionTuning
        public const int MaxCondition = ConditionTuning.MaxCondition;
        public const int StartingMorale = ConditionTuning.StartingMorale;
        public const int TiredFatigue = ConditionTuning.TiredFatigue;          // 이상이면 공격·방어 -10%
        public const int ExhaustedFatigue = ConditionTuning.ExhaustedFatigue;  // 이상이면 -25%, 파견·훈련 불가
        public const int LowMorale = ConditionTuning.LowMorale;                // 미만이면 공격·방어 -10%

        /// <summary>피로 0~100. 파견·전투·훈련으로 오르고 마을에서 자면 내린다.</summary>
        public int Fatigue { get; private set; }
        /// <summary>사기 0~100. 승리·급여로 오르고 패배·동료 전사·미지급으로 내린다. 0이면 길드를 떠난다.</summary>
        public int Morale { get; private set; } = StartingMorale;

        public bool IsExhausted => Fatigue >= ExhaustedFatigue;

        /// <summary>피로·사기에 따른 공격·방어 배율(1 = 정상).</summary>
        public float ConditionMultiplier
        {
            get
            {
                float m = Fatigue >= ExhaustedFatigue ? ConditionTuning.ExhaustedMultiplier
                    : Fatigue >= TiredFatigue ? ConditionTuning.TiredMultiplier : 1f;
                if (Morale < LowMorale) m *= ConditionTuning.LowMoraleMultiplier;
                return m;
            }
        }

        public void AddFatigue(int amount) => Fatigue = Math.Clamp(Fatigue + amount, 0, MaxCondition);
        public void AddMorale(int amount) => Morale = Math.Clamp(Morale + amount, 0, MaxCondition);

        /// <summary>저장 파일에서 불러올 때 피로·사기를 되돌린다.</summary>
        public void RestoreCondition(int fatigue, int morale)
        {
            Fatigue = Math.Clamp(fatigue, 0, MaxCondition);
            Morale = Math.Clamp(morale, 0, MaxCondition);
        }

        // ---------- 수련 단계 (마법사 = 마법 연구소, 힐러 = 성당 — RestVenues) ----------

        public const float ResearchHoursPerBonus = 4f;
        public const int MaxResearchLevel = 10;
        public const int HealerHolyPerResearchLevel = 2;

        /// <summary>마법사: 공격 대신 마력으로 싸운다(데이터의 공격 기본값·성장치·성장 횟수가 곧 마력).</summary>
        public bool UsesMagic => Class.Kind == MercenaryClassKind.Mage;

        /// <summary>UI용 공격 능력치 이름: 마법사 "마력", 그 외 "공격".</summary>
        public string OffenseLabel => UsesMagic ? "마력" : "공격";

        /// <summary>연구소·성당에서 쌓은 수련 단계(4시간마다 +1, 최대 10). 단계가 어떤 능력치가 되는지는 직업이 정한다(ResearchStatBonus).</summary>
        public int ResearchLevel { get; private set; }
        /// <summary>다음 단계까지 쌓인 수련 시간(0~4).</summary>
        public float ResearchHours { get; private set; }

        public bool CanResearchMore => ResearchLevel < MaxResearchLevel;

        /// <summary>수련 단계가 주는 능력치: 마법사 마력 +1/단계(공격에 더한 뒤 마력으로 옮겨진다), 힐러 신성력 +2/단계, 그 외 없음.</summary>
        public (int attack, int defense, int health, int holy) ResearchStatBonus() => Class.Kind switch
        {
            MercenaryClassKind.Mage => (ResearchLevel, 0, 0, 0),
            MercenaryClassKind.Healer => (0, 0, 0, ResearchLevel * HealerHolyPerResearchLevel),
            _ => (0, 0, 0, 0),
        };

        /// <summary>UI용: "마력 +3" / "신성력 +4" (보너스가 없으면 빈 문자열).</summary>
        public string ResearchBonusText()
        {
            var (attack, defense, health, holy) = ResearchStatBonus();
            var parts = new List<string>();
            if (attack > 0) parts.Add($"{OffenseLabel} +{attack}");
            if (defense > 0) parts.Add($"방어 +{defense}");
            if (health > 0) parts.Add($"체력 +{health}");
            if (holy > 0) parts.Add($"신성력 +{holy}");
            return string.Join(" · ", parts);
        }

        /// <summary>UI용 수련 이름: 마법사 "연구", 힐러 "기도".</summary>
        public string ResearchLabel => Class.Kind == MercenaryClassKind.Healer ? "기도" : "연구";

        /// <summary>수련 시간을 더하고 새로 오른 단계 수를 돌려준다. 최대 체력이 오르면 현재 체력도 그만큼 오른다. 최대가 되면 더 쌓지 않는다.</summary>
        public int AddResearch(float hours)
        {
            if (hours <= 0f || !CanResearchMore) return 0;
            ResearchHours += hours;
            int gained = 0;
            while (CanResearchMore && ResearchHours >= ResearchHoursPerBonus)
            {
                ResearchHours -= ResearchHoursPerBonus;
                int oldMax = CurrentStats.MaxHealth;
                ResearchLevel++;
                gained++;
                if (IsAlive)
                    CurrentHealth = Math.Min(CurrentStats.MaxHealth, CurrentHealth + (CurrentStats.MaxHealth - oldMax));
            }
            if (!CanResearchMore) ResearchHours = 0f;
            return gained;
        }

        /// <summary>저장 파일에서 불러올 때 수련 단계·시간을 되돌린다(체력보다 먼저 — 힐러는 최대 체력이 달라진다).</summary>
        public void RestoreResearch(float hours, int level)
        {
            ResearchLevel = Math.Clamp(level, 0, MaxResearchLevel);
            ResearchHours = CanResearchMore ? Math.Clamp(hours, 0f, ResearchHoursPerBonus) : 0f;
        }

        // ---------- 부상·질병 (규칙·수치는 Ailments, 입원은 Hospital) ----------

        private readonly List<Ailment> _ailments = new List<Ailment>();

        /// <summary>지금 앓는 부상·질병(부상 하나·질병 하나까지).</summary>
        public IReadOnlyList<Ailment> Ailments => _ailments;
        public bool HasAilment => _ailments.Count > 0;
        /// <summary>골절·열병처럼 중한 상태이상이 있으면 파견할 수 없다.</summary>
        public bool IsSeverelyAiling => _ailments.Exists(a => a.Def.isSevere);
        public bool HasInjury => _ailments.Exists(a => a.Def.IsInjury);
        public bool HasIllness => _ailments.Exists(a => !a.Def.IsInjury);

        /// <summary>같은 갈래(부상/질병)가 이미 있으면 걸리지 않는다. 걸렸으면 true.</summary>
        public bool AddAilment(AilmentSO def, float progress = 0f, int daysUntreated = 0)
        {
            if (def == null || _ailments.Exists(a => a.Def.IsInjury == def.IsInjury)) return false;
            _ailments.Add(new Ailment(def, Math.Clamp(progress, 0f, 0.99f), Math.Max(0, daysUntreated)));
            return true;
        }

        public void RemoveAilment(Ailment ailment) => _ailments.Remove(ailment);

        /// <summary>UI용: "골절 45% · 감기 10%" (없으면 빈 문자열).</summary>
        public string AilmentSummary() =>
            string.Join(" · ", _ailments.ConvertAll(a => $"{a.Def.displayName} {Math.Round(a.Progress * 100f)}%"));

        /// <summary>무기를 든다. 들고 있던 무기는 버린다(되팔기 없음).</summary>
        public void Equip(WeaponItem weapon)
        {
            var before = Grade;
            Weapon = weapon;
            NotifyGrade(before);
        }

        /// <summary>전투/이동 중 습격 등으로 깎이고, 회복 수단이 생기기 전까지는 계속 남아있는 실제 체력.</summary>
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;

        public Mercenary(string name, MercenaryClassSO mercenaryClass, int age = 25, int birthdayIndex = 0, ComposedCharacter appearance = null, Personality personality = Personality.Calm,
            bool hasRarePassive = false, MercenaryGrade talent = MercenaryGrade.D, string id = null)
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id; // id는 저장 파일에서 복원할 때만 넘긴다
            Talent = talent;
            Name = name;
            Class = mercenaryClass;
            Age = Math.Max(GrowthTuning.MinHireAge, age);
            BirthdayIndex = Math.Clamp(birthdayIndex, 0, 359);
            Appearance = appearance ?? new ComposedCharacter();
            Personality = personality;
            HasRarePassive = hasRarePassive;
            CurrentHealth = CurrentStats.MaxHealth;
        }

        // ---------- 나이·성장 (레벨·경험치 대신, 수치는 GrowthTuning) ----------

        private static readonly Random GrowthRng = new Random();

        /// <summary>나이(세). 해마다 생일(BirthdayIndex)에 한 살 먹는다(MercenaryAge). 나이가 성장 속도를 정한다(GrowthTuning.RateForAge).</summary>
        public int Age { get; private set; }
        /// <summary>생일: 한 해(360일)의 몇째 날(0~359).</summary>
        public int BirthdayIndex { get; private set; }

        /// <summary>능력치별 성장 횟수. 능력치 = 직업 기본값 + 횟수 × 직업 성장치(MercenaryStatCalculator).</summary>
        public int GrowthAttack { get; private set; }
        public int GrowthDefense { get; private set; }
        public int GrowthHealth { get; private set; }
        public int GrowthSpeed { get; private set; }
        public int GrowthHoly { get; private set; }
        public int TotalGrowth => GrowthAttack + GrowthDefense + GrowthHealth + GrowthSpeed + GrowthHoly;

        /// <summary>다음 성장 1회까지 쌓인 양(0~1). 훈련·실전이 함께 쌓는다.</summary>
        public float GrowthProgress { get; private set; }

        /// <summary>지금 나이의 성장 배율(0이면 더 자라지 않는다).</summary>
        public float GrowthRate => GrowthTuning.RateForAge(Age) * GrowthTuning.TalentRate(Talent);
        public bool CanGrow => GrowthRate > 0f;

        /// <summary>
        /// 성장량을 쌓는다(amount × 나이 배율). 1이 될 때마다 직업 성향대로 고른 능력치가 한 번 오르고, 오른 최대 체력만큼 현재 체력도 오른다.
        /// 돌려주는 값은 "공격 +3"처럼 오른 능력치 목록(없으면 빈 목록).
        /// </summary>
        public List<string> AddGrowth(float amount)
        {
            var gains = new List<string>();
            if (amount <= 0f || !CanGrow) return gains;
            var before = Grade;
            GrowthProgress += amount * GrowthRate;
            while (GrowthProgress >= 1f)
            {
                GrowthProgress -= 1f;
                gains.Add(GrowOnce(GrowthRng, +1));
            }
            NotifyGrade(before);
            return gains;
        }

        /// <summary>쇠퇴: 성장 count회만큼 직업 성향대로 고른 능력치가 내려간다(0 아래로는 안 내려간다). 내려간 능력치 목록.</summary>
        public List<string> Decline(int count)
        {
            var losses = new List<string>();
            var before = Grade;
            for (int i = 0; i < count && TotalGrowth > 0; i++) losses.Add(GrowOnce(GrowthRng, -1));
            NotifyGrade(before);
            return losses;
        }

        /// <summary>
        /// 예전 저장(버전 3 이하) 보정: 예전 공식(재능 능력치 배율)으로 잰 전투력에 가장 가깝게 성장을 더하거나 뺀다(직업 성향대로).
        /// 재능 배율이 빠지면서 S급은 약해지고 F급은 강해지는 것을 메운다. 알림은 내지 않는다.
        /// </summary>
        public void MatchLegacyPower(Random rng)
        {
            int target = GradeTable.CombatPower(ComputeStats(null, includeCondition: false, legacyTalent: true));
            for (int guard = 0; guard < 500; guard++)
            {
                int now = GradePower;
                if (now == target) break;
                int sign = now < target ? +1 : -1;
                if (sign < 0 && TotalGrowth == 0) break;
                // 한 번 더 움직였을 때 더 멀어지면 멈춘다
                var snapshot = (GrowthAttack, GrowthDefense, GrowthHealth, GrowthSpeed, GrowthHoly);
                GrowOnce(rng, sign);
                if (System.Math.Abs(GradePower - target) > System.Math.Abs(now - target))
                {
                    (GrowthAttack, GrowthDefense, GrowthHealth, GrowthSpeed, GrowthHoly) = snapshot;
                    break;
                }
            }
        }

        /// <summary>시장에 나올 때 이미 쌓은 경력: 성장 count회를 직업 성향대로 나눠 받고 체력을 가득 채운다.</summary>
        public void GrantStartingGrowth(int count, Random rng)
        {
            for (int i = 0; i < count; i++) GrowOnce(rng, +1);
            CurrentHealth = CurrentStats.MaxHealth;
        }

        /// <summary>성장(+1)·쇠퇴(-1) 한 번. 쇠퇴는 성장 횟수가 남은 능력치에서만 고른다. "공격 +3" / "체력 -6"을 돌려준다.</summary>
        private string GrowOnce(Random rng, int sign)
        {
            int stat = PickGrowthStat(rng, sign < 0);
            int oldMax = CurrentStats.MaxHealth;
            switch (stat)
            {
                case 0: GrowthAttack += sign; break;
                case 1: GrowthDefense += sign; break;
                case 2: GrowthHealth += sign; break;
                case 3: GrowthSpeed += sign; break;
                default: GrowthHoly += sign; break;
            }
            int newMax = CurrentStats.MaxHealth;
            if (IsAlive) CurrentHealth = Math.Clamp(CurrentHealth + (newMax - oldMax), 1, newMax);
            int amount = stat switch { 0 => Class.AttackGrowth, 1 => Class.DefenseGrowth, 2 => Class.HealthGrowth, 3 => Class.MoveSpeedGrowth, _ => Class.HolyGrowth };
            string label = stat switch { 0 => OffenseLabel, 1 => "방어", 2 => "체력", 3 => "속도", _ => "신성력" };
            return $"{label} {(sign > 0 ? "+" : "-")}{amount}";
        }

        /// <summary>
        /// 성장할 능력치를 직업 성향대로 고른다: 가중치 = 직업 성장치(마법사 공격 3 · 체력 3 · 속도 1이면 43% · 43% · 14%).
        /// 2026-10-16 전투력 가중치(공격 4 · 체력 1 …)를 곱하던 것을 뺐다 — 공격으로 70%까지 쏠려 원거리 직업이 키울수록 체력 낮은 유리대포가 됐다.
        /// 성장치가 0인 능력치(마법사 방어 등)는 오르지 않는다. onlyGrown이면 성장 횟수가 남은 능력치에서만(쇠퇴).
        /// </summary>
        private int PickGrowthStat(Random rng, bool onlyGrown)
        {
            int[] growth = { Class.AttackGrowth, Class.DefenseGrowth, Class.HealthGrowth, Class.MoveSpeedGrowth, Class.HolyGrowth };
            int[] counts = { GrowthAttack, GrowthDefense, GrowthHealth, GrowthSpeed, GrowthHoly };
            var weights = new int[growth.Length];
            int total = 0;
            for (int i = 0; i < growth.Length; i++)
            {
                if (onlyGrown && counts[i] <= 0) continue;
                weights[i] = Math.Max(0, growth[i]);
                total += weights[i];
            }
            if (total == 0) // 성장치가 모두 0이거나(이상한 직업) 쇠퇴할 게 남은 능력치의 성장치가 0일 때
            {
                for (int i = 0; i < growth.Length; i++)
                    if (!onlyGrown || counts[i] > 0) return i;
                return 2;
            }
            int roll = rng.Next(total);
            for (int i = 0; i < growth.Length; i++)
            {
                if (roll < weights[i]) return i;
                roll -= weights[i];
            }
            return 2;
        }

        /// <summary>UI용: 지금까지 성장으로 오른 능력치 "공격 +6 · 방어 +2 · 체력 +12"(없으면 빈 문자열).</summary>
        public string GrowthSummary()
        {
            var parts = new List<string>();
            if (GrowthAttack * Class.AttackGrowth > 0) parts.Add($"{OffenseLabel} +{GrowthAttack * Class.AttackGrowth}");
            if (GrowthDefense * Class.DefenseGrowth > 0) parts.Add($"방어 +{GrowthDefense * Class.DefenseGrowth}");
            if (GrowthHealth * Class.HealthGrowth > 0) parts.Add($"체력 +{GrowthHealth * Class.HealthGrowth}");
            if (GrowthSpeed * Class.MoveSpeedGrowth > 0) parts.Add($"속도 +{GrowthSpeed * Class.MoveSpeedGrowth}");
            if (GrowthHoly * Class.HolyGrowth > 0) parts.Add($"신성력 +{GrowthHoly * Class.HolyGrowth}");
            return string.Join(" · ", parts);
        }

        // ---------- 이번 달 기준값 (정보창 "공격 20 (+3 −2)"의 20, 매달 1일에 정산) ----------

        /// <summary>피로·사기·부상을 뺀 영구 능력치(성장·쇠퇴·무기·수련 포함).</summary>
        public CombatStats PermanentStats => ComputeStats(null, includeCondition: false);

        public int MonthBaseAttack { get; private set; }
        public int MonthBaseDefense { get; private set; }
        public int MonthBaseSpeed { get; private set; }
        public int MonthBaseHoly { get; private set; }
        public int MonthBasePower { get; private set; }

        /// <summary>이번 달 기준값을 지금 영구 수치로 찍는다(매달 1일 자정·고용 시장에서 만들 때·예전 저장 불러올 때).</summary>
        public void SnapshotMonth()
        {
            var s = PermanentStats;
            MonthBaseAttack = s.Offense;
            MonthBaseDefense = s.Defense;
            MonthBaseSpeed = s.MoveSpeed;
            MonthBaseHoly = s.Holy;
            MonthBasePower = GradeTable.CombatPower(s);
        }

        /// <summary>저장 파일에서 불러올 때 이번 달 기준값을 되돌린다.</summary>
        public void RestoreMonthBase(int attack, int defense, int speed, int holy, int power)
        {
            MonthBaseAttack = attack;
            MonthBaseDefense = defense;
            MonthBaseSpeed = speed;
            MonthBaseHoly = holy;
            MonthBasePower = power;
        }

        /// <summary>한 살 먹는다(MercenaryAge가 생일 자정에).</summary>
        public void GrowOlder() => Age++;

        /// <summary>저장 파일에서 불러올 때 나이·생일·성장을 되돌린다(체력보다 먼저 — 최대 체력이 달라진다).</summary>
        public void RestoreGrowth(int age, int birthdayIndex, int attack, int defense, int health, int speed, int holy, float progress)
        {
            Age = Math.Max(GrowthTuning.MinHireAge, age);
            BirthdayIndex = Math.Clamp(birthdayIndex, 0, 359);
            GrowthAttack = Math.Max(0, attack);
            GrowthDefense = Math.Max(0, defense);
            GrowthHealth = Math.Max(0, health);
            GrowthSpeed = Math.Max(0, speed);
            GrowthHoly = Math.Max(0, holy);
            GrowthProgress = Math.Clamp(progress, 0f, 0.999f);
        }

        /// <summary>저장 파일에서 불러올 때: 체력·무기를 그대로 되돌린다.</summary>
        public void RestoreState(int health, WeaponItem weapon)
        {
            Weapon = weapon;
            CurrentHealth = Math.Clamp(health, 0, CurrentStats.MaxHealth);
        }

        /// <summary>테스트·디버그용(게임 상태 조정 창). 재능을 바꾼다(성장 속도만 달라진다).</summary>
        public void SetTalent(MercenaryGrade talent) => Talent = talent;

        /// <summary>테스트·디버그용(게임 상태 조정 창). 나이를 바꾼다.</summary>
        public void SetAge(int age) => Age = Math.Max(GrowthTuning.MinHireAge, age);

        /// <summary>전투 시뮬레이션이 끝난 뒤 그 결과(Combatant.CurrentHealth)를 그대로 반영할 때 쓴다.</summary>
        public void SetHealth(int value)
        {
            CurrentHealth = Math.Clamp(value, 0, CurrentStats.MaxHealth);
        }

        /// <summary>휴식으로 체력을 완전히 회복한다.</summary>
        public void HealFully()
        {
            CurrentHealth = CurrentStats.MaxHealth;
        }

        public Combatant ToCombatant()
        {
            var combatant = new Combatant(Name, CurrentStats, ClassPassiveFactory.Create(Class.Kind, HasRarePassive));
            int missingHealth = combatant.Stats.MaxHealth - CurrentHealth;
            if (missingHealth > 0)
                combatant.TakeDamage(missingHealth);
            return combatant;
        }
    }
}
