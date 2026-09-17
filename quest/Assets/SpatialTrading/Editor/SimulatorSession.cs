using System;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using SpatialTrading.Interfaces;
using SpatialTrading.Spatial;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpatialTrading.Editor
{
    /// <summary>Editor-only simulator launcher and read-only evidence; never included in the APK.</summary>
    [InitializeOnLoad]
    public static class SimulatorSession
    {
        private const string RecordingKey = "SpatialTrading.RecordSimulatorEvidence";
        private static double _nextSnapshot;
        private static string EvidenceDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/milestone1/simulator"));

        static SimulatorSession()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += _ => _nextSnapshot = EditorApplication.timeSinceStartup + 2;
        }

        [MenuItem("Spatial Trading/5. Run in Meta XR Simulator")]
        public static void Start()
        {
            QuestShellProject.Validate();
            EditorApplication.ExecuteMenuItem("Meta/Meta XR Simulator/Activate");
            EditorApplication.ExecuteMenuItem("Meta/Meta XR Operator/Activate");
            // These are the SDK's public editor menu operations. Activation is process-local.
            Directory.CreateDirectory(EvidenceDirectory);
            SessionState.SetBool(RecordingKey, true);
            _nextSnapshot = EditorApplication.timeSinceStartup + 2;
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Spatial Trading/6. Stop simulator verification")]
        public static void Stop()
        {
            SessionState.SetBool(RecordingKey, false);
            EditorApplication.isPlaying = false;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(RecordingKey, false) || !EditorApplication.isPlaying ||
                EditorApplication.isCompiling || EditorApplication.timeSinceStartup < _nextSnapshot) return;
            _nextSnapshot = EditorApplication.timeSinceStartup + 1;
            var shell = Object.FindFirstObjectByType<FocusShell>();
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (shell == null || rig == null) return;
            var snapshot = new Snapshot
            {
                capturedAt = DateTimeOffset.UtcNow.ToString("O"),
                scope = "EDITOR_SIMULATOR_ONLY",
                selectedInstrument = shell.State.SelectedInstrument.Id,
                secondaryComponent = shell.State.SecondaryComponent?.ToString() ?? "closed",
                contextRevision = shell.State.Revision,
                applicationFocused = Application.isFocused,
                shellFocused = shell.HasInteractionFocus,
                xrInputFocus = OVRManager.hasInputFocus,
                activeController = OVRInput.GetActiveController().ToString(),
                connectedControllers = OVRInput.GetConnectedControllers().ToString(),
                rightTrigger = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger),
                head = PoseOf(rig.centerEyeAnchor),
                leftController = PoseOf(rig.leftHandAnchor),
                rightController = PoseOf(rig.rightHandAnchor),
                trackingSpace = PoseOf(rig.trackingSpace),
                surfaces = Object.FindObjectsByType<SurfaceBounds>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Select(surface => PoseOf(surface.transform)).ToArray(),
                rays = Object.FindObjectsByType<RayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Select(ray => new RaySnapshot
                    {
                        name = PathOf(ray.transform), active = ray.isActiveAndEnabled, state = ray.State.ToString(),
                        origin = ray.Origin, forward = ray.Forward,
                        target = ray.CandidateProperties is RayInteractor.RayCandidateProperties candidate && candidate.ClosestInteractable != null
                            ? PathOf(candidate.ClosestInteractable.transform) : "none"
                    }).ToArray(),
                buttons = Object.FindObjectsByType<ShellButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Select(button => new ButtonSnapshot
                    {
                        name = button.name,
                        active = button.gameObject.activeInHierarchy,
                        interactable = button.IsInteractable(),
                        center = button.transform.TransformPoint(((RectTransform)button.transform).rect.center),
                        rotation = button.transform.rotation
                    }).ToArray()
            };
            var target = Path.Combine(EvidenceDirectory, "state.json");
            var temporary = target + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(snapshot, true));
            if (File.Exists(target)) File.Delete(target);
            File.Move(temporary, target);
        }

        private static PoseSnapshot PoseOf(Transform transform)
        {
            var box = transform.GetComponent<BoxCollider>();
            return new PoseSnapshot
            {
                name = transform.name, active = transform.gameObject.activeInHierarchy,
                position = transform.position, rotation = transform.rotation, scale = transform.lossyScale,
                // Unity components use overloaded null equality for missing native objects.
                handleCenter = box != null ? transform.TransformPoint(box.center) : transform.position
            };
        }

        private static string PathOf(Transform transform) => transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;

        [Serializable] private sealed class Snapshot
        {
            public string capturedAt, scope, selectedInstrument, secondaryComponent;
            public long contextRevision;
            public bool applicationFocused, shellFocused, xrInputFocus;
            public string activeController, connectedControllers;
            public float rightTrigger;
            public PoseSnapshot head, trackingSpace, leftController, rightController;
            public PoseSnapshot[] surfaces;
            public ButtonSnapshot[] buttons;
            public RaySnapshot[] rays;
        }
        [Serializable] private sealed class RaySnapshot
        {
            public string name, state, target;
            public bool active;
            public Vector3 origin, forward;
        }
        [Serializable] private sealed class PoseSnapshot
        {
            public string name;
            public bool active;
            public Vector3 position, scale, handleCenter;
            public Quaternion rotation;
        }
        [Serializable] private sealed class ButtonSnapshot
        {
            public string name;
            public bool active, interactable;
            public Vector3 center;
            public Quaternion rotation;
        }
    }
}
