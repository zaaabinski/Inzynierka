using UnityEngine;
using UnityEngine.InputSystem;

namespace WFC.Runtime
{
    /// <summary>
    /// Kamera do oglądania i zrzutów ekranu, sterowana jak widok Scene w Unity:
    /// <list type="bullet">
    /// <item>prawy przycisk myszy przytrzymany: rozglądanie się (obrót kamery w miejscu),</item>
    /// <item>prawy przycisk + W, S, A, D: lot do przodu, do tyłu i na boki; Q i E: w dół i w górę; Shift: 3 razy szybciej,</item>
    /// <item>kółko myszy: przybliżanie i oddalanie, a z prawym przyciskiem zmiana prędkości lotu,</item>
    /// <item>środkowy przycisk przytrzymany: przesuwanie widoku.</item>
    /// </list>
    /// </summary>
    public sealed class FlyCamera : MonoBehaviour
    {
        [Tooltip("Obrót w stopniach na piksel ruchu myszy.")]
        [SerializeField, Min(0.01f)] float lookSensitivity = 0.15f;
        [Tooltip("Prędkość lotu w metrach na sekundę; kółko z prawym przyciskiem zmienia ją o 20% na ząbek.")]
        [SerializeField, Min(0.1f)] float flySpeed = 20f;
        [SerializeField] Vector2 flySpeedRange = new Vector2(2f, 200f);
        [SerializeField, Min(1f)] float fastMultiplier = 3f;
        [Tooltip("Przesunięcie do przodu lub do tyłu w metrach na ząbek kółka.")]
        [SerializeField, Min(0.1f)] float zoomStep = 4f;
        [Tooltip("Przesunięcie widoku w metrach na piksel ruchu myszy przy środkowym przycisku.")]
        [SerializeField, Min(0.001f)] float panSpeed = 0.05f;

        float yaw;
        float pitch;
        bool looking;

        public float FlySpeed
        {
            get => flySpeed;
            set => flySpeed = Mathf.Clamp(value, flySpeedRange.x, flySpeedRange.y);
        }

        /// <summary>Ustawia kamerę w odległości distance od celu, pod kątami yaw (wokół osi Y) i pitch (w dół).</summary>
        public void LookAt(Vector3 target, float distance, float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, -89f, 89f);
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(target - rotation * Vector3.forward * distance, rotation);
        }

        void OnEnable()
        {
            Vector3 angles = transform.eulerAngles;
            pitch = angles.x > 180f ? angles.x - 360f : angles.x;
            yaw = angles.y;
        }

        void OnDisable() => SetLooking(false);

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            Keyboard keyboard = Keyboard.current;

            bool rightHeld = mouse.rightButton.isPressed;
            if (rightHeld != looking)
                SetLooking(rightHeld);

            Vector2 delta = mouse.delta.ReadValue();
            float notches = ScrollNotches(mouse.scroll.ReadValue().y);
            bool fast = keyboard != null && keyboard.shiftKey.isPressed;

            if (looking)
            {
                yaw += delta.x * lookSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

                if (keyboard != null)
                {
                    var local = Vector3.zero;
                    if (keyboard.wKey.isPressed) local += Vector3.forward;
                    if (keyboard.sKey.isPressed) local += Vector3.back;
                    if (keyboard.dKey.isPressed) local += Vector3.right;
                    if (keyboard.aKey.isPressed) local += Vector3.left;
                    float vertical = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                    Vector3 move = transform.TransformDirection(local) + Vector3.up * vertical;
                    transform.position += move * (flySpeed * (fast ? fastMultiplier : 1f) * Time.unscaledDeltaTime);
                }

                if (notches != 0f)
                    FlySpeed = flySpeed * Mathf.Pow(1.2f, notches);
            }
            else if (notches != 0f)
            {
                transform.position += transform.forward * (notches * zoomStep * (fast ? fastMultiplier : 1f));
            }

            if (mouse.middleButton.isPressed)
                transform.position -= (transform.right * delta.x + transform.up * delta.y) * panSpeed;
        }

        /// <summary>Ząbki kółka myszy: Windows podaje 120 na ząbek, inne platformy 1.</summary>
        static float ScrollNotches(float scroll) => Mathf.Abs(scroll) >= 10f ? scroll / 120f : scroll;

        void SetLooking(bool value)
        {
            looking = value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }
    }
}
