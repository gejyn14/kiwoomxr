using System;
using SpatialTrading.Components;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Spread
{
    /// <summary>
    /// Always-visible restore dock under the left end of the eye row: one button per dismissed panel (short name) and
    /// "기본 배치로" last. It grows to the right with its content, stays flat and follows the workspace origin and rotation.
    /// The editor's mouse fallback reaches the buttons through <see cref="TryPress"/> (one collider each).
    /// </summary>
    public sealed class SpreadDock : MonoBehaviour
    {
        public const float PadMm = 12f;
        public const float GapMm = 8f;
        public const float ButtonMm = 64f;
        public const float ResetWidthMm = 150f;
        public const float ButtonHeightMm = 55f;
        public const float HeightMm = 60f;
        /// <summary>Top-left corner in workspace space: left edge at the eye row's x = −13 cells, top 5 mm under its bottom.</summary>
        public static readonly Vector3 LocalTopLeft = new Vector3(-13 * SpreadLayout.CellMeters, -7 * SpreadLayout.CellMeters - 0.005f, SpreadWorkspace.EyeDistance);

        [Serializable]
        public sealed class Entry
        {
            public bool IsReset;
            public PanelId Id;
            public Button Button;
            public RectTransform Rect;
            public BoxCollider Collider;
        }

        [SerializeField] private SpreadWorkspace _workspace;
        [SerializeField] private RectTransform _canvas;
        [SerializeField] private Entry[] _entries = new Entry[0];

        public RectTransform Canvas => _canvas;
        public Entry[] Entries => _entries;

        /// <summary>Builds one button (and collider) per panel plus the reset button; the scene generator saves them.</summary>
        public void Configure(SpreadWorkspace workspace, RectTransform canvas, TMP_FontAsset font = null)
        {
            _workspace = workspace;
            _canvas = canvas;
            canvas.pivot = canvas.anchorMin = canvas.anchorMax = new Vector2(0f, 1f);
            canvas.anchoredPosition = Vector2.zero;
            var list = new System.Collections.Generic.List<Entry>();
            foreach (PanelId id in Enum.GetValues(typeof(PanelId))) list.Add(Build(false, id, PalmTray.Abbreviation(id), ButtonMm, font));
            list.Add(Build(true, default, "기본 배치로", ResetWidthMm, font));
            _entries = list.ToArray();
            Refresh();
        }

        private Entry Build(bool isReset, PanelId id, string text, float width, TMP_FontAsset font)
        {
            var name = isReset ? "Dock reset" : "Dock " + id;
            var button = Holo.Button(_canvas, name);
            var rect = (RectTransform)button.transform;
            rect.sizeDelta = new Vector2(width, ButtonHeightMm);
            var label = Holo.Text(rect, "Label", font, 20f, HoloStyle.Ink, TextAlignmentOptions.Center);
            label.text = text;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            var holder = new GameObject(name + " collider");
            holder.transform.SetParent(transform, false);
            return new Entry { IsReset = isReset, Id = id, Button = button, Rect = rect, Collider = holder.AddComponent<BoxCollider>() };
        }

        private void Awake()
        {
            foreach (var entry in _entries)
            {
                if (entry?.Button == null) continue;
                var captured = entry;
                Holo.OnClick(entry.Button, () => Activate(captured));
            }
            if (_workspace != null) _workspace.LayoutChanged += Refresh;
        }

        private void OnDestroy()
        {
            if (_workspace != null) _workspace.LayoutChanged -= Refresh;
        }

        private void Activate(Entry entry)
        {
            if (_workspace == null) return;
            if (entry.IsReset) _workspace.Reset();
            else _workspace.Restore(entry.Id);
        }

        /// <summary>Mouse fallback: presses the button whose collider this is (its own click path, tick included).</summary>
        public bool TryPress(Collider c)
        {
            if (c == null) return false;
            foreach (var entry in _entries)
            {
                if (entry?.Collider != c || !c.gameObject.activeInHierarchy) continue;
                entry.Button.onClick.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>Shows a button for each dismissed panel, then "기본 배치로", and sizes the dock to fit.</summary>
        public void Refresh()
        {
            if (_canvas == null) return;
            var x = PadMm;
            var count = 0;
            foreach (var entry in _entries)
            {
                if (entry?.Rect == null) continue;
                var on = entry.IsReset || (_workspace != null && _workspace.Layout.IsDismissed(entry.Id));
                entry.Button.gameObject.SetActive(on);
                if (entry.Collider != null) entry.Collider.gameObject.SetActive(on);
                if (!on) continue;
                if (!entry.IsReset) count++;
                var width = entry.Rect.sizeDelta.x;
                Holo.Place(entry.Button, x, (HeightMm - ButtonHeightMm) * .5f, width, ButtonHeightMm);
                if (entry.Collider != null)
                {
                    entry.Collider.transform.localPosition = Vector3.zero;
                    entry.Collider.transform.localRotation = Quaternion.identity;
                    entry.Collider.center = new Vector3((x + width * .5f) * .001f, -HeightMm * .5f * .001f, 0f);
                    entry.Collider.size = new Vector3(width * .001f, ButtonHeightMm * .001f, .01f);
                }
                x += width + GapMm;
            }
            _canvas.sizeDelta = new Vector2(SpreadLayout.DockWidthMm(count), HeightMm);
        }

        /// <summary>Re-seats the dock on the workspace origin and rotation (called with the panels' placement).</summary>
        public void Place()
        {
            if (_workspace == null) return;
            transform.SetPositionAndRotation(_workspace.Origin + _workspace.Rotation * LocalTopLeft, _workspace.Rotation);
        }
    }
}
