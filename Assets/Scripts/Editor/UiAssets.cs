using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>Ustawienia panelu UI Toolkit dla okna aplikacji i domyślny motyw kontrolek.</summary>
    static class UiAssets
    {
        public const string ThemePath = WfcPaths.UiSettings + "/WfcRuntimeTheme.tss";
        public const string PanelSettingsPath = WfcPaths.UiSettings + "/PanelSettings.asset";

        public static PanelSettings EnsurePanelSettings()
        {
            AssetFolders.Ensure(WfcPaths.UiSettings);
            if (!File.Exists(ThemePath))
            {
                // Ten sam wpis, który Unity tworzy dla domyślnego motywu runtime.
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
            }

            var settings = AssetFolders.LoadOrCreate<PanelSettings>(PanelSettingsPath);
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
