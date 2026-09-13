using UnityEngine;

namespace GN3.Mercenaries
{
    [CreateAssetMenu(menuName = "GN3/Mercenary Class", fileName = "NewMercenaryClass")]
    public class MercenaryClassSO : ScriptableObject
    {
        public string ClassName;
        public MercenaryClassKind Kind;

        [Header("Base Stats (Level 1)")]
        public int BaseAttack;
        public int BaseDefense;
        public int BaseHealth;

        [Header("Growth Per Level")]
        public int AttackGrowth;
        public int DefenseGrowth;
        public int HealthGrowth;

        [Header("Move Speed (회피 확률에 영향)")]
        public int BaseMoveSpeed;
        public int MoveSpeedGrowth;
    }
}
