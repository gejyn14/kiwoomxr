using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SpatialTrading.Market;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpatialTrading.Editor
{
    /// <summary>
    /// SDK 205 injects local AgentBridge credentials even when its assistant is disabled.
    /// The gateway pairing token must also never ship: devices read it from persistentDataPath.
    /// </summary>
    public sealed class BuildCredentialGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        // A gateway.json-shaped token entry: "token": "<32+ non-space characters>" (gateway minimum is 32).
        private static readonly Regex TokenShape = new Regex("\"token\"\\s*:\\s*\"[^\"\\s]{32,}\"", RegexOptions.CultureInvariant);
        private const long ShapeScanLimit = 32L * 1024 * 1024;

        // Run after the SDK's DevAgentBuildProcessor (order 1).
        public int callbackOrder => int.MaxValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            var stray = Directory.GetFiles("Assets", MarketDataSession.DeviceSettingsFile, SearchOption.AllDirectories);
            if (stray.Length > 0)
                throw new BuildFailedException("Remove " + stray[0].Replace('\\', '/') + ": gateway settings are pushed to the device, never built in.");
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

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            var output = report.summary.outputPath;
            if (!File.Exists(output))
            { Debug.LogWarning("[M2] Pairing-token scan not run: no APK/AAB at the build output path."); return; }
            var entry = FindPairingSecret(output, EditorPrefs.GetString(MarketDataSession.EditorTokenPref, string.Empty));
            if (entry == null) { Debug.Log("[M2] Pairing-token scan passed for the built package."); return; }
            File.Delete(output);
            // Names the entry only; the matched bytes are never printed.
            throw new BuildFailedException("Gateway pairing token found in package entry '" + entry + "'. The output was deleted.");
        }

        /// <summary>First archive entry holding the configured token (UTF-8 or UTF-16) or a gateway.json-shaped token, else null.</summary>
        internal static string FindPairingSecret(string archive, string token)
        {
            var needles = new List<byte[]>();
            if (!string.IsNullOrEmpty(token)) { needles.Add(Encoding.UTF8.GetBytes(token)); needles.Add(Encoding.Unicode.GetBytes(token)); }
            using (var file = File.OpenRead(archive))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                foreach (var entry in zip.Entries)
                {
                    if (Path.GetFileName(entry.FullName) == MarketDataSession.DeviceSettingsFile) return entry.FullName;
                    byte[] bytes;
                    using (var stream = entry.Open())
                    using (var memory = new MemoryStream())
                    { stream.CopyTo(memory); bytes = memory.ToArray(); }
                    foreach (var needle in needles)
                        if (IndexOf(bytes, needle) >= 0) return entry.FullName;
                    // Latin-1 maps bytes 1:1 so the pattern also matches inside binary resources.
                    if (bytes.LongLength <= ShapeScanLimit && TokenShape.IsMatch(Encoding.GetEncoding(28591).GetString(bytes)))
                        return entry.FullName;
                }
            return null;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0) return -1;
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack[i] != needle[0]) continue;
                var j = 1;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        private static SerializedProperty Require(SerializedObject settings, string field) =>
            settings.FindProperty(field) ?? throw new BuildFailedException("SDK development settings changed; review credential guard before building.");
    }
}
