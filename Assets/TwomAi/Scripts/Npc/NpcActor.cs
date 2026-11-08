using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // Identity, health, defense, hidden flag, speech, tint and fog visibility shared by guards and civilians.
    public sealed class NpcActor : MonoBehaviour, IGraphAgent
    {
        private static readonly List<NpcActor> AllActors = new List<NpcActor>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private int _npcId = 1;
        [SerializeField] private NpcKind _kind;
        [SerializeField] private string _displayName = "NPC";
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private Transform _modelRoot;
        [SerializeField] private Color _tint = Color.white;
        [SerializeField] private float _eyeHeight = 1.6f;

        private Renderer[] _renderers = Array.Empty<Renderer>();
        private MaterialPropertyBlock _block;
        private CharacterAnimator _animator;
        private IDefender _defender;
        private float _health;
        private string _speech;
        private float _speechUntil;
        private bool _shouting;
        private bool _visibleToPlayer = true;
        private float _staggerUntil;

        public static IReadOnlyList<NpcActor> All => AllActors;

        public int Id => _npcId;
        public int AgentId => _npcId;
        public bool IsIncapacitated => IsDead;
        public NpcKind Kind => _kind;
        public string DisplayName => _displayName;
        public bool IsDead { get; private set; }
        public bool IsHidden { get; private set; }
        public bool IsAlertedToPlayer { get; set; }
        public bool IsVisibleToPlayer => _visibleToPlayer;
        public float Health01 => _maxHealth > 0f ? Mathf.Clamp01(_health / _maxHealth) : 0f;
        public Vector3 EyePosition => transform.position + Vector3.up * _eyeHeight;
        public string Speech => Time.time < _speechUntil ? _speech : null;
        public bool IsShouting => _shouting && Time.time < _speechUntil;
        public CharacterAnimator Animator => _animator;
        public bool IsStaggered => Time.time < _staggerUntil;

        public event Action<NpcActor> Died;

        // (health01 before the hit, attacker position)
        public event Action<float, Vector3> Damaged;

        private void Awake()
        {
            _health = _maxHealth;
            _block = new MaterialPropertyBlock();
            _animator = GetComponent<CharacterAnimator>();
            _defender = GetComponent<IDefender>();
            if (_modelRoot != null)
                _renderers = _modelRoot.GetComponentsInChildren<Renderer>(true);

            ApplyTint(_tint);
        }

        private void OnEnable() => AllActors.Add(this);
        private void OnDisable() => AllActors.Remove(this);

        public void Say(string text, float duration = 2.2f)
        {
            _speech = text;
            _speechUntil = Time.time + duration;
            _shouting = false;
        }

        // Alarm yell: shown in a bigger, shaking subtitle box.
        public void Shout(string text, float duration = 2.6f)
        {
            Say(text, duration);
            _shouting = true;
        }

        // Hit from the player. Aware targets may block (reduced) or dodge (none) melee blows.
        // A full-aim shot staggers: the target is knocked back and briefly loses its action.
        public DefenseResult ReceiveAttack(float damage, bool canDefend, bool stagger = false, Vector3 from = default)
        {
            if (IsDead)
                return DefenseResult.None;

            var result = canDefend && _defender != null ? _defender.Defend() : DefenseResult.None;
            if (result == DefenseResult.Dodge)
            {
                if (_animator != null) _animator.Play(CharacterAction.Dodge);
                AiLog.Add($"{name} dodged");
                return result;
            }

            if (result == DefenseResult.Block)
                damage *= 0.4f;

            float before = Health01;
            TakeDamage(damage);
            if (!IsDead)
                Damaged?.Invoke(before, from);
            if (IsDead)
                return result;

            if (stagger)
            {
                _staggerUntil = Time.time + 0.7f;
                KnockBack(from, 0.5f);
                if (_animator != null) _animator.Play(CharacterAction.Stagger);
                return result;
            }

            if (_animator != null) _animator.Play(result == DefenseResult.Block ? CharacterAction.Block : CharacterAction.HitReact);

            return result;
        }

        private void KnockBack(Vector3 from, float distance)
        {
            Vector3 away = AiMath.Flat(transform.position - from);
            if (away.sqrMagnitude < 0.0001f)
                return;

            Vector3 target = transform.position + away.normalized * distance;
            if (!AiPhysics.IsOccluded(transform.position + Vector3.up, target + Vector3.up))
                transform.position = target;
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            _health -= amount;
            if (_health > 0f)
            {
                AiLog.Add($"{name} hit ({_health:0} hp left)");
                return;
            }

            _health = 0f;
            Die();
        }

        public void SetHidden(bool hidden)
        {
            if (IsDead)
                return;

            IsHidden = hidden;
            ApplyRendererVisibility();
        }

        // Fog of war: renderers are hidden while the character's room is out of the player's sight.
        public void SetVisibleToPlayer(bool visible)
        {
            if (_visibleToPlayer == visible)
                return;

            _visibleToPlayer = visible;
            ApplyRendererVisibility();
        }

        // A hidden NPC (inside a wardrobe) is not drawn at all until it comes out, regardless of fog.
        private void ApplyRendererVisibility()
        {
            bool show = _visibleToPlayer && !IsHidden;
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].enabled = show;
        }

        private void ApplyTint(Color color)
        {
            color.a = 1f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void Die()
        {
            IsDead = true;
            IsHidden = false;
            ApplyRendererVisibility();
            IsAlertedToPlayer = false;
            _speech = null;
            AiLog.Add($"{name} died");
            ApplyTint(_tint * 0.6f);

            if (_animator != null && _animator.HasDeathAnimation)
            {
                _animator.Play(CharacterAction.Die);
            }
            else if (_modelRoot != null)
            {
                // No death clip: lay the model down so it reads clearly from the side camera.
                _modelRoot.localRotation = Quaternion.Euler(0f, 0f, 90f);
                _modelRoot.localPosition = new Vector3(0f, 0.25f, 0f);
            }

            var colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            Died?.Invoke(this);
        }
    }
}
