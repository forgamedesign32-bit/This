using UnityEngine;

namespace TwomAi
{
    // The thing NPCs perceive and attack.
    public sealed class PlayerTarget : MonoBehaviour, IGraphAgent
    {
        [SerializeField] private string _displayName = "Pavle";
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private float _chestHeight = 1.2f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private CharacterAnimator _animator;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private float _health;
        private string _speech;
        private float _speechUntil;

        public static PlayerTarget Instance { get; private set; }

        public int AgentId => AiIds.PlayerSourceId;
        public bool IsIncapacitated => IsDead;
        public string DisplayName => _displayName;
        public Vector3 Position => transform.position;
        public Vector3 ChestPosition => transform.position + Vector3.up * _chestHeight;
        public float Health => _health;
        public float MaxHealth => _maxHealth;
        public float Health01 => _maxHealth > 0f ? Mathf.Clamp01(_health / _maxHealth) : 0f;
        public bool IsDead { get; private set; }
        public string Speech => Time.time < _speechUntil ? _speech : null;

        // Hidden in a shadowed alcove: not seen by NPC sight (see PerceptionSensor / GuardBrain search).
        public bool IsHidden { get; private set; }

        public void SetHidden(bool hidden)
        {
            if (IsHidden == hidden)
                return;

            IsHidden = hidden;
            var color = hidden ? new Color(0.25f, 0.25f, 0.3f) : Color.white;
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        // Character "thought" shown above the head (original: survivor comments on situations).
        public void Say(string text, float duration = 3f)
        {
            _speech = text;
            _speechUntil = Time.time + duration;
        }

        // Raised on every hit taken (aim gauge penalty).
        public event System.Action Damaged;

        private void Awake()
        {
            Instance = this;
            _health = _maxHealth;
            _animator = GetComponent<CharacterAnimator>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            var director = AiDirector.Instance;
            if (director != null && director.GodMode)
                return;

            _health = Mathf.Max(0f, _health - amount);
            Damaged?.Invoke();
            if (_health > 0f)
            {
                if (_animator != null) _animator.Play(CharacterAction.HitReact);
                return;
            }

            IsDead = true;
            SetHidden(false);
            AiLog.Add("Player died");
            if (_animator != null) _animator.Play(CharacterAction.Die);
        }
    }
}
