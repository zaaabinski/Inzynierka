using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>
    /// Buduje aplikację Zamek.exe (Windows, 64 bity, wersja Release) ze sceną Generator. Backend IL2CPP (kod C# zamieniany
    /// na C++), jeśli w Unity Hub jest moduł „Windows Build Support (IL2CPP)”; bez niego Mono z ostrzeżeniem.
    /// </summary>
    public static class AppBuilder
    {
        [MenuItem("WFC/Zbuduj aplikację (Windows)", priority = 50)]
        static void BuildFromMenu()
        {
            BuildSummary summary = Build(WfcPaths.BuildExecutable);
            EditorUtility.DisplayDialog("Budowanie aplikacji", Describe(summary), "OK");
        }

        /// <summary>Tryb wsadowy: Unity -batchmode -executeMethod WFC.Editor.AppBuilder.BuildBatch [-wfcBuildPath Build/Zamek.exe].</summary>
        public static void BuildBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-wfcBuildPath");
                string path = index >= 0 && index + 1 < args.Length ? args[index + 1] : WfcPaths.BuildExecutable;
                BuildSummary summary = Build(path);
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(2);
            }
        }

        public static BuildSummary Build(string executablePath)
        {
            bool il2cpp = Il2CppAvailable;
            PlayerSettings.productName = "Zamek";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            if (il2cpp)
                PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone, Il2CppCompilerConfiguration.Release);
            else
                Debug.LogWarning("[AppBuilder] Brak modułu „Windows Build Support (IL2CPP)” w Unity Hub; buduję z Mono. " +
                                 "Do pomiarów z handoffu doinstaluj moduł IL2CPP i zbuduj jeszcze raz.");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { WfcPaths.GeneratorScene },
                locationPathName = executablePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[AppBuilder] {Describe(report.summary)}");
            return report.summary;
        }

        public static bool Il2CppAvailable =>
            Directory.Exists(Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport", "Variations", "win64_player_nondevelopment_il2cpp"));

        static string Describe(BuildSummary summary) =>
            $"Wynik: {summary.result}, backend: {(Il2CppAvailable ? "IL2CPP" : "Mono")}, plik: {summary.outputPath}, " +
            $"rozmiar: {summary.totalSize / (1024.0 * 1024.0):0.0} MB, czas: {summary.totalTime.TotalSeconds:0} s, błędy: {summary.totalErrors}.";
    }
}
