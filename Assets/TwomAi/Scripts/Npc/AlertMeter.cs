using UnityEngine;

namespace TwomAi
{
    // 0..100 alert value. Accumulates on evidence, decays after a delay, stage drops with hysteresis.
    public sealed class AlertMeter
    {
        private readonly AiTuningConfig _config;
        private float _lastEvidenceTime = float.NegativeInfinity;

        public AlertMeter(AiTuningConfig config)
        {
            _config = config;
        }

        public float Value { get; private set; }
        public AlertStage Stage { get; private set; }

        public void Add(float amount, float now)
        {
            Value = Mathf.Clamp(Value + amount, 0f, 100f);
            _lastEvidenceTime = now;
            UpdateStage();
        }

        // Keeps the meter from decaying without adding (continuous sighting).
        public void Refresh(float now) => _lastEvidenceTime = now;

        public void SetValue(float value, float now)
        {
            Value = Mathf.Clamp(value, 0f, 100f);
            _lastEvidenceTime = now;
            Stage = RawStage(Value);
        }

        public void Tick(float deltaTime, float now)
        {
            if (Value <= 0f || now - _lastEvidenceTime < _config.AlertDecayDelay)
                return;

            Value = Mathf.Max(0f, Value - _config.AlertDecayPerSecond * deltaTime);
            UpdateStage();
        }

        private void UpdateStage()
        {
            var raw = RawStage(Value);
            if (raw > Stage)
            {
                Stage = raw;
                return;
            }

            while (Stage > AlertStage.Normal && Value < LowerBound(Stage) - _config.StageHysteresis)
                Stage--;
        }

        private AlertStage RawStage(float value)
        {
            if (value >= _config.StageCombatReady)
                return AlertStage.CombatReady;

            if (value >= _config.StageSearching)
                return AlertStage.Searching;

            return value >= _config.StageSuspicious ? AlertStage.Suspicious : AlertStage.Normal;
        }

        private float LowerBound(AlertStage stage)
        {
            switch (stage)
            {
                case AlertStage.CombatReady: return _config.StageCombatReady;
                case AlertStage.Searching: return _config.StageSearching;
                case AlertStage.Suspicious: return _config.StageSuspicious;
                default: return 0f;
            }
        }
    }
}
