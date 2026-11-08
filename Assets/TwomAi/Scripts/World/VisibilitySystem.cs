using UnityEngine;

namespace TwomAi
{
    // TWOM-style line of sight: rooms the player cannot see into are darkened and their occupants hidden.
    // Closed doors and walls (Occluder) block sight; open doorways and the stair hole do not.
    public sealed class VisibilitySystem : MonoBehaviour
    {
        [SerializeField] private float _updateInterval = 0.15f;
        [SerializeField] private float _sightRange = 22f;
        [SerializeField] private float _sampleSpacing = 1.2f;
        [SerializeField] private float _eyeHeight = 1.5f;
        [SerializeField] private float _floorHeight = 3.2f;

        private float _nextUpdate;

        public static VisibilitySystem Instance { get; private set; }

        // Debug: reveal everything.
        public static bool XRay { get; set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => XRay = false;

        private void Update()
        {
            if (Time.time < _nextUpdate)
                return;

            _nextUpdate = Time.time + _updateInterval;
            var player = PlayerTarget.Instance;
            if (player == null)
                return;

            Vector3 eye = player.Position + Vector3.up * _eyeHeight;
            var rooms = RoomVolume.All;
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                bool otherFloor = Mathf.Abs(room.FloorY - player.Position.y) > _floorHeight * 0.5f;
                // Like the original: another floor is only seen while standing on the stairs.
                bool allowed = !otherFloor || IsOnStairs(player.Position.y);
                room.SetVisible(room.AlwaysVisible || room.Contains(player.Position) || (allowed && CanSeeInto(room, eye)));
            }

            var actors = NpcActor.All;
            for (int i = 0; i < actors.Count; i++)
                actors[i].SetVisibleToPlayer(XRay || IsPointVisible(actors[i].transform.position + Vector3.up));
        }

        private bool IsOnStairs(float y)
        {
            float t = y / _floorHeight;
            return Mathf.Abs(t - Mathf.Round(t)) * _floorHeight > 0.3f;
        }

        public bool IsPointVisible(Vector3 position)
        {
            if (XRay)
                return true;

            var rooms = RoomVolume.All;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].Contains(position))
                    return rooms[i].IsRevealedAt(position);
            }

            return true;
        }

        private bool CanSeeInto(RoomVolume room, Vector3 eye)
        {
            float y = room.FloorY + 1.2f;
            float width = room.MaxX - room.MinX;
            int samples = Mathf.Max(2, Mathf.CeilToInt(width / _sampleSpacing));
            for (int i = 0; i <= samples; i++)
            {
                float x = Mathf.Lerp(room.MinX + 0.3f, room.MaxX - 0.3f, i / (float)samples);
                var point = new Vector3(x, y, 0f);
                if (Vector3.Distance(eye, point) <= _sightRange && !AiPhysics.IsOccluded(eye, point))
                    return true;
            }

            return false;
        }
    }
}
