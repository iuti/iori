using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Iori.Ui.EditorTools
{
    /// <summary>
    /// Builds for trying the UI on a phone. Run from the command line with
    ///   Unity.exe -batchmode -quit -buildTarget Android -projectPath . -executeMethod Iori.Ui.EditorTools.UiBuild.BuildAndroidTestBatch
    /// </summary>
    public static class UiBuild
    {
        const string ScenePath = "Assets/IORI/Scenes/Camp.unity";
        const string TestId = "com.iuti.iori.uitest";
        const string TestName = "IORI ui test";

        /// <summary>
        /// Development APK of the camp scene with the UI, under a package id of its own (so it stands
        /// beside the installed app instead of replacing it). -apk says where to write it.
        /// </summary>
        public static void BuildAndroidTestBatch()
        {
            string apk = ArgValue("-apk") ?? Path.GetFullPath("Builds/iori-uitest.apk");
            string oldId = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
            string oldName = PlayerSettings.productName;
            try
            {
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, TestId);
                PlayerSettings.productName = TestName;
                Directory.CreateDirectory(Path.GetDirectoryName(apk));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    target = BuildTarget.Android,
                    locationPathName = apk,
                    options = BuildOptions.Development,
                });
                Debug.Log("[IORI] apk build " + report.summary.result + " " + report.summary.totalSize + " bytes, " + report.summary.totalTime);
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, oldId);
                PlayerSettings.productName = oldName;
            }
        }

        static string ArgValue(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}
