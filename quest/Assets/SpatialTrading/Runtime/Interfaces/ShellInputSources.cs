using Oculus.Interaction;
using SpatialTrading.Domain;
using UnityEngine;

namespace SpatialTrading.Interfaces
{
    /// <summary>
    /// Maps ISDK pointer identity to physical input mode for shell diagnostics only.
    /// It is not proof of physical order confirmation and exposes no trading operation.
    /// </summary>
    public sealed class ShellInputSources : MonoBehaviour
    {
        [SerializeField] private Transform _rig;
        public void Configure(Transform rig) => _rig = rig;

        public InterfaceSource Resolve(int pointerId)
        {
            // Disabled hand/controller branches may not have run Awake. Read only live
            // interactors at event time, so mode switching never caches uninitialized IDs.
            var knownPointer = false;
            foreach (var component in _rig.GetComponentsInChildren<MonoBehaviour>())
                if (component.isActiveAndEnabled && component is IInteractorView interactor && interactor.Identifier == pointerId)
                { knownPointer = true; break; }
            if (!knownPointer) return InterfaceSource.UI;
            var active = OVRInput.GetActiveController();
            if ((active & OVRInput.Controller.Hands) != 0) return InterfaceSource.Hand;
            if ((active & OVRInput.Controller.Touch) != 0) return InterfaceSource.Controller;
            return InterfaceSource.UI; // Unknown input mode must not masquerade as physical evidence.
        }
    }
}
