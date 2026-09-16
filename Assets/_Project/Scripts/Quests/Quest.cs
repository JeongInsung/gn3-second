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

        /// <summary>게임 내 임무 소요 일수. "하루 지나기"로 줄어드는 RemainingDays의 초기값.</summary>
        public int DurationDays { get; }
        public int RemainingDays { get; private set; }
        public bool IsReady => RemainingDays <= 0;

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
            RemainingDays = durationDays;
        }

        public void AdvanceDay()
        {
            if (RemainingDays > 0)
                RemainingDays--;
        }

        /// <summary>휴식 등으로 도착이 늦어질 때 남은 일수를 늘린다.</summary>
        public void Delay(int days)
        {
            if (days > 0)
                RemainingDays += days;
        }
    }
}
