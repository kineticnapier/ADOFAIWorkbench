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
        private readonly StockOverlayRegion settings = new StockOverlayRegion("settingsPanel");
        private readonly StockOverlayRegion events = new StockOverlayRegion("levelEventsPanel");
        private readonly StockOverlayRegion bottom = new StockOverlayRegion("bottomPanel");

        public string Id { get { return "adofai.chart"; } }
        public string Title { get { return "ADOFAI Chart"; } }
        public bool CanClose { get { return true; } }

        public void Mount(RectTransform parent)
        {
            Unmount();
            if (parent == null) return;

            root = new GameObject("ADOFAIChartPane", typeof(RectTransform), typeof(ChartCompositeFollower));
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            Stretch(rootRect, Vector2.zero, Vector2.zero);

            RectTransform leftHost = CreateHost(rootRect, "SettingsRegion");
            RectTransform rightHost = CreateHost(rootRect, "EventsRegion");
            RectTransform bottomHost = CreateHost(rootRect, "BottomRegion");
            RectTransform viewport = CreateHost(rootRect, "ChartViewport");

            scnEditor editor = ADOBase.editor;
            if (editor != null)
            {
                settings.Mount(leftHost, editor);
                events.Mount(rightHost, editor);
                bottom.Mount(bottomHost, editor);
            }

            ChartCompositeFollower follower = root.GetComponent<ChartCompositeFollower>();
            follower.Root = rootRect;
            follower.LeftHost = leftHost;
            follower.RightHost = rightHost;
            follower.BottomHost = bottomHost;
            follower.Viewport = viewport;
            follower.Settings = settings;
            follower.Events = events;
            follower.Bottom = bottom;
            follower.NativeSideWidth = NativeSideWidth;
            follower.NativeBottomHeight = NativeBottomHeight;
            follower.MinimumSideWidth = MinimumSideWidth;
            follower.MinimumWorldWidth = MinimumWorldWidth;
            follower.ApplyNow(true);
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
            GameObject go = new GameObject(name, typeof(RectTransform));
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
    }

    // Stock ADOFAI panels are deliberately NOT parented under the Workbench content.
    // They stay directly under the editor canvas and are positioned over reserved
    // rectangles in ChartPane. This preserves their native Canvas/layout behaviour and,
    // crucially, keeps Workbench tabs/toolbars later in the Canvas draw order.
    internal sealed class StockOverlayRegion
    {
        private readonly string memberName;
        private readonly Vector3[] hostCorners = new Vector3[4];

        private RectTransform host;
        private RectTransform targetRect;
        private GameObject target;
        private Transform originalParent;
        private int originalSiblingIndex;
        private bool originalActive;
        private StockEditorPane.RectState originalRect;
        private Vector2 nativeSize;
        private RectTransform canvasRoot;

        private float lastWidth = -1f;
        private float lastHeight = -1f;
        private Vector3 lastCenter = new Vector3(float.NaN, float.NaN, float.NaN);

        internal StockOverlayRegion(string memberName)
        {
            this.memberName = memberName;
        }

        internal void Mount(RectTransform targetHost, scnEditor editor)
        {
            Restore();
            if (targetHost == null || editor == null) return;

            host = targetHost;
            target = StockEditorPane.Resolve(editor, memberName);
            if (target == null) return;

            targetRect = target.transform as RectTransform;
            if (targetRect == null)
            {
                target = null;
                return;
            }

            canvasRoot = ADOFAIEditorUiHost.Root;
            if (canvasRoot == null)
            {
                target = null;
                targetRect = null;
                return;
            }

            nativeSize = new Vector2(
                Mathf.Max(1f, targetRect.rect.width),
                Mathf.Max(1f, targetRect.rect.height));

            originalParent = targetRect.parent;
            originalSiblingIndex = targetRect.GetSiblingIndex();
            originalRect = StockEditorPane.RectState.Capture(targetRect);

            // Apply() runs before the Workbench shell and may already have hidden this
            // subtree. Claim restores the saved native active states first.
            StockEditorOverride.Claim(target);
            originalActive = target.activeSelf;

            // Keep the stock UI on the editor Canvas, but always immediately below the
            // Workbench overlay root. This makes native controls visible while chrome,
            // tabs and pane borders remain on top.
            targetRect.SetParent(canvasRoot, false);
            int workbenchIndex = FindWorkbenchSiblingIndex(canvasRoot);
            if (workbenchIndex >= 0)
                targetRect.SetSiblingIndex(Mathf.Clamp(workbenchIndex, 0, Math.Max(0, canvasRoot.childCount - 1)));
            else
                targetRect.SetAsFirstSibling();

            targetRect.anchorMin = new Vector2(0.5f, 0.5f);
            targetRect.anchorMax = new Vector2(0.5f, 0.5f);
            targetRect.pivot = new Vector2(0.5f, 0.5f);
            targetRect.sizeDelta = nativeSize;
            targetRect.localScale = Vector3.one;
            target.SetActive(true);

            Apply(true);
        }

        internal void Apply(bool force)
        {
            if (host == null || targetRect == null || canvasRoot == null) return;

            host.GetWorldCorners(hostCorners);
            Vector3 localBottomLeft = canvasRoot.InverseTransformPoint(hostCorners[0]);
            Vector3 localTopRight = canvasRoot.InverseTransformPoint(hostCorners[2]);
            float width = Mathf.Abs(localTopRight.x - localBottomLeft.x);
            float height = Mathf.Abs(localTopRight.y - localBottomLeft.y);
            Vector3 center = (hostCorners[0] + hostCorners[2]) * 0.5f;

            if (!force
                && Mathf.Abs(width - lastWidth) < 0.01f
                && Mathf.Abs(height - lastHeight) < 0.01f
                && (center - lastCenter).sqrMagnitude < 0.0001f)
                return;

            lastWidth = width;
            lastHeight = height;
            lastCenter = center;

            float scale = Mathf.Min(width / nativeSize.x, height / nativeSize.y);
            scale = Mathf.Clamp(scale, 0.05f, 1f);

            targetRect.position = center;
            targetRect.anchorMin = new Vector2(0.5f, 0.5f);
            targetRect.anchorMax = new Vector2(0.5f, 0.5f);
            targetRect.pivot = new Vector2(0.5f, 0.5f);
            targetRect.sizeDelta = nativeSize;
            targetRect.localScale = new Vector3(scale, scale, 1f);
        }

        internal void Restore()
        {
            if (target == null) return;

            if (targetRect != null && originalParent != null)
            {
                targetRect.SetParent(originalParent, false);
                targetRect.SetSiblingIndex(Mathf.Clamp(
                    originalSiblingIndex,
                    0,
                    Math.Max(0, originalParent.childCount - 1)));
                originalRect.Apply(targetRect);
            }

            target.SetActive(originalActive);
            StockEditorOverride.Release(target);

            host = null;
            targetRect = null;
            target = null;
            originalParent = null;
            canvasRoot = null;
            lastWidth = -1f;
            lastHeight = -1f;
            lastCenter = new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static int FindWorkbenchSiblingIndex(RectTransform root)
        {
            if (root == null) return -1;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null && string.Equals(child.name, "ADOFAI Workbench Root", StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }
    }

    internal sealed class ChartCompositeFollower : MonoBehaviour
    {
        internal RectTransform Root;
        internal RectTransform LeftHost;
        internal RectTransform RightHost;
        internal RectTransform BottomHost;
        internal RectTransform Viewport;
        internal StockOverlayRegion Settings;
        internal StockOverlayRegion Events;
        internal StockOverlayRegion Bottom;
        internal float NativeSideWidth;
        internal float NativeBottomHeight;
        internal float MinimumSideWidth;
        internal float MinimumWorldWidth;

        private float lastWidth = -1f;
        private float lastHeight = -1f;

        private void LateUpdate()
        {
            ApplyNow(false);
        }

        internal void ApplyNow(bool force)
        {
            if (Root == null || LeftHost == null || RightHost == null || BottomHost == null || Viewport == null)
                return;

            float width = Mathf.Max(1f, Root.rect.width);
            float height = Mathf.Max(1f, Root.rect.height);
            bool layoutChanged = force
                || Mathf.Abs(width - lastWidth) >= 0.01f
                || Mathf.Abs(height - lastHeight) >= 0.01f;

            if (layoutChanged)
            {
                lastWidth = width;
                lastHeight = height;

                float bottomHeight = Mathf.Min(
                    NativeBottomHeight,
                    Mathf.Max(52f, height * 0.22f));

                float availableForSides = Mathf.Max(0f, width - MinimumWorldWidth);
                float sideWidth = Mathf.Min(NativeSideWidth, availableForSides * 0.5f);
                if (availableForSides >= MinimumSideWidth * 2f)
                    sideWidth = Mathf.Max(MinimumSideWidth, sideWidth);

                LeftHost.anchorMin = new Vector2(0f, 0f);
                LeftHost.anchorMax = new Vector2(0f, 1f);
                LeftHost.pivot = new Vector2(0f, 0.5f);
                LeftHost.anchoredPosition = new Vector2(0f, bottomHeight * 0.5f);
                LeftHost.sizeDelta = new Vector2(sideWidth, -bottomHeight);

                RightHost.anchorMin = new Vector2(1f, 0f);
                RightHost.anchorMax = new Vector2(1f, 1f);
                RightHost.pivot = new Vector2(1f, 0.5f);
                RightHost.anchoredPosition = new Vector2(0f, bottomHeight * 0.5f);
                RightHost.sizeDelta = new Vector2(sideWidth, -bottomHeight);

                BottomHost.anchorMin = new Vector2(0f, 0f);
                BottomHost.anchorMax = new Vector2(1f, 0f);
                BottomHost.pivot = new Vector2(0.5f, 0f);
                BottomHost.anchoredPosition = Vector2.zero;
                BottomHost.sizeDelta = new Vector2(0f, bottomHeight);

                Viewport.anchorMin = Vector2.zero;
                Viewport.anchorMax = Vector2.one;
                Viewport.pivot = new Vector2(0.5f, 0.5f);
                Viewport.anchoredPosition = Vector2.zero;
                Viewport.sizeDelta = Vector2.zero;
                Viewport.offsetMin = new Vector2(sideWidth, bottomHeight);
                Viewport.offsetMax = new Vector2(-sideWidth, 0f);
            }

            // These calls are cheap: each region only writes when its world rect changed.
            if (Settings != null) Settings.Apply(force || layoutChanged);
            if (Events != null) Events.Apply(force || layoutChanged);
            if (Bottom != null) Bottom.Apply(force || layoutChanged);
            ChartCameraViewport.Apply(Viewport);
        }

        private void OnDisable()
        {
            ChartCameraViewport.Restore();
        }

        private void OnDestroy()
        {
            ChartCameraViewport.Restore();
        }
    }

    internal static class ChartCameraViewport
    {
        private const int CameraRetryFrames = 120;

        private static readonly List<Camera> chartCameras = new List<Camera>(2);
        private static readonly Dictionary<Camera, Rect> originalRects = new Dictionary<Camera, Rect>();
        private static readonly Vector3[] rootCorners = new Vector3[4];
        private static readonly Vector3[] targetCorners = new Vector3[4];
        private static Rect lastAppliedRect;
        private static bool hasLastAppliedRect;
        private static int nextCameraDiscoveryFrame;

        internal static void Apply(RectTransform target)
        {
            if (target == null) return;
            RectTransform canvasRoot = ADOFAIEditorUiHost.Root;
            if (canvasRoot == null) return;

            EnsureCameras();
            if (chartCameras.Count == 0) return;

            Rect normalized = ToNormalizedRect(canvasRoot, target);
            normalized.x = Mathf.Clamp01(normalized.x);
            normalized.y = Mathf.Clamp01(normalized.y);
            normalized.width = Mathf.Clamp(normalized.width, 0.01f, 1f - normalized.x);
            normalized.height = Mathf.Clamp(normalized.height, 0.01f, 1f - normalized.y);

            if (hasLastAppliedRect && NearlyEqual(lastAppliedRect, normalized)) return;

            for (int i = 0; i < chartCameras.Count; i++)
            {
                Camera camera = chartCameras[i];
                if (camera == null) continue;
                if (!originalRects.ContainsKey(camera)) originalRects.Add(camera, camera.rect);
                camera.rect = normalized;
            }

            lastAppliedRect = normalized;
            hasLastAppliedRect = true;
        }

        internal static void Restore()
        {
            foreach (KeyValuePair<Camera, Rect> pair in originalRects)
                if (pair.Key != null) pair.Key.rect = pair.Value;

            originalRects.Clear();
            chartCameras.Clear();
            hasLastAppliedRect = false;
            nextCameraDiscoveryFrame = 0;
        }

        private static void EnsureCameras()
        {
            for (int i = chartCameras.Count - 1; i >= 0; i--)
                if (chartCameras[i] == null) chartCameras.RemoveAt(i);
            if (chartCameras.Count > 0) return;
            if (Time.frameCount < nextCameraDiscoveryFrame) return;

            nextCameraDiscoveryFrame = Time.frameCount + CameraRetryFrames;

            GameObject camParent = GameObject.Find("CamParent");
            if (camParent != null)
            {
                Transform mainTransform = camParent.transform.Find("Camera");
                if (mainTransform != null)
                {
                    AddCamera(mainTransform.GetComponent<Camera>());

                    Transform overlayTransform = mainTransform.Find("OverlayCam");
                    if (overlayTransform != null)
                        AddCamera(overlayTransform.GetComponent<Camera>());
                }
            }

            if (chartCameras.Count > 0) return;

            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (IsChartCamera(camera)) AddCamera(camera);
            }
        }

        private static void AddCamera(Camera camera)
        {
            if (camera != null && !chartCameras.Contains(camera)) chartCameras.Add(camera);
        }

        private static Rect ToNormalizedRect(RectTransform root, RectTransform target)
        {
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

        private static bool NearlyEqual(Rect a, Rect b)
        {
            const float epsilon = 0.0001f;
            return Mathf.Abs(a.x - b.x) < epsilon
                && Mathf.Abs(a.y - b.y) < epsilon
                && Mathf.Abs(a.width - b.width) < epsilon
                && Mathf.Abs(a.height - b.height) < epsilon;
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
