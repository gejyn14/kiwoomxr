using SpatialTrading.Market;
using UnityEditor;
using UnityEngine;

namespace SpatialTrading.Editor
{
    /// <summary>
    /// Editor-only gateway stream settings, kept in this user's EditorPrefs. Fields are deliberately
    /// not serialized so the token never lands in window layout files, the project, or a build.
    /// </summary>
    public sealed class GatewaySettingsWindow : EditorWindow
    {
        private const string DefaultUrl = "ws://127.0.0.1:8471/v1/stream";
        private string _url;
        private string _token;
        private string _message;

        public static void Open()
        {
            var window = GetWindow<GatewaySettingsWindow>(true, "Gateway stream", true);
            window.minSize = new Vector2(460, 170);
            window._url = EditorPrefs.GetString(MarketDataSession.EditorUrlPref, DefaultUrl);
            window._token = EditorPrefs.GetString(MarketDataSession.EditorTokenPref, string.Empty);
            window._message = null;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Saved in this Mac's EditorPrefs only — never in the project or the APK. " +
                "Quest reads gateway.json from the app's persistent data folder instead (docs/QUEST_SETUP.md).", MessageType.Info);
            _url = EditorGUILayout.TextField("Stream URL", _url ?? string.Empty);
            _token = EditorGUILayout.PasswordField("Pairing token", _token ?? string.Empty);
            if (_message != null) EditorGUILayout.HelpBox(_message, MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save")) Save();
                if (GUILayout.Button("Remove token"))
                {
                    EditorPrefs.DeleteKey(MarketDataSession.EditorTokenPref);
                    _token = string.Empty;
                    _message = "Pairing token removed from EditorPrefs.";
                }
            }
        }

        private void Save()
        {
            var url = (_url ?? string.Empty).Trim();
            var token = (_token ?? string.Empty).Trim();
            if (!MarketDataSession.AllowedGateway(url))
            { _message = "Use wss://, or ws:// to loopback or a private IPv4 LAN (10/8, 172.16/12, 192.168/16); no query, fragment or user info."; return; }
            if (token.Length == 0) { _message = "The gateway pairing token is required."; return; }
            EditorPrefs.SetString(MarketDataSession.EditorUrlPref, url);
            EditorPrefs.SetString(MarketDataSession.EditorTokenPref, token);
            Debug.Log("[Gateway] Stream settings saved to EditorPrefs. Use Spatial Trading → 7 to stream from the gateway.");
            Close();
        }
    }
}
