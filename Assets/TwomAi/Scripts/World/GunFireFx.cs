using UnityEngine;

namespace TwomAi
{
    // Gunfire presentation modeled on the original night footage:
    // a short, very bright soft-white flash stretched along the barrel that lights the room for a frame or two,
    // a thin smoke wisp at the muzzle, a dark mist puff on a hit and a grey dust puff where a miss lands.
    // No visible bullet line. Self-creating, no scene setup needed.
    public sealed class GunFireFx : MonoBehaviour
    {
        private const int PoolSize = 16;
        private const float FlashDuration = 0.07f;
        private const float LightDuration = 0.09f;
        private const float FrontDepth = -0.6f;

        private enum Kind { Flash, Smoke, Blood, Dust }

        private sealed class Puff
        {
            public SpriteRenderer Renderer;
            public Kind Kind;
            public float Start;
            public float Duration;
            public Vector3 Origin;
            public Vector3 Drift;
            public Vector2 StartScale;
            public Vector2 EndScale;
            public Color Color;
        }

        private readonly Puff[] _puffs = new Puff[PoolSize];
        private Light _light;
        private float _lightUntil;
        private float _lightPeak;
        private int _next;

        private static GunFireFx _instance;
        private static Sprite _softSprite;

        public static GunFireFx Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new GameObject("GunFireFx").AddComponent<GunFireFx>();
                return _instance;
            }
        }

        private void Awake()
        {
            _instance = this;
            var sprite = GetSoftSprite();
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Puff_" + i);
                go.transform.SetParent(transform, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = 50;
                renderer.enabled = false;
                _puffs[i] = new Puff { Renderer = renderer };
            }

            var lightObject = new GameObject("MuzzleLight");
            lightObject.transform.SetParent(transform, false);
            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 7f;
            _light.color = new Color(1f, 0.93f, 0.8f);
            _light.shadows = LightShadows.None;
            _light.enabled = false;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        // Muzzle flash + room light + smoke; then a hit or miss puff at the impact point.
        public void Shot(Vector3 muzzle, Vector3 target, bool hit)
        {
            Vector3 direction = target - muzzle;
            direction.z = 0f;
            float sign = direction.x >= 0f ? 1f : -1f;
            float angle = Mathf.Atan2(direction.y, Mathf.Abs(direction.x)) * Mathf.Rad2Deg * sign;

            Vector3 flashCenter = muzzle + new Vector3(sign * 0.35f, 0f, 0f);
            Spawn(Kind.Flash, flashCenter, Vector3.zero, new Vector2(1.1f, 0.42f), new Vector2(1.3f, 0.3f),
                new Color(1f, 1f, 1f, 1f), FlashDuration, angle);
            Spawn(Kind.Smoke, muzzle, new Vector3(sign * 0.15f, 0.35f, 0f), new Vector2(0.25f, 0.25f), new Vector2(0.8f, 0.8f),
                new Color(0.75f, 0.78f, 0.82f, 0.35f), 0.7f, 0f);

            _light.transform.position = new Vector3(muzzle.x, muzzle.y, -0.8f);
            _lightPeak = 5f;
            _light.intensity = _lightPeak;
            _light.enabled = true;
            _lightUntil = Time.time + LightDuration;

            if (hit)
            {
                Spawn(Kind.Blood, target, new Vector3(sign * 0.25f, 0.05f, 0f), new Vector2(0.2f, 0.2f), new Vector2(0.7f, 0.55f),
                    new Color(0.45f, 0.05f, 0.05f, 0.7f), 0.45f, 0f);
                return;
            }

            // Miss: the round goes past and kicks dust off the wall / floor behind the target.
            Vector3 beyond = target + new Vector3(sign * 0.8f, Random.Range(-0.4f, 0.3f), 0f);
            Spawn(Kind.Dust, beyond, new Vector3(0f, 0.2f, 0f), new Vector2(0.15f, 0.15f), new Vector2(0.6f, 0.6f),
                new Color(0.7f, 0.7f, 0.72f, 0.6f), 0.5f, 0f);
        }

        // Thrown at the player's position when an enemy round lands.
        public void PlayerHit(Vector3 chest)
        {
            Spawn(Kind.Blood, chest, new Vector3(0f, 0.05f, 0f), new Vector2(0.2f, 0.2f), new Vector2(0.7f, 0.55f),
                new Color(0.45f, 0.05f, 0.05f, 0.7f), 0.45f, 0f);
        }

        private void Spawn(Kind kind, Vector3 position, Vector3 drift, Vector2 startScale, Vector2 endScale, Color color, float duration, float angle)
        {
            var puff = _puffs[_next];
            _next = (_next + 1) % PoolSize;
            puff.Kind = kind;
            puff.Start = Time.time;
            puff.Duration = duration;
            puff.Origin = new Vector3(position.x, position.y, FrontDepth);
            puff.Drift = drift;
            puff.StartScale = startScale;
            puff.EndScale = endScale;
            puff.Color = color;
            puff.Renderer.transform.SetPositionAndRotation(puff.Origin, Quaternion.Euler(0f, 0f, angle));
            puff.Renderer.enabled = true;
            Apply(puff, 0f);
        }

        private void LateUpdate()
        {
            float now = Time.time;
            if (_light.enabled)
            {
                float left = _lightUntil - now;
                if (left <= 0f)
                    _light.enabled = false;
                else
                    _light.intensity = _lightPeak * (left / LightDuration);
            }

            for (int i = 0; i < PoolSize; i++)
            {
                var puff = _puffs[i];
                if (!puff.Renderer.enabled)
                    continue;

                float t = (now - puff.Start) / puff.Duration;
                if (t >= 1f)
                {
                    puff.Renderer.enabled = false;
                    continue;
                }

                Apply(puff, t);
            }
        }

        private static void Apply(Puff puff, float t)
        {
            float ease = 1f - (1f - t) * (1f - t);
            var transform = puff.Renderer.transform;
            transform.position = puff.Origin + puff.Drift * ease;
            Vector2 scale = Vector2.Lerp(puff.StartScale, puff.EndScale, ease);
            transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var color = puff.Color;
            // The flash pops at full strength and cuts out; puffs fade out smoothly.
            color.a *= puff.Kind == Kind.Flash ? (t < 0.6f ? 1f : (1f - t) / 0.4f) : 1f - t;
            puff.Renderer.color = color;
        }

        // Radial falloff sprite (bright core, soft edge), 1 world unit across.
        private static Sprite GetSoftSprite()
        {
            if (_softSprite != null)
                return _softSprite;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            _softSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            return _softSprite;
        }
    }
}
