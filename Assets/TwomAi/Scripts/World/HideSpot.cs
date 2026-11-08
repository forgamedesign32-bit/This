using System;
using UnityEngine;

namespace TwomAi
{
    public sealed class HideSpot : MonoBehaviour
    {
        private static readonly System.Collections.Generic.List<HideSpot> AllSpots = new System.Collections.Generic.List<HideSpot>();

        [SerializeField] private float _safetyScore = 1f;

        public static System.Collections.Generic.IReadOnlyList<HideSpot> All => AllSpots;

        private void OnEnable() => AllSpots.Add(this);
        private void OnDisable() => AllSpots.Remove(this);

        public NpcActor OccupiedBy { get; private set; }
        public float SafetyScore => _safetyScore;
        public bool IsOccupied => OccupiedBy != null && !OccupiedBy.IsDead;

        public event Action<HideSpot> Inspected;

        public bool TryOccupy(NpcActor actor)
        {
            if (IsOccupied && OccupiedBy != actor)
                return false;

            OccupiedBy = actor;
            return true;
        }

        public void Release(NpcActor actor)
        {
            if (OccupiedBy == actor)
                OccupiedBy = null;
        }

        // Someone actually checks this spot; the occupant (if any) is discovered.
        public bool Inspect()
        {
            bool occupied = IsOccupied;
            Inspected?.Invoke(this);
            return occupied;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = IsOccupied ? Color.magenta : new Color(1f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.8f, new Vector3(1f, 1.6f, 0.6f));
        }
    }
}
