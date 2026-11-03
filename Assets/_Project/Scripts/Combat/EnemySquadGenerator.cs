using System;
using System.Collections.Generic;
using GN3.World;

namespace GN3.Combat
{
    public static class EnemySquadGenerator
    {
        // 전투 전체 난도(자동·직접·습격 공통). 2026-10-16 체력·공격 ×1.4 더 올림(1.3·1.15 → 1.82·1.61):
        // 용병 성장·연구·스킬이 붙으며 예전 값으로는 판정 '중간' 승률이 82%까지 올라 쉬웠다.
        // 권장 전투력(QuestDifficulty.RecommendedScale)도 함께 다시 맞췄다 — 승률 수치는 그쪽 주석.
        // 같은 날 전투 피해·회복 ×0.35(CombatFormulas.DamageScale)와 함께 체력 1.82 → 2.4, 공격 1.61 → 1.2:
        // 용병이 판정 3 몬스터를 4~5대에 잡고(전엔 한 방), 판정별 승률은 그대로(자동 전투 3000판: 중간 59→60%, 어려움 27→25%).
        public const float HealthMultiplier = 2.4f;
        // 같은 날 다시 공격 1.2 → 1.44(×1.2): 전투 긴박감. 판정(권장 전투력)은 그대로 둬 같은 판정에서 조금 더 어렵다
        // (자동 전투 6000판: 중간 64→49%, 어려움 26→15%. ×1.4는 중간 33%로 너무 어려웠다).
        // 2026-10-26 1.44 → 1.52: 피해 배율 상향(CombatFormulas.DamageScale 0.35 → 0.55)으로 직접 전투 승률이 조금 올라(Lv2 66→75%) 되돌린다.
        // 2026-10-26 1.52 → 1.3: "너무 어렵다" — 피해 배율 0.9 상향 뒤 직접 전투 승률(조작 없이) Lv1 78 · Lv2 48 · Lv3 34%를 레벨마다 약 +10%p.
        public const float AttackMultiplier = 1.3f;

        // 보스(계층 2)는 여러 라운드 퀘스트의 마지막에 혼자 나와 앞 라운드를 버틴 파견대(최대 인원 = 가장 큰 라운드 + 1)를
        // 상대하므로 따로 세게.
        public const float SporeHealthMultiplier = 0.67f; // 독버섯(독이 더해지는 원거리 몹). 피해 ×0.35 뒤 전투가 길어져 0.8이면 늑대보다 어려웠다(중간 24% vs 39%) → 0.67(39%)
        public const float RiderHealthMultiplier = 0.9f; // 고블린 늑대기수(늑대를 탄 고블린): 빠른 대신(회피↑) 조금 덜 단단 — 1.2면 고블린보다 크게 어려웠다(중간 28% vs 43%), 0.9면 47%
        public const int RiderMoveSpeedBonus = 2;
        // 그루터기 엔트(숲 지형 근접 몬스터, 2026-10-26): 느리고 단단한 탱커 — 체력을 늘린 대신 느리다(공격은 보통).
        public const float StumpHealthMultiplier = 1.5f;
        public const int StumpMoveSpeedPenalty = 1;
        // 보스 앤트킹(예전 이름 거목 엔트, 은월의 숲의 주인, 은월의 숲 Lv5): 아주 단단하고 세게 친다. 제자리에 뿌리박고 3타마다 뿌리 내려찍기
        // (직접 전투는 범위 예고 뒤 분출 — 분대장은 피할 수 있다, RootSlamPassive). 2026-10-19 사용자 요청으로 체력 1.1 → 2.5, 공격 ×1.5.
        // 2026-10-28 2.5 → 1.9(×0.75): 격노 2페이즈(포효가 잦아지고 그루터기 엔트 2마리 소환)를 더하며 판정 '중간' 승률이 약 −35%p 떨어져
        // 체력을 덜어 맞췄다(자동 전투 Lv5 600판: 격노 전 59% → 격노 포함 50%).
        public const float EntHealthMultiplier = 1.9f;
        public const float EntAttackMultiplier = 1.5f;
        public const int EntMoveSpeedPenalty = 2;
        public const float BossHealthMultiplier = 3f;
        // 은월의 숲 Lv5 앤트킹은 6명(두 분대)이 같이 잡는다 → 체력을 더 두껍게.
        public const float TwoSquadBossHealthMultiplier = 1.6f;
        public const int TwoSquadBossDifficulty = 5;
        public const float BossAttackMultiplier = 1.3f;

