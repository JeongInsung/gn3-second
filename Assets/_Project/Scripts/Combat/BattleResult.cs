using System.Collections.Generic;

namespace GN3.Combat
{
    public enum BattleOutcome
    {
        TeamAVictory,
        TeamBVictory,
        Draw
    }

    public class BattleResult
    {
        public BattleOutcome Outcome;
        public int Rounds;
        public List<BattleEvent> Log;
        public List<Combatant> TeamA;
        public List<Combatant> TeamB;
    }
}
