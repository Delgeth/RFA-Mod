using System.Globalization;
using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's human ragdoll as an Armageddon ragdoll table (ragdoll_rfg_human.xtbl), for
/// GuerrillaPeople. Guerrilla builds its people's ragdoll in code, not from a file: its ragdoll set-up
/// finds 16 bones by name in the character's own skeleton and gives the 12 parts their sizes and masses,
/// and its ragdoll maker gives each joint its limits, axes and the bodies' damping, friction and bounce,
/// all as numbers in its code. Armageddon's ragdoll is the same maker with those numbers moved into a table
/// a character names (ragdoll_type): the same 12 parts in the same order, the same sphere head and capsule
/// limbs, the same joints (a cone-and-planes joint, or a hinge for the knees and elbows). So Guerrilla's
/// numbers are written here, in that table's layout:
///   - the angles in degrees (Armageddon's table takes degrees), the axes in the skeleton's rest pose
///     (+X right, +Y up, +Z forward), the radii in metres;
///   - each part's mass as a share of the character's Mass (Guerrilla's are kilograms, 91.6 in all; its
///     people weigh 90, Armageddon's Mass default);
///   - the upper arms' twist axis as Guerrilla works it out from the bones (Armageddon's own when it's left
///     out points the other way, which turns the arms' one-sided limits round);
///   - recovery_scalar Guerrilla's flinch recovery (9 on the upper arms, 5 on the lower ones, else 2), and
///     apply_angular_impulse on the parts Guerrilla turns by a hit (head, body, upper body, upper legs);
///   - the head bounces (restitution 1), the rest Guerrilla's 0.075.
/// Both games find the bones by part of their name in the character's own skeleton; these names pick the
/// same 16 bones in every one of Guerrilla's people's skeletons (all share the first 52 bones in the same
/// order), so one table serves them all. Not carried by a table: how Guerrilla spreads a bullet's push over
/// the parts (Armageddon has its own), and its "upper body" being asked for where Armageddon has only one
/// centre of mass (the body).
/// </summary>
public static class GuerrillaRagdoll
{
    /// <summary>The table's file name, which the people's character entries name (ragdoll_type).</summary>
    public const string FileName = "ragdoll_rfg_human.xtbl";

    /// <summary>A cone-and-planes joint (Armageddon's constraint_type_1): degrees and axes in the rest pose.</summary>
    sealed record Cone(float ConeMin, float ConeMax, float PlaneMin, float PlaneMax, float TwistMin, float TwistMax,
                       (float X, float Y, float Z) TwistAxis, (float X, float Y, float Z) PlaneAxis);

    /// <summary>A hinge (constraint_type_2): its least and most bend, in degrees.</summary>
    sealed record Hinge(float Min, float Max);

    sealed record Part(string Name, string Start, string End, string? Parent, object? Joint, float Radius, float Kilograms,
                       float FrictionTorque, float Restitution, float Recovery, string[] Flags);

    /// <summary>Guerrilla's total (91.6 kg), which the parts' shares are of.</summary>
    const float GuerrillaKilograms = 91.6f;
    const float GroundFriction = 7, AngularDamping = 2, LinearDamping = 0.2f;

    static readonly (float, float, float) Up = (0, 1, 0), Forward = (0, 0, 1), Back = (0, -1, 0), Down = (0, -1, 0), Left = (-1, 0, 0);
    const string Impulse = "apply_angular_impulse";

