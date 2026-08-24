using System;
using System.Collections.Generic;

namespace KineticNapier.ADOFAIWorkbench
{
    public enum DockSplitDirection
    {
        Columns,
        Rows
    }

    public abstract class DockNode { }

    public sealed class DockGroup : DockNode
    {
        private readonly List<string> paneIds = new List<string>();

        public DockGroup(string id)
        {
            Id = id;
        }

        public string Id { get; private set; }
        public IList<string> PaneIds { get { return paneIds; } }
        public string ActivePaneId { get; set; }

        public void Open(string paneId)
        {
            if (string.IsNullOrEmpty(paneId)) return;
            if (!paneIds.Contains(paneId)) paneIds.Add(paneId);
            ActivePaneId = paneId;
        }

        public void Close(string paneId)
        {
            int index = paneIds.IndexOf(paneId);
            if (index < 0) return;
            paneIds.RemoveAt(index);
            if (ActivePaneId == paneId)
                ActivePaneId = paneIds.Count == 0 ? null : paneIds[Math.Min(index, paneIds.Count - 1)];
        }
    }

    public sealed class DockSplit : DockNode
    {
        public DockSplit(DockSplitDirection direction, DockNode first, DockNode second, float ratio)
        {
            Direction = direction;
            First = first;
            Second = second;
            Ratio = ratio;
        }

        public DockSplitDirection Direction { get; set; }
        public DockNode First { get; set; }
        public DockNode Second { get; set; }
        public float Ratio { get; set; }
    }

    public sealed class DockWorkspace
    {
        private int nextGroupId = 2;

        public DockWorkspace()
        {
            FocusedGroup = new DockGroup("group-1");
            Root = FocusedGroup;
        }

        public DockNode Root { get; private set; }
        public DockGroup FocusedGroup { get; private set; }

        public IEnumerable<DockGroup> Groups
        {
            get
            {
                var result = new List<DockGroup>();
                CollectGroups(Root, result);
                return result;
            }
        }

        public void Focus(DockGroup group)
        {
            if (group != null) FocusedGroup = group;
        }

        public void OpenInFocused(string paneId)
        {
            if (FocusedGroup == null) return;
            FocusedGroup.Open(paneId);
        }

        public DockGroup SplitFocused(DockSplitDirection direction)
        {
            DockGroup current = FocusedGroup;
            if (current == null) return null;
            DockGroup created = new DockGroup("group-" + nextGroupId++);
            Root = ReplaceNode(Root, current, new DockSplit(direction, current, created, 0.5f));
            FocusedGroup = created;
            return created;
        }

        private static DockNode ReplaceNode(DockNode node, DockNode target, DockNode replacement)
        {
            if (ReferenceEquals(node, target)) return replacement;
            DockSplit split = node as DockSplit;
            if (split == null) return node;
            split.First = ReplaceNode(split.First, target, replacement);
            split.Second = ReplaceNode(split.Second, target, replacement);
            return split;
        }

        private static void CollectGroups(DockNode node, IList<DockGroup> output)
        {
            DockGroup group = node as DockGroup;
            if (group != null)
            {
                output.Add(group);
                return;
            }
            DockSplit split = node as DockSplit;
            if (split == null) return;
            CollectGroups(split.First, output);
            CollectGroups(split.Second, output);
        }
    }
}
