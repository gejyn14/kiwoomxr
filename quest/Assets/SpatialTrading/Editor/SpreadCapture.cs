using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpatialTrading.Domain;
using SpatialTrading.Market;
using SpatialTrading.Spread;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpatialTrading.Editor
{
    /// <summary>
    /// Editor-only screenshot run for the trader spread (stands in for hand-driving the simulator). Launch the GUI editor
    /// (not -batchmode, so cameras render) with <c>-executeMethod SpatialTrading.Editor.SpreadCapture.Run</c> and the
    /// gateway pairing token in the <c>SPATIAL_PAIRING_TOKEN</c> environment variable. It opens TraderSpread.unity, enters
    /// Play mode, waits for the gateway's data, drives states (a)–(g) through the <see cref="SpreadWorkspace"/> API, renders
    /// each to a 1920×1080 PNG in artifacts/trader-spread/ and exits. The token stays in the process environment, which
    /// <see cref="MarketDataSession"/> reads ahead of EditorPrefs in the editor: it is never logged and never written to
    /// EditorPrefs or any file, so a killed or hung editor leaves nothing behind. Hand gestures (flick, palm tray, corner
    /// drag) are not exercised here.
    /// </summary>
    [InitializeOnLoad]
    public static class SpreadCapture
    {
        public const string TokenVariable = MarketDataSession.EditorTokenVariable;
        public const string UrlVariable = MarketDataSession.EditorUrlVariable;
        private const string DefaultUrl = "ws://127.0.0.1:8471/v1/stream";
        private const string Active = "SpatialTrading.SpreadCapture.Active";
        private const string Stage = "SpatialTrading.SpreadCapture.Stage";
        private const string StartedAt = "SpatialTrading.SpreadCapture.StartedAt";
        private const string Exiting = "SpatialTrading.SpreadCapture.Exiting";
        private const string ExitCode = "SpatialTrading.SpreadCapture.ExitCode";
        private const double PlaceTimeoutSeconds = 30;
        private const double DataTimeoutSeconds = 120;
        private const double RunTimeoutSeconds = 420;
        private const int Width = 1920, Height = 1080;
        /// <summary>The capture eye: the pinned head pose stepped back and down so all eight panels share one 16:9 frame.</summary>
        /// <summary>No pitch: the image plane stays parallel to every panel, so flat panels read as true rectangles.</summary>
        public static readonly Vector3 CameraOffset = new Vector3(0f, -0.23f, -0.55f);
        public const float CameraFieldOfView = 42f;
        /// <summary>Stand-in for passthrough: a dark room tone so the translucent panels read.</summary>
        public static readonly Color Backdrop = new Color(0.035f, 0.045f, 0.058f, 1f);

        private static string OutputDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/trader-spread"));

        private static double _stageAt;
        private static bool _resumed;
        private static int _stage;
        private static readonly List<Shot> Shots = new List<Shot>();

        static SpreadCapture()
        {
            if (!SessionState.GetBool(Active, false) && !SessionState.GetBool(Exiting, false)) return;
            EditorApplication.update += Step; // resumes after the Play-mode domain reload
        }

        public static void Run()
        {
            try
            {
                var token = Environment.GetEnvironmentVariable(TokenVariable);
                if (string.IsNullOrEmpty(token)) Fail("Set " + TokenVariable + " (the gateway pairing token); a capture never falls back to synthetic data.");
                var url = Environment.GetEnvironmentVariable(UrlVariable);
                if (string.IsNullOrEmpty(url)) url = DefaultUrl;
                if (!MarketDataSession.AllowedGateway(url)) Fail("Gateway URL is not an allowed stream address.");
                // Process-local only: MarketDataSession reads the environment before EditorPrefs, so nothing reaches disk.
                Environment.SetEnvironmentVariable(UrlVariable, url);
                Directory.CreateDirectory(OutputDirectory);
                foreach (var old in Directory.GetFiles(OutputDirectory, "*.png")) File.Delete(old);
                EditorSceneManager.OpenScene(QuestShellProject.ScenePath);
                SessionState.SetBool(Active, true);
                SessionState.SetInt(Stage, 0);
                SessionState.SetFloat(StartedAt, (float)EditorApplication.timeSinceStartup);
                // Hooked now as well as after the Play-mode domain reload, so the run timeout also covers a Play mode
                // that never starts.
                EditorApplication.update -= Step;
                EditorApplication.update += Step;
                Debug.Log("[Capture] Entering Play mode with the live gateway.");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error)
            {
                Debug.LogError("[Capture] " + error.Message);
                Finish(1);
            }
        }

        private static void Fail(string message) => throw new InvalidOperationException(message);

        private sealed class Shot
        {
            public string File, Note;
            public string Selected, Session, Ticket, Tray, Head;
            public string[] Dismissed;
            public List<string> Labels = new List<string>();
        }

        private static void Step()
        {
            if (SessionState.GetBool(Exiting, false))
            {
                // Quit only after Play mode has fully stopped: quitting with the XR runtime live aborts the editor.
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetBool(Exiting, false);
                EditorApplication.update -= Step;
                EditorApplication.Exit(SessionState.GetInt(ExitCode, 1));
                return;
            }
            if (!SessionState.GetBool(Active, false)) { EditorApplication.update -= Step; return; }
            var now = EditorApplication.timeSinceStartup;
            if (now - SessionState.GetFloat(StartedAt, (float)now) > RunTimeoutSeconds)
            {
                Debug.LogError("[Capture] Capture run timed out" + (EditorApplication.isPlaying ? "." : " before Play mode started."));
                Finish(1);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (!_resumed) { _resumed = true; _stageAt = now; _stage = SessionState.GetInt(Stage, 0); }
            try
            {
                var workspace = Object.FindFirstObjectByType<SpreadWorkspace>();
                if (workspace == null || workspace.Market == null) return;
                var tray = Object.FindFirstObjectByType<PalmTray>();
                var elapsed = now - _stageAt;
                switch (_stage)
                {
                    case 0: // live data: every chart's daily series arrived, account and news are no longer loading
                        var placed = workspace.IsPlaced || elapsed > PlaceTimeoutSeconds;
                        var ready = placed && SpreadWorkspace.ChartIds.All(id => workspace.Market.Daily(SpreadWorkspace.InstrumentFor(id).Code)?.Value != null)
                                    && workspace.Market.Balance().State != DataState.Loading
                                    && workspace.Market.News().State != DataState.Loading;
                        if (ready || elapsed > DataTimeoutSeconds)
                        {
                            Debug.Log("[Capture] Data " + (ready ? "ready" : "timed out; capturing the honest state") + " after " + elapsed.ToString("0") +
                                " s; workspace " + (workspace.IsPlaced ? "pinned to the tracked head" : "on its provisional origin") + ".");
                            Next(); _stageAt = now + 2; // let the coalesced render and layout settle
                        }
                        break;
                    case 1: if (elapsed > 0) { Capture(workspace, "a-default", "기본 배치 (트레이더 기본), 삼성전자 선택"); Next(); } break;
                    case 2: if (elapsed > 0) { workspace.SelectChart(PanelId.ChartSkHynix); Next(); } break;
                    case 3: if (elapsed > 1) { Capture(workspace, "b-skhynix", "SK하이닉스 선택: 호가 전환, 주문 잠금"); Next(); } break;
                    case 4: if (elapsed > 0) { workspace.SelectChart(PanelId.ChartKospi); Next(); } break;
                    case 5: if (elapsed > 1) { Capture(workspace, "c-kospi", "KOSPI 선택: 지수 · 호가 없음"); Next(); } break;
                    case 6: if (elapsed > 0) { workspace.Resize(PanelId.ChartSamsung, 8, 9); Next(); } break;
                    case 7: if (elapsed > 1) { Capture(workspace, "d-chart-large", "삼성전자 차트 8×9칸: 봉+거래량+가격·시간축"); Next(); } break;
                    case 8: if (elapsed > 0) { workspace.Dismiss(PanelId.News); Next(); } break;
                    case 9: if (elapsed > 0.15) { Capture(workspace, "e0-news-flying", "뉴스 치우는 중 (0.15 s: 날아가며 흐려짐)"); Next(); } break;
                    case 10: if (elapsed > 0.6) { if (tray != null) tray.Pin(); Next(); } break;
                    case 11: if (elapsed > 0.5) { Capture(workspace, "e-news-dismissed", "뉴스 치운 직후, 트레이(T)에 뉴스 아이콘"); Next(); } break;
                    case 12: if (elapsed > 0) { workspace.Restore(PanelId.News); Next(); } break;
                    case 13: if (elapsed > 0.8) { Capture(workspace, "f-news-restored", "트레이에서 뉴스 되살림"); Next(); } break;
                    case 14: if (elapsed > 0) { workspace.Reset(); Next(); } break;
                    case 15: if (elapsed > 1) { Capture(workspace, "g-reset", "R 리셋 후 기본 배치"); Next(); } break;
                    default:
                        File.WriteAllText(Path.Combine(OutputDirectory, "capture.json"), Summary());
                        Debug.Log("[Capture] Saved " + Shots.Count + " screenshots to " + OutputDirectory);
                        Finish(0);
                        break;
                }
            }
            catch (Exception error)
            {
                Debug.LogError("[Capture] " + error.Message);
                Finish(1);
            }
        }

        private static void Next()
        {
            _stage++;
            SessionState.SetInt(Stage, _stage);
            _stageAt = EditorApplication.timeSinceStartup;
        }

        private static void Capture(SpreadWorkspace workspace, string name, string note)
        {
            Canvas.ForceUpdateCanvases();
            var go = new GameObject("Capture eye");
            var camera = go.AddComponent<Camera>();
            var texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            // The simulator's controller/hand models sit near the head; the stepped-back eye would see them in front of
            // the hand-plane panels, so they are hidden for the render only.
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            var hidden = rig == null ? new Renderer[0] : rig.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
            foreach (var r in hidden) r.enabled = false;
            try
            {
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Backdrop;
                camera.fieldOfView = CameraFieldOfView;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 20f;
                camera.transform.SetPositionAndRotation(workspace.Origin + workspace.Rotation * CameraOffset, workspace.Rotation);
                camera.targetTexture = texture;
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                foreach (var r in hidden) if (r != null) r.enabled = true;
                camera.targetTexture = null;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(image);
            }
            Shots.Add(Describe(workspace, name + ".png", note));
        }

        // Honesty labels and headers only; the account line is never read.
        private static readonly string[] LabelNames = { "Title", "Status", "Hint", "Placeholder", "BadgeText", "Price", "PriceChange", "Empty", "ToastText", "LastPrice" };

        private static Shot Describe(SpreadWorkspace workspace, string file, string note)
        {
            var shot = new Shot
            {
                File = file, Note = note, Selected = workspace.SelectedInstrument.DisplayName, Session = workspace.Session.ToString(),
                Ticket = workspace.Ticket.State + (workspace.Ticket.LockReason != null ? " · " + workspace.Ticket.LockReason : ""),
                Dismissed = workspace.Layout.Dismissed.Select(d => d.ToString()).ToArray(),
                Head = Local(workspace, workspace.Head.position)
            };
            var tray = Object.FindFirstObjectByType<PalmTray>();
            shot.Tray = tray == null ? "none" : tray.IsShown ? "shown at " + Local(workspace, tray.TrayRoot.position) : "hidden";
            foreach (var panel in workspace.Panels)
            {
                if (!panel.gameObject.activeInHierarchy) continue;
                var parts = panel.Content.GetComponentsInChildren<TMP_Text>(false)
                    .Where(t => t.transform.parent == panel.Content.transform || t.name == "BadgeText" || t.name == "LastPrice" || t.name == "ToastText")
                    .Where(t => LabelNames.Contains(t.name) && !string.IsNullOrEmpty(t.text))
                    .Select(t => t.name + "=" + t.text);
                shot.Labels.Add(panel.Id + " " + workspace.Layout[panel.Id] + " | " + string.Join(" | ", parts));
            }
            return shot;
        }

        private static string Local(SpreadWorkspace w, Vector3 world)
        {
            var l = Quaternion.Inverse(w.Rotation) * (world - w.Origin);
            return "(" + l.x.ToString("0.000") + ", " + l.y.ToString("0.000") + ", " + l.z.ToString("0.000") + ") from origin";
        }

        private static string Summary()
        {
            var lines = new List<string> { "[" };
            for (var i = 0; i < Shots.Count; i++)
            {
                var s = Shots[i];
                lines.Add("  {");
                lines.Add("    \"file\": " + Json(s.File) + ", \"note\": " + Json(s.Note) + ",");
                lines.Add("    \"selected\": " + Json(s.Selected) + ", \"session\": " + Json(s.Session) + ", \"ticket\": " + Json(s.Ticket) + ",");
                lines.Add("    \"dismissed\": [" + string.Join(", ", s.Dismissed.Select(Json)) + "], \"tray\": " + Json(s.Tray) + ", \"head\": " + Json(s.Head) + ",");
                lines.Add("    \"panels\": [");
                lines.Add(string.Join(",\n", s.Labels.Select(l => "      " + Json(l))));
                lines.Add("    ]");
                lines.Add(i == Shots.Count - 1 ? "  }" : "  },");
            }
            lines.Add("]");
            return string.Join("\n", lines) + "\n";
        }

        private static string Json(string s) =>
            "\"" + (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

        /// <summary>Leaves Play mode if needed, then quits the editor. EditorPrefs were never touched.</summary>
        private static void Finish(int code)
        {
            SessionState.SetBool(Active, false);
            SessionState.EraseFloat(StartedAt);
            if (EditorApplication.isPlaying)
            {
                SessionState.SetInt(ExitCode, code);
                SessionState.SetBool(Exiting, true);
                EditorApplication.update -= Step;
                EditorApplication.update += Step; // keep exactly one hook alive to quit after Play mode ends
                EditorApplication.ExitPlaymode();
                return;
            }
            EditorApplication.update -= Step;
            EditorApplication.Exit(code);
        }
    }
}
