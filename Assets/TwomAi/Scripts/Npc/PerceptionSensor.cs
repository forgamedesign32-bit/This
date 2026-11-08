using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // Sight (distance, cone, occlusion, hold time) and hearing (distance + wall attenuation).
    // Only evidence leaves this class: a sighted position, a heard noise position, or a corpse.
    [RequireComponent(typeof(NpcActor))]
    public sealed class PerceptionSensor : MonoBehaviour
    {
        private readonly HashSet<int> _knownCorpses = new HashSet<int>();

        private NpcActor _self;
        private AiTuningConfig _config;
        private float _nextSenseTime;
        private float _exposure;

        public bool HasVisualContact { get; private set; }
        public float Exposure01 => _config != null && _config.VisionDetectHold > 0f ? Mathf.Clamp01(_exposure / _config.VisionDetectHold) : 0f;

        public event Action ContactStarted;
        public event Action<Vector3> ContactUpdated;
        public event Action ContactLost;
        public event Action<NoiseEvent, float> NoiseHeard;
        public event Action<NpcActor> CorpseFound;

        private void Awake()
        {
            _self = GetComponent<NpcActor>();
        }

        private void Start()
        {
            _config = AiDirector.Instance.Config;
            // Spread sensor ticks across NPCs to avoid all raycasts in the same frame.
            _nextSenseTime = Time.time + (_self.Id * 0.037f) % _config.SenseInterval;
        }

        private void OnEnable() => AiEventBus.NoiseEmitted += OnNoiseEmitted;
        private void OnDisable() => AiEventBus.NoiseEmitted -= OnNoiseEmitted;

        private void Update()
        {
            if (_self.IsDead || _config == null || Time.time < _nextSenseTime)
                return;

            _nextSenseTime = Time.time + _config.SenseInterval;
            SenseSight();
            if (_self.Kind == NpcKind.Guard)
                SenseCorpses();
        }

        public bool CanSee(Vector3 point)
        {
            Vector3 eye = _self.EyePosition;
            // 3D cone around horizontal facing; other floors are blocked by slabs (Occluder).
            Vector3 toTarget = point - eye;
            float distance = toTarget.magnitude;
            if (distance > _config.ViewDistance)
                return false;

            bool inCone = distance <= _config.CloseSenseRadius
                || Vector3.Angle(AiMath.Flat(transform.forward), toTarget) <= _config.ViewAngle * 0.5f;
            return inCone && !AiPhysics.IsOccluded(eye, point);
        }

        private void SenseSight()
        {
            var player = PlayerTarget.Instance;
            bool visible = player != null && !player.IsDead && !player.IsHidden && CanSee(player.ChestPosition);
            if (!visible)
            {
                _exposure = 0f;
                if (!HasVisualContact)
                    return;

                HasVisualContact = false;
                ContactLost?.Invoke();
                return;
            }

            _exposure += _config.SenseInterval;
            if (!HasVisualContact && _exposure + 0.0001f >= _config.VisionDetectHold)
            {
                HasVisualContact = true;
                ContactStarted?.Invoke();
            }

            if (HasVisualContact)
                ContactUpdated?.Invoke(player.Position);
        }

        private void SenseCorpses()
        {
            var actors = NpcActor.All;
            for (int i = 0; i < actors.Count; i++)
            {
                var other = actors[i];
                if (other == _self || !other.IsDead || other.Kind != NpcKind.Guard || _knownCorpses.Contains(other.Id))
                    continue;

                if (!CanSee(other.transform.position + Vector3.up * 0.3f))
                    continue;

                _knownCorpses.Add(other.Id);
                CorpseFound?.Invoke(other);
            }
        }

        private void OnNoiseEmitted(NoiseEvent noise)
        {
            if (_self == null || _self.IsDead || _config == null || noise.SourceId == _self.Id)
                return;

            float distance = AiMath.Distance(noise.Position, transform.position);
            if (distance > _config.HearingRadius)
                return;

            int walls = AiPhysics.CountOccluders(noise.Position + Vector3.up, _self.EyePosition);
            float effective = noise.Loudness * Mathf.Max(0f, 1f - distance / _config.HearingRadius)
                * Mathf.Pow(_config.WallNoiseMultiplier, walls);
            if (effective < _config.HearingThreshold)
                return;

            NoiseHeard?.Invoke(noise, effective);
        }
    }
}
