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
        private GameObject root;

        public string Id { get { return "adofai.chart"; } }
        public string Title { get { return "ADOFAI Chart"; } }
        public bool CanClose { get { return true; } }

        public void Mount(RectTransform parent)
        {
            Unmount();
            if (parent == null) return;

            root = new GameObject("ADOFAIChartPane", typeof(RectTransform), typeof(ChartViewportFollower));
            RectTransform rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            ChartViewportFollower follower = root.GetComponent<ChartViewportFollower>();
            follower.Target = rect;
        }

        public void Unmount()
        {
            ChartCameraViewport.Restore();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
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

            float x = (targetCorners[0].x - rootCorners[0].x) / rootWidth;
            float y = (targetCorners[0].y - rootCorners[0].y) / rootHeight;
            float width = (targetCorners[2].x - targetCorners[0].x) / rootWidth;
            float height = (targetCorners[2].y - targetCorners[0].y) / rootHeight;
            return new Rect(x, y, width, height);
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
