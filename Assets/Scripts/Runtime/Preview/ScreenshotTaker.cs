using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WFC.Runtime
{
    /// <summary>
    /// Zrzuty ekranu do pracy: F12 zapisuje PNG w Wyniki/Zrzuty. Z wiersza poleceń
    /// -zrzut plik.png [-zrzutKlatki 120] robi jeden zrzut po podanej liczbie klatek i zamyka aplikację.
    /// </summary>
    public sealed class ScreenshotTaker : MonoBehaviour
    {
        [Tooltip("Mnożnik rozdzielczości zrzutu względem okna (2 = dwa razy większy obraz).")]
        [SerializeField, Range(1, 4)] int superSize = 1;

        string commandLinePath;
        int framesLeft;
        int quitAfterFrames = -1;

        public static string ScreenshotFolder => Path.Combine(BenchmarkSession.ResultsFolder, "Zrzuty");

        void Start()
        {
            commandLinePath = CommandLine.Get("-zrzut");
            framesLeft = CommandLine.GetInt("-zrzutKlatki", 120);
        }

        void Update()
        {
            if (quitAfterFrames >= 0 && quitAfterFrames-- == 0)
                Application.Quit(0);

            if (commandLinePath != null && --framesLeft == 0)
            {
                Capture(commandLinePath);
                commandLinePath = null;
                quitAfterFrames = 10;
            }

            if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame && !UiInput.IsTyping)
                Capture(Path.Combine(ScreenshotFolder, $"zamek_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
        }

        public void Capture(string path)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
            ScreenCapture.CaptureScreenshot(fullPath, superSize);
            Debug.Log($"[ScreenshotTaker] Zrzut ekranu: {fullPath}");
        }
    }
}
