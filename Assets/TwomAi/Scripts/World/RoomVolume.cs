using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // One room of the cross-section, veiled by vertical fog strips in front of it.
    // Visible: all strips clear. Peeking through a door: strips clear progressively from the door outward
    // (the original's spreading view cone through the keyhole).
    public sealed class RoomVolume : MonoBehaviour
    {
        private static readonly List<RoomVolume> AllRooms = new List<RoomVolume>();
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private string _displayName = "Room";
        [SerializeField] private float _minX;
        [SerializeField] private float _maxX;
        [SerializeField] private float _floorY;
        [SerializeField] private float _height = 3.2f;
        [SerializeField] private bool _alwaysVisible;
        [SerializeField] private Renderer[] _fogStrips = Array.Empty<Renderer>();
        [SerializeField] private Color _fogColor = new Color(0.01f, 0.015f, 0.03f, 1f);
        [SerializeField] private float _exploredAlpha = 0.72f;
        [SerializeField] private float _unexploredAlpha = 0.94f;
        [SerializeField] private float _fadeSpeed = 4f;
        [SerializeField] private float _peekSpreadSpeed = 7f;

        private MaterialPropertyBlock _block;
        private float[] _alphas = Array.Empty<float>();
        private bool _peeking;
        private float _peekOriginX;
        private float _peekStartedAt;

        public static IReadOnlyList<RoomVolume> All => AllRooms;

        public string DisplayName => _displayName;
        public float MinX => _minX;
        public float MaxX => _maxX;
        public float FloorY => _floorY;
        public bool AlwaysVisible => _alwaysVisible;
        public bool IsVisible { get; private set; }
        public bool IsExplored { get; private set; }

        private float PeekRadius => _peeking ? (Time.time - _peekStartedAt) * _peekSpreadSpeed : -1f;

        private void OnEnable() => AllRooms.Add(this);
        private void OnDisable() => AllRooms.Remove(this);

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            IsVisible = _alwaysVisible;
            IsExplored = _alwaysVisible;
            _alphas = new float[_fogStrips.Length];
            for (int i = 0; i < _alphas.Length; i++)
                _alphas[i] = -1f;
        }

        public bool Contains(Vector3 position)
        {
            return position.x >= _minX && position.x <= _maxX
                && position.y >= _floorY - 0.3f && position.y < _floorY + _height - 0.3f;
        }

        public void SetVisible(bool visible)
        {
            IsVisible = visible || _alwaysVisible;
            if (IsVisible)
                IsExplored = true;
        }

        public void BeginPeek(float doorX)
        {
            _peeking = true;
            _peekOriginX = doorX;
            _peekStartedAt = Time.time;
            IsExplored = true;
        }

        public void EndPeek() => _peeking = false;

        // Visible to the player at this point (whole room seen, or inside the spreading peek view).
        public bool IsRevealedAt(Vector3 position)
        {
            return IsVisible || VisibilitySystem.XRay || (_peeking && Mathf.Abs(position.x - _peekOriginX) <= PeekRadius);
        }

        private void Update()
        {
            float radius = PeekRadius;
            for (int i = 0; i < _fogStrips.Length; i++)
            {
                var strip = _fogStrips[i];
                if (strip == null)
                    continue;

                float centerX = strip.transform.position.x;
                bool clear = IsVisible || VisibilitySystem.XRay || (_peeking && Mathf.Abs(centerX - _peekOriginX) <= radius);
                float target = clear ? 0f : IsExplored ? _exploredAlpha : _unexploredAlpha;
                // Peek reveal snaps open strip by strip; normal visibility fades.
                float speed = _peeking && clear ? _fadeSpeed * 3f : _fadeSpeed;
                _alphas[i] = _alphas[i] < 0f ? target : Mathf.MoveTowards(_alphas[i], target, speed * Time.deltaTime);
                strip.enabled = _alphas[i] > 0.001f;
                var color = _fogColor;
                color.a = _alphas[i];
                strip.GetPropertyBlock(_block);
                _block.SetColor(ColorId, color);
                strip.SetPropertyBlock(_block);
            }
        }
    }
}
