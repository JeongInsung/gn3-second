using System;
using System.Collections.Generic;
using System.Linq;

namespace GN3.Mercenaries
{
    public class PlayerParty
    {
        private static PlayerParty _instance;
        public static PlayerParty Instance => _instance ??= new PlayerParty();

        private readonly List<Mercenary> _members = new List<Mercenary>();

        public int MaxSize { get; set; } = 5;
        public IReadOnlyList<Mercenary> Members => _members;

        public event Action OnChanged;

        public bool TryAdd(Mercenary mercenary)
        {
            if (_members.Count >= MaxSize) return false;
            if (_members.Any(m => m.Id == mercenary.Id)) return false;

            _members.Add(mercenary);
            OnChanged?.Invoke();
            return true;
        }

        public bool Remove(Mercenary mercenary)
        {
            bool removed = _members.RemoveAll(m => m.Id == mercenary.Id) > 0;
            if (removed)
                OnChanged?.Invoke();
            return removed;
        }
    }
}
