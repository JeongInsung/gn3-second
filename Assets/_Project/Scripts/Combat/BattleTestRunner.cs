using System.Collections.Generic;
using UnityEngine;

namespace GN3.Combat
{
    public class BattleTestRunner : MonoBehaviour
    {
        [SerializeField] private int seed = 0;

        private void Start()
        {
            var teamA = new List<Combatant>
            {
                new Combatant("전사", new CombatStats(attack: 12, defense: 5, maxHealth: 40)),
                new Combatant("궁수", new CombatStats(attack: 15, defense: 2, maxHealth: 25)),
            };

            var teamB = new List<Combatant>
            {
                new Combatant("고블린", new CombatStats(attack: 8, defense: 3, maxHealth: 30)),
                new Combatant("오크", new CombatStats(attack: 10, defense: 4, maxHealth: 35)),
            };

            var simulator = new AutoBattleSimulator(seed);
            var result = simulator.Simulate(teamA, teamB);

            foreach (var evt in result.Log)
            {
                string suffix = evt.TargetDefeated ? " (defeated)" : "";
                Debug.Log($"[R{evt.Round}] {evt.Attacker.Name} -> {evt.Target.Name} : {evt.Damage} dmg{suffix}");
            }

            Debug.Log($"Outcome: {result.Outcome} after {result.Rounds} rounds");
        }
    }
}
