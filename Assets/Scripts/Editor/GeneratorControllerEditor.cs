using System;
using UnityEditor;
using UnityEngine;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>Przyciski Generuj, Krok, Do końca, Wyczyść i Następne ziarno w inspektorze GeneratorController.</summary>
    [CustomEditor(typeof(GeneratorController))]
    sealed class GeneratorControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var controller = (GeneratorController)target;

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generuj"))
                    Run(controller, controller.Generate);
                if (GUILayout.Button("Krok"))
                    Run(controller, () => controller.Step());
                if (GUILayout.Button("Do końca"))
                    Run(controller, controller.RunToEnd);
                if (GUILayout.Button("Wyczyść"))
                    Run(controller, controller.Clear);
            }
            if (GUILayout.Button("Następne ziarno"))
                Run(controller, controller.NextSeed);

            if (!string.IsNullOrEmpty(controller.LastReport))
                EditorGUILayout.HelpBox(controller.LastReport, MessageType.None);
            if (!Application.isPlaying && controller.MapBuilder != null && controller.MapBuilder.InstanceCount > 0)
            {
                if (controller.MapBuilder.Combined != null)
                    EditorGUILayout.HelpBox("Scalona mapa poza trybem Play to tylko podgląd, nie zapisze się ze sceną. Zapis jako prefab: WFC → Generator zamków.", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("Kostki postawione poza trybem Play zapiszą się w scenie. Przed zapisem sceny użyj Wyczyść.", MessageType.Warning);
            }
        }

        static void Run(GeneratorController controller, Action action)
        {
            uint seed = controller.Seed;
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, controller);
            }
            if (controller.Seed != seed && !Application.isPlaying)
                EditorUtility.SetDirty(controller);
        }
    }
}
