using System.Numerics;
using RfaFormats.Meshes;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Moves people's animations between Guerrilla's player's skeleton (Mason's, mason.rig_pc, 53 bones) and
/// Armageddon's (Darius's, player_v2.rig_pc, 137): Armageddon's own onto Mason (<see cref="ToMason"/>),
/// so a player on Mason's skeleton keeps Armageddon's moves (dodging, free running, the Nano Forge, its
/// guns) beside Guerrilla's; and Guerrilla's onto Darius (<see cref="ToDarius"/>), for Guerrilla's guns
/// and vehicles played by Darius.
///
/// How each of Mason's bones follows Darius's is learnt, each time it is built, from the player animations
/// Armageddon's own developers moved from Guerrilla onto Darius: Armageddon's anims.vpp_pc has Guerrilla's
/// clips under Guerrilla's names (plyr_lddr_*, plyr_rfl_* ...), so each is a pair of the same movement
/// on both skeletons. For each of Mason's bones a bone of Darius's is named (<see cref="Sources"/>; where
/// a few are possible, the one the pairs fit best) and one constant turn found, in the bone's own frame,
/// that takes the Darius bone's turn in the world to the Mason bone's: the two skeletons' rest poses point
/// their bones differently, and what Armageddon's developers changed is learnt with them. Two things the
/// pairs show that a plain bone-for-bone copy gets wrong: Mason's root is his hips (it turns with them),
/// Darius's root never turns (his hips are his Pelvis, his spine hangs from his root); and Darius's upper
/// arm is Mason's second upper-arm twist, not his first.
///
/// Each frame is posed on Darius, and each of Mason's bones turned as its Darius bone (and its constant),
/// written back as a turn from its parent; the hips move as Darius's (his movement from his rest, scaled by
/// the two hip heights); the hand props (what a hand holds a gun by) sit where Darius's are, from the hand.
/// Bones whose Darius bone the animation doesn't move are left out, so an animation played over another
/// moves only its own part, each bone with its Darius bone's weight curve; the character's own movement
/// (the root track) is kept. An additive animation (Armageddon's flag Additive) gives each bone its Darius
/// bone's turns, carried into the bone's own frame.
/// </summary>
public sealed class PlayerRetarget
{
    /// <summary>
    /// Each of Mason's bones and the bones of Darius's it may follow, best first (the pairs choose among
    /// several). Mason's bones not named keep their rest pose on their parent (his camera, the spine-bend
    /// bone the game turns itself).
    /// </summary>
    static readonly (string Mason, string[] Darius)[] Sources =
    [
        ("root", ["Pelvis"]), ("pelvis", ["Pelvis"]),
        ("spine", ["Spine"]), ("spine1", ["Spine1"]), ("spine2", ["Spine2"]), ("neck", ["Neck"]), ("head", ["Head"]),
        ("l-thigh", ["L-Thigh"]), ("l-calf", ["L-Calf"]), ("l-foot", ["L-Foot"]), ("l-toe0", ["L-Toe"]),
        ("r-thigh", ["R-Thigh"]), ("r-calf", ["R-Calf"]), ("r-foot", ["R-Foot"]), ("r-toe0", ["R-Toe"]),
        ("l-clavicle", ["L-clavicle"]), ("r-clavicle", ["r-clavicle"]),
        ("l-upperarmtwist1", ["L-upperarm-twst1", "l-upperarm"]), ("l-upperarmtwist2", ["l-upperarm", "L-upperarm-twst1"]),
        ("l-foretwist", ["l-forearm", "L-forearm-twst1"]), ("l-foretwist1", ["L-forearm-twst2", "L-forearm-twst1", "l-forearm"]),
        ("r-upperarmtwist1", ["r-upperarm-twst1", "r-upperarm"]), ("r-upperarmtwist2", ["r-upperarm", "r-upperarm-twst1"]),
        ("r-foretwist", ["r-forearm", "r-forearm-twst1"]), ("r-foretwist1", ["r-foerarm-twst2", "r-forearm-twst1", "r-forearm"]),
        ("l-hand", ["L-Hand"]), ("r-hand", ["R-Hand"]), ("l-handprop", ["item2"]), ("r-handprop", ["item1"]),
        ("l-finger1", ["L-fing-idx1"]), ("l-finger2", ["L-fing-mid1"]), ("l-finger3", ["L-fing-ring1"]), ("l-finger4", ["l-fing-pinky1"]), ("l-thumb1", ["L-fing-thmb1"]),
        ("l-finger11", ["L-fing-idx2"]), ("l-finger21", ["L-fing-mid2"]), ("l-finger31", ["L-fing-ring2"]), ("l-finger41", ["L-fing-pinky2"]), ("l-thumb11", ["L-fing-thmb2"]),
        ("r-finger1", ["R-fing-idx1"]), ("r-finger2", ["R-fing-mid1"]), ("r-finger3", ["R-fing-ring1"]), ("r-finger4", ["R-fing-pinky1"]), ("r-thumb1", ["R-fing-thmb1"]),
        ("r-finger11", ["R-fing-idx2"]), ("r-finger21", ["R-fing-mid2"]), ("r-finger31", ["R-fing-ring2"]), ("r-finger41", ["R-fing-pinky2"]), ("r-thumb11", ["R-fing-thmb2"]),
        ("l-eye", ["face-l-eye"]), ("r-eye", ["face-r-eye"]),
    ];

