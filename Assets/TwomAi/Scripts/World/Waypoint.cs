using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    public sealed class Waypoint : MonoBehaviour
    {
        [SerializeField] private List<Waypoint> _links = new List<Waypoint>();
        [SerializeField] private int _regionId;
        [SerializeField] private bool _isExit;

        public IReadOnlyList<Waypoint> Links => _links;
        public int RegionId => _regionId;
        public bool IsExit => _isExit;
        public Vector3 Position => transform.position;
        public int Index { get; private set; } = -1;

        internal void SetIndex(int index) => Index = index;

        internal void EnsureLink(Waypoint other)
        {
            if (other == null || other == this || _links.Contains(other))
                return;

            _links.Add(other);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _isExit ? Color.green : new Color(0.3f, 0.8f, 1f);
            Gizmos.DrawSphere(Position + Vector3.up * 0.05f, 0.15f);
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            for (int i = 0; i < _links.Count; i++)
            {
                if (_links[i] != null)
                    Gizmos.DrawLine(Position + Vector3.up * 0.05f, _links[i].Position + Vector3.up * 0.05f);
            }
        }
    }
}