        /// <summary>
        /// 초반 소규모 의뢰(은월의 숲 Lv1 혼자 · Lv2 둘이 — Quest.EarlyMaxDispatch)의 적 체력·공격 배율: Lv1 ×0.75, Lv2 ×0.85.
        /// 같은 난이도의 이동 중 습격도 함께 약하다. 권장 전투력(QuestDifficulty.PowerPerEnemy)도 같은 배율을 쓴다.
        /// </summary>
        public static float EarlyStatMultiplier(int difficulty) => difficulty <= 1 ? 0.75f : difficulty == 2 ? 0.85f : 1f;

        /// <summary>tierOverride를 주면 이름 계층(약탈 세력/지형 동물/보스)은 그 값으로 고정하고
        /// difficulty는 스탯 강도에만 반영한다. 예: 이동 중 습격은 항상 "약탈 세력" 이름(tier 0)을 쓰되,
        /// 퀘스트 난이도에 맞춰 스탯만 세게 만들 때 사용. name을 주면 모두 그 이름(퀘스트 라운드의 적), 없으면 계층 목록에서 무작위.</summary>
        public static List<Combatant> Generate(Region region, int count, int difficulty, Random rng, int? tierOverride = null, string name = null)
        {
            int tier = tierOverride ?? Math.Clamp(difficulty - 1, 0, 2);
            float healthMul = HealthMultiplier * (tier >= 2 ? BossHealthMultiplier : 1f)
                * (tier >= 2 && difficulty >= TwoSquadBossDifficulty ? TwoSquadBossHealthMultiplier : 1f);
            float attackMul = AttackMultiplier * (tier >= 2 ? BossAttackMultiplier : 1f);
            float early = EarlyStatMultiplier(difficulty); // 초반(Lv1·2) 소규모 의뢰는 약하게
            healthMul *= early;
            attackMul *= early;

            var result = new List<Combatant>(count);
            for (int i = 0; i < count; i++)
            {
                string enemyName = name ?? RegionInfo.GetMonsterName(region, tier, rng);
                int attack = 6 + difficulty * 2 + rng.Next(-1, 2);
                int defense = 2 + difficulty + rng.Next(-1, 2);
                int health = 20 + difficulty * 8 + rng.Next(-3, 4);
                int moveSpeed = 4 + difficulty + rng.Next(-1, 2);

                // 독버섯: 맞히면 독(PoisonSporePassive)이 더해지는 원거리 몹이라 체력은 조금 낮게.
                bool spore = enemyName.Contains("버섯");
                bool rider = enemyName.Contains("늑대기수");
                bool ent = RegionInfo.IsEnt(enemyName);
                bool stump = RegionInfo.IsStump(enemyName);
                float speciesHealth = spore ? SporeHealthMultiplier : rider ? RiderHealthMultiplier : ent ? EntHealthMultiplier
                    : stump ? StumpHealthMultiplier : 1f;
                if (rider) moveSpeed += RiderMoveSpeedBonus;
                if (ent) moveSpeed -= EntMoveSpeedPenalty;
                if (stump) moveSpeed -= StumpMoveSpeedPenalty;

                // 개체값: 같은 몬스터라도 개체마다 0.85~1.15배(속도 −2~+2), 합으로 등급(약한·보통·강한·우두머리)
                var iv = MonsterIV.Roll(rng);
                moveSpeed += MonsterIV.SpeedBonus(iv.Speed);

                var stats = new CombatStats(
                    attack: Math.Max(1, (int)Math.Round(attack * attackMul * (ent ? EntAttackMultiplier : 1f) * MonsterIV.Multiplier(iv.Attack))),
                    defense: Math.Max(0, (int)Math.Round(defense * MonsterIV.Multiplier(iv.Defense))),
                    maxHealth: Math.Max(1, (int)Math.Round(health * healthMul * speciesHealth * MonsterIV.Multiplier(iv.Health))),
                    moveSpeed: Math.Max(0, moveSpeed));

                var passives = spore ? new List<PassiveBase> { new PoisonSporePassive() }
                    : ent ? new List<PassiveBase> { new RootSlamPassive() } : null;
                // 등급 앞말("강한 고블린"). 보스는 칭호·배너가 정확한 이름을 쓰므로 앞말 없이 개체값만.
                string shownName = tier >= 2 ? enemyName : MonsterIV.Name(iv.Rank, enemyName);
                result.Add(new Combatant(shownName, stats, passives));
            }
            return result;
        }