    /// <summary>
    /// Each of Darius's bones Armageddon's developers turn in their versions of Guerrilla's animations, and
    /// the bones of Mason's it may follow. The rest (his root, which they only move; his muscle helpers,
    /// the deltoids, glutes and biceps, which the game turns itself; his face) keep their rest pose on their parent.
    /// </summary>
    static readonly (string Darius, string[] Mason)[] ForwardSources =
    [
        ("Pelvis", ["root", "pelvis"]),
        ("Spine", ["spine"]), ("Spine1", ["spine1"]), ("Spine2", ["spine2"]), ("Neck", ["neck"]), ("Head", ["head"]),
        ("L-Thigh", ["l-thigh"]), ("L-Calf", ["l-calf"]), ("L-Foot", ["l-foot"]), ("L-Toe", ["l-toe0"]), ("l-thigh-twst1", ["l-thigh"]),
        ("R-Thigh", ["r-thigh"]), ("R-Calf", ["r-calf"]), ("R-Foot", ["r-foot"]), ("R-Toe", ["r-toe0"]), ("r-thigh-twst1", ["r-thigh"]),
        ("l-satchel", ["root", "pelvis"]), ("r-satchel", ["root", "pelvis"]),
        ("L-clavicle", ["l-clavicle"]), ("r-clavicle", ["r-clavicle"]),
        ("l-upperarm", ["l-upperarmtwist2", "l-upperarmtwist1"]), ("L-upperarm-twst1", ["l-upperarmtwist1", "l-upperarmtwist2"]),
        ("l-forearm", ["l-foretwist", "l-foretwist1"]), ("L-forearm-twst1", ["l-foretwist", "l-foretwist1"]), ("L-forearm-twst2", ["l-foretwist1", "l-foretwist"]),
        ("r-upperarm", ["r-upperarmtwist2", "r-upperarmtwist1"]), ("r-upperarm-twst1", ["r-upperarmtwist1", "r-upperarmtwist2"]),
        ("r-forearm", ["r-foretwist", "r-foretwist1"]), ("r-forearm-twst1", ["r-foretwist", "r-foretwist1"]), ("r-foerarm-twst2", ["r-foretwist1", "r-foretwist"]),
        ("L-Hand", ["l-hand"]), ("R-Hand", ["r-hand"]), ("item2", ["l-handprop"]), ("item1", ["r-handprop"]),
        ("L-fing-idx1", ["l-finger1"]), ("L-fing-mid1", ["l-finger2"]), ("L-fing-ring1", ["l-finger3"]), ("l-fing-pinky1", ["l-finger4"]), ("L-fing-thmb1", ["l-thumb1"]),
        ("L-fing-idx2", ["l-finger11"]), ("L-fing-mid2", ["l-finger21"]), ("L-fing-ring2", ["l-finger31"]), ("L-fing-pinky2", ["l-finger41"]),
        ("L-fing-thmb2", ["l-thumb11"]), ("L-fing-thmb3", ["l-thumb11"]),
        ("R-fing-idx1", ["r-finger1"]), ("R-fing-mid1", ["r-finger2"]), ("R-fing-ring1", ["r-finger3"]), ("R-fing-pinky1", ["r-finger4"]), ("R-fing-thmb1", ["r-thumb1"]),
        ("R-fing-idx2", ["r-finger11"]), ("R-fing-mid2", ["r-finger21"]), ("R-fing-ring2", ["r-finger31"]), ("R-fing-pinky2", ["r-finger41"]),
        ("R-fing-thmb2", ["r-thumb11"]), ("R-fing-thmb3", ["r-thumb11"]),
        ("face-l-eye", ["l-eye"]), ("face-r-eye", ["r-eye"]),
    ];

