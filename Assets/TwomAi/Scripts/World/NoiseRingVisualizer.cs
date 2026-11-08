using UnityEngine;

namespace TwomAi
{
    // Expanding ring "shockwaves" drawn in front of the fog: red = NPC footsteps/noises,
    // white = player running, doors and attacks. Walking is silent visually, as in the original.
    public sealed class NoiseRingVisualizer : MonoBehaviour
    {
        private const int PoolSize = 24;
        private const int Segments = 40;

        [SerializeField] private Material _material;
        [SerializeField] private float _depth = -1.75f;
        [SerializeField] private float _duration = 0.8f;
        [SerializeField] private float _width = 0.06f;

        private readonly LineRenderer[] _lines = new LineRenderer[PoolSize];
        private readonly float[] _startTimes = new float[PoolSize];
        private readonly float[] _radii = new float[PoolSize];
        private readonly Vector3[] _centers = new Vector3[PoolSize];
        private readonly Color[] _colors = new Color[PoolSize];
        private readonly Vector3[] _points = new Vector3[Segments];
        private const int TracerCount = 6;
        private const float TracerDuration = 0.09f;

        private readonly LineRenderer[] _tracers = new LineRenderer[TracerCount];
        private readonly float[] _tracerUntil = new float[TracerCount];
        private AiTuningConfig _config;
        private int _next;
        private int _nextTracer;

        public static NoiseRingVisualizer Instance { get; private set; }

        public static readonly Color EnemyColor = new Color(0.9f, 0.12f, 0.1f);
        public static readonly Color PlayerColor = new Color(0.95f, 0.95f, 0.95f);

        private void Awake()
        {
            Instance = this;
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Ring_" + i);
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = _material;
                line.loop = true;
                line.useWorldSpace = true;
                line.positionCount = Segments;
                line.widthMultiplier = _width;
                line.numCapVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                _lines[i] = line;
                _startTimes[i] = float.NegativeInfinity;
            }

            for (int i = 0; i < TracerCount; i++)
            {
                var go = new GameObject("Tracer_" + i);
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = _material;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.widthMultiplier = 0.035f;
                line.startColor = new Color(1f, 0.92f, 0.6f, 1f);
                line.endColor = new Color(1f, 0.92f, 0.6f, 0.2f);
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.enabled = false;
                _tracers[i] = line;
            }
        }

        // Muzzle-to-target bullet streak for gunshots.
        public void SpawnTracer(Vector3 from, Vector3 to)
        {
            var line = _tracers[_nextTracer];
            _tracerUntil[_nextTracer] = Time.time + TracerDuration;
            _nextTracer = (_nextTracer + 1) % TracerCount;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.enabled = true;
        }

        private void Start()
        {
            _config = AiDirector.Instance.Config;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnEnable() => AiEventBus.NoiseEmitted += OnNoise;
        private void OnDisable() => AiEventBus.NoiseEmitted -= OnNoise;

        private void OnNoise(NoiseEvent noise)
        {
            if (_config == null)
                return;

            bool fromPlayer = noise.SourceId == AiIds.PlayerSourceId;
            if (fromPlayer && noise.Loudness < _config.RunNoise - 0.01f && noise.Loudness < _config.AttackNoise - 0.01f)
                return;

            Spawn(noise.Position, fromPlayer ? PlayerColor : EnemyColor, 0.6f + noise.Loudness * 1.4f);
        }

        public void Spawn(Vector3 position, Color color, float radius)
        {
            int index = _next;
            _next = (_next + 1) % PoolSize;
            _centers[index] = new Vector3(position.x, position.y + 0.25f, _depth);
            _radii[index] = radius;
            _colors[index] = color;
            _startTimes[index] = Time.time;
            _lines[index].enabled = true;
        }

        private void LateUpdate()
        {
            float now = Time.time;
            for (int i = 0; i < TracerCount; i++)
            {
                if (_tracers[i].enabled && now >= _tracerUntil[i])
                    _tracers[i].enabled = false;
            }

            for (int i = 0; i < PoolSize; i++)
            {
                var line = _lines[i];
                if (!line.enabled)
                    continue;

                float t = (now - _startTimes[i]) / _duration;
                if (t >= 1f)
                {
                    line.enabled = false;
                    continue;
                }

                float radius = Mathf.Lerp(0.15f, _radii[i], 1f - (1f - t) * (1f - t));
                var color = _colors[i];
                color.a = 1f - t;
                line.startColor = color;
                line.endColor = color;
                for (int s = 0; s < Segments; s++)
                {
                    float angle = s * Mathf.PI * 2f / Segments;
                    // Squashed vertically: rings read as ground-level ripples.
                    _points[s] = _centers[i] + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.45f, 0f);
                }

                line.SetPositions(_points);
            }
        }
    }
}
