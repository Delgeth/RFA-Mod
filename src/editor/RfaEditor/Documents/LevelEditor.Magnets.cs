using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RfaFormats.Zones;

namespace RfaEditor;

// Magnets: an object being moved jumps to touch the objects drawn with models near it, corner to
// corner, edge to edge or face to face, so chunks line up without gaps or overlaps.
public partial class LevelEditor
{
    readonly ModelVisual3D magnetVisual = new();

    bool Magnets => MagnetBox.IsChecked == true;

    void ToggleMagnets()
    {
        MagnetBox.IsChecked = !Magnets;
        context.Log($"Magnets {(Magnets ? "on: a moved object jumps to touch the corners, edges and faces of the models near it" : "off")}.");
        if (!Magnets) ShowMagnet(null);
    }

    /// <summary>
    /// A box's magnet points, placed: its 8 corners, the middles of its 12 edges and of its 6 faces
    /// (every mix of its lowest, middle and highest x, y and z but the very middle).
    /// </summary>
    static IEnumerable<Vector3> BoxPoints(Vector3 min, Vector3 max, Func<Vector3, Vector3> place)
    {
        Vector3 mid = (min + max) / 2;
        float[] xs = [min.X, mid.X, max.X], ys = [min.Y, mid.Y, max.Y], zs = [min.Z, mid.Z, max.Z];
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 3; z++)
                    if (x != 1 || y != 1 || z != 1)
                        yield return place(new Vector3(xs[x], ys[y], zs[z]));
    }

    /// <summary>An object's magnet points if it were at this place: its model's box turned with it, or else its bounding box moved there.</summary>
    IEnumerable<Vector3> MagnetPointsOf(ZoneObject obj, Vector3 position, Matrix4x4 rotation)
    {
        if (scene?.LocalBox(obj) is { } local)
            return BoxPoints(local.Min, local.Max, q => Vector3.TransformNormal(q, rotation) + position);
        Vector3 by = position - obj.Placement.Position;
        return BoxPoints(obj.BoundsMin + by, obj.BoundsMax + by, q => q);
    }

    /// <summary>
    /// Where an object being moved to a place should go instead to touch a model near it: the
    /// nearest pair of magnet points (its and another object's) closer than a reach that grows
    /// with its size, brought together. With an axis (dragging an arrow), only along it. Null if
    /// nothing is near enough.
    /// </summary>
    Vector3? MagnetSnap(ZoneObject moving, Vector3 position, Matrix4x4 rotation, Vector3? axis = null)
    {
        if (scene == null) return null;
        List<Vector3> own = MagnetPointsOf(moving, position, rotation).ToList();
        Vector3 low = own.Aggregate((a, b) => Vector3.Min(a, b)), high = own.Aggregate((a, b) => Vector3.Max(a, b));
        float reach = Math.Clamp((high - low).Length() * 0.08f, 0.35f, 2.5f);
        Vector3 nearLow = low - new Vector3(reach), nearHigh = high + new Vector3(reach);
        var skip = new HashSet<ZoneObject>(carried.Select(c => c.Object)) { moving };
        float best = reach * reach;
        Vector3? shift = null, touching = null;
        foreach (ZoneObject other in zone.Objects)
        {
            if (skip.Contains(other) || scene.LocalBox(other) is not { } box) continue;
            // Only those whose bounding box is near (it holds their model, turned).
            Vector3 min = other.BoundsMin, max = other.BoundsMax;
            if (min.X > nearHigh.X || max.X < nearLow.X || min.Y > nearHigh.Y || max.Y < nearLow.Y || min.Z > nearHigh.Z || max.Z < nearLow.Z) continue;
            (Vector3 at, Matrix4x4 turned) = other.Placement;
            foreach (Vector3 point in BoxPoints(box.Min, box.Max, q => Vector3.TransformNormal(q, turned) + at))
                foreach (Vector3 mine in own)
                {
                    Vector3 gap = point - mine;
                    float distance = gap.LengthSquared();
                    if (distance >= best) continue;
                    best = distance;
                    shift = axis is Vector3 along ? along * Vector3.Dot(gap, along) : gap;
                    touching = point;
                }
        }
        ShowMagnet(touching);
        return shift is Vector3 by ? position + by : null;
    }

    /// <summary>Marks where the magnet holds (a small bright diamond), or nowhere.</summary>
    void ShowMagnet(Vector3? at)
    {
        if (at is not Vector3 p)
        {
            magnetVisual.Content = null;
            return;
        }
        double size = Math.Max(0.12, (camera.Position - ToView(p)).Length * 0.008);
        var centre = ToView(p);
        var mesh = new MeshGeometry3D();
        Point3D[] tips =
        [
            centre + new Vector3D(size, 0, 0), centre + new Vector3D(-size, 0, 0),
            centre + new Vector3D(0, size, 0), centre + new Vector3D(0, -size, 0),
            centre + new Vector3D(0, 0, size), centre + new Vector3D(0, 0, -size),
        ];
        foreach (Point3D tip in tips) mesh.Positions.Add(tip);
        int[] faces = [0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5];
        foreach (int i in faces) mesh.TriangleIndices.Add(i);
        mesh.Freeze();
        var material = new MaterialGroup { Children = { new DiffuseMaterial(Brushes.Black), new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(255, 80, 200))) } };
        material.Freeze();
        magnetVisual.Content = new GeometryModel3D(mesh, material) { BackMaterial = material };
    }
}
