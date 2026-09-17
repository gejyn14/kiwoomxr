using System;
using System.IO;
using System.Linq;
using Meta.XR;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using SpatialTrading.Components;
using SpatialTrading.Interfaces;
using SpatialTrading.Spatial;
using SpatialTrading.Market;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using Object = UnityEngine.Object;

namespace SpatialTrading.Editor
{
    /// <summary>Repeatable scene authoring using the pinned SDK's public setup APIs.</summary>
    public static class QuestShellProject
    {
        public const string ScenePath = "Assets/SpatialTrading/Scenes/FocusShell.unity";
        private const string Generated = "Assets/Generated";
        private static TMP_FontAsset _font;
        private static readonly Color Background = new Color(0.065f, 0.085f, 0.14f, 0.98f);
        private static readonly Color Ink = new Color(0.91f, 0.96f, 1f);
        private static readonly Color Muted = new Color(0.57f, 0.72f, 0.78f);
        private static readonly Color Accent = new Color(0.39f, 0.93f, 0.82f);

        [MenuItem("Spatial Trading/1. Configure Quest project")]
        public static void Configure()
        {
            if (Application.unityVersion != "6000.0.67f1")
                throw new BuildFailedException("Use the pinned Unity 6000.0.67f1 editor.");
            PlayerSettings.companyName = "SpatialTrading";
            PlayerSettings.productName = "Spatial Trading Shell";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.kiwoomxr.spatialshell");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.runInBackground = false;
            PlayerSettings.Android.forceInternetPermission = false;
            QualitySettings.antiAliasing = 4;
            QualitySettings.vSyncCount = 0;

            // OpenXR requires the new Input System. This serialized Unity 6 setting is checked below.
            var playerSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = playerSettings.FindProperty("activeInputHandler");
            if (input == null) throw new BuildFailedException("Cannot find Unity 6 activeInputHandler setting.");
            input.intValue = 1;
            playerSettings.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settings))
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, Generated + "/XRGeneralSettings.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            }
            // Standalone settings drive Editor Play Mode with Meta XR Simulator; Android drives the APK.
            foreach (var target in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!settings.HasManagerSettingsForBuildTarget(target))
                    settings.CreateDefaultManagerSettingsForBuildTarget(target);
                var general = settings.SettingsForBuildTarget(target);
                general.InitManagerOnStart = true;
                if (!XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", target))
                    throw new BuildFailedException("OpenXR loader assignment failed for " + target);
                EditorUtility.SetDirty(general);
                var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(target);
                if (openxr == null) throw new BuildFailedException("OpenXR settings unavailable; finish package import first.");
                var meta = openxr.GetFeature<MetaXRFeature>();
                var touch = openxr.GetFeature<OculusTouchControllerProfile>();
                if (meta == null || touch == null) throw new BuildFailedException("Required Meta/OpenXR feature not found.");
                meta.enabled = true;
                touch.enabled = true;
                // Quest 3 has no eye tracking. Never enable an eye-gaze or eye-tracked foveation feature.
                foreach (var feature in openxr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>())
                    if (feature.GetType().Name.IndexOf("Eye", StringComparison.OrdinalIgnoreCase) >= 0) feature.enabled = false;
                openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                EditorUtility.SetDirty(openxr);
            }
            EditorUtility.SetDirty(settings);

            var config = OVRProjectConfig.CachedProjectConfig;
            if (config == null) throw new BuildFailedException("Meta project config not ready; reopen after package import.");
            config.targetDeviceTypes = new System.Collections.Generic.List<OVRProjectConfig.DeviceType> { OVRProjectConfig.DeviceType.Quest3 };
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            config.eyeTrackingSupport = OVRProjectConfig.FeatureSupport.None;
            config.faceTrackingSupport = OVRProjectConfig.FeatureSupport.None;
            config.bodyTrackingSupport = OVRProjectConfig.FeatureSupport.None;
            config.isPassthroughCameraAccessEnabled = false;
            OVRProjectConfig.CommitProjectConfig(config);
            var debuggerSettings = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/ImmersiveDebuggerSettings.asset");
            if (debuggerSettings != null)
            {
                var serialized = new SerializedObject(debuggerSettings);
                serialized.FindProperty("immersiveDebuggerDisplayAtStartup").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[M1] Quest configuration saved. Reopen the editor if Input System requests a restart.");
        }

