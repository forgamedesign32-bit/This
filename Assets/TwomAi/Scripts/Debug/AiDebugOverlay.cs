using UnityEngine;
using UnityEngine.InputSystem;

namespace TwomAi
{
    // Developer overlay (Tab): per-NPC state/alert/LKP/role/report/seed, event log, state tags over heads,
    // and dev buttons (AI mode, seed, restart, X-ray, god mode).
    public sealed class AiDebugOverlay : MonoBehaviour
    {
        private const float ButtonBarWidth = 520f;

        [SerializeField] private bool _visible;

        private GuardBrain[] _guards;
        private CivilianBrain[] _civilians;
        private GUIStyle _panelStyle;
        private GUIStyle _worldStyle;

        private void Start()
        {
            _guards = FindObjectsByType<GuardBrain>(FindObjectsSortMode.None);
            _civilians = FindObjectsByType<CivilianBrain>(FindObjectsSortMode.None);
            System.Array.Sort(_guards, (a, b) => string.CompareOrdinal(a.name, b.name));
            System.Array.Sort(_civilians, (a, b) => string.CompareOrdinal(a.name, b.name));
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
                _visible = !_visible;

            if (_visible)
            {
                UiBlocker.Add(ButtonBarRect());
                UiBlocker.Add(NoiseBarRect());
                UiBlocker.Add(new Rect(10f, 225f, 420f, Screen.height - 440f));
            }
        }

        private static Rect NoiseBarRect()
        {
            var bar = ButtonBarRect();
            return new Rect(bar.center.x - 130f, bar.yMax + 4f, 260f, 26f);
        }

        private static Rect ButtonBarRect() => new Rect((Screen.width - ButtonBarWidth) * 0.5f, 64f, ButtonBarWidth, 30f);

        private void OnGUI()
        {
            if (!_visible)
                return;

            EnsureStyles();
            DrawButtons();
            DrawStateTags();
            DrawPanel();
            DrawLog();
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
                return;

            _panelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 11, wordWrap = false };
            TwomHud.SetTextColor(_panelStyle, Color.white);
            _worldStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 11, fontStyle = FontStyle.Bold };
            TwomHud.FreezeStates(_panelStyle);
            TwomHud.FreezeStates(_worldStyle);
        }

        private void DrawButtons()
        {
            var director = AiDirector.Instance;
            if (director == null)
                return;

            var bar = ButtonBarRect();
            float width = (bar.width - 25f) / 6f;
            float x = bar.x;
            if (GUI.Button(new Rect(x, bar.y, width, bar.height), director.EnhancedMode ? "AI: NEW" : "AI: OLD")) director.ToggleMode();
            x += width + 5f;
            if (GUI.Button(new Rect(x, bar.y, width, bar.height), $"SEED {director.BaseSeed} +1")) director.ChangeSeed(1);
            x += width + 5f;
            if (GUI.Button(new Rect(x, bar.y, width, bar.height), "RESTART")) director.Restart();
            x += width + 5f;
            if (GUI.Button(new Rect(x, bar.y, width, bar.height), VisibilitySystem.XRay ? "X-RAY ON" : "X-RAY OFF")) VisibilitySystem.XRay = !VisibilitySystem.XRay;
            x += width + 5f;
            if (GUI.Button(new Rect(x, bar.y, width, bar.height), director.GodMode ? "GOD ON" : "GOD OFF")) director.ToggleGodMode();
            x += width + 5f;
            if (GUI.Button(new Rect(x, bar.y, width, bar.height), AiDirector.GhostMode ? "GHOST ON" : "GHOST OFF")) director.ToggleGhostMode();

            // Test noise at the player's position: [-] [emit] [+]
            var noise = NoiseBarRect();
            if (GUI.Button(new Rect(noise.x, noise.y, 40f, noise.height), "-")) director.AdjustTestNoise(-0.2f);
            if (GUI.Button(new Rect(noise.x + 45f, noise.y, noise.width - 90f, noise.height), $"NOISE {director.TestNoiseLoudness:0.0}")) director.EmitTestNoise();
            if (GUI.Button(new Rect(noise.xMax - 40f, noise.y, 40f, noise.height), "+")) director.AdjustTestNoise(0.2f);
        }

        private void DrawStateTags()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            for (int i = 0; i < _guards.Length; i++)
            {
                var guard = _guards[i];
                if (guard == null || !guard.isActiveAndEnabled || guard.Alert == null)
                    continue;

                DrawTag(camera, guard.transform.position, $"{guard.State} {guard.Alert.Value:0} {guard.Role}", StageColor(guard.Alert.Stage, guard.State));
            }

            for (int i = 0; i < _civilians.Length; i++)
            {
                if (_civilians[i] != null)
                    DrawTag(camera, _civilians[i].transform.position, _civilians[i].State.ToString(), new Color(0.6f, 0.8f, 1f));
            }
        }

        private void DrawTag(Camera camera, Vector3 feet, string text, Color color)
        {
            Vector3 screen = camera.WorldToScreenPoint(feet + Vector3.up * 0.1f);
            if (screen.z <= 0f)
                return;

            TwomHud.SetTextColor(_worldStyle, color);
            GUI.Label(new Rect(screen.x - 100f, Screen.height - screen.y, 200f, 18f), text, _worldStyle);
        }

        private void DrawPanel()
        {
            var director = AiDirector.Instance;
            GUILayout.BeginArea(new Rect(10f, 225f, 420f, Screen.height - 440f));
            if (director != null)
            {
                GUILayout.Label(
                    $"F1:{On(director.IsF1Active)} F2:{On(director.IsF2Active)} F3:{On(director.IsF3Active)}\n" +
                    "F5 mode | PgUp/PgDn seed | F6 restart | F7 god | F8 dup-noise | F9 alert70",
                    _panelStyle);
            }

            if (SquadCoordinator.Instance != null)
                GUILayout.Label($"Squad last report #{SquadCoordinator.Instance.LastReportId}", _panelStyle);

            for (int i = 0; i < _guards.Length; i++)
            {
                if (_guards[i] != null)
                    GUILayout.Label(_guards[i].BuildDebugText(), _panelStyle);
            }

            for (int i = 0; i < _civilians.Length; i++)
            {
                if (_civilians[i] != null)
                    GUILayout.Label(_civilians[i].BuildDebugText(), _panelStyle);
            }

            GUILayout.EndArea();
        }

        private void DrawLog()
        {
            const float width = 400f;
            GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 60f, width, Screen.height - 270f));
            foreach (string line in AiLog.Entries)
                GUILayout.Label(line, _panelStyle);
            GUILayout.EndArea();
        }

        private static Color StageColor(AlertStage stage, GuardState state)
        {
            if (state == GuardState.Dead)
                return Color.gray;

            if (state == GuardState.Chase)
                return Color.red;

            switch (stage)
            {
                case AlertStage.CombatReady: return new Color(1f, 0.35f, 0.2f);
                case AlertStage.Searching: return new Color(1f, 0.65f, 0.1f);
                case AlertStage.Suspicious: return Color.yellow;
                default: return Color.white;
            }
        }

        private static string On(bool value) => value ? "on" : "off";
    }
}
