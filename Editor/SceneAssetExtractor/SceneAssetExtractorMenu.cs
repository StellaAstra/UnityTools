using UnityEditor;
using UnityEngine;

namespace SceneAssetExtractor
{
    public static class SceneAssetExtractorMenu
    {
        [MenuItem("Tools/快速提取当前场景资产")]
        public static void QuickExtractCurrentScene()
        {
            var window = EditorWindow.GetWindow<SceneAssetExtractor>("场景资产提取器");
            window.RefreshAssets();
        }

        [MenuItem("Assets/查看场景中的使用情况", false, 20)]
        public static void FindAssetUsageInScene()
        {
            var selected = Selection.activeObject;
            if (selected == null) return;

            string assetPath = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(assetPath)) return;

            var window = EditorWindow.GetWindow<SceneAssetExtractor>("场景资产提取器");
            window.HighlightAsset(assetPath);
        }

        [MenuItem("Assets/查看场景中的使用情况", true)]
        public static bool ValidateFindAssetUsageInScene()
        {
            var selected = Selection.activeObject;
            return selected != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(selected));
        }
    }
}