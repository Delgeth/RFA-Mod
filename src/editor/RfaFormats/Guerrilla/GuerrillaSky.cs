using System.Numerics;
using System.Text;
using RfaFormats.Meshes;
using RfaFormats.Textures;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's clouds and far backdrop for the guerrilla_world mod, which draws them over
/// its sky: clouds.bin and the textures as DDS, in the mod's folder (mods\guerrilla_world\); its star
/// catalogue, stars.bin (<see cref="WriteStars"/>); and the sun's corona, corona.bin (<see cref="WriteCorona"/>).
///
/// The cloud model is the one both games ship in skybox.vpp_pc (skybox_clouds; Armageddon's copy is
/// read, Guerrilla's is an older mesh version): an overhead dome (radius 13) and a ring round the
/// horizon (radius 16), each with two textures of two cloud layers, and a backdrop ring (radius 18,
/// one texture, vfx_sky_backdrop_01: the far horizon). The textures are Guerrilla's own (the cloud
/// layers from its skybox.vpp_pc; the backdrop, the sun's and the moons' from its misc.vpp_pc &gt;
/// always_loaded), decoded and saved as 32-bit DDS with as many mip levels as Guerrilla's (made
/// again), so they aren't compressed twice; the mod reads them as sRGB colour, as Guerrilla does.
///
/// clouds.bin (little-endian):
///   "GWCLOUD1"
///   vertex count, then each vertex: position (3 floats, the model's own: around the eye), normal (3),
///     texture coordinates (2; 1 = the texture's width)
///   part count, then each part: first index, index count, kind (0 overhead clouds, 1 clouds along
///     the horizon, 2 the backdrop: which shader and time of day curves), the height its clouds fade
///     in from and are full at (the up part of the look direction), its layers' strengths as the
///     model gives them (4 floats), its two textures' names (64 bytes each, NUL-padded, without .tga;
///     the backdrop has one, the second empty)
///   index count, then the indices (32-bit), triangle lists
/// </summary>
public static class GuerrillaSky
{
    static readonly string[] FromAlwaysLoaded = ["vfx_sky_backdrop_01", "vfx_sun_01", "deimos_d", "phobos_d"];

