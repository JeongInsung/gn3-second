using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Combat;

namespace GN3.Mercenaries
{
    public class PlayerParty
    {
        private static PlayerParty _instance;
        public static PlayerParty Instance => _instance ??= new PlayerParty();

        private readonly List<Mercenary> _members = new List<Mercenary>();
        private readonly HashSet<string> _activeIds = new HashSet<string>();

        public int MaxSize { get; set; } = 5;
        public IReadOnlyList<Mercenary> Members => _members;

        public event Action OnChanged;

        public bool TryAdd(Mercenary mercenary)
        {
            if (_members.Count >= MaxSize) return false;
            if (_members.Any(m => m.Id == mercenary.Id)) return false;

            _members.Add(mercenary);
            _activeIds.Add(mercenary.Id);
            OnChanged?.Invoke();
            return true;
        }

        public bool Remove(Mercenary mercenary)
        {
            bool removed = _members.RemoveAll(m => m.Id == mercenary.Id) > 0;
            if (removed)
            {
                _activeIds.Remove(mercenary.Id);
                OnChanged?.Invoke();
            }
            return removed;
        }

        public bool IsActive(Mercenary mercenary) => _activeIds.Contains(mercenary.Id);

        public void SetActive(Mercenary mercenary, bool active)
        {
            bool changed = active ? _activeIds.Add(mercenary.Id) : _activeIds.Remove(mercenary.Id);
            if (changed) OnChanged?.Invoke();
        }

        public List<Combatant> ToCombatants()
        {
            return _members.Where(IsActive).Select(m => m.ToCombatant()).ToList();
        }
    }
}
