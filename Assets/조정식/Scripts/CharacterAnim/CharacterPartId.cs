namespace GN3.CharacterAnim
{
    /// <summary>파츠 시트 파일명(확장자 제외)과 1:1로 대응하는 파츠 이름 상수.</summary>
    public static class CharacterPartId
    {
        public const string Torso = "torso";
        public const string ArmFront = "arm_f";
        public const string ArmBack = "arm_b";
        public const string LegFront = "leg_f";
        public const string LegBack = "leg_b";
        public const string Head = "head";
        public const string Hair = "hair";
        public const string Weapon = "weapon";
        public const string SlashFx = "slash_fx";

        /// <summary>옷이 섞이지 않도록 반드시 같은 캐릭터 폴더에서 함께 뽑아야 하는 파츠.</summary>
        public static readonly string[] BodySetParts = { Torso, ArmFront, ArmBack, LegFront, LegBack };

        /// <summary>
        /// 머리 세트: head/hair를 같은 캐릭터 폴더에서 함께 뽑는다.
        /// 툴의 "파츠 나누기"로 만든 캐릭터는 head에 얼굴+머리카락이 통째로 들어가고 hair가 비어 있어서,
        /// 서로 다른 캐릭터에서 따로 뽑으면 머리카락이 겹치거나 대머리가 된다. (기존 캐릭터는 head가 전부 같은 맨몸 얼굴이라 결과 동일)
        /// </summary>
        public static readonly string[] HeadSetParts = { Head, Hair };

        /// <summary>캐릭터 폴더와 무관하게 각각 독립적으로 랜덤 선택되는 파츠.</summary>
        public static readonly string[] IndependentParts = { Weapon };
    }
}
