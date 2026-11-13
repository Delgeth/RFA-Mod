using System.Numerics;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's constraints in its zones (the joints between two objects that Havok holds together), as
/// Armageddon's zone objects, for the world builder. So far its hinges, with the motors that turn some of them: the
/// wind turbines' blades (19, turning on their hub at Guerrilla's speed), the big mining machine's blade, and the
/// cranes' hanging parts (72, swinging up to 70° each way).
///
/// Both games have the class and the code that makes it (Armageddon's is Guerrilla's): a constraint names its host
/// and its child (host_handle, child_handle, and which of their pieces, host_index and child_index, -1 for the
/// object itself), its place (op), and a template: a type (1 a hinge, 2 a point, 4 a motor), each body's frame,
/// a breaking threshold (-1 never), then the type's own data (Guerrilla's constraint_template, 156 bytes).
///
/// The two games differ in the motor. Guerrilla's is a constraint of its own (type 4) whose child is the hinge it
/// turns, its speed and gain the first two values of its data (+0x7C, +0x80); its constraint::stream (RM 0xA39A80)
/// adds a Havok velocity motor to that hinge (havok_constraint_add_motor, RM 0x817E10). Armageddon has no motor
/// constraint: its template is 8 bytes longer (164), with a hinge's own motor speed and gain at +0x7C and +0x80 and
/// the type's data after them (+0x84), and its constraint::stream (0x7BF3A0, at 0x7BF817-0x7BF876) adds the same
/// velocity motor (0x777C50, Guerrilla's function) when both are above 0. Its own hinges have -1 and -1 there. So a
/// hinge here is Guerrilla's template with its motor's speed and gain put in at +0x7C (-1, -1 for one without), and
/// Guerrilla's motor constraints aren't placed.
///
/// Written as Armageddon's own (world_14_05's catwalks, and Guerrilla's the same way): a child of the object it
/// holds, first in its list of children, after both objects in the zone; flags 0 (the high byte of Guerrilla's is
/// its tools'); its properties in Volition's tools' order for Armageddon; the handle Guerrilla's unless taken. A
/// constraint whose host or child isn't placed is left out.
///
/// The wind turbines also get what Guerrilla's turbine code does when their blades load
/// (windmill_blade_stream_in_callback, RM 0xA52380): the blades turned to a random angle about their own z axis,
/// round the hinge's place, so the turbines don't all turn in step. Here that is done once, in the zone (the same
/// angle every load, from the blades' handle), which keeps the hinge's frames as they are: the hinge's place and
/// axis stay where they were in the blades' own frame. And each turbine's four objects (tower, blades, motor,
/// hinge) are listed for the guerrilla_world mod, which plays the rest of Guerrilla's turbine code
/// (<see cref="TurbineList"/>).
///
/// Not yet: Guerrilla's 74 point constraints (type 2), which hold its banners (items) to buildings: they come with
/// the banners and their cloth.
/// </summary>
public static class GuerrillaConstraints
{
    /// <summary>A wind turbine's objects, by handle (the motor <see cref="None"/> if not placed).</summary>
    public sealed record Turbine(uint Tower, uint Blades, uint Motor, uint Hinge);

    /// <summary>
    /// The file in mods\guerrilla_world\ listing the turbines, a line each: the zone, then the tower's, blades',
    /// motor's and hinge's handles in hex.
    /// </summary>
    public const string TurbineList = "turbines.txt";

    /// <summary>Guerrilla's turbine parts, by their level object (the "props" of Guerrilla's zone objects).</summary>
    const string TurbineBlades = "0101windmill_a_blades", TurbineMotor = "0101windmill_a_motor";
    const uint None = 0xFFFFFFFF;
    static readonly uint Constraint = NameHashes.ClassHash("constraint");
    static readonly uint RfgMover = NameHashes.ClassHash("rfg_mover"), GeneralMover = NameHashes.ClassHash("general_mover");

    /// <summary>The order Volition's tools wrote a constraint's properties in for Armageddon (its own zones).</summary>
    static readonly string[] Order = ["host_handle", "host_index", "op", "child_handle", "child_index", "template"];

    /// <summary>The template's types placed (or read): a hinge, and Guerrilla's motor that turns one.</summary>
    public const uint Hinge = 1, Motor = 4;

    /// <summary>Where a template's motor speed and gain are (Armageddon's hinge; the first values of Guerrilla's motor's data).</summary>
    public const int MotorAt = 0x7C;

    /// <summary>The template's size in Guerrilla (constraint_template) and in Armageddon.</summary>
    public const int GuerrillaTemplateSize = 156, TemplateSize = 164;

    /// <summary>How many constraints the game holds at once (its constraint pool; Guerrilla's 512).</summary>
    public const int MostConstraints = 850;

    /// <summary>A template's type (its first value), or null if it has none.</summary>
    public static uint? TypeOf(byte[]? template) => template is { Length: >= 4 } ? BitConverter.ToUInt32(template, 0) : null;

    static uint? HandleIn(GuerrillaZoneObject o, string name) => o.Value(name) is { Length: 4 } v ? BitConverter.ToUInt32(v, 0) : null;

