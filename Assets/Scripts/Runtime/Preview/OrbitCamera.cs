using UnityEngine;
using UnityEngine.InputSystem;

namespace WFC.Runtime
{
    /// <summary>
    /// Kamera do oglądania i zrzutów ekranu: prawy przycisk myszy obraca wokół celu, środkowy przesuwa cel,
    /// kółko przybliża, W A S D przesuwa cel w poziomie, Q i E obniżają i podnoszą cel.
    /// </summary>
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] Vector3 target;
        [SerializeField, Min(1f)] float distance = 60f;
        [SerializeField] float yaw = 30f;
        [SerializeField, Range(-89f, 89f)] float pitch = 40f;
        [SerializeField] float rotateSpeed = 0.25f;
        [SerializeField] float panSpeed = 0.002f;
        [SerializeField] float zoomSpeed = 0.001f;
        [SerializeField] float moveSpeed = 30f;
        [SerializeField] Vector2 distanceRange = new Vector2(3f, 600f);

        public Vector3 Target
        {
            get => target;
            set => target = value;
        }

        public float Distance
        {
            get => distance;
            set => distance = Mathf.Clamp(value, distanceRange.x, distanceRange.y);
        }

        public void SetView(Vector3 newTarget, float newDistance, float newYaw, float newPitch)
        {
            target = newTarget;
            Distance = newDistance;
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, -89f, 89f);
            Apply();
        }

        void LateUpdate()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (mouse.rightButton.isPressed)
                {
                    yaw += delta.x * rotateSpeed;
                    pitch = Mathf.Clamp(pitch - delta.y * rotateSpeed, -89f, 89f);
                }
                if (mouse.middleButton.isPressed)
                    target -= (transform.right * delta.x + transform.up * delta.y) * (panSpeed * distance);
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f)
                    Distance = distance * (1f - scroll * zoomSpeed);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
                Vector3 move = Vector3.zero;
                if (keyboard.wKey.isPressed) move += forward;
                if (keyboard.sKey.isPressed) move -= forward;
                if (keyboard.dKey.isPressed) move += right;
                if (keyboard.aKey.isPressed) move -= right;
                if (keyboard.eKey.isPressed) move += Vector3.up;
                if (keyboard.qKey.isPressed) move -= Vector3.up;
                target += move * (moveSpeed * Time.unscaledDeltaTime);
            }

            Apply();
        }

        void OnValidate() => Apply();

        void Apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(target - rotation * Vector3.forward * distance, rotation);
        }
    }
}
