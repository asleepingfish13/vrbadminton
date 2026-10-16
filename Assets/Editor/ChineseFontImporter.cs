using UnityEditor;

namespace ViveShuttleVR.Editor
{
    public sealed class ChineseFontImporter : AssetPostprocessor
    {
        private void OnPreprocessFont()
        {
            if (assetPath != "Assets/Resources/Fonts/SourceHanSansSC-Regular.otf") return;
            var importer = (TrueTypeFontImporter)assetImporter;
            importer.fontTextureCase = FontTextureCase.Dynamic;
            importer.includeFontData = true;
        }
    }
}
