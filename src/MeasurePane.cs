using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace KineticNapier.ADOFAIWorkbench
{
    internal sealed class MeasurePaneProvider : IDockablePaneProvider
    {
        private readonly MeasurePane pane = new MeasurePane();
        private float refreshElapsed;

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return pane;
        }

        internal void Refresh(float deltaTime)
        {
            refreshElapsed += Math.Max(0f, deltaTime);
            if (refreshElapsed < 0.05f) return;
            refreshElapsed = 0f;
            pane.Refresh();
        }
    }

    internal sealed class MeasurePane : IDockablePane
    {
        private MeasureState state = MeasureState.Empty("Select a range of at least two tiles.");

        public string Id { get { return "adofai.measure"; } }
        public string Title { get { return "Measure"; } }
        public bool CanClose { get { return true; } }

        public WorkbenchPaneView BuildView()
        {
            WorkbenchPaneView view = new WorkbenchPaneView()
                .Spacer(14)
                .Text("Tile Measure", 20f, true)
                .Spacer(8);

            if (!state.HasMeasurement)
            {
                return view.Text(state.Message, 11f, false);
            }

            return view
                .Text("Range: Tile " + state.FromTile.ToString(CultureInfo.InvariantCulture)
                    + " -> " + state.ToTile.ToString(CultureInfo.InvariantCulture), 11f, false)
                .Spacer(10)
                .Text("Delta X: " + FormatSigned(state.DeltaX) + " tiles", 13f, true)
                .Text("Delta Y: " + FormatSigned(state.DeltaY) + " tiles", 13f, true)
                .Text("Distance: " + state.Distance.ToString("0.000", CultureInfo.InvariantCulture) + " tiles", 13f, true);
        }

        public void HandleAction(string actionId, string argument)
        {
        }

        internal void Refresh()
        {
            MeasureState next = MeasureReader.Read();
            if (state.Equals(next)) return;
            state = next;
            Workbench.PublishPane(Id);
        }

        private static string FormatSigned(double value)
        {
            if (Math.Abs(value) < 0.0005d) return "0.000";
            return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class MeasureState : IEquatable<MeasureState>
    {
        private const double Epsilon = 0.0000001d;

        internal bool HasMeasurement;
        internal string Message;
        internal int FromTile;
        internal int ToTile;
        internal double DeltaX;
        internal double DeltaY;
        internal double Distance;

        internal static MeasureState Empty(string message)
        {
            return new MeasureState { Message = message ?? string.Empty };
        }

        internal static MeasureState Value(int fromTile, int toTile, double deltaX, double deltaY)
        {
            return new MeasureState
            {
                HasMeasurement = true,
                FromTile = fromTile,
                ToTile = toTile,
                DeltaX = deltaX,
                DeltaY = deltaY,
                Distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY)
            };
        }

        public bool Equals(MeasureState other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (HasMeasurement != other.HasMeasurement) return false;
            if (!HasMeasurement) return string.Equals(Message, other.Message, StringComparison.Ordinal);

            return FromTile == other.FromTile
                && ToTile == other.ToTile
                && Math.Abs(DeltaX - other.DeltaX) < Epsilon
                && Math.Abs(DeltaY - other.DeltaY) < Epsilon;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as MeasureState);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HasMeasurement ? 1 : 0;
                hash = (hash * 397) ^ FromTile;
                hash = (hash * 397) ^ ToTile;
                return hash;
            }
        }
    }

    internal static class MeasureReader
    {
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static Type cachedAdoBaseType;

        internal static MeasureState Read()
        {
            try
            {
                Type adoBaseType = FindType("ADOBase");
                if (adoBaseType == null)
                    return MeasureState.Empty("ADOFAI editor API is not available.");

                object editor = ReadMember(adoBaseType, null, "editor", AnyStatic);
                if (editor == null)
                    return MeasureState.Empty("Open the ADOFAI level editor to measure tiles.");

                object selectedObject = ReadMember(editor.GetType(), editor, "selectedFloors", AnyInstance);
                IList selected = selectedObject as IList;
                if (selected == null || selected.Count < 2)
                    return MeasureState.Empty("Select a range of at least two tiles.");

                object fromFloor = null;
                object toFloor = null;
                int fromId = int.MaxValue;
                int toId = int.MinValue;

                for (int i = 0; i < selected.Count; i++)
                {
                    object floor = selected[i];
                    if (floor == null) continue;

                    int seqId;
                    if (!TryReadInt(floor, "seqID", out seqId)) continue;

                    if (seqId < fromId)
                    {
                        fromId = seqId;
                        fromFloor = floor;
                    }
                    if (seqId > toId)
                    {
                        toId = seqId;
                        toFloor = floor;
                    }
                }

                if (fromFloor == null || toFloor == null || fromId == toId)
                    return MeasureState.Empty("Select a range of at least two tiles.");

                double fromX;
                double fromY;
                double toX;
                double toY;
                if (!TryReadFloorPosition(fromFloor, out fromX, out fromY)
                    || !TryReadFloorPosition(toFloor, out toX, out toY))
                    return MeasureState.Empty("Could not read tile center positions.");

                double tileSize;
                if (!TryReadTileSize(fromFloor, out tileSize) || tileSize <= 0.0000001d)
                    return MeasureState.Empty("Could not read ADOFAI tile size.");

                return MeasureState.Value(
                    fromId,
                    toId,
                    (toX - fromX) / tileSize,
                    (toY - fromY) / tileSize);
            }
            catch (Exception ex)
            {
                Main.LogError("Measure pane refresh failed", ex);
                return MeasureState.Empty("Measure is unavailable for the current editor state.");
            }
        }

        private static bool TryReadFloorPosition(object floor, out double x, out double y)
        {
            x = 0d;
            y = 0d;
            if (floor == null) return false;

            object transform = ReadMember(floor.GetType(), floor, "thisTransform", AnyInstance);
            if (transform == null)
                transform = ReadMember(floor.GetType(), floor, "transform", AnyInstance);
            if (transform == null) return false;

            object position = ReadMember(transform.GetType(), transform, "position", AnyInstance);
            if (position == null) return false;

            return TryReadDouble(position, "x", out x) && TryReadDouble(position, "y", out y);
        }

        private static bool TryReadTileSize(object floor, out double tileSize)
        {
            tileSize = 0d;

            object controller = ReadMember(floor.GetType(), floor, "controller", AnyInstance);
            if (controller == null)
            {
                Type adoBaseType = FindType("ADOBase");
                if (adoBaseType != null)
                    controller = ReadMember(adoBaseType, null, "controller", AnyStatic);
            }
            if (controller == null) return false;

            return TryReadDouble(controller, "tileSize", out tileSize);
        }

        private static bool TryReadInt(object target, string name, out int value)
        {
            value = 0;
            object raw = ReadMember(target.GetType(), target, name, AnyInstance);
            if (raw == null) return false;
            try
            {
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadDouble(object target, string name, out double value)
        {
            value = 0d;
            object raw = ReadMember(target.GetType(), target, name, AnyInstance);
            if (raw == null) return false;
            try
            {
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object ReadMember(Type type, object target, string name, BindingFlags flags)
        {
            if (type == null) return null;

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(target, null);

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(target);

            Type baseType = type.BaseType;
            return baseType != null ? ReadMember(baseType, target, name, flags) : null;
        }

        private static Type FindType(string fullName)
        {
            if (string.Equals(fullName, "ADOBase", StringComparison.Ordinal) && cachedAdoBaseType != null)
                return cachedAdoBaseType;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type type = assemblies[i].GetType(fullName, false, false);
                    if (type != null)
                    {
                        if (string.Equals(fullName, "ADOBase", StringComparison.Ordinal)) cachedAdoBaseType = type;
                        return type;
                    }
                }
                catch
                {
                }
            }
            return null;
        }
    }
}
