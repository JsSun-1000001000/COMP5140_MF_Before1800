using UnityEngine;
using UnityEngine.InputSystem;

namespace Before1800
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonPlayer : MonoBehaviour
    {
        public Camera viewCamera;
        public float walkSpeed = 3.0f;
        public float sensitivity = 0.11f;
        public bool inputEnabled;
        private CharacterController body;
        private float pitch;
        private float verticalSpeed;

        private void Awake() { body = GetComponent<CharacterController>(); }
        private void Update()
        {
            if (!inputEnabled || Keyboard.current == null || Mouse.current == null) return;
            Vector2 look = Mouse.current.delta.ReadValue() * sensitivity;
            transform.Rotate(0, look.x, 0);
            pitch = Mathf.Clamp(pitch - look.y, -75, 75);
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            var keyboard = Keyboard.current;
            float x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            float z = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            Vector3 move = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1);
            verticalSpeed = body.isGrounded ? -2 : verticalSpeed + Physics.gravity.y * Time.deltaTime;
            body.Move((move * walkSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        public void ResetPosition(Vector3 point, float yaw)
        {
            if (body == null) body = GetComponent<CharacterController>();
            body.enabled = false;
            transform.SetPositionAndRotation(point, Quaternion.Euler(0, yaw, 0));
            pitch = 0;
            viewCamera.transform.localRotation = Quaternion.identity;
            verticalSpeed = 0;
            body.enabled = true;
        }
    }
}
