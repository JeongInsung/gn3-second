using System;
using UnityEngine;

namespace GN3.Characters
{
    [Serializable]
    public class CharacterAppearance
    {
        public Sprite Head;
        public Sprite Body;
        public Sprite Arm;
        public Sprite Leg;
        public Sprite Weapon;

        public static CharacterAppearance GenerateRandom(System.Random rng)
        {
            return new CharacterAppearance
            {
                Head = CharacterPartLibrary.GetRandom(CharacterPartCategory.Head, rng),
                Body = CharacterPartLibrary.GetRandom(CharacterPartCategory.Body, rng),
                Arm = CharacterPartLibrary.GetRandom(CharacterPartCategory.Arm, rng),
                Leg = CharacterPartLibrary.GetRandom(CharacterPartCategory.Leg, rng),
                Weapon = CharacterPartLibrary.GetRandom(CharacterPartCategory.Weapon, rng),
            };
        }
    }
}
