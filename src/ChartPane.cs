using System;
using System.Collections.Generic;
using ADOFAI.EditorToolkit.Game;
using UnityEngine;

namespace KineticNapier.ADOFAIWorkbench
{
    internal sealed class ChartPaneProvider : IDockablePaneProvider
    {
        private readonly ChartPane pane = new ChartPane();

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return pane;
        }
    }

    internal sealed class ChartPane : IDockablePane
    {
        private const float NativeSideWidth = 340f;
        private const float NativeBottomHeight = 120f;
        private const float MinimumWorldWidth = 240f;
        private const float MinimumSideWidth = 100f;

        private GameObject root;
        private readonly MountedStockRegion settings = new MountedStockRegion("settingsPanel");
        private readonly MountedStockRegion events = new MountedStockRegion("levelEventsPanel");
        private readonly MountedStockRegion bottom = new MountedStockRegion("bottomPanel");

        public string Id { get { return "adofai.chart"; } }
        public string Title { get { return "ADOFAI Chart"; } }
        public bool CanClose { get { return true; } }

        public void Mount(RectTransform parent)
        {
            Unmount();
            if (parent == null) return;

            root = new GameObject("ADOFAIChartPane", typeof(RectTransform), typeof(ChartLayoutFollower));
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            Stretch(rootRect, Vector2.zero, Vector2.zero);

            RectTransform leftHost = CreateHost(rootRect, "SettingsHost");
            RectTransform rightHost = CreateHost(rootRect, "EventsHost");
            RectTransform bottomHost = CreateHost(rootRect, "BottomHost");

            GameObject viewportObject = new GameObject("ChartViewport", typeof(RectTransform), typeof(ChartViewportFollower));
            RectTransform viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(rootRect, false);
            Stretch(viewport, new Vector2(NativeSideWidth, NativeBottomHeight), new Vector2(-NativeSideWidth, 0f));
            viewportObject.GetComponent<ChartViewportFollower>().Target = viewport;

            ChartLayoutFollower layout = root.GetComponent<ChartLayoutFollower>();
            layout.Root = rootRect;
            layout.LeftHost = leftHost;
            layout.RightHost = rightHost;
            layout.BottomHost = bottomHost;
            layout.Viewport = viewport;
            layout.NativeSideWidth = NativeSideWidth;
            layout.NativeBottomHeight = NativeBottomHeight;
            layout.MinimumSideWidth = MinimumSideWidth;
            layout.MinimumWorldWidth = MinimumWorldWidth;
            layout.ApplyNow();

            scnEditor editor = ADOBase.editor;
            if (editor != null)
            {
                settings.Mount(leftHost, editor);
                events.Mount(rightHost, editor);
                bottom.Mount(bottomHost, editor);
            }
        }

        public void Unmount()
        {
            ChartCameraViewport.Restore();
            settings.Restore();
            events.Restore();
            bottom.Restore();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }

        private static RectTransform CreateHost(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(NativeStockFitter));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private sealed class MountedStockRegion
        {
            private readonly string memberName;
            private GameObject target;
            private Transform originalParent;
            private int originalSiblingIndex;
            private bool originalActive;
            private StockEditorPane.RectState originalRect;

            internal MountedStockRegion(string memberName)
            {
                this.memberName = memberName;
            }

            internal void Mount(RectTransform host, scnEditor editor)
            {
                Restore();
                if (host == null || editor == null) return;

                target = StockEditorPane.Resolve(editor, memberName);
                if (target == null) return;

                RectTransform rect = target.transform as RectTransform;
                if (rect == null)
                {
                    target = null;
                    return;
                }

                Vector2 nativeSize = new Vector2(
                    Mathf.Max(1f, rect.rect.width),
                    Mathf.Max(1f, rect.rect.height));

                originalParent = rect.parent;
                originalSiblingIndex = rect.GetSiblingIndex();
                originalRect = StockEditorPane.RectState.Capture(rect);

                // Apply() runs before the Workbench shell. Claiming restores any stock
                // children it hid earlier in the same frame (event tabs/panels, etc.).
                StockEditorOverride.Claim(target);
                originalActive = target.activeSelf;

                rect.SetParent(host, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = nativeSize;
                rect.localScale = Vector3.one;
                target.SetActive(true);

                NativeStockFitter fitter = host.GetComponent<NativeStockFitter>();
                if (fitter != null)
                {
                    fitter.Target = rect;
                    fitter.NativeSize = nativeSize;
                    fitter.ApplyNow();
                }
            }

            internal void Restore()
            {
                if (target == null) return;

                RectTransform rect = target.transform as RectTransform;
                if (rect != null && originalParent != null)
                {
                    rect.SetParent(originalParent, false);
                    rect.SetSiblingIndex(Mathf.Clamp(
                        originalSiblingIndex,
                        0,
                        Math.Max(0, originalParent.childCount - 1)));
                    originalRect.Apply(rect);
                }

                target.SetActive(originalActive);
                StockEditorOverride.Release(target);
                target = null;
                originalParent = null;
            }
        }
    }

    internal sealed class ChartLayoutFollower : MonoBehaviour
    {
        internal RectTransform Root;
        internal RectTransform LeftHost;
        internal RectTransform RightHost;
        internal RectTransform BottomHost;
        internal RectTransform Viewport;
        internal float NativeSideWidth;
        internal float NativeBottomHeight;
        internal float MinimumSideWidth;
        internal float MinimumWorldWidth;

        private void LateUpdate()
        {
            ApplyNow();
        }

        internal void ApplyNow()
        {
            if (Root == null || LeftHost == null || RightHost == null || BottomHost == null || Viewport == null)
                return;

            float width = Mathf.Max(1f, Root.rect.width);
            float height = Mathf.Max(1f, Root.rect.height);
            float bottomHeight = Mathf.Min(NativeBottomHeight, Mathf.Max(52f, height * 0.22f));
            float sideWidth = Mathf.Min(
                NativeSideWidth,
                Mathf.Max(MinimumSideWidth, (width - MinimumWorldWidth) * 0.5f));

            // Left stock inspector.
            LeftHost.anchorMin = new Vector2(0f, 0f);
            LeftHost.anchorMax = new Vector2(0f, 1f);
            LeftHost.pivot = new Vector2(0f, 0.5f);
            LeftHost.anchoredPosition = new Vector2(0f, bottomHeight * 0.5f);
            LeftHost.sizeDelta = new Vector2(sideWidth, -bottomHeight);

            // Right stock event inspector.
            RightHost.anchorMin = new Vector2(1f, 0f);
            RightHost.anchorMax = new Vector2(1f, 1f);
            RightHost.pivot = new Vector2(1f, 0.5f);
            RightHost.anchoredPosition = new Vector2(0f, bottomHeight * 0.5f);
            RightHost.sizeDelta = new Vector2(sideWidth, -bottomHeight);

            // Stock bottom toolbar remains one horizontal strip across the whole chart.
            BottomHost.anchorMin = new Vector2(0f, 0f);
            BottomHost.anchorMax = new Vector2(1f, 0f);
            BottomHost.pivot = new Vector2(0.5f, 0f);
            BottomHost.anchoredPosition = Vector2.zero;
            BottomHost.sizeDelta = new Vector2(0f, bottomHeight);

            // Only this center region belongs to the actual chart camera.
            Viewport.anchorMin = Vector2.zero;
            Viewport.anchorMax = Vector2.one;
            Viewport.pivot = new Vector2(0.5f, 0.5f);
            Viewport.anchoredPosition = Vector2.zero;
            Viewport.sizeDelta = Vector2.zero;
            Viewport.offsetMin = new Vector2(sideWidth, bottomHeight);
            Viewport.offsetMax = new Vector2(-sideWidth, 0f);
        }
    }

    internal sealed class NativeStockFitter : MonoBehaviour
    {
        internal RectTransform Target;
        internal Vector2 NativeSize;

        private void LateUpdate()
        {
            ApplyNow();
        }

        internal void ApplyNow()
        {
            RectTransform host = transform as RectTransform;
            if (host == null || Target == null || NativeSize.x <= 0f || NativeSize.y <= 0f) return;

            float scale = Mathf.Min(
                host.rect.width / NativeSize.x,
                host.rect.height / NativeSize.y);
            scale = Mathf.Clamp(scale, 0.05f, 1f);

            // Preserve every native anchor/child coordinate inside the stock panel.
            // Only the whole subtree is scaled, so ScrollRects and event/settings tabs
            // continue to use the dimensions ADOFAI expects.
            Target.anchorMin = new Vector2(0.5f, 0.5f);
            Target.anchorMax = new Vector2(0.5f, 0.5f);
            Target.pivot = new Vector2(0.5f, 0.5f);
            Target.anchoredPosition = Vector2.zero;
            Target.sizeDelta = NativeSize;
            Target.localScale = new Vector3(scale, scale, 1f);
        }
    }

    internal sealed class ChartViewportFollower : MonoBehaviour
    {
        internal RectTransform Target;

        private void LateUpdate()
        {
            if (Target == null || ADOBase.editor == null) return;
            ChartCameraViewport.Apply(Target);
        }

        private void OnDisable() { ChartCameraViewport.Restore(); }
        private void OnDestroy() { ChartCameraViewport.Restore(); }
    }

    internal static class ChartCameraViewport
    {
        private static readonly Dictionary<Camera, Rect> originalRects = new Dictionary<Camera, Rect>();

        internal static void Apply(RectTransform target)
        {
            if (target == null) return;
            RectTransform canvasRoot = ADOFAIEditorUiHost.Root;
            if (canvasRoot == null) return;

            Rect normalized = ToNormalizedRect(canvasRoot, target);
            normalized.x = Mathf.Clamp01(normalized.x);
            normalized.y = Mathf.Clamp01(normalized.y);
            normalized.width = Mathf.Clamp(normalized.width, 0.01f, 1f - normalized.x);
            normalized.height = Mathf.Clamp(normalized.height, 0.01f, 1f - normalized.y);

            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (!IsChartCamera(camera)) continue;
                if (!originalRects.ContainsKey(camera)) originalRects.Add(camera, camera.rect);
                camera.rect = normalized;
            }
        }

        internal static void Restore()
        {
            foreach (KeyValuePair<Camera, Rect> pair in originalRects)
                if (pair.Key != null) pair.Key.rect = pair.Value;
            originalRects.Clear();
        }

        private static Rect ToNormalizedRect(RectTransform root, RectTransform target)
        {
            var rootCorners = new Vector3[4];
            var targetCorners = new Vector3[4];
            root.GetWorldCorners(rootCorners);
            target.GetWorldCorners(targetCorners);

            float rootWidth = rootCorners[2].x - rootCorners[0].x;
            float rootHeight = rootCorners[2].y - rootCorners[0].y;
            if (Mathf.Abs(rootWidth) < 0.0001f || Mathf.Abs(rootHeight) < 0.0001f)
                return new Rect(0f, 0f, 1f, 1f);

            return new Rect(
                (targetCorners[0].x - rootCorners[0].x) / rootWidth,
                (targetCorners[0].y - rootCorners[0].y) / rootHeight,
                (targetCorners[2].x - targetCorners[0].x) / rootWidth,
                (targetCorners[2].y - targetCorners[0].y) / rootHeight);
        }

        private static bool IsChartCamera(Camera camera)
        {
            if (camera == null || camera.targetTexture != null) return false;
            string path = PathOf(camera.transform);
            return string.Equals(path, "/CamParent/Camera", StringComparison.Ordinal)
                || string.Equals(path, "/CamParent/Camera/OverlayCam", StringComparison.Ordinal);
        }

        private static string PathOf(Transform transform)
        {
            if (transform == null) return "<null>";
            var names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return "/" + string.Join("/", names.ToArray());
        }
    }
}
