using UnityEngine;

namespace TwomAi
{
    // IMGUI imitation of the original night-scavenging HUD (placeholder art):
    // paper strip (night / clock / items), Backpack + Leave bars, orange tabs, message bar,
    // polaroid character card, exit icon, round interaction icons, overhead HP bars and subtitles.
    public sealed class TwomHud : MonoBehaviour
    {
        private const int NightNumber = 18;
        private const float IconRange = 8f;

        private static readonly Color Paper = new Color(0.9f, 0.88f, 0.84f, 0.97f);
        private static readonly Color Ink = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color Rust = new Color(0.72f, 0.24f, 0.12f);
        private static readonly Color Orange = new Color(0.78f, 0.33f, 0.12f, 0.92f);
        private static readonly Color DarkBar = new Color(0.05f, 0.05f, 0.06f, 0.82f);
        private static readonly Color HealthBlue = new Color(0.45f, 0.8f, 0.92f);

        private PlayerClickController _controller;
        private PlayerTarget _player;
        private GUIStyle _paperLarge;
        private GUIStyle _paperSmall;
        private GUIStyle _barText;
        private GUIStyle _subtitle;
        private GUIStyle _shout;
        private GUIStyle _shadow;
        private GUIStyle _iconGlyph;
        private GUIStyle _iconGlyphSmall;
        private GUIStyle _cardName;
        private GUIStyle _cardText;
        private GUIStyle _center;
        private bool _backpackOpen;
        private readonly System.Collections.Generic.List<(string name, int count)> _slotItems = new System.Collections.Generic.List<(string name, int count)>();
        private readonly System.Collections.Generic.List<(string name, int count)> _containerItems = new System.Collections.Generic.List<(string name, int count)>();
        private GUIStyle _slotName;

        private void Start()
        {
            _controller = FindFirstObjectByType<PlayerClickController>();
            _player = PlayerTarget.Instance;
        }

        private void Update()
        {
            // Interactive HUD areas (clicks there must not move the character).
            UiBlocker.Add(ToScreen(MenuArea));
            UiBlocker.Add(ToScreen(ExitIconRect));
            bool container = _controller != null && _controller.OpenContainer != null;
            if (container)
            {
                UiBlocker.Add(ToScreen(ContainerRect));
                var backpack = BackpackRect;
                backpack.x = ContainerRect.xMax + 12f;
                UiBlocker.Add(ToScreen(backpack));
            }
            else if (_backpackOpen)
            {
                UiBlocker.Add(ToScreen(BackpackRect));
            }
            if (IsEndScreen())
                UiBlocker.Add(new Rect(0f, 0f, Screen.width, Screen.height));
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawOverheads();
            DrawButtons();
            DrawPanels();
        }

        private bool IsEndScreen() => _player != null && (_player.IsDead || (_controller != null && _controller.HasExited));

        // Screen panels are scaled with the window height (reference 720p); the end screen blocks everything.

