namespace GN3.Traits
{
    public readonly struct PersonalityModifier
    {
        public readonly string Label;
        public readonly string Description;
        public readonly float AttackMultiplier;
        public readonly float DefenseMultiplier;
        public readonly float HealthMultiplier;

        public PersonalityModifier(string label, string description, float attackMultiplier, float defenseMultiplier, float healthMultiplier)
        {
            Label = label;
            Description = description;
            AttackMultiplier = attackMultiplier;
            DefenseMultiplier = defenseMultiplier;
            HealthMultiplier = healthMultiplier;
        }
    }
}
