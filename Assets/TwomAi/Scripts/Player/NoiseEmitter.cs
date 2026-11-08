using UnityEngine;

namespace TwomAi
{
    // F1-3: one NoiseEvent per footstep (stride distance), never per frame.
    // Walk vs run is derived from the mover's actual speed.
    [RequireComponent(typeof(NpcMover))]
    public sealed class NoiseEmitter : MonoBehaviour
    {
        private const float MaxStepDelta = 1f;

        [SerializeField] private float _walkStride = 0.8f;
        [SerializeField] private float _runStride = 1.3f;
        [SerializeField] private float _minMoveSpeed = 0.2f;

        private NpcMover _mover;
        private AiTuningConfig _config;
        private Vector3 _lastPosition;
        private float _distanceSinceStep;

        public bool IsRunning { get; private set; }
        public float LastLoudness { get; private set; }
        public float LastStepTime { get; private set; } = float.NegativeInfinity;

        private void Awake()
        {
            _mover = GetComponent<NpcMover>();
        }

        private void Start()
        {
            _config = AiDirector.Instance.Config;
            _lastPosition = transform.position;
        }

        private void Update()
        {
            Vector3 position = transform.position;
            float moved = Vector3.Distance(position, _lastPosition);
            _lastPosition = position;
            if (moved > MaxStepDelta || _mover.CurrentSpeed < _minMoveSpeed)
            {
                IsRunning = false;
                return;
            }

            IsRunning = _mover.CurrentSpeed >= _config.RunSpeedThreshold;
            _distanceSinceStep += moved;
            float stride = IsRunning ? _runStride : _walkStride;
            if (_distanceSinceStep < stride)
                return;

            _distanceSinceStep -= stride;
            LastLoudness = IsRunning ? _config.RunNoise : _config.WalkNoise;
            LastStepTime = Time.time;
            AiEventBus.EmitNoise(position, LastLoudness, AiIds.PlayerSourceId);
        }
    }
}
