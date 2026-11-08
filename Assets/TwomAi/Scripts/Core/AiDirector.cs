using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TwomAi
{
    // Scene-wide mode (Legacy / Enhanced), seed and QA hotkeys.
    [DefaultExecutionOrder(-100)]
    public sealed class AiDirector : MonoBehaviour
    {
        [SerializeField] private AiTuningConfig _config;
        [SerializeField] private bool _enhancedMode = true;

        // Survives scene reloads so mode/seed hotkeys can restart the scene with new settings.
        private static bool _hasSessionOverride;
        private static bool _sessionEnhanced;
        private static int _sessionSeed;

        private int _baseSeed;

        public static AiDirector Instance { get; private set; }

        public AiTuningConfig Config => _config;
        public bool EnhancedMode => _enhancedMode;
        public int BaseSeed => _baseSeed;
        public bool GodMode { get; private set; }
        public bool IsF1Active => _enhancedMode && _config.EnableF1;
        public bool IsF2Active => _enhancedMode && _config.EnableF2;
        public bool IsF3Active => _enhancedMode && _config.EnableF3;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => _hasSessionOverride = false;

        private void Awake()
        {
            Instance = this;
            if (_config == null)
            {
                _config = ScriptableObject.CreateInstance<AiTuningConfig>();
                Debug.LogWarning("[TwomAi] AiTuningConfig missing; using runtime defaults.");
            }

            _baseSeed = _config.BaseRandomSeed;
            if (!_hasSessionOverride)
                return;

            _enhancedMode = _sessionEnhanced;
            _baseSeed = _sessionSeed;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public int GetNpcSeed(int npcId) => unchecked(_baseSeed + npcId * 1009);

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f5Key.wasPressedThisFrame)
                Reload(!_enhancedMode, _baseSeed);
            else if (keyboard.pageUpKey.wasPressedThisFrame)
                Reload(_enhancedMode, _baseSeed + 1);
            else if (keyboard.pageDownKey.wasPressedThisFrame)
                Reload(_enhancedMode, _baseSeed - 1);
            else if (keyboard.f6Key.wasPressedThisFrame)
                Reload(_enhancedMode, _baseSeed);

            if (keyboard.f7Key.wasPressedThisFrame)
                ToggleGodMode();

            if (keyboard.f8Key.wasPressedThisFrame)
                InjectDuplicateNoise();

            if (keyboard.f9Key.wasPressedThisFrame)
                ForceGuardAlert(70f);
        }

        public void ToggleMode() => Reload(!_enhancedMode, _baseSeed);
        public void Restart() => Reload(_enhancedMode, _baseSeed);
        public void ChangeSeed(int delta) => Reload(_enhancedMode, _baseSeed + delta);

        public void ToggleGodMode()
        {
            GodMode = !GodMode;
            AiLog.Add($"QA: god mode {(GodMode ? "ON" : "OFF")}");
        }

        private void Reload(bool enhanced, int seed)
        {
            _hasSessionOverride = true;
            _sessionEnhanced = enhanced;
            _sessionSeed = seed;

            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex < 0)
            {
                Debug.LogError("[TwomAi] Scene is not in Build Settings; cannot reload.");
                return;
            }

            SceneManager.LoadScene(scene.buildIndex);
        }

        // QA-A06: the same NoiseEvent (same EventId) is delivered twice.
        private void InjectDuplicateNoise()
        {
            var player = PlayerTarget.Instance;
            Vector3 position = player != null ? player.Position : Vector3.zero;
            var noise = new NoiseEvent(AiIds.Next(), position, _config.RunNoise, Time.time, AiIds.PlayerSourceId);
            AiEventBus.Raise(noise);
            AiEventBus.Raise(noise);
            AiLog.Add($"QA: noise #{noise.EventId} raised twice");
        }

        // QA-A05: alert 70 with no threat present.
        private void ForceGuardAlert(float value)
        {
            var guards = FindObjectsByType<GuardBrain>(FindObjectsSortMode.None);
            for (int i = 0; i < guards.Length; i++)
                guards[i].DebugForceAlert(value);

            AiLog.Add($"QA: all guards alert -> {value:0}");
        }
    }
}
