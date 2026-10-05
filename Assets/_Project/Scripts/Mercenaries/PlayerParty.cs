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

        /// <summary>새로 시작·불러오기 전에 파티를 비운다.</summary>
        public void Clear()
        {
            if (_members.Count == 0) return;
            _members.Clear();
            OnChanged?.Invoke();
        }

        /// <summary>불러올 때: 정원과 관계없이 넣는다(정원보다 많이 데리고 있던 저장도 그대로 복원).</summary>
        public void RestoreAdd(Mercenary mercenary)
        {
            _members.Add(mercenary);
            OnChanged?.Invoke();
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
