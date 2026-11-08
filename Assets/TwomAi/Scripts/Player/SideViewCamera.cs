using UnityEngine;
using UnityEngine.InputSystem;

namespace TwomAi
{
    // Building cross-section view (camera looks along +Z at the open front of the building).
    // Follows the character, mouse wheel zooms, A/D or arrows pan, Space re-centers.
    [RequireComponent(typeof(Camera))]
    public sealed class SideViewCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float _distance = 12f;
        [SerializeField] private float _minDistance = 6f;
        [SerializeField] private float _maxDistance = 20f;
        [SerializeField] private float _zoomStep = 1.5f;
        [SerializeField] private float _heightOffset = 1.6f;
        [SerializeField] private float _pitch = 4f;
        [SerializeField] private float _followSharpness = 6f;
        [SerializeField] private float _panSpeed = 10f;
        [SerializeField] private float _maxPanOffset = 14f;
        [SerializeField] private float _minX = -22f;
        [SerializeField] private float _maxX = 16f;

        private Vector3 _focus;
        private float _panOffset;

        private void Start()
        {
            if (_target == null && PlayerTarget.Instance != null)
                _target = PlayerTarget.Instance.transform;

            if (_target == null)
                return;

            _focus = DesiredFocus();
            Apply();
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            HandleInput();
            float t = 1f - Mathf.Exp(-_followSharpness * Time.deltaTime);
            _focus = Vector3.Lerp(_focus, DesiredFocus(), t);
            Apply();
        }

        private void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse != null && !UiBlocker.IsPointerOverUi(mouse.position.ReadValue()))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    _distance = Mathf.Clamp(_distance - Mathf.Sign(scroll) * _zoomStep, _minDistance, _maxDistance);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            float pan = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) pan -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) pan += 1f;
            _panOffset = Mathf.Clamp(_panOffset + pan * _panSpeed * Time.deltaTime, -_maxPanOffset, _maxPanOffset);
            if (keyboard.spaceKey.wasPressedThisFrame)
                _panOffset = 0f;
        }

        private Vector3 DesiredFocus()
        {
            Vector3 target = _target.position;
            float x = Mathf.Clamp(target.x + _panOffset, _minX, _maxX);
            return new Vector3(x, target.y + _heightOffset, 0f);
        }

        private void Apply()
        {
            transform.rotation = Quaternion.Euler(_pitch, 0f, 0f);
            transform.position = _focus - transform.forward * _distance;
        }
    }
}