    /// <summary>Writes clouds.bin and the textures into <paramref name="output"/>, from the two games' cache folders (build\pc\cache).</summary>
    public static void Write(string armageddonCache, string guerrillaCache, string output, Action<string> log)
    {
        Packfile Inner(string archive, string container)
        {
            var outer = Packfile.Open(archive);
            return Packfile.FromBytes(container, outer.Read(outer.Find(container) ?? throw new InvalidDataException($"no {container} in {archive}")));
        }
        var ours = Inner(Path.Combine(armageddonCache, "skybox.vpp_pc"), "skybox_clouds.csmesh_pc.str2_pc");
        var theirs = Inner(Path.Combine(guerrillaCache, "skybox.vpp_pc"), "skybox_clouds.csmesh_pc.str2_pc");
        var model = ModelFile.Read("skybox_clouds.csmesh_pc", ours.Read(ours.Find("skybox_clouds.csmesh_pc")!), ours.Read(ours.Find("skybox_clouds.gsmesh_pc")!));
        var peg = PegFile.Read(theirs.Read(theirs.Find("skybox_clouds.cpeg_pc")!), theirs.Read(theirs.Find("skybox_clouds.gpeg_pc")!));
        Directory.CreateDirectory(output);

        var misc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        var always = PegFile.Read(misc.Read(misc.Find("always_loaded.cpeg_pc")!), misc.Read(misc.Find("always_loaded.gpeg_pc")!));
        foreach (var (pegFile, texture) in peg.Textures.Select(t => (peg, t))
                     .Concat(always.Textures.Where(t => FromAlwaysLoaded.Contains(Path.GetFileNameWithoutExtension(t.Name), StringComparer.OrdinalIgnoreCase)).Select(t => (always, t))))
        {
            // With as many mip levels as Guerrilla's own (its cloud layers and backdrop 1, the sun and
            // moons 4), made again from the picture.
            var image = pegFile.Decode(texture);
            int mips = Math.Clamp(texture.MipLevels, 1, 1 + (int)Math.Log2(Math.Min(image.Width, image.Height)));
            var dds = new DdsImage(TextureFormat.Argb8888, image.Width, image.Height, mips, TextureCodec.EncodeWithMips(TextureFormat.Argb8888, image, mips));
            File.WriteAllBytes(Path.Combine(output, Path.GetFileNameWithoutExtension(texture.Name) + ".dds"), dds.ToBytes());
        }

        // The two cloud parts (the materials whose textures are cloud layers) and the backdrop.
        var parts = new List<(int First, int Count, int Kind, float Fade, float Full, float[] Strengths, string Layer01, string Layer23)>();
        var indices = new List<int>();
        foreach (var submesh in model.Submeshes)
            foreach (var part in submesh.Parts)
            {
                var material = model.Materials[part.Material];
                string? layer01 = material.Texture("Layer01_map"), layer23 = material.Texture("Layer23_map");
                if (layer01 == null || layer23 == null)
                {
                    if (material.Texture("Diffuse_map") is not { } backdrop) continue;
                    parts.Add((indices.Count, part.Triangles.Length, 2, 0, 1, [1, 1, 1, 1], Path.GetFileNameWithoutExtension(backdrop), ""));
                    indices.AddRange(part.Triangles);
                    continue;
                }
                int kind = layer01.Contains("overhead", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                var strengths = material.Constant("Layer_strengths") ?? new Vector4(1);
                parts.Add((indices.Count, part.Triangles.Length, kind, material.Constant("Cloud_Fade_Height")?.X ?? 0, material.Constant("Cloud_Full_Height")?.X ?? 1,
                           [strengths.X, strengths.Y, strengths.Z, strengths.W], Path.GetFileNameWithoutExtension(layer01), Path.GetFileNameWithoutExtension(layer23)));
                indices.AddRange(part.Triangles);
            }
        var file = new MemoryStream();
        var w = new BinaryWriter(file);
        w.Write(Encoding.ASCII.GetBytes("GWCLOUD1"));
        w.Write(model.Positions.Length);
        for (int v = 0; v < model.Positions.Length; v++)
        {
            w.Write(model.Positions[v].X); w.Write(model.Positions[v].Y); w.Write(model.Positions[v].Z);
            w.Write(model.Normals[v].X); w.Write(model.Normals[v].Y); w.Write(model.Normals[v].Z);
            w.Write(model.TextureCoordinates[v].X); w.Write(model.TextureCoordinates[v].Y);
        }
        w.Write(parts.Count);
        foreach (var p in parts)
        {
            w.Write(p.First); w.Write(p.Count); w.Write(p.Kind); w.Write(p.Fade); w.Write(p.Full);
            foreach (float f in p.Strengths) w.Write(f);
            foreach (string name in new[] { p.Layer01, p.Layer23 })
            {
                byte[] fixedName = new byte[64];
                Encoding.ASCII.GetBytes(name).CopyTo(fixedName, 0);
                w.Write(fixedName);
            }
        }
        w.Write(indices.Count);
        foreach (int i in indices) w.Write(i);
        File.WriteAllBytes(Path.Combine(output, "clouds.bin"), file.ToArray());
        log($"Clouds and backdrop: {parts.Count} parts, {indices.Count / 3} triangles, {peg.Textures.Count + FromAlwaysLoaded.Length} textures");
    }

    /// <summary>A corona's numbers in corona.bin, in this order: its key lists in Guerrilla's effect
    /// (32-bit, version 26: a corona 0x108 bytes, from +0x4C a count and an offset for each list, 8 bytes
    /// apart, in this order of the template's), each taken at its first key.</summary>
    static readonly int[] CoronaNumbers =
    [
        2, 3, 14, 15,     // radius start, end; the distances they go between
        16, 17, 21, 22,   // the distances the colour goes between; opacity's and radius's visibility decay
        4, 5, 8, 9,       // width scale, visibility radius, position offset, saturation
        10, 11, 12, 13,   // rotation scale, intensity scale, intensity's decay, opacity's decay
        19, 20,           // view fade scale, view fade decay
    ];
    const int CoronaSize = 0x108;

    /// <summary>
    /// Writes corona.bin and its textures into <paramref name="output"/>: the corona effect of each of
    /// Guerrilla's orbitals that has one (its terr01.gtodx gives the Sun env_sun_corona_a), which
    /// Armageddon never plays, so the guerrilla_world mod draws it as Guerrilla's corona shader does.
    /// The effect (in Guerrilla's effects.vpp_pc) is a few coronas, each a texture laid over the picture
    /// round the orbital with its own size, colour and fades, every one of them keyed once; its textures
    /// are in Guerrilla's effects.vpp_pc (vfx_containers) or misc.vpp_pc (always_loaded), saved as DDS.
    ///
    /// corona.bin (little-endian): "GWCORON1", the corona count, then each corona: the orbital's name
    /// and the effect's (32 bytes each, NUL-padded), its texture's name (64 bytes, without .tga), its
    /// type (0 laid on the picture, 1 in the world), whether its view fade is on for intensity, opacity
    /// and radius (3 bytes and one more, 0), its rotation offset (degrees), its colour at the start and
    /// at the end (4 floats each), then the 18 numbers of <see cref="CoronaNumbers"/>.
    /// </summary>
    public static void WriteCorona(string guerrillaCache, string output, Action<string> log)
    {
        var misc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        var effects = Packfile.Open(Path.Combine(guerrillaCache, "effects.vpp_pc"));
        string tod = Encoding.Latin1.GetString(misc.Read(misc.Find("terr01.gtodx") ?? throw new InvalidDataException("no terr01.gtodx in misc.vpp_pc")));
        var file = new MemoryStream();
        var w = new BinaryWriter(file);
        void Name(string name, int size)
        {
            byte[] fixedName = new byte[size];
            Encoding.ASCII.GetBytes(name).AsSpan(0, Math.Min(name.Length, size - 1)).CopyTo(fixedName);
            w.Write(fixedName);
        }
        var coronas = new List<Action>();
        var textures = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match orbital in System.Text.RegularExpressions.Regex.Matches(tod, "<orbital>(.*?)</orbital>", System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            string Tag(string tag) => System.Text.RegularExpressions.Regex.Match(orbital.Groups[1].Value, $"<{tag}>([^<]*)</{tag}>").Groups[1].Value.Trim();
            string orbitalName = Tag("name"), effect = Tag("effect_name");
            if (effect.Length == 0) continue;
            var container = effects.Find(effect + ".cefct_pc.str2_pc");
            var inner = container == null ? null : effects.OpenContainer(container);
            var entry = inner?.Find(effect + ".cefct_pc");
            if (inner == null || entry == null)
            {
                log($"Corona: no {effect} in Guerrilla's effects.vpp_pc; {orbitalName} gets none");
                continue;
            }
            byte[] c = inner.Read(entry);
            uint U(int at) => BitConverter.ToUInt32(c, at);
            int I(int at) => BitConverter.ToInt32(c, at);
            float F(long at) => BitConverter.ToSingle(c, (int)at);
            string Text(long at) { int end = Array.IndexOf(c, (byte)0, (int)at); return Encoding.ASCII.GetString(c, (int)at, end - (int)at); }
            if (U(4) != 26) log($"Corona: {effect} is effect version {U(4)}, not 26; read as 26");
            int bitmapCount = I(0x1C);
            uint bitmaps = U(0x20);
            int count = I(0x48);
            uint first = U(0x4C);
            for (int k = 0; k < count; k++)
            {
                int at = (int)first + k * CoronaSize;
                int material = at + 4;
                int textureIndex = BitConverter.ToInt16(c, material + 6) > 0 ? I((int)U(material + 8) + 4) : -1;
                string texture = textureIndex >= 0 && textureIndex < bitmapCount ? Path.GetFileNameWithoutExtension(Text(U((int)bitmaps + textureIndex * 4))) : "";
                float Key(int list, int valueAt)  // (a float key: its time, its value; a colour key: its time, then at +16 its four)
                {
                    int keys = I(at + 0x4C + list * 8);
                    if (keys > 1) log($"Corona: {effect} {Text(U(at))} has {keys} keys in list {list}; its first used");
                    return keys > 0 ? F(U(at + 0x50 + list * 8) + valueAt) : 0;
                }
                string coronaName = Text(U(at));
                int type = I(at + 0x34);
                byte[] viewFade = [c[at + 0x104], c[at + 0x105], c[at + 0x106], 0];
                float rotationOffset = I(at + 0x3C) > 0 ? F(U(at + 0x38) + 4) : 0;
                float[] colourStart = [.. Enumerable.Range(0, 4).Select(i => Key(6, 16 + i * 4))];
                float[] colourEnd = [.. Enumerable.Range(0, 4).Select(i => Key(7, 16 + i * 4))];
                float[] numbers = [.. CoronaNumbers.Select(list => Key(list, 4))];
                if (texture.Length > 0) textures.Add(texture);
                coronas.Add(() =>
                {
                    Name(orbitalName, 32); Name(effect, 32); Name(texture, 64);
                    w.Write(type); w.Write(viewFade); w.Write(rotationOffset);
                    foreach (float f in colourStart) w.Write(f);
                    foreach (float f in colourEnd) w.Write(f);
                    foreach (float f in numbers) w.Write(f);
                });
                log($"Corona: {orbitalName}'s {effect}: {coronaName}, {texture}, type {type}");
            }
        }
        // The textures, as the sky's (with Guerrilla's own mip levels, made again).
        var always = PegFile.Read(misc.Read(misc.Find("always_loaded.cpeg_pc")!), misc.Read(misc.Find("always_loaded.gpeg_pc")!));
        var vfx = PegFile.Read(effects.Read(effects.Find("vfx_containers.cpeg_pc")!), effects.Read(effects.Find("vfx_containers.gpeg_pc")!));
        foreach (string name in textures)
        {
            var found = new[] { always, vfx }.SelectMany(p => p.Textures.Select(t => (Peg: p, Texture: t)))
                .FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Texture.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (found.Peg == null)
            {
                log($"Corona: no texture {name} in Guerrilla's always_loaded or vfx_containers; that corona draws nothing");
                continue;
            }
            var image = found.Peg.Decode(found.Texture);
            int mips = Math.Clamp(found.Texture.MipLevels, 1, 1 + (int)Math.Log2(Math.Min(image.Width, image.Height)));
            var dds = new DdsImage(TextureFormat.Argb8888, image.Width, image.Height, mips, TextureCodec.EncodeWithMips(TextureFormat.Argb8888, image, mips));
            File.WriteAllBytes(Path.Combine(output, name + ".dds"), dds.ToBytes());
        }
        w.Write(Encoding.ASCII.GetBytes("GWCORON1"));
        w.Write(coronas.Count);
        foreach (var write in coronas) write();
        File.WriteAllBytes(Path.Combine(output, "corona.bin"), file.ToArray());
        log($"Corona: {coronas.Count} coronas, {textures.Count} textures (corona.bin)");
    }

    // Guerrilla's star catalogue begins with its brightest star: right ascension, declination
    // (radians) and brightness, as floats.
    static readonly float[] FirstStar = [-3.7335f, -0.3348f, 0.2679f];
    const int FewestStars = 1000;

    /// <summary>
    /// Writes stars.bin into <paramref name="output"/>: Guerrilla's star catalogue, which its star field
    /// draws the night sky's stars from. The catalogue isn't in Guerrilla's data files but in its exe
    /// (RFG.exe in <paramref name="guerrilla"/>, Guerrilla's folder; the Steam Edition and
    /// Re-Mars-tered hold the same one): the stars' right ascension, declination (radians) and
    /// brightness, three floats each, brightest first. It is found by its first star and read for as
    /// long as the entries are stars (right ascension −2π to 0, declination −π/2 to π/2, brightness
    /// above 0 and no brighter than the one before). Without it the mod draws no stars (logged).
    ///
    /// stars.bin (little-endian): "GWSTARS1", the star count, then each star's three floats.
    /// </summary>
    public static void WriteStars(string guerrilla, string output, Action<string> log)
    {
        string? exe = Directory.Exists(guerrilla)
            ? Directory.EnumerateFiles(guerrilla, "*.exe").FirstOrDefault(f => Path.GetFileName(f).Equals("RFG.exe", StringComparison.OrdinalIgnoreCase))
            : null;
        if (exe == null)
        {
            log($"Stars: no RFG.exe in {guerrilla}; the night sky gets no stars");
            return;
        }
        byte[] data = File.ReadAllBytes(exe);
        byte[] first = new byte[12];
        for (int i = 0; i < 3; i++) BitConverter.GetBytes(FirstStar[i]).CopyTo(first, i * 4);
        int at = data.AsSpan().IndexOf(first);
        var stars = new List<(float Ra, float Dec, float Brightness)>();
        for (int offset = at; at >= 0 && offset + 12 <= data.Length; offset += 12)
        {
            float ra = BitConverter.ToSingle(data, offset), dec = BitConverter.ToSingle(data, offset + 4), brightness = BitConverter.ToSingle(data, offset + 8);
            bool star = ra >= -2 * MathF.PI && ra <= 0 && dec >= -MathF.PI / 2 && dec <= MathF.PI / 2 && brightness > 0 &&
                        (stars.Count == 0 || brightness <= stars[^1].Brightness);
            if (!star) break;
            stars.Add((ra, dec, brightness));
        }
        if (stars.Count < FewestStars)
        {
            log($"Stars: Guerrilla's star catalogue isn't in {exe}; the night sky gets no stars");
            return;
        }
        var file = new MemoryStream();
        var w = new BinaryWriter(file);
        w.Write(Encoding.ASCII.GetBytes("GWSTARS1"));
        w.Write(stars.Count);
        foreach (var (ra, dec, brightness) in stars)
        {
            w.Write(ra); w.Write(dec); w.Write(brightness);
        }
        File.WriteAllBytes(Path.Combine(output, "stars.bin"), file.ToArray());
        log($"Stars: Guerrilla's star catalogue, {stars.Count:N0} stars (from its exe; stars.bin)");
    }
}
