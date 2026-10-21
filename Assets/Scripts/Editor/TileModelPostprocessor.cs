using System;
using UnityEditor;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>
    /// Ustawienia importu modeli z folderu Resources/Models/CastlePieces: skala z pliku (eksport „FBX All”), bez animacji,
    /// kamer, świateł i colliderów. Obrót bazowy 180° jest w prefabie, nie w imporcie.
    /// </summary>
    sealed class TileModelPostprocessor : AssetPostprocessor
    {
        // Zmiana numeru wymusza ponowny import modeli po zmianie ustawień poniżej.
        public override uint GetVersion() => 1;

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(WfcPaths.Models + "/", StringComparison.Ordinal))
                return;

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }
    }
}
