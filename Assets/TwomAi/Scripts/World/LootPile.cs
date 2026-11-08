using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // Scavenge container (rubble, shelf, safe, a body). Searching it opens a container window
    // where the player picks items into the backpack (original: container panel + backpack panel).
    public sealed class LootPile : MonoBehaviour
    {
        public struct Entry
        {
            public string Name;
            public int Count;
        }

        private static readonly List<LootPile> AllPiles = new List<LootPile>();

        [SerializeField] private string _displayName = "Rubble";
        [SerializeField] private int _items = 3;
        [SerializeField] private float _searchDuration = 2.5f;
        [SerializeField] private bool _containsLockpick;
        [SerializeField] private string _itemKind = "잡동사니";

        private readonly List<Entry> _entries = new List<Entry>();
        private bool _built;

        public static IReadOnlyList<LootPile> All => AllPiles;

        public string DisplayName => _displayName;
        public float SearchDuration => _searchDuration;
        public bool IsSearched { get; private set; }
        public IReadOnlyList<Entry> Entries
        {
            get
            {
                Build();
                return _entries;
            }
        }

        public bool IsEmpty
        {
            get
            {
                Build();
                return _entries.Count == 0;
            }
        }

        private void OnEnable() => AllPiles.Add(this);
        private void OnDisable() => AllPiles.Remove(this);

        // Runtime containers (bodies).
        public void Initialize(string displayName, float searchDuration)
        {
            _displayName = displayName;
            _searchDuration = searchDuration;
            _items = 0;
            _containsLockpick = false;
            _built = true;
        }

        public void Add(string name, int count)
        {
            Build();
            if (count > 0)
                _entries.Add(new Entry { Name = name, Count = count });
        }

        public void MarkSearched() => IsSearched = true;

        // Removes up to `amount` from an entry; returns how many were taken.
        public int Take(int index, int amount)
        {
            Build();
            if (index < 0 || index >= _entries.Count || amount <= 0)
                return 0;

            var entry = _entries[index];
            int taken = Mathf.Min(amount, entry.Count);
            entry.Count -= taken;
            if (entry.Count <= 0)
                _entries.RemoveAt(index);
            else
                _entries[index] = entry;

            return taken;
        }

        private void Build()
        {
            if (_built)
                return;

            _built = true;
            if (_items > 0)
                _entries.Add(new Entry { Name = _itemKind, Count = _items });
            if (_containsLockpick)
                _entries.Add(new Entry { Name = Inventory.Lockpick, Count = 1 });
        }
    }
}
