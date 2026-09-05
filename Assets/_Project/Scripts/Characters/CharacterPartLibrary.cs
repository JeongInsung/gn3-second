using System.Collections.Generic;
using UnityEngine;

namespace GN3.Characters
{
    public static class CharacterPartLibrary
    {
        private static readonly Dictionary<CharacterPartCategory, Sprite[]> Cache = new Dictionary<CharacterPartCategory, Sprite[]>();

        public static Sprite GetRandom(CharacterPartCategory category, System.Random rng)
        {
            var pool = GetPool(category);
            return pool.Length == 0 ? null : pool[rng.Next(pool.Length)];
        }

        private static Sprite[] GetPool(CharacterPartCategory category)
        {
            if (Cache.TryGetValue(category, out var cached))
                return cached;

            var loaded = Resources.LoadAll<Sprite>($"CharacterParts/{category}");
            Cache[category] = loaded;
            return loaded;
        }
    }
}
