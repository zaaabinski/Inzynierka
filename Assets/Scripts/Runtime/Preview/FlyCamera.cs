using UnityEngine;
using UnityEngine.InputSystem;

namespace WFC.Runtime
{
    /// <summary>
    /// Kamera do oglądania i zrzutów ekranu:
    /// <list type="bullet">
    /// <item>prawy przycisk myszy przytrzymany: obrót dookoła punktu przed kamerą (na starcie środek zamku),</item>
    /// <item>kółko myszy: przybliżanie do punktu obrotu, z Shift 2 razy szybciej,</item>
    /// <item>środkowy przycisk przytrzymany: przesuwanie widoku.</item>
    /// </list>
    /// Lewy przycisk zostaje wolny do klikania w oknie ustawień. Punkt obrotu leży zawsze w odległości orbitDistance
    /// przed kamerą, więc po przesunięciu widoku przesuwa się razem z nią, tak jak w widoku Scene w Unity.
    /// </summary>
    public sealed class FlyCamera : MonoBehaviour
    {
        enum Mode
        {
            None,
            Orbit
        }

        [Tooltip("Obrót w stopniach na piksel ruchu myszy.")]
        [SerializeField, Min(0.01f)] float rotateSensitivity = 0.2f;
        [Tooltip("Odległość od kamery do punktu, wokół którego obraca prawy przycisk [m]. Zmienia ją kółko myszy.")]
        [SerializeField, Min(1f)] float orbitDistance = 60f;
        [SerializeField] Vector2 orbitDistanceRange = new Vector2(2f, 600f);
        [Tooltip("Przybliżenie na ząbek kółka jako ułamek odległości od punktu obrotu.")]
        [SerializeField, Range(0.01f, 0.5f)] float zoomFraction = 0.12f;
        [Tooltip("Przesunięcie widoku na piksel ruchu myszy jako ułamek odległości od punktu obrotu.")]
        [SerializeField, Min(0.0001f)] float panFraction = 0.0015f;

        float yaw;
        float pitch;
        Mode mode;

        /// <summary>Punkt, wokół którego obraca prawy przycisk myszy.</summary>
        public Vector3 Pivot => transform.position + transform.forward * orbitDistance;

        /// <summary>Ustawia kamerę w odległości distance od celu, pod kątami yaw (wokół osi Y) i pitch (w dół); cel staje się punktem obrotu.</summary>
        public void LookAt(Vector3 target, float distance, float newYaw, float newPitch)
        {
            orbitDistance = Mathf.Clamp(distance, orbitDistanceRange.x, orbitDistanceRange.y);
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, -89f, 89f);
            PlaceAround(target);
        }

        /// <summary>Ustawia kamerę w odległości distance od celu pod obecnymi kątami; cel staje się punktem obrotu.</summary>
        public void Frame(Vector3 target, float distance) => LookAt(target, distance, yaw, pitch);

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

            // Kursor nad oknem ustawień: kliknięcia i kółko należą do okna, dopóki kamera nie jest już w ruchu.
            if (mode == Mode.None && UiInput.PointerOverUi)
                return;

            Mode wanted = mouse.rightButton.isPressed ? Mode.Orbit : Mode.None;
            if (wanted != mode)
                SetMode(wanted);

            Vector2 delta = mouse.delta.ReadValue();
            float notches = ScrollNotches(mouse.scroll.ReadValue().y);
            bool fast = keyboard != null && keyboard.shiftKey.isPressed;

            if (mode == Mode.Orbit)
            {
                Vector3 pivot = Pivot;
                Rotate(delta);
                PlaceAround(pivot);
            }

            if (notches != 0f)
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