    /// <summary>
    /// A zone's hinges as Armageddon's objects (handles not in <paramref name="taken"/>, which gains theirs), each on
    /// the objects it joins among <paramref name="placed"/> (the zone file's objects so far: its child's list of
    /// children gains it); one whose host or child isn't placed is left out, counted in <paramref name="leftOut"/>.
    /// <paramref name="motors"/> counts those given a motor; <paramref name="turbines"/> gains the zone's wind
    /// turbines, whose blades are turned to their own angle.
    /// </summary>
    public static List<ZoneObject> Convert(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlyList<ZoneObject> placed, ISet<uint> taken,
                                           out int leftOut, out int motors, List<Turbine> turbines)
    {
        // The movers placed, by Guerrilla's handle (the same object: its number too).
        var movers = placed.Where(o => o.ClassHash == RfgMover || o.ClassHash == GeneralMover).GroupBy(o => o.Handle).ToDictionary(g => g.Key, g => g.First());
        ZoneObject? Placed(uint? handle) =>
            handle is uint h && movers.TryGetValue(h, out var m) && zone.TryGetValue(h, out var g) && g.Number == m.Number ? m : null;

        // Guerrilla's motors, by the hinge they turn: their speed and gain.
        var motorOf = new Dictionary<uint, (float Speed, float Gain)>();
        foreach (var motor in zone.Values.Where(o => o.ClassHash == Constraint && TypeOf(o.Value("template")) == Motor))
            if (HandleIn(motor, "child_handle") is uint hinge && motor.Value("template") is { Length: GuerrillaTemplateSize } t)
                motorOf[hinge] = (BitConverter.ToSingle(t, MotorAt), BitConverter.ToSingle(t, MotorAt + 4));

        leftOut = 0;
        motors = 0;
        var made = new List<ZoneObject>();
        foreach (var hinge in zone.Values.Where(o => o.ClassHash == Constraint && TypeOf(o.Value("template")) == Hinge).OrderBy(o => o.Number))
        {
            var host = Placed(HandleIn(hinge, "host_handle"));
            var child = Placed(HandleIn(hinge, "child_handle"));
            if (host == null || child == null || hinge.Value("template") is not { Length: GuerrillaTemplateSize } guerrilla ||
                Order.Any(name => hinge.Value(name) == null) || hinge.Value("op") is not { Length: 48 })
            {
                leftOut++;
                continue;
            }
            // Armageddon's template: Guerrilla's to its data, the motor's speed and gain (none: -1), then its data.
            var template = new byte[TemplateSize];
            Array.Copy(guerrilla, 0, template, 0, MotorAt);
            var (speed, gain) = motorOf.TryGetValue(hinge.Handle, out var m) ? m : (-1f, -1f);
            if (speed > 0 && gain > 0) motors++;
            BitConverter.TryWriteBytes(template.AsSpan(MotorAt), speed);
            BitConverter.TryWriteBytes(template.AsSpan(MotorAt + 4), gain);
            Array.Copy(guerrilla, MotorAt, template, MotorAt + 8, GuerrillaTemplateSize - MotorAt);

            uint handle = hinge.Handle;
            while (!taken.Add(handle)) handle = (handle & 0xFFFF0000) | ((handle + 1) & 0xFFFF);
            var properties = Order.Select(name =>
            {
                var (type, raw) = hinge.Properties[NameHashes.PropertyHash(name)];
                byte[] value = name switch
                {
                    "template" => template,
                    "host_handle" => BitConverter.GetBytes(host.Handle),
                    "child_handle" => BitConverter.GetBytes(child.Handle),
                    _ => raw,
                };
                return new NewProperty(type, NameHashes.PropertyHash(name), value);
            });
            var obj = ZoneObject.Create(Constraint, handle, hinge.Number, hinge.BoundsMin, hinge.BoundsMax, [.. properties], 0);
            obj.Parent = child.Handle;
            obj.Sibling = child.Child;
            obj.Child = None;
            child.Child = obj.Handle;
            made.Add(obj);

            // A wind turbine: its blades turned to their own angle, and its objects listed.
            uint childHandle = HandleIn(hinge, "child_handle")!.Value, hostHandle = HandleIn(hinge, "host_handle")!.Value;
            if (zone[childHandle].Text("props") is { } props && props.Equals(TurbineBlades, StringComparison.OrdinalIgnoreCase))
            {
                byte[] op = hinge.Value("op")!;
                var hub = new Vector3(BitConverter.ToSingle(op, 0), BitConverter.ToSingle(op, 4), BitConverter.ToSingle(op, 8));
                TurnAboutHub(child, hub, (float)(new Random(unchecked((int)child.Handle)).NextDouble() * 2 * Math.PI));
                var guerrillaMotor = zone.Values.FirstOrDefault(o => o.ClassHash == GeneralMover && o.Parent == hostHandle &&
                                                                     string.Equals(o.Text("props"), TurbineMotor, StringComparison.OrdinalIgnoreCase));
                turbines.Add(new Turbine(host.Handle, child.Handle, Placed(guerrillaMotor?.Handle)?.Handle ?? None, obj.Handle));
            }
        }
        return made;
    }

    /// <summary>
    /// Turns an object about its own z axis by <paramref name="angle"/>, round <paramref name="hub"/> (a point that
    /// stays where it is), as Guerrilla's rotate_around_local_z and its moving of the blades about the hinge do.
    /// </summary>
    static void TurnAboutHub(ZoneObject turned, Vector3 hub, float angle)
    {
        if (!turned.CanTurn) return;
        var (position, rotation) = turned.Placement;
        var newRotation = Matrix4x4.CreateRotationZ(angle) * rotation;
        Matrix4x4.Invert(rotation, out var undo);
        Vector3 offset = Vector3.Transform(position - hub, undo * newRotation);
        turned.TurnTo(newRotation);
        turned.MoveTo(hub + offset);
    }
}
