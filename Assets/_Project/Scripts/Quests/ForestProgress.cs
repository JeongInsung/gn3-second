using System;
using UnityEngine;

namespace GN3.Quests
{
    /// <summary>
    /// 은월의 숲 레벨(Lv1~5) 진행. Lv1부터 열려 있고, 지금 열린 최고 레벨 퀘스트를 깨면 다음 레벨이 열린다.
    /// 레벨이 곧 퀘스트 난이도(Quest.Difficulty)이고, 보스 앤트킹은 Lv5에서만 나온다(QuestGenerator.Generate).
    /// QuestClearHistory와 같은 패턴(static + 저장/불러오기).
    /// </summary>
    public static class ForestProgress
    {
        public const int MaxLevel = 5;

        public static int UnlockedLevel { get; private set; } = 1;

        public static event Action OnChanged;

        /// <summary>퀘스트 승리 시 호출(ExpeditionBattle.Conclude). 열린 최고 레벨을 깼으면 다음 레벨을 열고 true.</summary>
        public static bool TryUnlockNext(int clearedLevel)
        {
            if (clearedLevel < UnlockedLevel || UnlockedLevel >= MaxLevel) return false;
            UnlockedLevel++;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>숲의 주인 앤트킹(Lv5 의뢰 마지막 라운드)을 한 번이라도 쓰러뜨렸는가.</summary>
        public static bool BossDefeated { get; private set; }

        /// <summary>Lv5 의뢰를 이겼을 때(ExpeditionBattle). 처음이면 true(축하 알림).</summary>
        public static bool MarkBossDefeated()
        {
            if (BossDefeated) return false;
            BossDefeated = true;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>지금 목표 한 줄(게시판·길드 툴팁): 다음 레벨을 여는 법, 다 열렸으면 앤트킹, 쓰러뜨렸으면 완료.</summary>
        public static string GoalText =>
            UnlockedLevel < MaxLevel ? $"은월의 숲 Lv{UnlockedLevel}/{MaxLevel} · 다음 목표: Lv{UnlockedLevel} [도전] 의뢰를 깨면 Lv{UnlockedLevel + 1} 개방"
            : !BossDefeated ? $"은월의 숲 Lv{MaxLevel}/{MaxLevel} · 최종 목표: 숲의 주인 앤트킹 토벌"
            : $"은월의 숲 Lv{MaxLevel}/{MaxLevel} · 숲의 주인 앤트킹 토벌 완료";

        /// <summary>저장 파일에서 불러올 때. 예전 저장엔 값이 없어(0) Lv1부터.</summary>
        public static void Restore(int saved, bool bossDefeated = false)
        {
            UnlockedLevel = Math.Clamp(saved, 1, MaxLevel);
            BossDefeated = bossDefeated;
            OnChanged?.Invoke();
        }

        // Domain Reload가 꺼져 있어 Play마다 비운다(QuestClearHistory와 같은 사정).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            UnlockedLevel = 1;
            BossDefeated = false;
            OnChanged = null;
        }
    }
}
