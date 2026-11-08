using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // Shadowed alcove where the player can hide, knock to lure a guard, and ambush it as it passes.
    // The hiding position is set back from the walking line so patrols pass by without bumping into it.
    public sealed class PlayerHideSpot : MonoBehaviour
    {
        private static readonly List<PlayerHideSpot> AllSpots = new List<PlayerHideSpot>();

        [SerializeField] private float _depth = 0.6f;

        public static IReadOnlyList<PlayerHideSpot> All => AllSpots;

        public Vector3 HidePosition => transform.position + Vector3.forward * _depth;
        public Vector3 ApproachPosition => transform.position;

        private void OnEnable() => AllSpots.Add(this);
        private void OnDisable() => AllSpots.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.3f, 1f, 0.8f);
            Gizmos.DrawWireCube(HidePosition + Vector3.up * 0.9f, new Vector3(0.8f, 1.8f, 0.5f));
        }
    }
}
