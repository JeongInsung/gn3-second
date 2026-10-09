using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;

namespace GN3.Quests
{
    /// <summary>퀘스트 하나에 파견된 용병 조합. 여러 파견대가 동시에 진행될 수 있다.
    /// 목적지로 가는 중 → 도착(전투 대기) → 전투 후 귀환 중 → 마을 도착(ExpeditionLog.Complete) 순서로 진행된다.</summary>
    public class Expedition
    {
        public Quest Quest { get; }
        public IReadOnlyList<Mercenary> Members { get; }

        /// <summary>목적지까지 실제로 걸린 일수(자정마다 센다). 귀환 일수의 기준.</summary>
        public int OutboundDays { get; private set; }

        /// <summary>전투를 마치고 마을로 돌아오는 중.</summary>
        public bool IsReturning { get; private set; }
        public int ReturnDaysLeft { get; private set; }

        /// <summary>목적지에 도착해 전투를 기다리는 상태. 귀환 중에는 false.</summary>
        public bool IsReady => !IsReturning && Quest.RemainingDays <= 0;

        public Expedition(Quest quest, IReadOnlyList<Mercenary> members)
        {
            Quest = quest;
            Members = members;
        }

        /// <summary>저장 파일에서 불러올 때 이동·귀환 상태를 되돌린다.</summary>
        public void RestoreState(int outboundDays, bool returning, int returnDaysLeft)
        {
            OutboundDays = System.Math.Max(0, outboundDays);
            IsReturning = returning;
            ReturnDaysLeft = returning ? System.Math.Max(1, returnDaysLeft) : 0;
        }

        public void CountOutboundDay() => OutboundDays++;

        /// <summary>귀환길에 오른다. 갈 때 걸린 일수의 절반(올림, 최소 1일)이 걸린다.</summary>
        public void BeginReturn()
        {
            IsReturning = true;
            ReturnDaysLeft = System.Math.Max(1, (OutboundDays + 1) / 2);
        }

        public void AdvanceReturnDay()
        {
            if (ReturnDaysLeft > 0)
                ReturnDaysLeft--;
        }

        /// <summary>하루를 더 들여 생존 인원 전원을 완전히 회복시킨다.</summary>
        public void Rest()
        {
            if (IsReturning) ReturnDaysLeft++;
            else Quest.Delay(1);
            foreach (var member in Members.Where(m => m.IsAlive))
                member.HealFully();
        }
    }
}
