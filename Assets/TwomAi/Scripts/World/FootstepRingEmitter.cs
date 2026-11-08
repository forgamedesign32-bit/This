using UnityEngine;

namespace TwomAi
{
    // Visual-only footstep cue for NPCs (red ring per stride). Not an AI noise event.
    [RequireComponent(typeof(NpcMover), typeof(NpcActor))]
    public sealed class FootstepRingEmitter : MonoBehaviour
    {
        [SerializeField] private float _walkStride = 1.1f;
        [SerializeField] private float _runStride = 1.6f;
        [SerializeField] private float _runSpeed = 3f;

        private NpcMover _mover;
        private NpcActor _actor;
        private Vector3 _lastPosition;
        private float _distance;

        private void Awake()
        {
            _mover = GetComponent<NpcMover>();
            _actor = GetComponent<NpcActor>();
            _lastPosition = transform.position;
        }

        private void Update()
        {
            Vector3 position = transform.position;
            float moved = Vector3.Distance(position, _lastPosition);
            _lastPosition = position;
            if (_actor.IsDead || _mover.CurrentSpeed < 0.2f || moved > 1f)
                return;

            bool running = _mover.CurrentSpeed >= _runSpeed;
            _distance += moved;
            float stride = running ? _runStride : _walkStride;
            if (_distance < stride)
                return;

            _distance -= stride;
            var rings = NoiseRingVisualizer.Instance;
            if (rings != null)
                rings.Spawn(position, NoiseRingVisualizer.EnemyColor, running ? 1.5f : 0.9f);
        }
    }
}
