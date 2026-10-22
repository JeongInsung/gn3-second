using System;
using System.Collections.Generic;
using System.Linq;
using GN3.World;
using UnityEngine;

namespace GN3.Quests
{
    /// <summary>
    /// 지역·은월의 숲 레벨(1~5)별로 지금까지 몇 번 깼는지 센다(저장 필드 이름은 예전대로 tier — 예전 저장의 계층 0~2 기록은
    /// 레벨로 읽혀 가중치에만 조금 영향). QuestBoard가 게시판을 뽑을 때
    /// 이미 많이 깬 레벨은 가중치를 낮추고(= 나올 비율이 줄고) 아직 안 깨봤거나 적게 깬 레벨은 상대적으로
    /// 더 자주 나오게 해서, 플레이어가 성장할수록 "쉬운 건 줄고 어려운 게 늘어나는" 체감을 준다.
    /// Affinity.cs와 같은 패턴(static Dictionary + SaveEntries/Restore)을 따른다.
    /// </summary>
    public static class QuestClearHistory
    {
        private static readonly Dictionary<(Region region, int tier), int> _clears = new Dictionary<(Region, int), int>();

        public static event Action OnChanged;

        public static int ClearsOf(Region region, int tier) => _clears.TryGetValue((region, tier), out int v) ? v : 0;

        /// <summary>퀘스트 승리 시 호출(ExpeditionBattle.Conclude).</summary>
        public static void RecordClear(Region region, int tier)
        {
            var key = (region, tier);
            _clears[key] = ClearsOf(region, tier) + 1;
            OnChanged?.Invoke();
        }

        // ---------- 저장 ----------

        public static List<Save.QuestClearSave> SaveEntries() =>
            _clears.Select(kv => new Save.QuestClearSave { region = (int)kv.Key.region, tier = kv.Key.tier, count = kv.Value }).ToList();

        /// <summary>저장 파일에서 불러올 때.</summary>
        public static void Restore(IEnumerable<Save.QuestClearSave> entries)
        {
            _clears.Clear();
            if (entries != null)
                foreach (var e in entries)
                    if (e.count > 0) _clears[((Region)e.region, e.tier)] = e.count;
            OnChanged?.Invoke();
        }

        // 이 프로젝트는 Enter Play Mode Options에서 Domain Reload가 꺼져 있어, Play를 다시 시작해도 static이
        // 유지된다(Guild/Affinity 등과 같은 사정) - Play 진입마다 비워서 새 게임이 예전 클리어 기록을 안 물려받게 한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _clears.Clear();
            OnChanged = null;
        }
    }
}
