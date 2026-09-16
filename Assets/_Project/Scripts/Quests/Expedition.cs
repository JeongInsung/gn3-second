using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;

namespace GN3.Quests
{
    /// <summary>퀘스트 하나에 파견된 용병 조합. 여러 파견대가 동시에 진행될 수 있다.</summary>
    public class Expedition
    {
        public Quest Quest { get; }
        public IReadOnlyList<Mercenary> Members { get; }

        public bool IsReady => Quest.RemainingDays <= 0;

        public Expedition(Quest quest, IReadOnlyList<Mercenary> members)
        {
            Quest = quest;
            Members = members;
        }

        /// <summary>하루를 더 들여 생존 인원 전원을 완전히 회복시킨다.</summary>
        public void Rest()
        {
            Quest.Delay(1);
            foreach (var member in Members.Where(m => m.IsAlive))
                member.HealFully();
        }
    }
}
