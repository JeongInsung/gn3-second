using System;
using System.Collections.Generic;
using System.Linq;
using GN3.World;

namespace GN3.Quests
{
    public class Quest
    {
        public string Id { get; }
        public string Title { get; }
        public Region Region { get; }
        public string LocationName { get; }
        /// <summary>대표 적(마지막 라운드). 제목·예전 저장용.</summary>
        public string EnemyName { get; }
        /// <summary>모든 라운드 적 수 합(보상 계산 등).</summary>
        public int EnemyCount => Waves.Sum(w => w.Count);
        public int Difficulty { get; }

        /// <summary>부탁한 마을 사람과 부탁 글(QuestRequests). 예전 저장에서 불러온 퀘스트는 null.</summary>
        public string Requester { get; }
        public string RequestText { get; }
        /// <summary>게시판 맨 위 도전 퀘스트(열린 최고 레벨). 근처 길드가 가져가지 않는다.</summary>
        public bool IsChallenge { get; set; }
        /// <summary>연합 의뢰: 함께하는 근처 길드 id(없으면 null)와 그 길드가 보내는 용병 수. 보상은 나눈다(RivalDeals).</summary>
        public string AllyGuildId { get; set; }
        public int AllyCount { get; set; }
        public bool IsJoint => !string.IsNullOrEmpty(AllyGuildId);

        /// <summary>성공하면 금화와 함께 받는 건설 재료 묶음(없으면 빈 목록 — QuestGenerator가 일부 의뢰에 붙인다).</summary>
        public List<(GN3.Economy.LootItem item, int count)> MaterialReward { get; } = new List<(GN3.Economy.LootItem, int)>();

        /// <summary>이 의뢰의 적들에게서 나올 수 있는 건설 재료(게시판 "얻는 재료").</summary>
        public IEnumerable<GN3.Economy.LootItem> DroppedMaterials =>
            Waves.SelectMany(w => GN3.Economy.LootCatalog.MaterialsForEnemy(w.EnemyName)).Distinct();

        /// <summary>"쇠못 2 · 밧줄 1"(재료 보상이 없으면 빈 문자열).</summary>
        public string MaterialRewardText => string.Join(" · ", MaterialReward.Select(m => $"{m.item.Name} {m.count}"));

        /// <summary>라운드 구성(순서대로). 한 라운드짜리도 있다.</summary>
        public IReadOnlyList<QuestWave> Waves { get; }
        public int WaveCount => Waves.Count;
        /// <summary>한 라운드에 나오는 적의 최대 수(파견 인원·습격 규모 기준).</summary>
        public int MaxWaveSize => Waves.Max(w => w.Count);

        /// <summary>가장 높은 몬스터 계층(0=약탈 세력/1=지형 동물/2=보스). 난이도는 은월의 숲 Lv(1~5)라 계층과 1:1이 아니다.</summary>
        public int Tier => Waves.Max(w => w.Tier);

        public const float HoursPerDay = 24f;

        /// <summary>게임 내 임무 소요 일수(게시판·보상 계산용). 실제 이동은 출발 순간부터 DurationDays × 24시간(RemainingHours).</summary>
        public int DurationDays { get; }
        /// <summary>목적지까지 남은 게임 시간. 게임 시간이 흐를 때마다(ExpeditionLog) 줄어든다.</summary>
        public float RemainingHours { get; private set; }
        /// <summary>표시용: 남은 시간을 일로 올림.</summary>
        public int RemainingDays => (int)System.Math.Ceiling(RemainingHours / HoursPerDay);
        public bool IsReady => RemainingHours <= 0f;

        /// <summary>전투 로그 연출 재생 시간(초). DurationDays(게임 내 일수)와는 별개로, 전투 결과를 화면에 뿌리는 속도 조절용.</summary>
        public float EstimatedDurationSeconds => 3f + Difficulty * 1.5f + EnemyCount * 0.5f;

        /// <summary>은월의 숲 레벨별 필요 인원(정확히 이만큼 보내야 출발): Lv3·4 4명, Lv5 앤트킹 6명(두 분대). 0이면 1명~MaxDispatchSize 자유.</summary>
        public int RequiredDispatchSize => Difficulty >= 5 ? 6 : Difficulty >= 3 ? 4 : 0;
        /// <summary>분대 수. Lv5 앤트킹은 3명씩 두 분대가 한 판에서 같이 싸운다(분대마다 분대장).</summary>
        public int SquadCount => Difficulty >= 5 ? 2 : 1;
        /// <summary>분대 하나의 인원(필요 인원이 없으면 최대 파견 인원).</summary>
        public int SquadSize => RequiredDispatchSize > 0 ? RequiredDispatchSize / SquadCount : MaxDispatchSize;

        /// <summary>초반 소규모 의뢰의 최대 인원: Lv1은 혼자 가는 의뢰(1명), Lv2는 둘이 가는 의뢰(2명). 그 밖은 0(제한 없음).</summary>
        public static int EarlyMaxDispatch(int level) => level <= 1 ? 1 : level == 2 ? 2 : 0;

        /// <summary>이 퀘스트에 파견 가능한 최대 인원. 필요 인원이 정해진 레벨(Lv3+)은 그 인원, 초반(Lv1·2)은 1명·2명,
        /// 아니면 적 숫자에 인원을 묶어서 파티 전원을 몰아보내 무조건 이기는 것을 막는다.</summary>
        public int MaxDispatchSize => RequiredDispatchSize > 0 ? RequiredDispatchSize
            : EarlyMaxDispatch(Difficulty) > 0 ? EarlyMaxDispatch(Difficulty)
            : MaxWaveSize + 1;

        /// <param name="waves">라운드 구성. 없으면(예전 저장) enemyName × enemyCount 한 라운드.</param>
        public Quest(string title, Region region, string locationName, string enemyName, int enemyCount, int difficulty, int durationDays,
            IReadOnlyList<QuestWave> waves = null, string requester = null, string requestText = null)
        {
            Id = Guid.NewGuid().ToString();
            Requester = requester;
            RequestText = requestText;
            Title = title;
            Region = region;
            LocationName = locationName;
            EnemyName = enemyName;
            Difficulty = difficulty;
            Waves = waves != null && waves.Count > 0
                ? waves
                : new List<QuestWave> { new QuestWave(enemyName, enemyCount, Math.Clamp(difficulty - 1, 0, 2)) };
            DurationDays = durationDays;
            RemainingHours = durationDays * HoursPerDay;
        }

        /// <summary>저장 파일에서 불러올 때 남은 시간을 되돌린다.</summary>
        public void SetRemainingHours(float hours) => RemainingHours = System.Math.Max(0f, hours);

        /// <summary>이동한 시간만큼 남은 시간을 줄인다.</summary>
        public void Advance(float hours)
        {
            if (hours > 0f)
                RemainingHours = System.Math.Max(0f, RemainingHours - hours);
        }

        /// <summary>테스트·디버그용(게임 상태 조정 창). 바로 도착한 것으로 만든다.</summary>
        public void ArriveNow() => RemainingHours = 0f;

        /// <summary>전투력이 넉넉해 빨리 도착할 때 남은 시간을 days일만큼 줄인다(최소 1일 남김).</summary>
        public void Shorten(int days)
        {
            if (days > 0)
                RemainingHours = System.Math.Max(HoursPerDay, RemainingHours - days * HoursPerDay);
        }

        /// <summary>휴식 등으로 도착이 늦어질 때 남은 시간을 days일만큼 늘린다.</summary>
        public void Delay(int days)
        {
            if (days > 0)
                RemainingHours += days * HoursPerDay;
        }
    }
}
