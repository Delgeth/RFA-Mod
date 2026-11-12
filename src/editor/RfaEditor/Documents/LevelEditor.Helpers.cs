using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using RfaFormats.Zones;

namespace RfaEditor;

// What the game doesn't draw, drawn in the editor only (never saved): each spot light's cone (as
// far as it reaches, as wide as its hotspot and falloff), each spectator camera's view (the MP
// markers of type Spectator camera / Spectator intro camera), the invisible walls (each wall point
// to the next, as high as the game keeps you out, down too where it extends downward) and the
// invisible collision boxes. For this zone's objects and those of its other files shown with it.
public partial class LevelEditor
{
    readonly ModelVisual3D helpersVisual = new();
    bool helpersPending;

    static readonly uint LightClass = NameHashes.ClassHash("obj_light"), MarkerClass = NameHashes.ClassHash("multi_object_marker"),
                         WallClass = NameHashes.ClassHash("invisible_wall"), CollisionBoxClass = NameHashes.ClassHash("collision_box");

    const float CameraLength = 8, CameraFieldOfView = 60, WallUp = 40, WallDown = 20;

    /// <summary>Draws the helpers again soon (once, however often it is asked before then).</summary>
    void RefreshHelpers()
    {
        if (helpersPending) return;
        helpersPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            helpersPending = false;
            ShowHelpers();
        });
    }

    void OnHelpersChanged(object sender, RoutedEventArgs e) => ShowHelpers();

    void ShowHelpers()
    {
        // The box starts checked, so this is called while the view is being built, before the
        // zone is read: nothing to draw yet (BuildScene draws the helpers once it is).
        if (zone == null) return;
        if (ShowHelpersBox.IsChecked != true)
        {
            helpersVisual.Content = null;
            return;
        }
        var objects = zone.Objects.Concat(RelatedFiles().SelectMany(f => f.Zone.Objects)).ToList();
        var byHandle = new Dictionary<uint, ZoneObject>();
        foreach (ZoneObject o in objects) byHandle.TryAdd(o.Handle, o);
        var cones = new MeshGeometry3D();
        var cameras = new MeshGeometry3D();
        var walls = new MeshGeometry3D();
        var boxes = new MeshGeometry3D();
        foreach (ZoneObject o in objects)
        {
            (Vector3 at, Matrix4x4 r) = o.Placement;
            var forward = Vector3.Normalize(new Vector3(r.M31, r.M32, r.M33));
            if (o.ClassHash == LightClass && o.Property("type_enum")?.Text == "spot")
            {
                float range = Math.Clamp((Number(o, "atten_start") ?? 0) + (Number(o, "atten_range") ?? 5), 0.5f, 80);
                float half = Math.Clamp(((Number(o, "hotspot_size") ?? 20) + (Number(o, "hotspot_falloff_size") ?? 10)) / 2, 1, 85) * MathF.PI / 180;
                AddCone(cones, at, forward, new Vector3(r.M21, r.M22, r.M23), range, MathF.Tan(half) * range, MathF.Tan(half) * range, 24);
            }
            else if (o.ClassHash == MarkerClass && o.Property("marker_type")?.Text is { } type && type.StartsWith("Spectator", StringComparison.OrdinalIgnoreCase))
            {
                float halfWidth = MathF.Tan(CameraFieldOfView / 2 * MathF.PI / 180) * CameraLength;
                AddCone(cameras, at, forward, new Vector3(r.M21, r.M22, r.M23), CameraLength, halfWidth, halfWidth * 9 / 16, 4);
            }
            else if (o.ClassHash == WallClass && o.Property("next") is { Size: 4 } next &&
                     byHandle.TryGetValue(BitConverter.ToUInt32(next.Value), out ZoneObject? to) && to.ClassHash == WallClass)
            {
                Vector3 there = to.Placement.Position;
                bool down = o.Property("extend_downward") is not { Size: >= 1 } d || d.Value[0] != 0;
                float bottom = MathF.Min(at.Y, there.Y) - (down ? WallDown : 0), top = MathF.Max(at.Y, there.Y) + WallUp;
                AddQuad(walls, new Vector3(at.X, bottom, at.Z), new Vector3(there.X, bottom, there.Z),
                               new Vector3(there.X, top, there.Z), new Vector3(at.X, top, at.Z));
            }
            else if (o.ClassHash == CollisionBoxClass && o.Property("bb") is { Size: 24 } bb)
            {
                float[] b = bb.Floats();
                AddBox(boxes, new Vector3(b[0], b[1], b[2]), new Vector3(b[3], b[4], b[5]), at, r);
            }
        }
        var group = new Model3DGroup();
        void Add(MeshGeometry3D mesh, Color colour)
        {
            if (mesh.Positions.Count == 0) return;
            mesh.Freeze();
            var material = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))), new EmissiveMaterial(new SolidColorBrush(colour)) } };
            material.Freeze();
            group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
        Add(cones, Color.FromArgb(70, 255, 220, 90));
        Add(cameras, Color.FromArgb(80, 90, 200, 255));
        Add(walls, Color.FromArgb(70, 255, 70, 70));
        Add(boxes, Color.FromArgb(70, 255, 140, 40));
        group.Freeze();
        helpersVisual.Content = group;
    }

    static float? Number(ZoneObject o, string name) => o.Property(name) is { Size: 4 } p ? BitConverter.ToSingle(p.Value) : null;

    /// <summary>A cone (or pyramid, with 4 sides) from its tip along a direction, its far end that wide and high, in the view's space.</summary>
    void AddCone(MeshGeometry3D mesh, Vector3 tip, Vector3 along, Vector3 up, float length, float halfWidth, float halfHeight, int sides)
    {
        var side = Vector3.Cross(up, along);
        if (side.LengthSquared() < 1e-6f) side = Vector3.Cross(Vector3.UnitY, along);
        if (side.LengthSquared() < 1e-6f) side = Vector3.UnitX;
        side = Vector3.Normalize(side);
        up = Vector3.Normalize(Vector3.Cross(along, side));
        int start = mesh.Positions.Count;
        mesh.Positions.Add(ToView(tip));
        for (int i = 0; i < sides; i++)
        {
            float angle = sides == 4 ? (i * 2 + 1) * MathF.PI / 4 : i * 2 * MathF.PI / sides;
            Vector3 rim = tip + along * length + side * (MathF.Cos(angle) * halfWidth * (sides == 4 ? MathF.Sqrt(2) : 1)) +
                          up * (MathF.Sin(angle) * halfHeight * (sides == 4 ? MathF.Sqrt(2) : 1));
            mesh.Positions.Add(ToView(rim));
        }
        for (int i = 0; i < sides; i++)
        {
            mesh.TriangleIndices.Add(start);
            mesh.TriangleIndices.Add(start + 1 + i);
            mesh.TriangleIndices.Add(start + 1 + (i + 1) % sides);
        }
    }

    void AddQuad(MeshGeometry3D mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = mesh.Positions.Count;
        foreach (Vector3 p in new[] { a, b, c, d }) mesh.Positions.Add(ToView(p));
        foreach (int i in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(start + i);
    }

    /// <summary>A box (its corners in the object's own space) placed as the object.</summary>
    void AddBox(MeshGeometry3D mesh, Vector3 min, Vector3 max, Vector3 at, Matrix4x4 rotation)
    {
        Vector3 P(float x, float y, float z) => Vector3.TransformNormal(new Vector3(x, y, z), rotation) + at;
        Vector3[] c = [P(min.X, min.Y, min.Z), P(max.X, min.Y, min.Z), P(max.X, min.Y, max.Z), P(min.X, min.Y, max.Z),
                       P(min.X, max.Y, min.Z), P(max.X, max.Y, min.Z), P(max.X, max.Y, max.Z), P(min.X, max.Y, max.Z)];
        AddQuad(mesh, c[0], c[1], c[2], c[3]); AddQuad(mesh, c[4], c[5], c[6], c[7]);
        AddQuad(mesh, c[0], c[1], c[5], c[4]); AddQuad(mesh, c[1], c[2], c[6], c[5]);
        AddQuad(mesh, c[2], c[3], c[7], c[6]); AddQuad(mesh, c[3], c[0], c[4], c[7]);
    }
}
