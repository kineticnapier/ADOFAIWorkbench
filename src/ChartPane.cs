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
        private const float BottomHeight = 120f;
        private GameObject root;
        private GameObject bottomPanel;
        private Transform bottomOriginalParent;
        private int bottomOriginalSiblingIndex;
        private bool bottomOriginalActive;
        private RectState bottomOriginalRect;

        public string Id { get { return "adofai.chart"; } }
        public string Title { get { return "ADOFAI Chart"; } }
        public bool CanClose { get { return true; } }

        public void Mount(RectTransform parent)
        {
            Unmount();
            if (parent == null) return;

            root = new GameObject("ADOFAIChartPane", typeof(RectTransform));
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            Stretch(rootRect, Vector2.zero, Vector2.zero);

            GameObject viewportObject = new GameObject("ChartViewport", typeof(RectTransform), typeof(ChartViewportFollower));
            RectTransform viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(rootRect, false);
            Stretch(viewport, new Vector2(0f, BottomHeight), Vector2.zero);
            viewportObject.GetComponent<ChartViewportFollower>().Target = viewport;

            RectTransform bottomHost = new GameObject("BottomControlsHost", typeof(RectTransform)).transform as RectTransform;
            bottomHost.SetParent(rootRect, false);
            bottomHost.anchorMin = Vector2.zero;
            bottomHost.anchorMax = new Vector2(1f, 0f);
            bottomHost.pivot = new Vector2(0.5f, 0f);
            bottomHost.anchoredPosition = Vector2.zero;
            bottomHost.sizeDelta = new Vector2(0f, BottomHeight);

            MountBottomPanel(bottomHost);
        }

        public void Unmount()
        {
            ChartCameraViewport.Restore();
            RestoreBottomPanel();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }

        private void MountBottomPanel(RectTransform host)
        {
            scnEditor editor = ADOBase.editor;
            if (editor == null || host == null) return;
            bottomPanel = Resolve(editor, "bottomPanel");
            if (bottomPanel == null) return;
            RectTransform rect = bottomPanel.transform as RectTransform;
            if (rect == null)
            {
                bottomPanel = null;
                return;
            }

            bottomOriginalParent = rect.parent;
            bottomOriginalSiblingIndex = rect.GetSiblingIndex();
            bottomOriginalActive = bottomPanel.activeSelf;
            bottomOriginalRect = RectState.Capture(rect);

            StockEditorOverride.Claim(bottomPanel);
            rect.SetParent(host, false);
            Stretch(rect, Vector2.zero, Vector2.zero);
            bottomPanel.SetActive(true);
        }

        private void RestoreBottomPanel()
        {
            if (bottomPanel == null) return;
            RectTransform rect = bottomPanel.transform as RectTransform;
            if (rect != null && bottomOriginalParent != null)
            {
                rect.SetParent(bottomOriginalParent, false);
                rect.SetSiblingIndex(Mathf.Clamp(bottomOriginalSiblingIndex, 0, Math.Max(0, bottomOriginalParent.childCount - 1)));
                bottomOriginalRect.Apply(rect);
            }
            bottomPanel.SetActive(bottomOriginalActive);
            StockEditorOverride.Release(bottomPanel);
            bottomPanel = null;
            bottomOriginalParent = null;
        }

        private static GameObject Resolve(scnEditor editor, string name)
        {
            Type type = editor.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            object value = null;
            System.Reflection.FieldInfo field = type.GetField(name, flags);
            if (field != null) value = field.GetValue(editor);
            else
            {
                System.Reflection.PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.CanRead) value = property.GetValue(editor, null);
            }
            GameObject go = value as GameObject;
            if (go != null) return go;
            Component component = value as Component;
            return component != null ? component.gameObject : null;
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

        private struct RectState
        {
            internal Vector2 AnchorMin;
            internal Vector2 AnchorMax;
            internal Vector2 Pivot;
            internal Vector2 AnchoredPosition;
            internal Vector2 SizeDelta;
            internal Vector2 OffsetMin;
            internal Vector2 OffsetMax;
            internal Vector3 LocalScale;

            internal static RectState Capture(RectTransform rect)
            {
                return new RectState
                {
                    AnchorMin = rect.anchorMin,
                    AnchorMax = rect.anchorMax,
                    Pivot = rect.pivot,
                    AnchoredPosition = rect.anchoredPosition,
                    SizeDelta = rect.sizeDelta,
                    OffsetMin = rect.offsetMin,
                    OffsetMax = rect.offsetMax,
                    LocalScale = rect.localScale
                };
            }

            internal void Apply(RectTransform rect)
            {
                rect.anchorMin = AnchorMin;
                rect.anchorMax = AnchorMax;
                rect.pivot = Pivot;
                rect.anchoredPosition = AnchoredPosition;
                rect.sizeDelta = SizeDelta;
                rect.offsetMin = OffsetMin;
                rect.offsetMax = OffsetMax;
                rect.localScale = LocalScale;
            }
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