    /// <summary>Mason's hand props and the hands they hang from (placed from the hand as Darius's item bones are).</summary>
    static readonly (string Prop, string Hand, string DariusProp, string DariusHand)[] Props =
        [("r-handprop", "r-hand", "item1", "R-Hand"), ("l-handprop", "l-hand", "item2", "L-Hand")];

    /// <summary>The share of the pairs' poses each constant is averaged over: the ones it fits best (a few pairs Armageddon's developers re-made).</summary>
    const float Kept = 0.75f;

    readonly Rig darius, mason;
    readonly int[] source;                 // Mason bone -> its Darius bone (-1: none)
    readonly Matrix4x4[] constant;         // Mason bone -> its constant turn
    readonly int masonHips, dariusHips;
    readonly float hipScale;
    readonly string?[] forwardSource;      // Darius bone -> the name of its Mason bone (null: none)
    readonly Matrix4x4[] forwardConstant;  // Darius bone -> its constant turn
    readonly List<string> report = [];

    /// <summary>How each bone was fitted: its Darius bone and how far the pairs are from it (a line each).</summary>
    public IReadOnlyList<string> Report => report;

    /// <summary>The pairs found and the poses fitted on.</summary>
    public int Pairs { get; }

    PlayerRetarget(Rig darius, Rig mason, int pairs)
    {
        (this.darius, this.mason, Pairs) = (darius, mason, pairs);
        source = Enumerable.Repeat(-1, mason.Bones.Count).ToArray();
        constant = Enumerable.Repeat(Matrix4x4.Identity, mason.Bones.Count).ToArray();
        forwardSource = new string?[darius.Bones.Count];
        forwardConstant = Enumerable.Repeat(Matrix4x4.Identity, darius.Bones.Count).ToArray();
        masonHips = Find(mason, "root");
        dariusHips = Find(darius, "Pelvis");
        hipScale = masonHips >= 0 && dariusHips >= 0 && darius.Bones[dariusHips].Position.Y > 0.1f
            ? mason.Bones[masonHips].Position.Y / darius.Bones[dariusHips].Position.Y : 1;
    }