        [MenuItem("Spatial Trading/2. Generate Focus Shell scene")]
        public static void GenerateScene()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") != null)
            {
                FinishSceneGeneration();
                return;
            }
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            var resources = Path.Combine(package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            if (!File.Exists(resources)) throw new BuildFailedException("TMP Essential Resources not found.");
            AssetDatabase.importPackageCompleted += OnTextResourcesImported;
            AssetDatabase.importPackageFailed += OnTextResourcesFailed;
            AssetDatabase.importPackageCancelled += OnTextResourcesCancelled;
            // ImportPackage is asynchronous. Batch scene generation must not use -quit.
            AssetDatabase.ImportPackage(resources, false);
        }

        private static void RemoveImportCallbacks()
        {
            AssetDatabase.importPackageCompleted -= OnTextResourcesImported;
            AssetDatabase.importPackageFailed -= OnTextResourcesFailed;
            AssetDatabase.importPackageCancelled -= OnTextResourcesCancelled;
        }

        private static void OnTextResourcesImported(string packageName)
        {
            RemoveImportCallbacks();
            EditorApplication.delayCall += FinishSceneGeneration;
        }

        private static void OnTextResourcesFailed(string packageName, string error)
        {
            RemoveImportCallbacks();
            Debug.LogError("TMP resource import failed: " + error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        private static void OnTextResourcesCancelled(string packageName) => OnTextResourcesFailed(packageName, "Cancelled");

        private static void FinishSceneGeneration()
        {
            try
            {
                GenerateSceneCore();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static void GenerateSceneCore()
        {
            // Preserve any scene edited by a person. Regeneration uses a new path after explicit rename/removal.
            if (File.Exists(ScenePath)) throw new BuildFailedException("FocusShell.unity exists. Open it, or move it aside before regenerating.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFont();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            OVRQuickActionsAPI.AddOVRInteractionRig(false);
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) throw new BuildFailedException("Meta rig setup did not produce OVRCameraRig.");
            var manager = rig.GetComponent<OVRManager>();
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            manager.isInsightPassthroughEnabled = true;
            var passthrough = rig.gameObject.AddComponent<OVRPassthroughLayer>();
            passthrough.overlayType = OVROverlay.OverlayType.Underlay;
            foreach (var camera in rig.GetComponentsInChildren<Camera>(true))
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 20;
            }
            var head = rig.centerEyeAnchor;
            var cameraEye = head.GetComponent<Camera>();
            var app = new GameObject("Focus Shell");
            var shell = app.AddComponent<FocusShell>();
            var layout = app.AddComponent<SeatedLayout>();
            var market = app.AddComponent<MarketDataSession>();
            market.Configure(true); // Explicit design preview; never a fallback from live failures.
            var sources = app.AddComponent<ShellInputSources>();
            sources.Configure(rig.transform);

            var chart = Surface("Chart", 860, 590, head, cameraEye, true, out var chartCanvas);
            var chartView = chart.AddComponent<ChartComponent>();
            Label(chartCanvas, "K I W O O M   /   S P A C E", 34, 48, 460, 26, 16, Muted);
            var sourceLabel = Label(chartCanvas, "PREVIEW  /  합성 시세", 604, 47, 224, 28, 16, Accent);
            var instrument = Label(chartCanvas, "삼성전자", 32, 89, 796, 56, 40, Ink);
            var price = Label(chartCanvas, "—", 30, 139, 590, 100, 60, Ink);
            price.gameObject.name = "Price";
            var change = Label(chartCanvas, "전일 대비 —", 35, 231, 550, 34, 24, Muted);
            Card(chartCanvas, "Chart legend", 635, 202, 190, 48, new Color(.06f,.10f,.17f,.94f), 12);
            Label(chartCanvas, "CANDLE  /  거래량", 650, 212, 165, 26, 17, Muted);
            Image(chartCanvas, "Divider", 32, 283, 796, 1, new Color(.4f,.55f,.7f,.16f));
            var period = Label(chartCanvas, "차트", 34, 300, 640, 28, 18, Muted);
            var candlesObject = Rect("Candles", chartCanvas, 34, 341, 672, 172);
            var candles = candlesObject.gameObject.AddComponent<CandleChartGraphic>();
            candles.raycastTarget = false;
            var high = Label(chartCanvas, "—", 720, 344, 106, 52, 17, Muted);
            var low = Label(chartCanvas, "—", 720, 450, 106, 52, 17, Muted);
            var freshness = Label(chartCanvas, "디자인 미리보기 · 실제 시세 아님", 34, 539, 700, 27, 17, Muted);
            chartView.Configure(instrument, price, candles, change, sourceLabel, freshness, period, high, low);

            var secondary = Surface("Secondary Component", 420, 590, head, cameraEye, true, out var secondaryCanvas);
            var secondaryView = secondary.AddComponent<SecondaryComponent>();
            Label(secondaryCanvas, "MARKET INSIGHT", 28, 50, 360, 26, 15, Accent);
            var secondaryTitle = Label(secondaryCanvas, "호가", 28, 85, 360, 45, 33, Ink);
            Image(secondaryCanvas, "Divider", 28, 143, 364, 1, new Color(.4f,.55f,.7f,.16f));
            var secondaryBody = Label(secondaryCanvas, "", 28, 164, 364, 314, 24, Ink);
            secondaryView.Configure(secondaryTitle, secondaryBody);
            Button(secondaryCanvas, "닫기", 28, 510, 364, 56, shell, ShellControl.CloseSecondary);

            var dock = Surface("Reachable Controls", 650, 270, head, cameraEye, false, out var controls);
            Button(controls, "삼성전자", 20, 45, 297, 62, shell, ShellControl.SelectSamsung);
            Button(controls, "SK하이닉스", 333, 45, 297, 62, shell, ShellControl.SelectSkHynix);
            Button(controls, "차트", 20, 119, 143, 59, shell, ShellControl.OpenChart);
            Button(controls, "호가", 175, 119, 144, 59, shell, ShellControl.OpenOrderBook);
            Button(controls, "보유", 331, 119, 144, 59, shell, ShellControl.OpenPosition);
            Button(controls, "비교", 487, 119, 143, 59, shell, ShellControl.CompareOther);
            Button(controls, "앞으로 정렬", 20, 190, 185, 55, shell, ShellControl.Recenter);
            var status = Label(controls, "터치로 선택 · 손잡이로 이동", 224, 198, 403, 38, 18, Muted);
            layout.Configure(head, chart.transform, dock.transform, secondary.transform);
            shell.Configure(chartView, secondaryView, layout, sources, status, market);

            // An editor-visible starting pose; runtime places once from actual tracked head position.
            chart.transform.position = new Vector3(0, 1.1f, 0.95f);
            dock.transform.SetPositionAndRotation(new Vector3(0, 0.82f, 0.57f), Quaternion.Euler(25, 0, 0));
            secondary.transform.SetPositionAndRotation(new Vector3(0.61f, 1.04f, 0.78f), Quaternion.Euler(0, 25, 0));
            secondary.SetActive(false);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath))
                throw new BuildFailedException("Could not save Focus Shell scene.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[M1] Focus Shell scene generated with vendor poke/ray/grab interactors.");
        }

        [MenuItem("Spatial Trading/7. Connect to local gateway")]
        public static void UseGateway() => SetPreview(false);
        [MenuItem("Spatial Trading/8. Preview with synthetic data")]
        public static void UsePreview() => SetPreview(true);
        private static void SetPreview(bool preview)
        {
            var shell = Object.FindFirstObjectByType<FocusShell>();
            if (shell == null) throw new BuildFailedException("Open the Focus Shell scene first.");
            if (Application.isPlaying) shell.SetPreview(preview);
            else
            {
                var market = Object.FindFirstObjectByType<MarketDataSession>();
                market.Configure(preview);
                EditorUtility.SetDirty(market);
                EditorSceneManager.MarkSceneDirty(market.gameObject.scene);
                EditorSceneManager.SaveScene(market.gameObject.scene);
            }
        }

        private static GameObject Surface(string name, float width, float height, Transform head, Camera eye,
            bool resizable, out RectTransform canvasTransform)
        {
            var root = new GameObject(name);
            var canvasObject = new GameObject("Surface", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            canvasTransform = canvasObject.GetComponent<RectTransform>();
            canvasTransform.sizeDelta = new Vector2(width, height);
            canvasTransform.localScale = Vector3.one * 0.001f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eye;
            Card(canvasTransform, "Background", 0, 0, width, height, Background, 24);
            Card(canvasTransform, "Grab handle", width / 2 - 45, 14, 90, 5, new Color(.4f,.58f,.68f,.6f), 2);
            var hint = Label(canvasTransform, resizable ? "MOVE / RESIZE" : "CONTROL", width - 158, 8, 128, 22, 11, Muted);
            hint.alignment = TextAlignmentOptions.Right;
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, (height / 2 - 18.5f) * 0.001f, 0);
            collider.size = new Vector3(width * 0.001f, 0.037f, 0.025f);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            QuickActionsAPI.AddGrabInteraction(root);
            QuickActionsAPI.AddRayGrabInteraction(root);
            var grabbable = root.GetComponent<Grabbable>();
            if (grabbable == null) throw new BuildFailedException("SDK did not create Grabbable.");
            var transformer = root.AddComponent<GrabFreeTransformer>();
            var axis = new TransformerUtils.ConstrainedAxis
            {
                ConstrainAxis = true,
                AxisRange = new TransformerUtils.FloatRange { Min = resizable ? 0.85f : 1, Max = resizable ? 1.35f : 1 }
            };
            transformer.InjectOptionalScaleConstraints(new TransformerUtils.ScaleConstraints
            { ConstraintsAreRelative = true, XAxis = axis, YAxis = axis, ZAxis = axis });
            grabbable.MaxGrabPoints = 2;
            grabbable.InjectOptionalOneGrabTransformer(transformer);
            grabbable.InjectOptionalTwoGrabTransformer(transformer);
            grabbable.InjectOptionalRigidbody(body);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.InjectOptionalKinematicWhileSelected(true);
            root.AddComponent<SurfaceBounds>().Configure(head);
            QuickActionsAPI.AddPokeCanvasInteraction(canvasObject);
            QuickActionsAPI.AddRayCanvasInteraction(canvasObject);
            return root;
        }

        private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static Image Image(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var graphic = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            graphic.color = color;
            graphic.raycastTarget = false;
            return graphic;
        }

        private static SpatialCardGraphic Card(Transform parent, string name, float x, float y, float w, float h, Color color, float radius)
        {
            var graphic = Rect(name, parent, x, y, w, h).gameObject.AddComponent<SpatialCardGraphic>();
            graphic.color = color;
            graphic.BottomColor = new Color(color.r * .65f, color.g * .65f, color.b * .72f, color.a);
            graphic.Radius = radius;
            graphic.raycastTarget = false;
            return graphic;
        }

        private static TMP_Text Label(Transform parent, string text, float x, float y, float w, float h, float size, Color color)
        {
            var label = Rect("Label", parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = _font;
            label.fontSize = size;
            // Noto KR line metrics are taller than Latin defaults. Fit the whole line
            // within its authored rectangle instead of silently ellipsizing it away.
            label.enableAutoSizing = true;
            label.fontSizeMin = size * 0.8f;
            label.fontSizeMax = size;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }

        private static void Button(Transform parent, string text, float x, float y, float w, float h, FocusShell shell, ShellControl control)
        {
            var image = Card(parent, control.ToString(), x, y, w, h, new Color(0.11f, 0.17f, 0.25f), 14);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<ShellButton>();
            button.targetGraphic = image;
            button.Configure(shell, control);
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.70f, 1, 0.91f);
            colors.pressedColor = new Color(0.42f, 0.75f, 0.66f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var label = Label(image.transform, text, 8, 0, w - 16, h, 24, Ink);
            label.alignment = TextAlignmentOptions.Center;
        }

        private static void EnsureFont()
        {
            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
                throw new BuildFailedException("TMP Essential Resources settings were not imported.");
            const string fontPath = Generated + "/NotoSansKR.asset";
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (_font != null) return;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/SpatialTrading/Fonts/NotoSansKR-Regular.otf");
            if (source == null) throw new BuildFailedException("Korean font source missing.");
            _font = TMP_FontAsset.CreateFontAsset(source, 48, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            _font.name = "NotoSansKR Shell";
            AssetDatabase.CreateAsset(_font, fontPath);
            AssetDatabase.AddObjectToAsset(_font.material, _font);
            foreach (var atlas in _font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, _font);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Spatial Trading/3. Validate scene and build configuration")]
        public static void Validate()
        {
            if (!File.Exists(ScenePath)) throw new BuildFailedException("Generate Focus Shell scene first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Leave Play Mode before validating or building.");
            // Opening a scene in Single mode can discard unsaved edits. Never do that as a side
            // effect of validation/build, and never validate unsaved data then build older disk data.
            for (var index = 0; index < UnityEngine.SceneManagement.SceneManager.sceneCount; index++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(index).isDirty)
                    throw new BuildFailedException("Save your open scene changes before validating or building.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig == null || Object.FindObjectsByType<FocusShell>(FindObjectsSortMode.None).Length != 1)
                throw new BuildFailedException("Expected one FocusShell and an OVRCameraRig.");
            var manager = rig.GetComponent<OVRManager>();
            var passthrough = rig.GetComponent<OVRPassthroughLayer>();
            if (manager == null || !manager.isInsightPassthroughEnabled || passthrough == null ||
                passthrough.overlayType != OVROverlay.OverlayType.Underlay)
                throw new BuildFailedException("Passthrough is not configured.");
            if (rig.GetComponentsInChildren<PokeInteractor>(true).Length == 0 ||
                rig.GetComponentsInChildren<RayInteractor>(true).Length == 0)
                throw new BuildFailedException("The rig is missing required poke/ray interactors.");
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config == null || config.handTrackingSupport != OVRProjectConfig.HandTrackingSupport.ControllersAndHands ||
                config.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.Required)
                throw new BuildFailedException("Quest requires hands/controllers and passthrough support.");
            if (config.eyeTrackingSupport != OVRProjectConfig.FeatureSupport.None || config.isPassthroughCameraAccessEnabled)
                throw new BuildFailedException("The M1 shell does not use eye tracking or raw camera access.");
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (settings == null || settings.Manager == null ||
                !settings.Manager.activeLoaders.Any(loader => loader is UnityEngine.XR.OpenXR.OpenXRLoader))
                throw new BuildFailedException("Android OpenXR loader is missing.");
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (openxr == null || openxr.GetFeature<MetaXRFeature>()?.enabled != true ||
                openxr.GetFeature<OculusTouchControllerProfile>()?.enabled != true)
                throw new BuildFailedException("Meta XR or Oculus Touch OpenXR feature is disabled.");
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64 ||
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                throw new BuildFailedException("Android must use ARM64 IL2CPP.");
            var surfaces = Object.FindObjectsByType<SurfaceBounds>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (surfaces.Length != 3) throw new BuildFailedException("Expected chart, controls and secondary surfaces.");
            foreach (var surface in surfaces)
            {
                var canvas = surface.GetComponentInChildren<Canvas>(true);
                if (canvas == null || canvas.renderMode != RenderMode.WorldSpace || canvas.worldCamera == null ||
                    canvas.GetComponent<GraphicRaycaster>() == null)
                    throw new BuildFailedException("Invalid world-space canvas on " + surface.name);
                if (canvas.GetComponentsInChildren<PointableCanvas>(true).Length != 2 ||
                    canvas.GetComponentsInChildren<PokeInteractable>(true).Length == 0 ||
                    canvas.GetComponentsInChildren<RayInteractable>(true).Length == 0)
                    throw new BuildFailedException("Missing poke/ray canvas bindings on " + surface.name);
                if (surface.GetComponent<Grabbable>() == null || surface.GetComponent<GrabFreeTransformer>() == null ||
                    surface.GetComponent<Rigidbody>()?.isKinematic != true || surface.GetComponent<BoxCollider>() == null)
                    throw new BuildFailedException("Missing standard grab/resize setup on " + surface.name);
            }
            if (Object.FindObjectsByType<PointableCanvasModule>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 1)
                throw new BuildFailedException("Expected exactly one SDK PointableCanvasModule.");
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                        throw new BuildFailedException("Missing script on " + transform.name);
                    foreach (var label in transform.GetComponents<TMP_Text>())
                        if (label.font == null) throw new BuildFailedException("Missing font on " + transform.name);
                }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new BuildFailedException("Scene setup changed while loading. Review/save it, then validate again.");
            Debug.Log("[M1] Static scene/build validation passed. This does not validate headset behavior.");
        }

        [MenuItem("Spatial Trading/4. Build Quest APK")]
        public static void Build()
        {
            Validate();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Switch to Android before building (CLI: -buildTarget Android).");
            const string output = "Builds/Android/SpatialTrading-Shell.apk";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.Android, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Quest APK build failed: " + report.summary.result);
            Debug.Log("[M1] APK built: " + output + ". Quest device acceptance remains required.");
        }
    }
}
