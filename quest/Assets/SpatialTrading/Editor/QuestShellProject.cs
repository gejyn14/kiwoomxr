using System;
using System.IO;
using System.Linq;
using Meta.XR;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using SpatialTrading.Components;
using SpatialTrading.Domain;
using SpatialTrading.Market;
using SpatialTrading.Spread;
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
        public const string ScenePath = "Assets/SpatialTrading/Scenes/TraderSpread.unity";
        private const string ScenesFolder = "Assets/SpatialTrading/Scenes";
        private const string Generated = "Assets/Generated";
        /// <summary>Editor-visible head pose for authoring only; at runtime the workspace pins itself to the tracked head.</summary>
        private static readonly Vector3 AuthoringHead = new Vector3(0f, 1.2f, 0f);
        private static TMP_FontAsset _font;

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
            PlayerSettings.Android.forceInternetPermission = true;
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


        [MenuItem("Spatial Trading/2. Generate Trader Spread scene")]
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
            if (File.Exists(ScenePath)) throw new BuildFailedException("TraderSpread.unity exists. Open it, or move it aside before regenerating.");
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
            var eye = head.GetComponent<Camera>();

            var app = new GameObject("Trader Spread");
            var workspace = app.AddComponent<SpreadWorkspace>();
            var market = app.AddComponent<MarketDataSession>();
            market.Configure(false); // live gateway; menu 8 is the explicit synthetic preview, never a fallback
            var tray = app.AddComponent<PalmTray>();

            var layout = SpreadLayout.TraderDefault();
            var panels = ((PanelId[])Enum.GetValues(typeof(PanelId))).Select(id => BuildPanel(id, layout[id], eye)).ToArray();
            foreach (var panel in panels) panel.Bind(workspace);
            workspace.Configure(head, eye, market, tray, panels);
            BuildTray(tray, workspace, eye);

            // TraderSpread replaces the picker shell scene and is the only scene; the old scripts stay until their own cleanup.
            foreach (var other in OtherScenes())
                if (!AssetDatabase.DeleteAsset(other)) throw new BuildFailedException("Could not remove " + other);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath))
                throw new BuildFailedException("Could not save Trader Spread scene.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[M1] Trader Spread scene generated: 8 grabbable panels, palm tray, vendor poke/ray/grab interactors.");
        }

        /// <summary>
        /// One spread panel: world canvas (1 unit = 1 mm) with poke+ray canvas pointables, backing card, the Task 13
        /// component, a top grab bar, a full-size body collider (grab volume) behind the canvas, a narrow ray-grab strip on
        /// the top edge, a kinematic Rigidbody, a one-point Grabbable whose transformer locks pitch and roll, and the
        /// bottom-right resize handle (its own Grabbable, cyan triangle).
        /// </summary>
        private static SpreadPanel BuildPanel(PanelId id, PanelRect rect, Camera eye)
        {
            var root = new GameObject("Panel " + id);
            root.transform.SetPositionAndRotation(AuthoringHead + SpreadWorkspace.LocalCenter(rect), Quaternion.identity);
            var size = HoloStyle.SizeFor(rect);

            var canvasObject = new GameObject("Surface", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasObject.transform.SetParent(root.transform, false);
            var surface = (RectTransform)canvasObject.transform;
            surface.sizeDelta = size;
            surface.localScale = Vector3.one * 0.001f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eye;
            // Backing card: a soft dark plate behind the hologram frame so it reads over busy passthrough.
            Stretch(Card(surface, "Background", new Color(0f, .02f, .03f, .35f), Color.clear, 0f, 10f));

            var holo = new GameObject("Holo " + id, typeof(RectTransform));
            holo.transform.SetParent(surface, false);
            ((RectTransform)holo.transform).sizeDelta = size; // centre pivot: resizing grows about the panel centre
            HoloPanel content;
            switch (id)
            {
                case PanelId.OrderBook: content = holo.AddComponent<OrderBookPanel>(); break;
                case PanelId.News: content = holo.AddComponent<NewsPanel>(); break;
                case PanelId.Balance: content = holo.AddComponent<BalancePanel>(); break;
                case PanelId.Order: content = holo.AddComponent<OrderPanel>(); break;
                default: content = holo.AddComponent<ChartPanel>(); break;
            }
            content.Configure(_font); // fixed children are saved with the scene, so Validate can measure them

            var accent = id == PanelId.Order ? HoloStyle.Amber : HoloStyle.Cyan;
            var bar = Card(surface, "Grab bar", HoloStyle.Alpha(accent, .55f), Color.clear, 0f, 2.5f);
            var barRect = bar.rectTransform;
            barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -3f);
            barRect.sizeDelta = new Vector2(90f, 5f);

            root.AddComponent<BoxCollider>();
            var body = root.AddComponent<Rigidbody>();
            QuickActionsAPI.AddGrabInteraction(root);
            QuickActionsAPI.AddRayGrabInteraction(root);
            ConfigureGrab(root, body);
            var grabBar = new GameObject("Grab bar").AddComponent<BoxCollider>();
            grabBar.transform.SetParent(root.transform, false);
            // Ray grab only on the top strip: elsewhere a ray reaches the canvas buttons first.
            foreach (var colliderSurface in root.GetComponentsInChildren<ColliderSurface>(true)) colliderSurface.InjectCollider(grabBar);

            var handle = new GameObject("Resize handle");
            handle.transform.SetParent(root.transform, false);
            var handleCollider = handle.AddComponent<BoxCollider>();
            handleCollider.size = new Vector3(SpreadPanel.HandleSize, SpreadPanel.HandleSize, .02f);
            var handleBody = handle.AddComponent<Rigidbody>();
            QuickActionsAPI.AddGrabInteraction(handle);
            QuickActionsAPI.AddRayGrabInteraction(handle);
            ConfigureGrab(handle, handleBody);
            var markObject = new GameObject("Handle mark", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            markObject.transform.SetParent(handle.transform, false);
            var markCanvas = markObject.GetComponent<Canvas>();
            markCanvas.renderMode = RenderMode.WorldSpace;
            markCanvas.worldCamera = eye;
            var markRect = (RectTransform)markObject.transform;
            markRect.sizeDelta = Vector2.one * (SpreadPanel.HandleSize * 1000f);
            markRect.localScale = Vector3.one * 0.001f;
            markRect.localPosition = new Vector3(0f, 0f, -.001f);
            var triangle = new GameObject("Triangle", typeof(RectTransform)).AddComponent<CornerHandleGraphic>();
            triangle.rectTransform.SetParent(markRect, false);
            Stretch(triangle);
            triangle.color = HoloStyle.Alpha(accent, .9f);
            triangle.raycastTarget = false;

            QuickActionsAPI.AddPokeCanvasInteraction(canvasObject);
            QuickActionsAPI.AddRayCanvasInteraction(canvasObject);

            var panel = root.AddComponent<SpreadPanel>();
            panel.Configure(id, null, content, surface, canvasObject.GetComponent<CanvasGroup>(), root.GetComponent<BoxCollider>(),
                grabBar, root.GetComponent<Grabbable>(), handle.transform, handleCollider, handle.GetComponent<Grabbable>(),
                markObject.GetComponent<CanvasGroup>());
            panel.ApplySize(rect);
            return panel;
        }

        /// <summary>One-point grab, kinematic, no throw; rotation X and Z locked (pitch and roll stay 0), scale fixed.</summary>
        private static void ConfigureGrab(GameObject target, Rigidbody body)
        {
            body.isKinematic = true;
            body.useGravity = false;
            var grabbable = target.GetComponent<Grabbable>();
            if (grabbable == null) throw new BuildFailedException("SDK did not create Grabbable on " + target.name);
            var transformer = target.AddComponent<GrabFreeTransformer>();
            var zero = new TransformerUtils.ConstrainedAxis { ConstrainAxis = true, AxisRange = new TransformerUtils.FloatRange { Min = 0, Max = 0 } };
            transformer.InjectOptionalRotationConstraints(new TransformerUtils.RotationConstraints
            { XAxis = zero, YAxis = TransformerUtils.ConstrainedAxis.Unconstrained, ZAxis = zero });
            var one = new TransformerUtils.ConstrainedAxis { ConstrainAxis = true, AxisRange = new TransformerUtils.FloatRange { Min = 1, Max = 1 } };
            transformer.InjectOptionalScaleConstraints(new TransformerUtils.ScaleConstraints
            { ConstraintsAreRelative = true, XAxis = one, YAxis = one, ZAxis = one });
            grabbable.MaxGrabPoints = 1;
            grabbable.InjectOptionalOneGrabTransformer(transformer);
            grabbable.InjectOptionalTwoGrabTransformer(transformer);
            grabbable.InjectOptionalRigidbody(body);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.InjectOptionalKinematicWhileSelected(true);
        }

        /// <summary>The restore tray: a canvas with "기본 배치로" (poke/ray) and one grabbable icon per panel.</summary>
        private static void BuildTray(PalmTray tray, SpreadWorkspace workspace, Camera eye)
        {
            var root = new GameObject("Palm Tray");
            root.transform.SetPositionAndRotation(AuthoringHead + PalmTray.PinnedOffset, Quaternion.identity);
            var canvasObject = new GameObject("Tray surface", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            var surface = (RectTransform)canvasObject.transform;
            surface.sizeDelta = new Vector2(2 * PalmTray.PadMm + PalmTray.ResetWidthMm, PalmTray.HeightMm);
            surface.localScale = Vector3.one * 0.001f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eye;
            Stretch(Card(surface, "Background", HoloStyle.PanelFill, HoloStyle.Alpha(HoloStyle.Cyan, .55f), HoloStyle.LineWidth, 12f));

            var resetCard = Card(surface, "Reset", HoloStyle.Alpha(HoloStyle.Cyan, .12f), HoloStyle.Cyan, HoloStyle.LineWidth, 8f);
            resetCard.raycastTarget = true;
            var resetRect = resetCard.rectTransform;
            resetRect.anchorMin = resetRect.anchorMax = resetRect.pivot = new Vector2(.5f, .5f);
            resetRect.sizeDelta = new Vector2(PalmTray.ResetWidthMm, PalmTray.HeightMm - 2 * PalmTray.PadMm);
            var button = resetCard.gameObject.AddComponent<Button>();
            button.targetGraphic = resetCard;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var resetLabel = Text(resetRect, "Label", "기본 배치로", 20f, HoloStyle.Ink);
            Stretch(resetLabel);
            var resetCollider = new GameObject("Reset collider").AddComponent<BoxCollider>();
            resetCollider.transform.SetParent(root.transform, false);
            QuickActionsAPI.AddPokeCanvasInteraction(canvasObject);
            QuickActionsAPI.AddRayCanvasInteraction(canvasObject);

            var icons = new System.Collections.Generic.List<PalmTray.Icon>();
            foreach (PanelId id in Enum.GetValues(typeof(PanelId)))
            {
                var icon = new GameObject("Tray icon " + id);
                icon.transform.SetParent(root.transform, false);
                var collider = icon.AddComponent<BoxCollider>();
                collider.size = new Vector3(PalmTray.IconMm * .001f, PalmTray.IconMm * .001f, .02f);
                var body = icon.AddComponent<Rigidbody>();
                QuickActionsAPI.AddGrabInteraction(icon);
                QuickActionsAPI.AddRayGrabInteraction(icon);
                ConfigureGrab(icon, body);
                var face = new GameObject("Icon face", typeof(RectTransform), typeof(Canvas));
                face.transform.SetParent(icon.transform, false);
                var faceCanvas = face.GetComponent<Canvas>();
                faceCanvas.renderMode = RenderMode.WorldSpace;
                faceCanvas.worldCamera = eye;
                var faceRect = (RectTransform)face.transform;
                faceRect.sizeDelta = Vector2.one * PalmTray.IconMm;
                faceRect.localScale = Vector3.one * 0.001f;
                Stretch(Card(faceRect, "Icon", new Color(.02f, .09f, .12f, .92f), HoloStyle.Cyan, HoloStyle.LineWidth, 10f));
                Stretch(Text(faceRect, "Label", PalmTray.Abbreviation(id), 20f, HoloStyle.Ink));
                icon.SetActive(false);
                icons.Add(new PalmTray.Icon { Id = id, Root = icon.transform, Collider = collider, Grabbable = icon.GetComponent<Grabbable>() });
            }
            tray.Configure(workspace, root.transform, surface, resetRect, button, resetCollider, icons.ToArray());
            root.SetActive(false);
        }

        private static SpatialCardGraphic Card(Transform parent, string name, Color fill, Color edge, float edgeWidth, float radius)
        {
            var graphic = new GameObject(name, typeof(RectTransform)).AddComponent<SpatialCardGraphic>();
            graphic.rectTransform.SetParent(parent, false);
            graphic.color = fill;
            graphic.BottomColor = new Color(fill.r * .55f, fill.g * .55f, fill.b * .6f, fill.a);
            graphic.EdgeColor = edge;
            graphic.EdgeWidth = edgeWidth;
            graphic.Radius = radius;
            graphic.raycastTarget = false;
            return graphic;
        }

        private static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color)
        {
            var label = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(parent, false);
            label.font = _font;
            label.fontSize = Mathf.Max(size, HoloStyle.MinText);
            label.enableAutoSizing = false;
            label.color = color;
            label.text = text;
            label.richText = false;
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        private static void Stretch(Graphic graphic)
        {
            var r = graphic.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, .5f);
            r.offsetMin = r.offsetMax = Vector2.zero;
            r.SetAsFirstSibling();
            if (graphic is TMP_Text) r.SetAsLastSibling();
        }

        [MenuItem("Spatial Trading/7. Connect to local gateway")]
        public static void UseGateway()
        {
            if (!MarketDataSession.AllowedGateway(EditorPrefs.GetString(MarketDataSession.EditorUrlPref, string.Empty)) ||
                EditorPrefs.GetString(MarketDataSession.EditorTokenPref, string.Empty).Length == 0)
                Debug.LogWarning("[Gateway] Set the stream address and pairing token first (Spatial Trading → 9). " +
                    "Until then the shell shows GATEWAY_CONFIGURATION_REQUIRED.");
            SetPreview(false);
        }
        // Stored per user in EditorPrefs, outside the project and every build.
        [MenuItem("Spatial Trading/9. Set gateway stream address and pairing token")]
        public static void ConfigureGateway() => GatewaySettingsWindow.Open();
        [MenuItem("Spatial Trading/8. Preview with synthetic data")]
        public static void UsePreview() => SetPreview(true);
        private static void SetPreview(bool preview)
        {
            var workspace = Object.FindFirstObjectByType<SpreadWorkspace>();
            if (workspace == null) throw new BuildFailedException("Open the Trader Spread scene first.");
            if (Application.isPlaying) workspace.SetPreview(preview);
            else
            {
                var market = Object.FindFirstObjectByType<MarketDataSession>();
                market.Configure(preview);
                EditorUtility.SetDirty(market);
                EditorSceneManager.MarkSceneDirty(market.gameObject.scene);
                EditorSceneManager.SaveScene(market.gameObject.scene);
            }
        }

        [MenuItem("Spatial Trading/3. Validate scene and build configuration")]
        public static void Validate()
        {
            if (!File.Exists(ScenePath)) throw new BuildFailedException("Generate Trader Spread scene first.");
            if (OtherScenes().Length > 0) throw new BuildFailedException("TraderSpread must be the only scene; remove " + string.Join(", ", OtherScenes()));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Leave Play Mode before validating or building.");
            // Opening a scene in Single mode can discard unsaved edits. Never do that as a side
            // effect of validation/build, and never validate unsaved data then build older disk data.
            for (var index = 0; index < UnityEngine.SceneManagement.SceneManager.sceneCount; index++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(index).isDirty)
                    throw new BuildFailedException("Save your open scene changes before validating or building.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);
            if (EditorBuildSettings.scenes.Length != 1 || EditorBuildSettings.scenes[0].path != ScenePath || !EditorBuildSettings.scenes[0].enabled)
                throw new BuildFailedException("TraderSpread.unity must be the only scene in build settings.");
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig == null || Object.FindObjectsByType<SpreadWorkspace>(FindObjectsSortMode.None).Length != 1 ||
                Object.FindObjectsByType<MarketDataSession>(FindObjectsSortMode.None).Length != 1 ||
                Object.FindObjectsByType<PalmTray>(FindObjectsSortMode.None).Length != 1)
                throw new BuildFailedException("Expected one SpreadWorkspace, MarketDataSession, PalmTray and an OVRCameraRig.");
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

            var panels = Object.FindObjectsByType<SpreadPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (panels.Length != 8) throw new BuildFailedException("Expected 8 SpreadPanels, found " + panels.Length + ".");
            foreach (PanelId id in Enum.GetValues(typeof(PanelId)))
                if (panels.Count(p => p.Id == id) != 1) throw new BuildFailedException("Expected exactly one panel for " + id + ".");
            foreach (var panel in panels)
            {
                if (panel.Content == null || panel.Handle == null) throw new BuildFailedException("Unbound SpreadPanel " + panel.name);
                if (!Flat(panel.transform.rotation) || !Flat(panel.GetComponentInChildren<Canvas>(true).transform.rotation))
                    throw new BuildFailedException("Panel " + panel.name + " is pitched or rolled; spread panels are flat and front-facing.");
                var canvas = panel.GetComponentInChildren<Canvas>(true);
                if (canvas.renderMode != RenderMode.WorldSpace || canvas.worldCamera == null || canvas.GetComponent<GraphicRaycaster>() == null)
                    throw new BuildFailedException("Invalid world-space canvas on " + panel.name);
                if (canvas.GetComponentsInChildren<PointableCanvas>(true).Length != 2 ||
                    canvas.GetComponentsInChildren<PokeInteractable>(true).Length == 0 ||
                    canvas.GetComponentsInChildren<RayInteractable>(true).Length == 0)
                    throw new BuildFailedException("Missing poke/ray canvas bindings on " + panel.name);
                if (panel.GetComponent<Grabbable>() == null || panel.GetComponent<GrabFreeTransformer>() == null ||
                    panel.GetComponent<Rigidbody>()?.isKinematic != true || panel.GetComponent<BoxCollider>() == null ||
                    panel.GetComponent<Grabbable>().MaxGrabPoints != 1)
                    throw new BuildFailedException("Missing standard grab setup on " + panel.name);
                if (panel.Handle.GetComponent<Grabbable>() == null || panel.Handle.GetComponent<GrabFreeTransformer>() == null)
                    throw new BuildFailedException("Missing resize handle grab on " + panel.name);
            }
            if (Object.FindObjectsByType<PointableCanvasModule>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 1)
                throw new BuildFailedException("Expected exactly one SDK PointableCanvasModule.");
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                        throw new BuildFailedException("Missing script on " + transform.name);
                    foreach (var label in transform.GetComponents<TMP_Text>())
                    {
                        if (label.font == null) throw new BuildFailedException("Missing font on " + transform.name);
                        if (label.fontSize < HoloStyle.MinText || (label.enableAutoSizing && label.fontSizeMin < HoloStyle.MinText))
                            throw new BuildFailedException("Text below " + HoloStyle.MinText + " on " + transform.name);
                        var number = label.name.Contains("Price") || label.name.Contains("Qty");
                        if (number && (label.fontSize < HoloStyle.MinNumber || (label.enableAutoSizing && label.fontSizeMin < HoloStyle.MinNumber)))
                            throw new BuildFailedException("Price/quantity text below " + HoloStyle.MinNumber + " on " + transform.name);
                    }
                }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new BuildFailedException("Scene setup changed while loading. Review/save it, then validate again.");
            Debug.Log("[M1] Static scene/build validation passed. This does not validate headset behavior.");
        }

        private static string[] OtherScenes() =>
            Directory.Exists(ScenesFolder)
                ? Directory.GetFiles(ScenesFolder, "*.unity").Select(p => p.Replace('\\', '/')).Where(p => p != ScenePath).ToArray()
                : new string[0];

        /// <summary>Pitch and roll are exactly zero (yaw is free).</summary>
        private static bool Flat(Quaternion rotation)
        {
            var e = rotation.eulerAngles;
            return Mathf.Abs(Mathf.DeltaAngle(0f, e.x)) < 1e-3f && Mathf.Abs(Mathf.DeltaAngle(0f, e.z)) < 1e-3f;
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