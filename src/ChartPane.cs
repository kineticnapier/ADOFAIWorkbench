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
        // The stock editor Canvas uses a 1600x900 reference layout.  Keep that coordinate
        // system intact inside the dock pane instead of independently remapping each stock
        // panel.  Settings / Events / Bottom can therefore keep their original anchors,
        // pivots and offsets exactly as ADOFAI created them.
        private const float NativeWidth = 1600f;
        private const float NativeHeight = 900f;
        private const float NativeLeft = 340f;
        private const float NativeRight = 340f;
        private const float NativeTop = 55f;
        private const float NativeBottom = 120f;

        private GameObject root;
        private RectTransform nativeSurface;

        private readonly MountedNativePanel settings = new MountedNativePanel("settingsPanel");
        private readonly MountedNativePanel events = new MountedNativePanel("levelEventsPanel");
        private readonly MountedNativePanel bottom = new MountedNativePanel("bottomPanel");

        public string Id { get { return "adofai.chart"; } }
        public string Title { get { return "ADOFAI Chart"; } }
        public bool CanClose { get { return true; } }

        public void Mount(RectTransform parent)
        {
            Unmount();
            if (parent == null) return;

            root = new GameObject("ADOFAIChartPane", typeof(RectTransform), typeof(NativeEditorSurfaceFitter));
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            Stretch(rootRect);

            nativeSurface = CreateNativeSurface(rootRect);

            // The camera only owns the same center viewport as the stock editor.  This
            // child lives in the native 1600x900 coordinate system and inherits the one
            // uniform scale/translation applied to the whole surface.
            RectTransform viewport = CreateNativeRect(
                nativeSurface,
                "ChartViewport",
                NativeLeft,
                NativeBottom,
                NativeWidth - NativeLeft - NativeRight,
                NativeHeight - NativeTop - NativeBottom);

            GameObject viewportFollowerObject = new GameObject("ChartViewportFollower", typeof(RectTransform), typeof(ChartViewportFollower));
            RectTransform viewportFollowerRect = (RectTransform)viewportFollowerObject.transform;
            viewportFollowerRect.SetParent(viewport, false);
            Stretch(viewportFollowerRect);
            viewportFollowerObject.GetComponent<ChartViewportFollower>().Target = viewport;

            scnEditor editor = ADOBase.editor;
            if (editor != null)
            {
                settings.Mount(nativeSurface, editor);
                events.Mount(nativeSurface, editor);
                bottom.Mount(nativeSurface, editor);
            }

            NativeEditorSurfaceFitter fitter = root.GetComponent<NativeEditorSurfaceFitter>();
            fitter.Root = rootRect;
            fitter.Surface = nativeSurface;
            fitter.NativeSize = new Vector2(NativeWidth, NativeHeight);
            fitter.ApplyNow(true);
        }

        public void Unmount()
        {
            ChartCameraViewport.Restore();
            settings.Restore();
            events.Restore();
            bottom.Restore();

            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            nativeSurface = null;
        }

        private static RectTransform CreateNativeSurface(Transform parent)
        {
            GameObject go = new GameObject("NativeEditorSurface1600x900", typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(NativeWidth, NativeHeight);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static RectTransform CreateNativeRect(
            Transform parent,
            string name,
            float left,
            float bottom,
            float width,
            float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(left, bottom);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private sealed class MountedNativePanel
        {
            private readonly string memberName;
            private GameObject target;
            private RectTransform targetRect;
            private Transform originalParent;
            private int originalSiblingIndex;
            private bool originalActive;
            private StockEditorPane.RectState originalRect;

            internal MountedNativePanel(string memberName)
            {
                this.memberName = memberName;
            }

            internal void Mount(RectTransform nativeParent, scnEditor editor)
            {
                Restore();
                if (nativeParent == null || editor == null) return;

                target = StockEditorPane.Resolve(editor, memberName);
                if (target == null) return;

                targetRect = target.transform as RectTransform;
                if (targetRect == null)
                {
                    target = null;
                    return;
                }

                originalParent = targetRect.parent;
                originalSiblingIndex = targetRect.GetSiblingIndex();
                originalRect = StockEditorPane.RectState.Capture(targetRect);

                // Apply() may already have hidden this subtree this frame. Claim first so
                // nested tabs/event panels are restored before we snapshot active state.
                StockEditorOverride.Claim(target);
                originalActive = target.activeSelf;

                // nativeParent deliberately has the same size/pivot as /levelEditorScene.
                // Applying the exact original RectTransform state therefore recreates the
                // stock location without any bespoke coordinate conversion.
                targetRect.SetParent(nativeParent, false);
                originalRect.Apply(targetRect);
                target.SetActive(true);
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

                target = null;
                targetRect = null;
                originalParent = null;
            }
        }
    }

    internal sealed class NativeEditorSurfaceFitter : MonoBehaviour
    {
        internal RectTransform Root;
        internal RectTransform Surface;
        internal Vector2 NativeSize;

        private float lastWidth = -1f;
        private float lastHeight = -1f;

        private void LateUpdate()
        {
            ApplyNow(false);
        }

        internal void ApplyNow(bool force)
        {
            if (Root == null || Surface == null || NativeSize.x <= 0f || NativeSize.y <= 0f)
                return;

            float width = Mathf.Max(1f, Root.rect.width);
            float height = Mathf.Max(1f, Root.rect.height);
            if (!force
                && Mathf.Abs(width - lastWidth) < 0.01f
                && Mathf.Abs(height - lastHeight) < 0.01f)
                return;

            lastWidth = width;
            lastHeight = height;

            float scale = Mathf.Min(width / NativeSize.x, height / NativeSize.y);
            scale = Mathf.Max(0.01f, scale);

            Surface.anchorMin = new Vector2(0.5f, 0.5f);
            Surface.anchorMax = new Vector2(0.5f, 0.5f);
            Surface.pivot = new Vector2(0.5f, 0.5f);
            Surface.anchoredPosition = Vector2.zero;
            Surface.sizeDelta = NativeSize;
            Surface.localScale = new Vector3(scale, scale, 1f);
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

        // During editor play-test transitions a previously cropped viewport can leave
        // stale framebuffer pixels outside the next editor viewport. Give the stock
        // renderer a few full-screen editor frames before docking again.
        internal static void ForceFullScreen()
        {
            EnsureCameras();
            Rect full = new Rect(0f, 0f, 1f, 1f);
            for (int i = 0; i < chartCameras.Count; i++)
            {
                Camera camera = chartCameras[i];
                if (camera != null) camera.rect = full;
            }

            // The next docked Apply() should regard fullscreen as the original state.
            originalRects.Clear();
            hasLastAppliedRect = false;
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
