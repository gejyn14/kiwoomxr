using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpatialTrading.Editor
{
    /// <summary>SDK 205 injects local AgentBridge credentials even when its assistant is disabled.</summary>
    public sealed class BuildCredentialGuard : IPreprocessBuildWithReport
    {
        // Run after the SDK's DevAgentBuildProcessor (order 1).
        public int callbackOrder => int.MaxValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            // Operator is an Editor simulator test tool for this project, not an APK feature.
            // SDK 205 otherwise adds its AAR to every Development build, including capture
            // services and obsolete permissions implied by that AAR's manifest.
            if (report.summary.platform == BuildTarget.Android)
                foreach (var importer in PluginImporter.GetAllImporters())
                    if (System.IO.Path.GetFileName(importer.assetPath) == "XrApiLayer_METAX_operator_unity_android.aar")
                        importer.SetIncludeInBuildDelegate(_ => false);
            var asset = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/DevAgentSettings.asset");
            if (asset == null) return;
            var settings = new SerializedObject(asset);
            Require(settings, "enabled").boolValue = false;
            foreach (var field in new[] { "serverAddress", "accessToken", "witClientAccessToken" })
                Require(settings, field).stringValue = string.Empty;
            Require(settings, "witConfiguration").objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(asset);
            Debug.Log("[M1] Disabled SDK development assistant and removed its connection credentials from build resources.");
        }

        private static SerializedProperty Require(SerializedObject settings, string field) =>
            settings.FindProperty(field) ?? throw new BuildFailedException("SDK development settings changed; review credential guard before building.");
    }
}
