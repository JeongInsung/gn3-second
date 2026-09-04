using UnityEngine;

namespace GN3.Mercenaries
{
    [CreateAssetMenu(menuName = "GN3/Mercenary Class", fileName = "NewMercenaryClass")]
    public class MercenaryClassSO : ScriptableObject
    {
        public string ClassName;

        [Header("Base Stats (Level 1)")]
        public int BaseAttack;
        public int BaseDefense;
        public int BaseHealth;

        [Header("Growth Per Level")]
        public int AttackGrowth;
        public int DefenseGrowth;
        public int HealthGrowth;
    }
}