        private void EnsureStyles()
        {
            // Checks the last style built, so a failed earlier pass is retried instead of leaving nulls.
            if (_center != null && _cardName != null && _cardText != null && _slotName != null)
                return;

            _paperLarge = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleLeft };
            SetTextColor(_paperLarge, Ink);
            _paperSmall = new GUIStyle(_paperLarge) { fontSize = 18 };
            _barText = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleRight };
            SetTextColor(_barText, new Color(0.92f, 0.92f, 0.92f));
            _subtitle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(0, 0, 0, 0) };
            SetTextColor(_subtitle, Color.white);
            _shout = new GUIStyle(_subtitle) { fontSize = 22, fontStyle = FontStyle.Bold };
            SetTextColor(_shout, Color.white);
            _shadow = new GUIStyle(_subtitle);
            SetTextColor(_shadow, new Color(0f, 0f, 0f, 0.85f));
            _iconGlyph = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            SetTextColor(_iconGlyph, new Color(0.15f, 0.15f, 0.15f));
            _iconGlyphSmall = new GUIStyle(_iconGlyph) { fontSize = 12 };
            _cardName = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            SetTextColor(_cardName, Color.white);
            _cardText = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            SetTextColor(_cardText, new Color(0.85f, 0.85f, 0.85f));
            _center = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            SetTextColor(_center, Ink);
            _slotName = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperLeft, wordWrap = true };
            SetTextColor(_slotName, new Color(0.88f, 0.88f, 0.88f));

            // All styles exist now; derived styles (_paperSmall, _shadow, _iconGlyphSmall) copied their parent's states.
            foreach (var style in new[] { _paperLarge, _paperSmall, _barText, _subtitle, _shout, _shadow, _iconGlyph, _iconGlyphSmall, _cardName, _cardText, _center, _slotName })
                FreezeStates(style);
        }

        // ---------------- Screen panels (reference layout 1280x720, from the original footage) ----------------

        private const float RefHeight = 720f;

        private static float UiScale => Screen.height / RefHeight;
        private static float RefWidth => Screen.width / UiScale;

        private static Rect ToScreen(Rect r)
        {
            float s = UiScale;
            return new Rect(r.x * s, r.y * s, r.width * s, r.height * s);
        }

        private static Rect MenuArea => new Rect(0f, 88f, 300f, 230f);
        private static Rect ExitIconRect => new Rect(RefWidth - 58f, 26f, 36f, 34f);
        private const int BackpackColumns = 5;
        private const int BackpackRows = 3;
        private const float SlotSize = 62f;
        private const float SlotGap = 6f;

        private static Rect BackpackRect => new Rect(300f, 92f,
            BackpackColumns * (SlotSize + SlotGap) + SlotGap + 24f, BackpackRows * (SlotSize + SlotGap) + SlotGap + 48f);

        private void DrawPanels()
        {
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));
            DrawTopStrip();
            DrawLeftMenu();
            DrawMessageBar();
            DrawCharacterCard();
            DrawExitIcon();
            bool container = _controller != null && _controller.OpenContainer != null;
            if (container)
                DrawContainer();
            if (_backpackOpen || container)
                DrawBackpackAt(container ? ContainerRect.xMax + 12f : BackpackRect.x);
            if (IsEndScreen())
                DrawEndScreen();
            GUI.matrix = previous;
        }

        // Paper strip: "night N", clock, ammo count.
        private void DrawTopStrip()
        {
            var strip = new Rect(0f, 22f, 392f, 56f);
            Fill(strip, Paper);
            Fill(new Rect(strip.x, strip.yMax - 3f, strip.width, 3f), new Color(0.6f, 0.58f, 0.55f, 0.9f));

            SetTextColor(_paperSmall, Ink);
            GUI.Label(new Rect(62f, 30f, 30f, 40f), "밤", _paperSmall);
            SetTextColor(_paperLarge, Rust);
            GUI.Label(new Rect(90f, 26f, 54f, 48f), NightNumber.ToString(), _paperLarge);

            var clock = new Rect(148f, 34f, 32f, 32f);
            Tint(clock, HudTextures.Ring, Rust);
            Fill(new Rect(clock.center.x - 1f, clock.y + 7f, 2f, 10f), Ink);
            SetTextColor(_paperLarge, Ink);
            GUI.Label(new Rect(186f, 26f, 132f, 48f), ClockText(), _paperLarge);

            SetTextColor(_paperLarge, Rust);
            int ammo = _controller != null ? _controller.Ammo : 0;
            GUI.Label(new Rect(318f, 26f, 74f, 48f), $"|||{ammo}", _paperLarge);
        }

        private static string ClockText() => NightClock.Text;

        // Backpack / Leave bars with the round icon outside the bar, then the orange slots.
        private void DrawLeftMenu()
        {
            if (MenuBar(new Rect(0f, 92f, 240f, 38f), "백팩", "[=]"))
                _backpackOpen = !_backpackOpen;

            if (MenuBar(new Rect(0f, 140f, 240f, 38f), "나가기", ">|") && _controller != null)
                _controller.CommandExit();

            if (_controller == null)
                return;

            bool busy = _controller.IsBusy;
            DrawTab(new Rect(0f, 226f, 240f, 28f), busy ? $"{TimedLabel(_controller.Timed)} {Mathf.RoundToInt(_controller.TimedProgress01 * 100f)}%" : "수집", busy ? 1f : 0.55f);

            bool rifle = _controller.Weapon == WeaponKind.Rifle;
            if (TabButton(new Rect(0f, 258f, 240f, 24f), $"돌격 소총 + 쇠지레", rifle))
                _controller.EquipWeapon(WeaponKind.Rifle);
            if (TabButton(new Rect(0f, 288f, 240f, 22f), "쇠지레", !rifle))
                _controller.EquipWeapon(WeaponKind.Melee);
        }

        private static string TimedLabel(PlayerClickController.TimedKind kind)
        {
            switch (kind)
            {
                case PlayerClickController.TimedKind.PickLock: return "자물쇠 따기";
                case PlayerClickController.TimedKind.PryDoor: return "문 비틀기";
                default: return "수집";
            }
        }

        private bool MenuBar(Rect rect, string label, string icon)
        {
            Fill(rect, DarkBar);
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 12f, rect.height), label, _barText);
            var iconRect = new Rect(rect.xMax + 6f, rect.y + 2f, rect.height - 4f, rect.height - 4f);
            Tint(iconRect, HudTextures.Disc, new Color(0.85f, 0.85f, 0.85f));
            GUI.Label(iconRect, icon, _iconGlyphSmall);
            return GUI.Button(new Rect(rect.x, rect.y, rect.width + rect.height + 6f, rect.height), GUIContent.none, GUIStyle.none);
        }

        private bool TabButton(Rect rect, string label, bool active)
        {
            DrawTab(rect, label, active ? 1f : 0.45f);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private void DrawTab(Rect rect, string label, float alpha)
        {
            var color = Orange;
            color.a *= alpha;
            Fill(rect, color);
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 12f, rect.height), label, _barText);
        }

        // System message, bottom-left, right-aligned text.
        private void DrawMessageBar()
        {
            string message = _controller != null ? _controller.Message : null;
            if (string.IsNullOrEmpty(message))
                return;

            var rect = new Rect(0f, RefHeight - 110f, 336f, 30f);
            Fill(rect, new Color(0.25f, 0.25f, 0.25f, 0.85f));
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 14f, rect.height), message, _barText);
        }

        // Polaroid card bottom-right: name, trait, status (left), portrait (right).
        private void DrawCharacterCard()
        {
            if (_player == null)
                return;

            var card = new Rect(RefWidth - 332f, RefHeight - 185f, 292f, 155f);
            var previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(-2f, card.center * UiScale);
            Fill(card, Paper);
            var inner = new Rect(card.x + 10f, card.y + 10f, card.width - 20f, card.height - 20f);
            Fill(inner, new Color(0.16f, 0.16f, 0.17f));
            var photo = new Rect(inner.x + inner.width * 0.5f, inner.y, inner.width * 0.5f, inner.height);
            Fill(photo, new Color(0.32f, 0.32f, 0.33f));
            Tint(new Rect(photo.center.x - 24f, photo.y + 16f, 48f, 48f), HudTextures.Disc, new Color(0.5f, 0.5f, 0.5f));
            Fill(new Rect(photo.center.x - 34f, photo.y + 66f, 68f, photo.height - 66f), new Color(0.5f, 0.5f, 0.5f));

            GUI.Label(new Rect(inner.x + 10f, inner.y + 8f, 140f, 26f), _player.DisplayName, _cardName);
            GUI.Label(new Rect(inner.x + 10f, inner.y + 34f, 140f, 20f), "달리기가 빠름", _cardText);
            var status = _cardText.normal.textColor;
            SetTextColor(_cardText, new Color(0.95f, 0.5f, 0.15f));
            GUI.Label(new Rect(inner.x + 10f, inner.y + 54f, 140f, 20f), HealthStatus(_player.Health01), _cardText);
            SetTextColor(_cardText, status);
            GUI.matrix = previous;
        }

        private static string HealthStatus(float health01)
        {
            if (health01 <= 0f) return "사망";
            if (health01 >= 0.85f) return "건강함";
            if (health01 >= 0.6f) return "조금 아픔";
            if (health01 >= 0.3f) return "부상";
            return "심각한 부상";
        }

        private void DrawExitIcon()
        {
            var rect = ExitIconRect;
            Fill(new Rect(rect.x + 4f, rect.y + 2f, 14f, rect.height - 4f), new Color(0.8f, 0.8f, 0.8f, 0.8f));
            GUI.Label(new Rect(rect.x + 14f, rect.y, 24f, rect.height), "->", _barText);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && _controller != null)
                _controller.CommandExit();
        }

        private float _backpackX = 300f;

        private void DrawBackpackAt(float x)
        {
            _backpackX = x;
            DrawBackpack();
        }

        // Backpack like the original: dark panel, title, square item slots with counts.
        private void DrawBackpack()
        {
            _slotItems.Clear();
            if (_controller != null)
            {
                _slotItems.Add(("돌격 소총", 1));
                _slotItems.Add((Inventory.Ammo, _controller.Ammo));
                _slotItems.Add(("쇠지레", 1));
                if (_controller.HasLockpick)
                    _slotItems.Add((Inventory.Lockpick, 1));
                foreach (var pair in _controller.Backpack)
                    _slotItems.Add((pair.Key, pair.Value));
            }

            var rect = BackpackRect;
            rect.x = _backpackX;
            DrawItemPanel(rect, "백팩", _slotItems, false);
        }

        // Container window (left) next to the backpack (right), as in the original scavenging screen.
        private static Rect ContainerRect => new Rect(300f, 92f, BackpackRect.width, BackpackRect.height + 44f);

        private void DrawContainer()
        {
            var container = _controller != null ? _controller.OpenContainer : null;
            if (container == null)
                return;

            _containerItems.Clear();
            var entries = container.Entries;
            for (int i = 0; i < entries.Count; i++)
                _containerItems.Add((entries[i].Name, entries[i].Count));

            var rect = ContainerRect;
            int clicked = DrawItemPanel(new Rect(rect.x, rect.y, rect.width, rect.height - 44f), container.DisplayName, _containerItems, true);
            if (clicked >= 0)
                _controller.TakeFromContainer(clicked);

            var buttons = new Rect(rect.x, rect.yMax - 40f, rect.width, 40f);
            Fill(buttons, new Color(0.07f, 0.07f, 0.08f, 0.92f));
            if (TabButton(new Rect(buttons.x + 10f, buttons.y + 6f, 160f, 28f), "모두 가져가기", true))
                _controller.TakeAllFromContainer();
            if (TabButton(new Rect(buttons.xMax - 110f, buttons.y + 6f, 100f, 28f), "닫기", false))
                _controller.CloseContainer();
        }

        // Draws a titled grid of item slots; returns the clicked slot index (clickable panels only) or -1.
        private int DrawItemPanel(Rect rect, string title, System.Collections.Generic.List<(string name, int count)> items, bool clickable)
        {
            Fill(rect, new Color(0.07f, 0.07f, 0.08f, 0.92f));
            Fill(new Rect(rect.x, rect.y, rect.width, 34f), new Color(0.16f, 0.16f, 0.17f, 0.95f));
            GUI.Label(new Rect(rect.x + 14f, rect.y + 4f, rect.width - 28f, 26f), title, _cardName);

            int clicked = -1;
            float startX = rect.x + 12f + SlotGap;
            float startY = rect.y + 34f + SlotGap + 6f;
            for (int row = 0; row < BackpackRows; row++)
            {
                for (int column = 0; column < BackpackColumns; column++)
                {
                    int index = row * BackpackColumns + column;
                    var slot = new Rect(startX + column * (SlotSize + SlotGap), startY + row * (SlotSize + SlotGap), SlotSize, SlotSize);
                    Fill(slot, new Color(0.55f, 0.55f, 0.55f, 0.5f));
                    Fill(new Rect(slot.x + 2f, slot.y + 2f, slot.width - 4f, slot.height - 4f), new Color(0.12f, 0.12f, 0.13f, 0.95f));
                    if (index >= items.Count)
                        continue;

                    var item = items[index];
                    GUI.Label(new Rect(slot.x + 4f, slot.y + 4f, slot.width - 8f, 34f), item.name, _slotName);
                    GUI.Label(new Rect(slot.x + 4f, slot.yMax - 22f, slot.width - 8f, 20f), item.count.ToString(), _barText);
                    if (clickable && GUI.Button(slot, GUIContent.none, GUIStyle.none))
                        clicked = index;
                }
            }

            return clicked;
        }

        private void DrawEndScreen()
        {
            Fill(new Rect(0f, 0f, RefWidth, RefHeight), new Color(0f, 0f, 0f, 0.6f));
            var card = new Rect(RefWidth * 0.5f - 200f, RefHeight * 0.5f - 110f, 400f, 220f);
            Fill(card, Paper);
            bool dead = _player.IsDead;
            bool dawn = _controller != null && _controller.EndedAtDawn;
            string title = dead ? $"{_player.DisplayName}이(가) 돌아오지 못했다."
                : dawn ? "날이 밝았다. 은신처로 돌아간다." : "탐색을 마치고 은신처로 돌아간다.";
            int items = _controller != null ? _controller.Items : 0;
            GUI.Label(new Rect(card.x + 20f, card.y + 20f, card.width - 40f, 90f), $"{title}\n획득 물품 {items}개", _center);
            var director = AiDirector.Instance;
            if (director != null && GUI.Button(new Rect(card.center.x - 80f, card.yMax - 60f, 160f, 40f), "다시 하기"))
                director.Restart();
        }

        // ---------------- World-space widgets ----------------

        private void DrawOverheads()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            var actors = NpcActor.All;
            for (int i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                if (!actor.IsVisibleToPlayer || actor.IsHidden)
                    continue;

                DrawOverhead(camera, actor.transform.position, actor.IsDead ? -1f : actor.Health01, actor.Speech, actor.IsShouting);
            }

            if (_player == null)
                return;

            string playerText = _player.Speech;
            DrawOverhead(camera, _player.Position, _player.IsDead ? -1f : _player.Health01, playerText);
            if (_controller != null && _controller.IsBusy)
                DrawProgressRing(camera, _player.Position + Vector3.up * 1f, _controller.TimedProgress01);
        }

        // Thin light-blue HP bar above the head, white subtitle above it (both like the original).
        private void DrawOverhead(Camera camera, Vector3 feet, float health01, string speech, bool shouting = false)
        {
            Vector3 screen = camera.WorldToScreenPoint(feet + Vector3.up * 2.05f);
            if (screen.z <= 0f)
                return;

            float x = screen.x;
            float y = Screen.height - screen.y;
            if (health01 >= 0f)
            {
                var back = new Rect(x - 24f, y, 48f, 5f);
                Fill(back, new Color(0f, 0f, 0f, 0.6f));
                Fill(new Rect(back.x + 1f, back.y + 1f, (back.width - 2f) * health01, back.height - 2f),
                    health01 > 0.3f ? HealthBlue : new Color(0.9f, 0.3f, 0.25f));
            }

            if (string.IsNullOrEmpty(speech))
                return;

            // Subtitle in a dark box above the head (original style).
            // Alarm yell: larger box and text that jitters, like the original "intruder" call.
            var style = shouting ? _shout : _subtitle;
            float maxWidth = shouting ? 420f : 300f;
            float pad = shouting ? 16f : 10f;
            var content = new GUIContent(speech);
            float width = Mathf.Min(maxWidth, style.CalcSize(content).x) + 4f;
            float height = style.CalcHeight(content, width);
            var box = new Rect(x - width * 0.5f - pad, y - height - 18f - pad * 0.5f, width + pad * 2f, height + pad);
            if (shouting)
            {
                float time = Time.unscaledTime * 38f;
                box.x += Mathf.Sin(time) * 3f + Mathf.Sin(time * 1.7f) * 1.5f;
                box.y += Mathf.Cos(time * 1.3f) * 2.5f;
            }

            Fill(box, new Color(0.05f, 0.05f, 0.06f, shouting ? 0.88f : 0.78f));
            GUI.Label(new Rect(box.x + pad, box.y + pad * 0.5f, width, height), speech, style);
        }

        private void DrawProgressRing(Camera camera, Vector3 world, float progress)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
                return;

            var rect = new Rect(screen.x - 18f, Screen.height - screen.y - 18f, 36f, 36f);
            Tint(rect, HudTextures.Ring, new Color(1f, 1f, 1f, 0.3f));
            int dots = Mathf.RoundToInt(progress * 12f);
            for (int i = 0; i < dots; i++)
            {
                float angle = i / 12f * Mathf.PI * 2f - Mathf.PI * 0.5f;
                var dot = new Rect(rect.center.x + Mathf.Cos(angle) * 16f - 3f, rect.center.y + Mathf.Sin(angle) * 16f - 3f, 6f, 6f);
                Tint(dot, HudTextures.Disc, Color.white);
            }
        }

        // Round buttons at the center of each interactable (cursor itself never changes).
        private void DrawButtons()
        {
            if (_controller == null || IsEndScreen())
                return;

            var buttons = _controller.Buttons;
            for (int i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                bool hovered = i == _controller.HoveredButton;
                if (button.Kind == PlayerClickController.ButtonKind.Group)
                    DrawGroupButton(button, hovered);
                else if (button.Kind == PlayerClickController.ButtonKind.Shoot)
                    DrawShootButton(button, hovered);
                else
                    DrawRoundButton(button.Center, button.Radius, Glyph(button.Kind), hovered, button.Kind == PlayerClickController.ButtonKind.SneakAttack);
            }
        }

        private static string Glyph(PlayerClickController.ButtonKind kind)
        {
            switch (kind)
            {
                case PlayerClickController.ButtonKind.OpenDoor: return "문";
                case PlayerClickController.ButtonKind.CloseDoor: return "닫기";
                case PlayerClickController.ButtonKind.LockedDoor: return "|||";
                case PlayerClickController.ButtonKind.Search: return "?";
                case PlayerClickController.ButtonKind.Loot: return "손";
                case PlayerClickController.ButtonKind.Melee: return "공격";
                case PlayerClickController.ButtonKind.SneakAttack: return "기습";
                case PlayerClickController.ButtonKind.Hide: return "숨기";
                case PlayerClickController.ButtonKind.LeaveHide: return "나오기";
                case PlayerClickController.ButtonKind.Peek: return "엿보기";
                default: return "";
            }
        }

        // Closed: disc with member count. Open: faint guide ring + small close disc in the middle.
        private void DrawGroupButton(PlayerClickController.InteractionButton button, bool hovered)
        {
            if (button.Expanded)
            {
                float ringSize = button.RingRadius * 2f;
                var ring = new Rect(button.Center.x - button.RingRadius, button.Center.y - button.RingRadius, ringSize, ringSize);
                Tint(ring, HudTextures.Ring, new Color(1f, 1f, 1f, 0.25f));
                DrawRoundButton(button.Center, button.Radius * 0.8f, "X", hovered, false);
                return;
            }

            DrawRoundButton(button.Center, button.Radius, $"+{button.Count}", hovered, false);
        }

        private void DrawRoundButton(Vector2 center, float radius, string glyph, bool hovered, bool danger)
        {
            float size = radius * 2f * (hovered ? 1.15f : 1f);
            var rect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            Tint(rect, HudTextures.Disc, new Color(0.05f, 0.05f, 0.05f, 0.8f));
            var face = danger ? new Color(0.95f, 0.75f, 0.75f) : new Color(0.88f, 0.88f, 0.88f);
            face.a = hovered ? 1f : 0.8f;
            Tint(new Rect(rect.x + 5f, rect.y + 5f, size - 10f, size - 10f), HudTextures.Disc, face);
            _iconGlyph.fontSize = glyph.Length > 1 ? 20 : 28;
            GUI.Label(rect, glyph, _iconGlyph);
        }

        // Rifle target button: ring gauge fills while aiming; inner cross appears at full aim.
        private void DrawShootButton(PlayerClickController.InteractionButton button, bool hovered)
        {
            var npc = button.Target as NpcActor;
            bool aimed = npc != null && npc == _controller.AimTarget;
            float size = button.Radius * 2f * (hovered ? 1.1f : 1f);
            var center = button.Center;
            var rect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            Tint(rect, HudTextures.Ring, new Color(1f, 1f, 1f, hovered ? 0.9f : 0.55f));

            if (aimed)
            {
                const int segments = 28;
                float radius = size * 0.5f - 2f;
                int filled = Mathf.RoundToInt(_controller.AimGauge * segments);
                int cap = Mathf.RoundToInt(_controller.AimMax * segments);
                for (int i = 0; i < segments; i++)
                {
                    float angle = i / (float)segments * Mathf.PI * 2f - Mathf.PI * 0.5f;
                    var color = i < filled ? new Color(0.95f, 0.25f, 0.2f) : i < cap ? new Color(1f, 1f, 1f, 0.25f) : new Color(0f, 0f, 0f, 0.5f);
                    var dot = new Rect(center.x + Mathf.Cos(angle) * radius - 5f, center.y + Mathf.Sin(angle) * radius - 5f, 10f, 10f);
                    Tint(dot, HudTextures.Disc, color);
                }
            }

            var cross = aimed && _controller.IsFullAim ? new Color(0.95f, 0.25f, 0.2f) : new Color(1f, 1f, 1f, 0.7f);
            float arm = aimed && _controller.IsFullAim ? 24f : 14f;
            Fill(new Rect(center.x - 1.5f, center.y - arm, 3f, arm * 2f), cross);
            Fill(new Rect(center.x - arm, center.y - 1.5f, arm * 2f, 3f), cross);
        }

        // ---------------- Drawing helpers ----------------

        // IMGUI labels switch to the hover/active text color under the mouse (white in the default skin).
        // Copy the normal color into every state (values only; GUIStyleState objects must not be shared).
        public static void FreezeStates(GUIStyle style)
        {
            SetTextColor(style, style.normal.textColor);
        }

        public static void SetTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        private static void Fill(Rect rect, Color color) => Tint(rect, HudTextures.White, color);

        private static void Tint(Rect rect, Texture texture, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, texture);
            GUI.color = previous;
        }
    }
}
