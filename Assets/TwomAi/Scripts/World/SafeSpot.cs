using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // A room a civilian can flee into: inside node + optional lockable door + hide spots.
    public sealed class SafeSpot : MonoBehaviour
    {
        private static readonly List<SafeSpot> AllSpots = new List<SafeSpot>();

        [SerializeField] private Waypoint _insideNode;
        [SerializeField] private LockableDoor _door;
        [SerializeField] private HideSpot[] _hideSpots = new HideSpot[0];
        [SerializeField] private float _dangerBias;

        public static IReadOnlyList<SafeSpot> All => AllSpots;

        public Waypoint InsideNode => _insideNode;
        public LockableDoor Door => _door;
        public float DangerBias => _dangerBias;

        private void OnEnable() => AllSpots.Add(this);
        private void OnDisable() => AllSpots.Remove(this);

        public HideSpot FindFreeHideSpot(NpcActor actor)
        {
            for (int i = 0; i < _hideSpots.Length; i++)
            {
                var spot = _hideSpots[i];
                if (spot != null && (!spot.IsOccupied || spot.OccupiedBy == actor))
                    return spot;
            }

            return null;
        }

        public bool HasFreeHideSpot(NpcActor actor) => FindFreeHideSpot(actor) != null;
    }
}
