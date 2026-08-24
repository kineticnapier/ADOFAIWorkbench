using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace KineticNapier.ADOFAIWorkbench
{
    internal abstract class DockNode
    {
        internal DockSplitNode Parent;
    }

    internal sealed class DockGroupNode : DockNode
    {
        internal string Id = Guid.NewGuid().ToString("N");
        internal readonly List<string> PaneIds = new List<string>();
        internal string ActivePaneId;
    }

    internal sealed class DockSplitNode : DockNode
    {
        internal Orientation Orientation;
        internal float Ratio = 0.5f;
        internal DockNode First;
        internal DockNode Second;
    }

    internal sealed class DockWorkspace
    {
        internal DockNode Root;
        internal DockGroupNode FocusedGroup;

        internal DockWorkspace()
        {
            Reset();
        }

        internal IEnumerable<DockGroupNode> Groups
        {
            get { return EnumerateGroups(Root); }
        }

        internal void Reset()
        {
            DockGroupNode group = new DockGroupNode();
            Root = group;
            FocusedGroup = group;
        }

        internal void OpenPane(string paneId)
        {
            if (string.IsNullOrEmpty(paneId)) return;
            DockGroupNode existing = FindPaneGroup(paneId);
            if (existing != null)
            {
                existing.ActivePaneId = paneId;
                FocusedGroup = existing;
                return;
            }

            DockGroupNode target = FocusedGroup ?? Groups.FirstOrDefault();
            if (target == null)
            {
                Reset();
                target = FocusedGroup;
            }
            target.PaneIds.Add(paneId);
            target.ActivePaneId = paneId;
            FocusedGroup = target;
        }

        internal void ClosePane(string paneId)
        {
            DockGroupNode group = FindPaneGroup(paneId);
            if (group == null) return;
            int index = group.PaneIds.IndexOf(paneId);
            if (index < 0) return;
            group.PaneIds.RemoveAt(index);
            if (string.Equals(group.ActivePaneId, paneId, StringComparison.Ordinal))
            {
                if (group.PaneIds.Count == 0) group.ActivePaneId = null;
                else group.ActivePaneId = group.PaneIds[Math.Min(index, group.PaneIds.Count - 1)];
            }
        }

        internal void ActivatePane(DockGroupNode group, string paneId)
        {
            if (group == null || string.IsNullOrEmpty(paneId) || !group.PaneIds.Contains(paneId)) return;
            FocusedGroup = group;
            group.ActivePaneId = paneId;
        }

        internal DockGroupNode SplitGroup(DockGroupNode group, Orientation orientation, bool newAfter)
        {
            if (group == null) return null;
            DockSplitNode oldParent = group.Parent;
            DockGroupNode created = new DockGroupNode();
            DockSplitNode split = new DockSplitNode
            {
                Orientation = orientation,
                Ratio = 0.5f
            };

            if (newAfter)
            {
                split.First = group;
                split.Second = created;
            }
            else
            {
                split.First = created;
                split.Second = group;
            }

            group.Parent = split;
            created.Parent = split;
            split.Parent = oldParent;
            ReplaceChild(oldParent, group, split);
            FocusedGroup = created;
            return created;
        }

        internal void MovePane(string paneId, DockGroupNode target, int insertIndex)
        {
            if (string.IsNullOrEmpty(paneId) || target == null) return;
            DockGroupNode source = FindPaneGroup(paneId);
            if (ReferenceEquals(source, target))
            {
                int old = source.PaneIds.IndexOf(paneId);
                if (old < 0) return;
                source.PaneIds.RemoveAt(old);
                insertIndex = Math.Max(0, Math.Min(insertIndex, source.PaneIds.Count));
                source.PaneIds.Insert(insertIndex, paneId);
                source.ActivePaneId = paneId;
                FocusedGroup = source;
                return;
            }

            if (source != null)
            {
                source.PaneIds.Remove(paneId);
                if (string.Equals(source.ActivePaneId, paneId, StringComparison.Ordinal))
                    source.ActivePaneId = source.PaneIds.Count > 0 ? source.PaneIds[0] : null;
            }

            target.PaneIds.Remove(paneId);
            insertIndex = Math.Max(0, Math.Min(insertIndex, target.PaneIds.Count));
            target.PaneIds.Insert(insertIndex, paneId);
            target.ActivePaneId = paneId;
            FocusedGroup = target;
        }

        internal void CloseGroup(DockGroupNode group)
        {
            if (group == null || group.Parent == null) return;
            DockSplitNode parent = group.Parent;
            DockNode sibling = ReferenceEquals(parent.First, group) ? parent.Second : parent.First;
            DockGroupNode destination = FirstGroup(sibling);
            if (destination != null)
            {
                string[] panes = group.PaneIds.ToArray();
                for (int i = 0; i < panes.Length; i++)
                    MovePane(panes[i], destination, destination.PaneIds.Count);
            }

            DockSplitNode grand = parent.Parent;
            sibling.Parent = grand;
            ReplaceChild(grand, parent, sibling);
            FocusedGroup = destination ?? FirstGroup(Root);
        }

        internal DockGroupNode FindPaneGroup(string paneId)
        {
            if (string.IsNullOrEmpty(paneId)) return null;
            foreach (DockGroupNode group in Groups)
                if (group.PaneIds.Contains(paneId)) return group;
            return null;
        }

        internal DockGroupNode FindGroup(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (DockGroupNode group in Groups)
                if (string.Equals(group.Id, id, StringComparison.Ordinal)) return group;
            return null;
        }

        internal void Normalize()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DockGroupNode group in Groups)
            {
                for (int i = group.PaneIds.Count - 1; i >= 0; i--)
                {
                    string id = group.PaneIds[i];
                    if (string.IsNullOrEmpty(id) || !seen.Add(id)) group.PaneIds.RemoveAt(i);
                }
                if (string.IsNullOrEmpty(group.ActivePaneId) || !group.PaneIds.Contains(group.ActivePaneId))
                    group.ActivePaneId = group.PaneIds.Count > 0 ? group.PaneIds[0] : null;
            }
            if (FocusedGroup == null) FocusedGroup = FirstGroup(Root);
        }

        private void ReplaceChild(DockSplitNode parent, DockNode oldNode, DockNode newNode)
        {
            if (parent == null)
            {
                Root = newNode;
                if (newNode != null) newNode.Parent = null;
                return;
            }
            if (ReferenceEquals(parent.First, oldNode)) parent.First = newNode;
            else if (ReferenceEquals(parent.Second, oldNode)) parent.Second = newNode;
            if (newNode != null) newNode.Parent = parent;
        }

        private static DockGroupNode FirstGroup(DockNode node)
        {
            DockGroupNode group = node as DockGroupNode;
            if (group != null) return group;
            DockSplitNode split = node as DockSplitNode;
            return split == null ? null : FirstGroup(split.First) ?? FirstGroup(split.Second);
        }

        private static IEnumerable<DockGroupNode> EnumerateGroups(DockNode node)
        {
            DockGroupNode group = node as DockGroupNode;
            if (group != null)
            {
                yield return group;
                yield break;
            }
            DockSplitNode split = node as DockSplitNode;
            if (split == null) yield break;
            foreach (DockGroupNode child in EnumerateGroups(split.First)) yield return child;
            foreach (DockGroupNode child in EnumerateGroups(split.Second)) yield return child;
        }
    }

    [Serializable]
    public sealed class DockLayoutDocument
    {
        public int Version = 1;
        public int X = -1;
        public int Y = -1;
        public int Width = 1100;
        public int Height = 720;
        public bool Maximized;
        public string FocusedGroupId;
        public DockLayoutNodeDto Root;
    }

    [Serializable]
    public sealed class DockLayoutNodeDto
    {
        public string Type;
        public string GroupId;
        public string Orientation;
        public float Ratio = 0.5f;
        public string ActivePaneId;
        public List<string> PaneIds = new List<string>();
        public DockLayoutNodeDto First;
        public DockLayoutNodeDto Second;
    }

    internal static class DockLayoutStore
    {
        private static string LayoutPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ADOFAIWorkbench");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "layout.xml");
            }
        }

        internal static DockLayoutDocument Load()
        {
            try
            {
                if (!File.Exists(LayoutPath)) return null;
                using (FileStream stream = File.OpenRead(LayoutPath))
                    return (DockLayoutDocument)new XmlSerializer(typeof(DockLayoutDocument)).Deserialize(stream);
            }
            catch
            {
                return null;
            }
        }

        internal static void Save(DockLayoutDocument document)
        {
            try
            {
                string path = LayoutPath;
                string temp = path + ".tmp";
                using (FileStream stream = File.Create(temp))
                    new XmlSerializer(typeof(DockLayoutDocument)).Serialize(stream, document);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch { }
        }

        internal static void Delete()
        {
            try { if (File.Exists(LayoutPath)) File.Delete(LayoutPath); }
            catch { }
        }

        internal static DockLayoutNodeDto ToDto(DockNode node)
        {
            DockGroupNode group = node as DockGroupNode;
            if (group != null)
            {
                return new DockLayoutNodeDto
                {
                    Type = "group",
                    GroupId = group.Id,
                    ActivePaneId = group.ActivePaneId,
                    PaneIds = new List<string>(group.PaneIds)
                };
            }

            DockSplitNode split = node as DockSplitNode;
            if (split == null) return null;
            return new DockLayoutNodeDto
            {
                Type = "split",
                Orientation = split.Orientation == Orientation.Vertical ? "vertical" : "horizontal",
                Ratio = split.Ratio,
                First = ToDto(split.First),
                Second = ToDto(split.Second)
            };
        }

        internal static DockNode FromDto(DockLayoutNodeDto dto, DockSplitNode parent)
        {
            if (dto == null) return null;
            if (string.Equals(dto.Type, "group", StringComparison.OrdinalIgnoreCase))
            {
                DockGroupNode group = new DockGroupNode
                {
                    Parent = parent,
                    Id = string.IsNullOrEmpty(dto.GroupId) ? Guid.NewGuid().ToString("N") : dto.GroupId,
                    ActivePaneId = dto.ActivePaneId
                };
                if (dto.PaneIds != null) group.PaneIds.AddRange(dto.PaneIds.Where(id => !string.IsNullOrEmpty(id)));
                return group;
            }

            DockSplitNode split = new DockSplitNode
            {
                Parent = parent,
                Orientation = string.Equals(dto.Orientation, "horizontal", StringComparison.OrdinalIgnoreCase) ? Orientation.Horizontal : Orientation.Vertical,
                Ratio = Math.Max(0.1f, Math.Min(0.9f, dto.Ratio))
            };
            split.First = FromDto(dto.First, split) ?? new DockGroupNode { Parent = split };
            split.Second = FromDto(dto.Second, split) ?? new DockGroupNode { Parent = split };
            return split;
        }
    }
}
