using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>
    /// 길드 창고: 용병이 맡긴 무기와 상점에서 "창고로" 산 무기·약. 아이템 하나가 한 칸이고,
    /// 칸 수는 길드 단계(Guild.Current.StorageSlots)를 따른다. 저장은 이름 목록으로 한다.
    /// </summary>
    public static class GuildStorage
    {
        private static readonly List<WeaponItem> _weapons = new List<WeaponItem>();
        private static readonly List<HealingItem> _items = new List<HealingItem>();

        public static IReadOnlyList<WeaponItem> Weapons => _weapons;
        public static IReadOnlyList<HealingItem> Items => _items;
        public static int Count => _weapons.Count + _items.Count;
        public static int Capacity => Guild.Current.StorageSlots;
        public static bool IsFull => Count >= Capacity;

        public static event Action OnChanged;

        public static bool TryAdd(WeaponItem weapon)
        {
            if (weapon == null || IsFull) return false;
            _weapons.Add(weapon);
            OnChanged?.Invoke();
            return true;
        }

        public static bool TryAdd(HealingItem item)
        {
            if (item == null || IsFull) return false;
            _items.Add(item);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>장착하면서 들고 있던 무기를 돌려놓을 때: 하나 꺼내고 하나 넣는 맞바꿈이라 칸 제한을 보지 않는다.</summary>
        public static void Swap(WeaponItem taken, WeaponItem returned)
        {
            if (!_weapons.Remove(taken)) return;
            if (returned != null) _weapons.Add(returned);
            OnChanged?.Invoke();
        }

        public static bool Remove(WeaponItem weapon)
        {
            if (!_weapons.Remove(weapon)) return false;
            OnChanged?.Invoke();
            return true;
        }

        public static bool Remove(HealingItem item)
        {
            if (!_items.Remove(item)) return false;
            OnChanged?.Invoke();
            return true;
        }

        public static List<string> WeaponNames() => _weapons.Select(w => w.Name).ToList();
        public static List<string> ItemNames() => _items.Select(i => i.Name).ToList();

        /// <summary>저장 파일에서 불러올 때. 칸 제한은 보지 않는다(이름을 못 찾는 아이템은 버린다).</summary>
        public static void Restore(IEnumerable<string> weapons, IEnumerable<string> items)
        {
            _weapons.Clear();
            _items.Clear();
            if (weapons != null)
                foreach (var name in weapons)
                {
                    var weapon = WeaponCatalog.All.FirstOrDefault(w => w.Name == name);
                    if (weapon != null) _weapons.Add(weapon);
                }
            if (items != null)
                foreach (var name in items)
                {
                    var item = HealingItemCatalog.All.FirstOrDefault(i => i.Name == name);
                    if (item != null) _items.Add(item);
                }
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _weapons.Clear();
            _items.Clear();
            OnChanged = null;
        }
    }
}
