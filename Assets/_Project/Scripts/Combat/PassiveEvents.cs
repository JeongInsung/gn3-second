using System.Collections.Generic;

namespace GN3.Combat
{
    /// <summary>
    /// 패시브가 공격 판정 밖에서 일으킨 일(치유의 기운 회복 · 아군 부활 · 즉사 · 광역 도발)을 화면에 띄우려고 모아 두는 곳.
    /// 직접 전투(FieldBattleSession.Tick)가 모으는 동안만 쌓이고, 그 틱의 이벤트 끝에 붙는다. 자동 전투는 모으지 않는다(로그 그대로).
    /// </summary>
    public static class PassiveEvents
    {
        private static readonly List<BattleEvent> _pending = new List<BattleEvent>();

        /// <summary>지금 모으는 중인가(FieldBattleSession.Tick 안).</summary>
        public static bool Collecting { get; private set; }

        public static void Push(BattleEvent e)
        {
            if (Collecting) _pending.Add(e);
        }

        public static void Begin()
        {
            _pending.Clear();
            Collecting = true;
        }

        /// <summary>모은 것을 events 끝에 붙이고 모으기를 멈춘다.</summary>
        public static void End(List<BattleEvent> events)
        {
            events.AddRange(_pending);
            _pending.Clear();
            Collecting = false;
        }
    }
}
