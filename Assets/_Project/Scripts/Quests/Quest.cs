using System;
using GN3.World;

namespace GN3.Quests
{
    public class Quest
    {
        public string Id { get; }
        public string Title { get; }
        public Region Region { get; }
        public string LocationName { get; }
        public string EnemyName { get; }
        public int EnemyCount { get; }
        public int Difficulty { get; }

        /// <summary>몬스터 계층(0=약탈 세력/1=지형 동물/2=보스). QuestGenerator가 쓰는 공식과 같다
        /// (난이도 1~3이 계층 0~2에 정확히 1:1 대응) - QuestClearHistory가 "어느 계층을 얼마나 깼는지" 셀 때 쓴다.</summary>
        public int Tier => Math.Clamp(Difficulty - 1, 0, 2);

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

        /// <summary>이 퀘스트에 파견 가능한 최대 인원. 적 숫자에 인원을 묶어서(특히 1마리뿐인 보스전)
        /// 파티 전원을 몰아보내 무조건 이기는 것을 막는다.</summary>
        public int MaxDispatchSize => EnemyCount + 1;

        public Quest(string title, Region region, string locationName, string enemyName, int enemyCount, int difficulty, int durationDays)
        {
            Id = Guid.NewGuid().ToString();
            Title = title;
            Region = region;
            LocationName = locationName;
            EnemyName = enemyName;
            EnemyCount = enemyCount;
            Difficulty = difficulty;
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
