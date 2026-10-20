using UnityEngine;
using UnityEngine.InputSystem;

namespace WFC.Runtime
{
    /// <summary>
    /// Kamera do oglądania i zrzutów ekranu:
    /// <list type="bullet">
    /// <item>prawy przycisk myszy przytrzymany: obrót dookoła punktu przed kamerą (na starcie środek zamku),</item>
    /// <item>lewy przycisk przytrzymany: rozglądanie się, a z W, S, A, D lot do przodu, do tyłu i na boki;
    /// Q i E w dół i w górę; Shift 3 razy szybciej,</item>
    /// <item>kółko myszy: przybliżanie do punktu obrotu, a z lewym przyciskiem zmiana prędkości lotu,</item>
    /// <item>środkowy przycisk przytrzymany: przesuwanie widoku.</item>
    /// </list>
    /// Punkt obrotu leży zawsze w odległości orbitDistance przed kamerą, więc po przelocie przesuwa się razem z nią,
    /// tak jak w widoku Scene w Unity.
    /// </summary>
    public sealed class FlyCamera : MonoBehaviour
    {
        enum Mode
        {
            None,
            Orbit,
            Fly
        }

        [Tooltip("Obrót w stopniach na piksel ruchu myszy (obracanie i rozglądanie się).")]
        [SerializeField, Min(0.01f)] float rotateSensitivity = 0.2f;
        [Tooltip("Odległość od kamery do punktu, wokół którego obraca prawy przycisk [m]. Zmienia ją kółko myszy.")]
        [SerializeField, Min(1f)] float orbitDistance = 60f;
        [SerializeField] Vector2 orbitDistanceRange = new Vector2(2f, 600f);
        [Tooltip("Prędkość lotu w metrach na sekundę; kółko z lewym przyciskiem zmienia ją o 20% na ząbek.")]
        [SerializeField, Min(0.1f)] float flySpeed = 20f;
        [SerializeField] Vector2 flySpeedRange = new Vector2(2f, 200f);
        [SerializeField, Min(1f)] float fastMultiplier = 3f;
        [Tooltip("Przybliżenie na ząbek kółka jako ułamek odległości od punktu obrotu.")]
        [SerializeField, Range(0.01f, 0.5f)] float zoomFraction = 0.12f;
        [Tooltip("Przesunięcie widoku na piksel ruchu myszy jako ułamek odległości od punktu obrotu.")]
        [SerializeField, Min(0.0001f)] float panFraction = 0.0015f;

        float yaw;
        float pitch;
        Mode mode;

        /// <summary>Punkt, wokół którego obraca prawy przycisk myszy.</summary>
        public Vector3 Pivot => transform.position + transform.forward * orbitDistance;

        public float FlySpeed
        {
            get => flySpeed;
            set => flySpeed = Mathf.Clamp(value, flySpeedRange.x, flySpeedRange.y);
        }

        /// <summary>Ustawia kamerę w odległości distance od celu, pod kątami yaw (wokół osi Y) i pitch (w dół); cel staje się punktem obrotu.</summary>
        public void LookAt(Vector3 target, float distance, float newYaw, float newPitch)
        {
            orbitDistance = Mathf.Clamp(distance, orbitDistanceRange.x, orbitDistanceRange.y);
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, -89f, 89f);
            PlaceAround(target);
        }

        void OnEnable()
        {
            Vector3 angles = transform.eulerAngles;
            pitch = angles.x > 180f ? angles.x - 360f : angles.x;
            yaw = angles.y;
        }

        void OnDisable() => SetMode(Mode.None);

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            Keyboard keyboard = Keyboard.current;

            Mode wanted = mouse.leftButton.isPressed ? Mode.Fly : mouse.rightButton.isPressed ? Mode.Orbit : Mode.None;
            if (wanted != mode)
                SetMode(wanted);

            Vector2 delta = mouse.delta.ReadValue();
            float notches = ScrollNotches(mouse.scroll.ReadValue().y);
            bool fast = keyboard != null && keyboard.shiftKey.isPressed;

            switch (mode)
            {
                case Mode.Orbit:
                {
                    Vector3 pivot = Pivot;
                    Rotate(delta);
                    PlaceAround(pivot);
                    break;
                }
                case Mode.Fly:
                    Rotate(delta);
                    transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                    if (keyboard != null)
                        Fly(keyboard, fast);
                    if (notches != 0f)
                        FlySpeed = flySpeed * Mathf.Pow(1.2f, notches);
                    break;
            }

            if (mode != Mode.Fly && notches != 0f)
            {
                Vector3 pivot = Pivot;
                float factor = Mathf.Pow(1f - zoomFraction, notches * (fast ? 2f : 1f));
                orbitDistance = Mathf.Clamp(orbitDistance * factor, orbitDistanceRange.x, orbitDistanceRange.y);
                PlaceAround(pivot);
            }

            if (mouse.middleButton.isPressed)
                transform.position -= (transform.right * delta.x + transform.up * delta.y) * (panFraction * orbitDistance);
        }

        void Rotate(Vector2 delta)
        {
            yaw += delta.x * rotateSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * rotateSensitivity, -89f, 89f);
        }

        void Fly(Keyboard keyboard, bool fast)
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

        void PlaceAround(Vector3 pivot)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * orbitDistance, rotation);
        }

        /// <summary>Ząbki kółka myszy: Windows podaje 120 na ząbek, inne platformy 1.</summary>
        static float ScrollNotches(float scroll) => Mathf.Abs(scroll) >= 10f ? scroll / 120f : scroll;

        void SetMode(Mode value)
        {
            mode = value;
            bool locked = value != Mode.None;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
