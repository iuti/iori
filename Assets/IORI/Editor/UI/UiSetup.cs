using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Iori.Ui.EditorTools
{
    /// <summary>
    /// One-time preparation for the UI. The text is drawn with TextMeshPro, which needs its basic
    /// files (its settings and shaders) copied into the project once; that is what this does,
    /// without the usual window asking about it.
    /// </summary>
    public static class UiSetup
    {
        const string TmpSettings = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        public static bool HasTmpEssentials => File.Exists(TmpSettings);

        [MenuItem("IORI/UI/TextMeshPro の基本素材を入れる")]
        public static void ImportTmpEssentials()
        {
            if (HasTmpEssentials)
            {
                Debug.Log("[IORI] TextMeshPro の基本素材はもう入っています");
                return;
            }
            TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        /// <summary>
        /// For -batchmode -executeMethod (do not pass -quit: the editor leaves by itself once the files are in).
        /// </summary>
        public static void ImportTmpEssentialsBatch()
        {
            if (HasTmpEssentials)
            {
                Debug.Log("[IORI] TMP essentials already present");
                EditorApplication.Exit(0);
                return;
            }
            double start = EditorApplication.timeSinceStartup;
            AssetDatabase.importPackageCompleted += name =>
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[IORI] TMP essentials imported: " + name);
                EditorApplication.Exit(HasTmpEssentials ? 0 : 1);
            };
            AssetDatabase.importPackageFailed += (name, error) =>
            {
                Debug.LogError("[IORI] TMP essentials import failed: " + error);
                EditorApplication.Exit(1);
            };
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup - start < 180.0) return;
                Debug.LogError("[IORI] TMP essentials import timed out");
                EditorApplication.Exit(HasTmpEssentials ? 0 : 1);
            };
            TMP_PackageResourceImporter.ImportResources(true, false, false);
        }
    }
}