        // ---------- 라운드 꾸미기(직접·자동 전투 공통) ----------

        /// <summary>
        /// 한 라운드 적에 역할을 붙인다: 2명 이상이면 3명 중 1명꼴(3번째·6번째…)이 궁수("… 궁수"), 4명 이상이면 4번째 그냥 고블린이 목수,
        /// 3명 이상이면 둘째 그냥 고블린이 방패병. 짐승(늑대·버섯·엔트·그루터기)은 활을 쏘지 않는다 — 궁수 자리여도 그대로.
        /// 직접 전투(FieldBattleSession)와 자동 전투가 같은 라운드 구성을 쓰게 ExpeditionBattle.SpawnWave에서 부른다. 새 목록을 돌려준다.
        /// </summary>
        public static List<Combatant> Dress(List<Combatant> wave)
        {
            var result = new List<Combatant>(wave.Count);
            for (int i = 0; i < wave.Count; i++)
            {
                var c = wave[i];
                if (IsArcherSlot(i, wave.Count) && !IsBeast(c.Name))
                    c = Renamed(c, c.Name + " 궁수");
                else if (IsCarpenterSlot(i, wave.Count) && IsPlainGoblin(c.Name))
                    c = Renamed(c, c.Name + " 목수"); // 목재·쇠못을 짊어진 근접 일꾼(LootCatalog)
                else if (IsShieldSlot(i, wave.Count) && IsPlainGoblin(c.Name))
                    c = AsShieldBearer(c);
                result.Add(c);
            }
            return result;
        }

        /// <summary>이름으로 본 궁수(Dress가 붙인 "… 궁수").</summary>
        public static bool IsArcher(string name) => name != null && name.EndsWith("궁수");

        private static bool IsArcherSlot(int index, int count) => count >= 2 && index % 3 == 2;

        /// <summary>고블린 목수 자리: 4마리 이상 라운드의 4번째(궁수 자리와 겹치지 않는다).</summary>
        private static bool IsCarpenterSlot(int index, int count) => count >= 4 && index == 3;

        /// <summary>고블린 방패병 자리: 3마리 이상 라운드의 둘째 — 홀수 자리가 앞줄(x가 120 더 아군 쪽)이라 맨 앞에 선다
        /// (궁수 i%3==2·목수 i==3과 겹치지 않는다).</summary>
        private static bool IsShieldSlot(int index, int count) => count >= 3 && index == 1;

        // 고블린 방패병: 단단한 대신 약하게 친다. 막기(ShieldBlockPassive)까지 더해 앞에서 버틴다.
        private const float ShieldHealthMultiplier = 1.3f;
        private const float ShieldAttackMultiplier = 0.8f;

        /// <summary>그냥 고블린을 방패병으로: 이름 뒤에 "방패병", 체력↑·공격↓, 방패 막기 패시브.</summary>
        private static Combatant AsShieldBearer(Combatant c)
        {
            var s = c.Stats;
            var stats = new CombatStats(
                attack: Math.Max(1, (int)Math.Round(s.Attack * ShieldAttackMultiplier)),
                defense: s.Defense,
                maxHealth: Math.Max(1, (int)Math.Round(s.MaxHealth * ShieldHealthMultiplier)),
                moveSpeed: s.MoveSpeed, magic: s.Magic, holy: s.Holy);
            var passives = new List<PassiveBase>(c.Passives) { new ShieldBlockPassive() };
            return new Combatant(c.Name + " 방패병", stats, passives);
        }

        /// <summary>그냥 고블린(늑대기수·궁수·목수·방패병이 아닌).</summary>
        private static bool IsPlainGoblin(string name) =>
            name != null && name.Contains("고블린") && !name.Contains("늑대기수") && !name.Contains("궁수") && !name.Contains("목수")
            && !name.Contains("방패병");

        /// <summary>활을 쓰지 않는 짐승·괴물 적(이름에 늑대·울프·버섯·엔트·그루터기). 전투 그림도 그 종류로 나온다(BattleMonsterSprite.KindFor).</summary>
        private static bool IsBeast(string name) => name != null && (name.Contains("늑대") || name.Contains("울프") || name.Contains("버섯") || RegionInfo.IsEnt(name) || RegionInfo.IsStump(name));

        private static Combatant Renamed(Combatant c, string name) => new Combatant(name, c.Stats, c.Passives);
    }
}