    static int Find(Rig rig, string name)
    {
        for (int i = 0; i < rig.Bones.Count; i++)
            if (rig.Bones[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>
    /// Learns the bones' constants from the pairs: Guerrilla's player animations (its anims.vpp_pc) that
    /// Armageddon's anims.vpp_pc has on Darius under the same name and frame count.
    /// </summary>
    public static PlayerRetarget Fit(Packfile guerrillaTables, Packfile guerrillaAnims, Packfile gameTables, Packfile gameAnims)
    {
        Rig Read(Packfile pack, string name) => Rig.Read(pack.Read(pack.Find(name) ?? throw new InvalidDataException($"no {name} in {pack.Name}")));
        var darius = Read(gameTables, "player_v2.rig_pc");
        var mason = Read(guerrillaTables, "mason.rig_pc");
        var masonPoses = new List<Quaternion[]>();
        var dariusPoses = new List<Quaternion[]>();
        int pairs = 0;
        foreach (var entry in gameAnims.Entries.Where(e => e.Name.StartsWith("plyr_", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".anim_pc", StringComparison.OrdinalIgnoreCase)))
        {
            if (guerrillaAnims.Find(entry.Name) is not { } theirs) continue;
            Animation a, g;
            try
            {
                a = Animation.Read(gameAnims.Read(entry));
                g = Animation.ReadGuerrilla(guerrillaAnims.Read(theirs));
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException) { continue; }
            if (a.SkeletonBones != darius.Bones.Count || g.SkeletonBones < 52 || g.SkeletonBones > mason.Bones.Count || a.Frames != g.Frames) continue;
            pairs++;
            for (int frame = 0; frame <= a.Frames; frame += 2)
            {
                dariusPoses.Add(a.Pose(darius, frame).Select(Turn).ToArray());
                masonPoses.Add(g.Pose(mason, frame).Select(Turn).ToArray());
            }
        }
        if (pairs == 0) throw new InvalidDataException("Armageddon's anims.vpp_pc has none of Guerrilla's player animations to learn from");

        var retarget = new PlayerRetarget(darius, mason, pairs);
        foreach (var (masonBone, candidates) in Sources)
        {
            int m = Find(mason, masonBone);
            if (m < 0) continue;
            (int Bone, Quaternion Turn, float Off)? best = null;
            foreach (string candidate in candidates)
            {
                int d = Find(darius, candidate);
                if (d < 0) continue;
                var (turn, off) = FitConstant(masonPoses.Select(p => p[m]).ToList(), dariusPoses.Select(p => p[d]).ToList());
                if (best == null || off < best.Value.Off) best = (d, turn, off);
            }
            if (best is not { } b) continue;
            retarget.source[m] = b.Bone;
            retarget.constant[m] = Matrix4x4.CreateFromQuaternion(b.Turn);
            retarget.report.Add($"{masonBone} <- {darius.Bones[b.Bone].Name}: {Degrees(b.Turn):0}° constant, {b.Off:0.0}° from the pairs");
        }
        foreach (var (dariusBone, candidates) in ForwardSources)
        {
            int d = Find(darius, dariusBone);
            if (d < 0) continue;
            (int Bone, Quaternion Turn, float Off)? best = null;
            foreach (string candidate in candidates)
            {
                int m = Find(mason, candidate);
                if (m < 0) continue;
                var (turn, off) = FitConstant(dariusPoses.Select(p => p[d]).ToList(), masonPoses.Select(p => p[m]).ToList());
                if (best == null || off < best.Value.Off) best = (m, turn, off);
            }
            if (best is not { } b) continue;
            retarget.forwardSource[d] = mason.Bones[b.Bone].Name;
            retarget.forwardConstant[d] = Matrix4x4.CreateFromQuaternion(b.Turn);
            retarget.report.Add($"{dariusBone} <- {mason.Bones[b.Bone].Name}: {Degrees(b.Turn):0}° constant, {b.Off:0.0}° from the pairs");
        }
        return retarget;
    }

    /// <summary>
    /// The constant turn C with target ≈ C · source (each a bone's turn in the world, as matrices on rows: C
    /// first, in the bone's own frame), averaged over the poses it fits best; and how far, on average, those are from it.
    /// </summary>
    static (Quaternion Turn, float Off) FitConstant(List<Quaternion> targetTurns, List<Quaternion> sourceTurns)
    {
        var samples = new List<Quaternion>(targetTurns.Count);
        for (int i = 0; i < targetTurns.Count; i++)
            samples.Add(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(
                Matrix4x4.CreateFromQuaternion(targetTurns[i]) * Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(sourceTurns[i])))));
        Quaternion Average(IEnumerable<Quaternion> qs)
        {
            Quaternion sum = default, first = qs.First();
            foreach (var q in qs) sum += Quaternion.Dot(q, first) < 0 ? Quaternion.Negate(q) : q;
            return Quaternion.Normalize(sum);
        }
        var turn = Average(samples);
        var kept = samples.OrderBy(q => Degrees(Quaternion.Inverse(turn) * q)).Take(Math.Max(1, (int)(samples.Count * Kept))).ToList();
        turn = Average(kept);
        return (turn, kept.Average(q => Degrees(Quaternion.Inverse(turn) * q)));
    }

    static float Degrees(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        return 2 * MathF.Acos(Math.Clamp(MathF.Abs(q.W), 0, 1)) * 180 / MathF.PI;
    }

    static Quaternion Turn(Matrix4x4 m)
    {
        m.Translation = Vector3.Zero;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    /// <summary>One of Armageddon's player animations (for Darius's skeleton, Armageddon's packing) for Mason's.</summary>
    public byte[] ToMason(byte[] animation, bool additive)
    {
        var clip = Animation.Read(animation);
        if (clip.SkeletonBones != darius.Bones.Count)
            throw new InvalidDataException($"the animation is for a skeleton of {clip.SkeletonBones} bones, not Darius's {darius.Bones.Count}");
        var dariusTracks = clip.Tracks.ToDictionary(t => t.Bone);
        int n = mason.Bones.Count;
        bool Keyed(int m) => source[m] >= 0 && dariusTracks.ContainsKey(source[m]);
        var root = new AnimationTrack(-1, clip.Root.Rotations, clip.Root.Positions, [], clip.Root.Weights);
        if (additive) return Additive(clip, animation, n, dariusTracks, root);

        var props = Props.Select(p => (Prop: Find(mason, p.Prop), Hand: Find(mason, p.Hand), DariusProp: Find(darius, p.DariusProp), DariusHand: Find(darius, p.DariusHand)))
                         .Where(p => p.Prop >= 0 && p.Hand >= 0 && p.DariusProp >= 0 && p.DariusHand >= 0).ToList();
        var rotations = new List<AnimationKey<Quaternion>>[n];
        var positions = new List<AnimationKey<Vector3>>[n];
        for (int i = 0; i < n; i++) { rotations[i] = []; positions[i] = []; }
        var worldTurn = new Matrix4x4[n];
        var worldPlace = new Vector3[n];
        for (int frame = 0; frame <= clip.Frames; frame++)
        {
            Matrix4x4[] pose = clip.Pose(darius, frame);
            for (int i = 0; i < n; i++)
            {
                RigBone bone = mason.Bones[i];
                Matrix4x4 parentTurn = bone.Parent >= 0 ? worldTurn[bone.Parent] : Matrix4x4.Identity;
                Vector3 parentPlace = bone.Parent >= 0 ? worldPlace[bone.Parent] : Vector3.Zero;
                Matrix4x4 turn = source[i] >= 0 ? constant[i] * Matrix4x4.CreateFromQuaternion(Turn(pose[source[i]])) : parentTurn;
                Quaternion local = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(turn * Matrix4x4.Transpose(parentTurn)));
                worldTurn[i] = Matrix4x4.CreateFromQuaternion(local) * parentTurn;
                if (source[i] >= 0) rotations[i].Add(new(frame, local));

                Vector3 place = parentPlace + Vector3.Transform(bone.Offset, parentTurn);
                if (i == masonHips && dariusHips >= 0)
                    place = bone.Position + (pose[dariusHips].Translation - darius.Bones[dariusHips].Position) * hipScale;
                foreach (var p in props.Where(p => p.Prop == i))
                    place = worldPlace[p.Hand] + (pose[p.DariusProp].Translation - pose[p.DariusHand].Translation);
                if (place != parentPlace + Vector3.Transform(bone.Offset, parentTurn) || positions[i].Count > 0)
                    positions[i].Add(new(frame, Vector3.Transform(place - parentPlace, Matrix4x4.Transpose(parentTurn)) - bone.Offset));
                worldPlace[i] = place;
            }
        }
        var tracks = Enumerable.Range(0, n).Where(Keyed)
            .Select(i => new AnimationTrack(i, Reduce(rotations[i]), Reduce(positions[i]), [], dariusTracks[source[i]].Weights))
            .ToList();
        // The camera's look-at point, where Darius's moves (dodges, rolls, the head stomp) carry it: his
        // camera-lookat track as it is, for the bone of the same name WithCameraLookat adds after Mason's
        // own (a bone of its own on both skeletons, at the same rest place, moved by the same root track).
        int lookat = Find(darius, CameraLookat);
        if (lookat >= 0 && dariusTracks.TryGetValue(lookat, out var camera))
        {
            tracks.Add(new AnimationTrack(n, camera.Rotations, camera.Positions, camera.Scales, camera.Weights));
            return AnimationWriter.Write(animation.AsSpan(0, 0x30), clip.Frames, n + 1, tracks, root);
        }
        return AnimationWriter.Write(animation.AsSpan(0, 0x30), clip.Frames, n, tracks, root);
    }

    /// <summary>The bone Armageddon's player camera turns toward in its moves (a character entry finds it by name).</summary>
    public const string CameraLookat = "camera-lookat";

    /// <summary>
    /// Mason's skeleton (a .rig_pc, see <see cref="Rig"/>) with Darius's camera-lookat bone added after his
    /// own: Darius's record (a bone of its own, at his feet, half a metre back) and hash, its name after
    /// the names, the bone count (+0x24) and the count of bones after the body's (+0x2C) one more. The
    /// game's player camera turns toward it in Armageddon's moves ("camera lookat as rotate" triggers);
    /// a character entry finds it by name, and without it holds -1, and the camera goes where bone -1
    /// points. Mason's own bones, tags and their names stay where they are, so his animations (which
    /// leave it at rest) play as before. Unchanged if the skeleton has one already.
    /// </summary>
    public static byte[] WithCameraLookat(byte[] rig, byte[] dariusRig)
    {
        var mason = Rig.Read(rig);
        if (Find(mason, CameraLookat) >= 0) return rig;
        var darius = Rig.Read(dariusRig);
        int bone = Find(darius, CameraLookat);
        if (bone < 0) throw new InvalidDataException($"Darius's skeleton has no {CameraLookat} bone");
        if (darius.Bones[bone].Parent != -1) throw new InvalidDataException($"Darius's {CameraLookat} hangs from a bone; only one of its own can be carried over");
        int dCount = BitConverter.ToInt32(dariusRig, 0x24);
        uint hash = BitConverter.ToUInt32(dariusRig, 0x40 + 4 * bone);
        byte[] record = dariusRig.AsSpan(0x40 + 4 * dCount + 36 * bone, 36).ToArray();

        int count = BitConverter.ToInt32(rig, 0x24), tags = BitConverter.ToInt32(rig, 0x30);
        int bonesAt = 0x40 + 4 * count, tagsAt = bonesAt + 36 * count, namesAt = tagsAt + 60 * tags;
        using var output = new MemoryStream();
        output.Write(rig, 0, bonesAt);                        // the header and each bone's hash
        output.Write(BitConverter.GetBytes(hash));
        output.Write(rig, bonesAt, tagsAt - bonesAt);          // the bones
        BitConverter.GetBytes(rig.Length - namesAt).CopyTo(record, 0);  // its name, after the names
        output.Write(record);
        output.Write(rig, tagsAt, rig.Length - tagsAt);        // the tags and the names
        output.Write(System.Text.Encoding.Latin1.GetBytes(CameraLookat + "\0"));
        byte[] result = output.ToArray();
        BitConverter.GetBytes(count + 1).CopyTo(result, 0x24);
        BitConverter.GetBytes(BitConverter.ToInt32(rig, 0x2C) + 1).CopyTo(result, 0x2C);
        if (Find(Rig.Read(result), CameraLookat) != count) throw new InvalidDataException($"the {CameraLookat} bone didn't land after Mason's own");
        return result;
    }

    /// <summary>
    /// One of Guerrilla's people's animations (Guerrilla's packing) for Darius's skeleton: made for
    /// <paramref name="from"/> (Mason's, Guerrilla's player's, or another person's: they share Guerrilla's
    /// player's 52 bones by name). As Armageddon's developers moved Guerrilla's onto Darius: his root only
    /// moves, with the hips (Mason's root's movement from its rest, scaled by the two hip heights), and never
    /// turns; his Pelvis turns as Mason's root; each other bone as its Mason bone and its constant; the item
    /// bones he holds things by sit where Mason's hand props are, from the hand.
    /// </summary>
    public byte[] ToDarius(byte[] animation, Rig from, bool additive)
    {
        var clip = Animation.ReadGuerrilla(animation);
        if (clip.SkeletonBones != from.Bones.Count)
            throw new InvalidDataException($"the animation is for a skeleton of {clip.SkeletonBones} bones, not {from.Bones.Count}");
        var sourceTracks = clip.Tracks.ToDictionary(t => t.Bone);
        int n = darius.Bones.Count;
        var src = new int[n];
        for (int i = 0; i < n; i++) src[i] = forwardSource[i] is { } name ? Find(from, name) : -1;
        var root = new AnimationTrack(-1, clip.Root.Rotations, clip.Root.Positions, [], clip.Root.Weights);
        int fromRoot = Find(from, "root"), dariusRoot = Find(darius, "root");
        float scale = fromRoot >= 0 && dariusRoot >= 0 && from.Bones[fromRoot].Position.Y > 0.1f
            ? darius.Bones[dariusRoot].Position.Y / from.Bones[fromRoot].Position.Y : 1;

        if (additive)
        {
            var added = new List<AnimationTrack>();
            for (int d = 0; d < n; d++)
            {
                if (src[d] < 0 || !sourceTracks.TryGetValue(src[d], out var track)) continue;
                var c = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(forwardConstant[d]));
                var turns = track.Rotations.Select(k => new AnimationKey<Quaternion>(k.Frame, Quaternion.Normalize(Quaternion.Inverse(c) * k.Value * c))).ToList();
                added.Add(new AnimationTrack(d, turns, [], [], track.Weights));
            }
            if (dariusRoot >= 0 && fromRoot >= 0 && sourceTracks.TryGetValue(fromRoot, out var hips) && hips.Positions.Count > 0)
                added.Add(new AnimationTrack(dariusRoot, [], hips.Positions.Select(k => new AnimationKey<Vector3>(k.Frame, k.Value * scale)).ToList(), [], hips.Weights));
            added.Sort((x, y) => x.Bone.CompareTo(y.Bone));
            return AnimationWriter.Write(animation.AsSpan(0, 0x30), clip.Frames, n, added, root);
        }

        var props = Props.Select(p => (Item: Find(darius, p.DariusProp), Hand: Find(darius, p.DariusHand), Prop: Find(from, p.Prop), PropHand: Find(from, p.Hand)))
                         .Where(p => p.Item >= 0 && p.Hand >= 0 && p.Prop >= 0 && p.PropHand >= 0).ToList();
        var rotations = new List<AnimationKey<Quaternion>>[n];
        var positions = new List<AnimationKey<Vector3>>[n];
        for (int i = 0; i < n; i++) { rotations[i] = []; positions[i] = []; }
        var worldTurn = new Matrix4x4[n];
        var worldPlace = new Vector3[n];
        for (int frame = 0; frame <= clip.Frames; frame++)
        {
            Matrix4x4[] pose = clip.Pose(from, frame);
            for (int i = 0; i < n; i++)
            {
                RigBone bone = darius.Bones[i];
                Matrix4x4 parentTurn = bone.Parent >= 0 ? worldTurn[bone.Parent] : Matrix4x4.Identity;
                Vector3 parentPlace = bone.Parent >= 0 ? worldPlace[bone.Parent] : Vector3.Zero;
                Matrix4x4 turn = src[i] >= 0 ? forwardConstant[i] * Matrix4x4.CreateFromQuaternion(Turn(pose[src[i]])) : parentTurn;
                Quaternion local = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(turn * Matrix4x4.Transpose(parentTurn)));
                worldTurn[i] = Matrix4x4.CreateFromQuaternion(local) * parentTurn;
                if (src[i] >= 0) rotations[i].Add(new(frame, local));

                Vector3 rest = parentPlace + Vector3.Transform(bone.Offset, parentTurn), place = rest;
                if (i == dariusRoot && fromRoot >= 0)
                    place = bone.Position + (pose[fromRoot].Translation - from.Bones[fromRoot].Position) * scale;
                foreach (var p in props.Where(p => p.Item == i))
                    place = worldPlace[p.Hand] + (pose[p.Prop].Translation - pose[p.PropHand].Translation);
                if (place != rest || positions[i].Count > 0)
                    positions[i].Add(new(frame, Vector3.Transform(place - parentPlace, Matrix4x4.Transpose(parentTurn)) - bone.Offset));
                worldPlace[i] = place;
            }
        }
        var tracks = new List<AnimationTrack>();
        for (int i = 0; i < n; i++)
        {
            int moved = i == dariusRoot ? fromRoot : src[i];
            if (moved < 0 || !sourceTracks.TryGetValue(moved, out var track)) continue;
            if (rotations[i].Count == 0 && positions[i].Count == 0) continue;
            tracks.Add(new AnimationTrack(i, Reduce(rotations[i]), Reduce(positions[i]), [], track.Weights));
        }
        return AnimationWriter.Write(animation.AsSpan(0, 0x30), clip.Frames, n, tracks, root);
    }

    /// <summary>
    /// How far a key may be from what its neighbours give between them before it is kept: every frame is
    /// posed, and most frames' keys are what the game's interpolation between fewer keys gives anyway
    /// (Darius's own animations have about a fifth as many).
    /// </summary>
    const float KeptTurnDegrees = 0.25f, KeptMoveMetres = 0.002f;

    static List<AnimationKey<Quaternion>> Reduce(List<AnimationKey<Quaternion>> keys) =>
        Reduce(keys, (a, b, t) => Quaternion.Slerp(a, b, t), (a, b) => Degrees(Quaternion.Inverse(a) * b) <= KeptTurnDegrees);

    static List<AnimationKey<Vector3>> Reduce(List<AnimationKey<Vector3>> keys) =>
        Reduce(keys, Vector3.Lerp, (a, b) => Vector3.Distance(a, b) <= KeptMoveMetres);

    /// <summary>The keys kept: the first and last, and each one the run from the last kept to the next can't do without.</summary>
    static List<AnimationKey<T>> Reduce<T>(List<AnimationKey<T>> keys, Func<T, T, float, T> between, Func<T, T, bool> close)
    {
        if (keys.Count <= 2) return keys;
        var kept = new List<AnimationKey<T>> { keys[0] };
        int start = 0;
        for (int end = 2; end < keys.Count; end++)
        {
            bool fits = true;
            for (int i = start + 1; i < end && fits; i++)
            {
                float t = (keys[i].Frame - keys[start].Frame) / (float)(keys[end].Frame - keys[start].Frame);
                fits = close(between(keys[start].Value, keys[end].Value, t), keys[i].Value);
            }
            if (fits) continue;
            kept.Add(keys[end - 1]);
            start = end - 1;
        }
        kept.Add(keys[^1]);
        return kept;
    }

    /// <summary>
    /// An additive animation (its keys are turns added to another's pose): each of Mason's bones takes its
    /// Darius bone's keys carried into its own frame (C · turn · C⁻¹), the hips' movement scaled.
    /// </summary>
    byte[] Additive(Animation clip, byte[] animation, int n, Dictionary<int, AnimationTrack> dariusTracks, AnimationTrack root)
    {
        var tracks = new List<AnimationTrack>();
        for (int m = 0; m < n; m++)
        {
            if (source[m] < 0 || !dariusTracks.TryGetValue(source[m], out var track)) continue;
            var c = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(constant[m]));
            var turns = track.Rotations.Select(k => new AnimationKey<Quaternion>(k.Frame, Quaternion.Normalize(Quaternion.Inverse(c) * k.Value * c))).ToList();
            var moves = m == masonHips ? track.Positions.Select(k => new AnimationKey<Vector3>(k.Frame, k.Value * hipScale)).ToList() : [];
            tracks.Add(new AnimationTrack(m, turns, moves, [], track.Weights));
        }
        return AnimationWriter.Write(animation.AsSpan(0, 0x30), clip.Frames, n, tracks, root);
    }
}