    /// <summary>Guerrilla's parts, in its order (its ragdoll_part numbers 0 to 11).</summary>
    static readonly Part[] Parts =
    [
        new("head", "head", "head", "up_bdy", new Cone(-15, 15, -90, 90, -35, 35, Up, Forward), 0.125f, 4, 0.3f, 1, 2, [Impulse, "is_head"]),
        new("body", "root", "spine1", null, null, 0.16f, 20, 0.7f, 0.075f, 2, [Impulse, "is_body", "is_center_of_mass"]),
        new("spine", "spine1", "spine2", "body", new Cone(-25, 25, -20, 10, -5, 5, Forward, Back), 0.17f, 20, 0.7f, 0.075f, 2, []),
        new("up_bdy", "spine2", "head", "spine", new Cone(-25, 25, -20, 10, -5, 5, Forward, Back), 0.17f, 20, 0.7f, 0.075f, 2, [Impulse]),
        new("urleg", "r-thigh", "r-calf", "body", new Cone(-85, -1, 0, 10, -15, 15, Down, Left), 0.12f, 5.5f, 0.7f, 0.075f, 2, [Impulse]),
        new("ulleg", "l-thigh", "l-calf", "body", new Cone(-85, -1, -10, 0, -15, 15, Down, Left), 0.12f, 5.5f, 0.7f, 0.075f, 2, [Impulse]),
        new("lrleg", "r-calf", "r-foot", "urleg", new Hinge(0, 110), 0.1f, 4.5f, 0.5f, 0.075f, 2, []),
        new("llleg", "l-calf", "l-foot", "ulleg", new Hinge(0, 110), 0.1f, 4.5f, 0.5f, 0.075f, 2, []),
        new("urarm", "r-upperarm", "r-foretwist", "up_bdy", new Cone(-45, 10, -70, 2.5f, -20, 20, (0.674498f, -0.737588f, -0.031898f), Forward), 0.08f, 2, 0.7f, 0.075f, 9, []),
        new("ularm", "l-upperarm", "l-foretwist", "up_bdy", new Cone(-10, 45, -70, 2.5f, -20, 20, (-0.674499f, -0.737586f, -0.031901f), Forward), 0.08f, 2, 0.7f, 0.075f, 9, []),
        new("lrarm", "r-foretwist", "r-hand", "urarm", new Hinge(10, 80), 0.07f, 1.8f, 0.1f, 0.075f, 5, []),
        new("llarm", "l-foretwist", "l-hand", "ularm", new Hinge(10, 80), 0.07f, 1.8f, 0.1f, 0.075f, 5, []),
    ];

    /// <summary>The table, in the layout of Armageddon's ragdoll_human.xtbl.</summary>
    public static byte[] Table()
    {
        var document = new XtblElement("");  // (the writer writes a document's parts: here <root>)
        var root = document.AddElement("root");
        var table = root.AddElement("Table");
        foreach (var part in Parts)
        {
            var e = table.AddElement("ragdoll");
            e.AddElement("Name", part.Name);
            e.AddElement("bone_start", part.Start);
            e.AddElement("bone_end", part.End);
            if (part.Parent != null) e.AddElement("parent", part.Parent);
            if (part.Joint is Cone cone)
            {
                var c = e.AddElement("constraint_type_1");
                c.AddElement("cone_min", N(cone.ConeMin));
                c.AddElement("cone_max", N(cone.ConeMax));
                c.AddElement("plane_min", N(cone.PlaneMin));
                c.AddElement("plane_max", N(cone.PlaneMax));
                c.AddElement("twist_min", N(cone.TwistMin));
                c.AddElement("twist_max", N(cone.TwistMax));
                Axis(c.AddElement("twist_axis"), cone.TwistAxis);
                Axis(c.AddElement("plane_axis"), cone.PlaneAxis);
            }
            else if (part.Joint is Hinge hinge)
            {
                var c = e.AddElement("constraint_type_2");
                c.AddElement("min_angular_limit", N(hinge.Min));
                c.AddElement("max_angular_limit", N(hinge.Max));
            }
            var physics = e.AddElement("physics");
            physics.AddElement("max_friction_torque", N(part.FrictionTorque));
            physics.AddElement("ground_friction", N(GroundFriction));
            physics.AddElement("angular_dampening", N(AngularDamping));
            physics.AddElement("linear_dampening", N(LinearDamping));
            physics.AddElement("restitution", N(part.Restitution));
            physics.AddElement("radius", N(part.Radius));
            physics.AddElement("mass_pct", N(part.Kilograms / GuerrillaKilograms));
            physics.AddElement("recovery_scalar", N(part.Recovery));
            var flags = e.AddElement("flags");
            foreach (string flag in part.Flags) flags.AddElement("Flag", flag);
            e.AddElement("_Editor").AddElement("Category", "Entries");
        }
        return System.Text.Encoding.Latin1.GetBytes(XtblXml.Write(document).TrimStart());
    }

    static void Axis(XtblElement element, (float X, float Y, float Z) axis)
    {
        element.AddElement("X", N(axis.X));
        element.AddElement("Y", N(axis.Y));
        element.AddElement("Z", N(axis.Z));
    }

    static string N(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
