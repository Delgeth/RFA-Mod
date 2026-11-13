using System.Numerics;
using RfaFormats.Meshes;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's animations for Mason's skeleton (mason.rig_pc, 53 bones, or Guerrilla's
/// player's, player.rig_pc: the same without his camera bone) made for Darius's (Armageddon's
/// player_v2.rig_pc, 137 bones), so Guerrilla's vehicles' get-in, driving and get-out
/// animations play on Armageddon's player. Both games' animations are the same format (version 10),
/// but the skeletons aren't bone for bone: Mason's spine hangs from his pelvis, Darius's from the
/// root; Mason's clavicles from his neck, Darius's from Spine2; Mason's arms are chains of segments
/// (upper arm, its twist, forearm, its twist, hand), Darius's are upper arm, forearm, hand with the
/// twists beside them. So the keys can't just be renumbered.
///
/// Instead each frame is posed on Mason's skeleton and carried over bone by bone: both rest poses have
/// no rotations, so a bone's turn in the world is how far it has turned from its rest, and Darius's
/// matching bone gets that same turn in the world (<see cref="Same"/>), written back as a turn from
/// its own parent. Darius's helper bones beside a limb follow Mason's twist segments
/// (<see cref="Followers"/>) or sit halfway between a joint's two sides (<see cref="Halfway"/>: the
/// deltoids, the glutes); the rest of Darius's bones (the face, the third finger joints, belt and
/// satchels) keep their place on their parent. The hips and the spine are also placed where Mason's
/// are, his movement from his rest scaled by the two skeletons' hip heights, and the props Darius holds
/// things by (item1, item2) where Mason's hand props are, from his hands. The character's own movement
/// (the root track) and the clip's turn and movement in the header are kept. An animation that moves
/// only part of Mason (played over another) keys only the matching part of Darius, each bone with its
/// Mason bone's weight curve.
/// </summary>
public static class GuerrillaAnimations
{
    /// <summary>Mason's bones and Darius's that are the same bone (by name, any case).</summary>
    static readonly (string Mason, string Darius)[] Same =
    [
        ("root", "root"), ("pelvis", "Pelvis"), ("spine", "Spine"), ("spine1", "Spine1"), ("spine2", "Spine2"),
        ("neck", "Neck"), ("head", "Head"), ("spinebend", "spinebend"),
        ("l-thigh", "L-Thigh"), ("l-calf", "L-Calf"), ("l-foot", "L-Foot"), ("l-toe0", "L-Toe"),
        ("r-thigh", "R-Thigh"), ("r-calf", "R-Calf"), ("r-foot", "R-Foot"), ("r-toe0", "R-Toe"),
        ("l-clavicle", "L-clavicle"), ("l-upperarmtwist1", "l-upperarm"), ("l-foretwist", "l-forearm"), ("l-hand", "L-Hand"),
        ("r-clavicle", "r-clavicle"), ("r-upperarmtwist1", "r-upperarm"), ("r-foretwist", "r-forearm"), ("r-hand", "R-Hand"),
        ("l-finger1", "L-fing-idx1"), ("l-finger2", "L-fing-mid1"), ("l-finger3", "L-fing-ring1"), ("l-finger4", "l-fing-pinky1"), ("l-thumb1", "L-fing-thmb1"),
        ("l-finger11", "L-fing-idx2"), ("l-finger21", "L-fing-mid2"), ("l-finger31", "L-fing-ring2"), ("l-finger41", "L-fing-pinky2"), ("l-thumb11", "L-fing-thmb2"),
        ("r-finger1", "R-fing-idx1"), ("r-finger2", "R-fing-mid1"), ("r-finger3", "R-fing-ring1"), ("r-finger4", "R-fing-pinky1"), ("r-thumb1", "R-fing-thmb1"),
        ("r-finger11", "R-fing-idx2"), ("r-finger21", "R-fing-mid2"), ("r-finger31", "R-fing-ring2"), ("r-finger41", "R-fing-pinky2"), ("r-thumb11", "R-fing-thmb2"),
        ("l-handprop", "item2"), ("r-handprop", "item1"), ("l-eye", "face-l-eye"), ("r-eye", "face-r-eye"),
    ];

    /// <summary>Darius's twist bones, turned as Mason's twist segments.</summary>
    static readonly (string Darius, string Mason)[] Followers =
    [
        ("L-upperarm-twst1", "l-upperarmtwist2"), ("r-upperarm-twst1", "r-upperarmtwist2"),
        ("L-forearm-twst1", "l-foretwist1"), ("L-forearm-twst2", "l-foretwist1"),
        ("r-forearm-twst1", "r-foretwist1"), ("r-foerarm-twst2", "r-foretwist1"),
    ];

    /// <summary>Darius's muscle bones over a joint, turned halfway between Mason's bones on its two sides.</summary>
    static readonly (string Darius, string From, string To)[] Halfway =
    [
        ("L-Deltoid", "l-clavicle", "l-upperarmtwist1"), ("R-Deltoid", "r-clavicle", "r-upperarmtwist1"),
        ("l-gluteus-maximus", "pelvis", "l-thigh"), ("r-gluteus-maximus", "pelvis", "r-thigh"),
        ("l-gluteus-medias", "pelvis", "l-thigh"), ("r-gluteus-medias", "pelvis", "r-thigh"),
    ];

    /// <summary>
    /// Each of Darius's limb bones and the joint it points at. The rest poses don't point their limbs
    /// alike (Darius's forearms are bent about 40° forward, Mason's hang straight), so each such bone is
    /// first turned so it points where Mason's points at rest, and then by Mason's turn: Darius's hands
    /// then land where Mason's do, give or take the skeletons' lengths.
    /// </summary>
    static readonly (string Bone, string PointsAt)[] Aims =
    [
        ("Spine", "Spine1"), ("Spine1", "Spine2"), ("Spine2", "Neck"), ("Neck", "Head"),
        ("L-clavicle", "l-upperarm"), ("l-upperarm", "l-forearm"), ("l-forearm", "L-Hand"), ("L-Hand", "L-fing-mid1"),
        ("r-clavicle", "r-upperarm"), ("r-upperarm", "r-forearm"), ("r-forearm", "R-Hand"), ("R-Hand", "R-fing-mid1"),
        ("L-Thigh", "L-Calf"), ("L-Calf", "L-Foot"), ("L-Foot", "L-Toe"), ("R-Thigh", "R-Calf"), ("R-Calf", "R-Foot"), ("R-Foot", "R-Toe"),
        ("L-fing-idx1", "L-fing-idx2"), ("L-fing-mid1", "L-fing-mid2"), ("L-fing-ring1", "L-fing-ring2"), ("l-fing-pinky1", "L-fing-pinky2"), ("L-fing-thmb1", "L-fing-thmb2"),
        ("R-fing-idx1", "R-fing-idx2"), ("R-fing-mid1", "R-fing-mid2"), ("R-fing-ring1", "R-fing-ring2"), ("R-fing-pinky1", "R-fing-pinky2"), ("R-fing-thmb1", "R-fing-thmb2"),
    ];

    /// <summary>Darius's bones that follow another's correction (see <see cref="Aims"/>): the twists their limb's.</summary>
    static readonly (string Bone, string As)[] AimedAs =
    [
        ("L-upperarm-twst1", "l-upperarm"), ("r-upperarm-twst1", "r-upperarm"),
        ("L-forearm-twst1", "l-forearm"), ("L-forearm-twst2", "l-forearm"), ("r-forearm-twst1", "r-forearm"), ("r-foerarm-twst2", "r-forearm"),
    ];

    /// <summary>Darius's bones placed where Mason's are, not only turned: the hips and spine, and the props (from the hand).</summary>
    static readonly (string Darius, string Mason, string? FromMason, string? FromDarius)[] Placed =
    [
        ("Pelvis", "pelvis", null, null), ("Spine", "spine", null, null),
        ("item1", "r-handprop", "r-hand", "R-Hand"), ("item2", "l-handprop", "l-hand", "L-Hand"),
    ];

    enum Drive { None, Turn, Halfway }

    /// <summary>One of Mason's animations (for <paramref name="from"/>) as Darius's (for <paramref name="to"/>).</summary>
    public static byte[] Retarget(byte[] animation, Rig from, Rig to)
    {
        var source = Animation.ReadGuerrilla(animation);
        if (source.SkeletonBones != from.Bones.Count)
            throw new InvalidDataException($"the animation is for a skeleton of {source.SkeletonBones} bones, not {from.Bones.Count}");
        int Find(Rig rig, string name)
        {
            for (int i = 0; i < rig.Bones.Count; i++)
                if (rig.Bones[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // How each of Darius's bones is driven.
        int n = to.Bones.Count;
        var drive = new Drive[n];
        var a = new int[n];
        var b = new int[n];
        Array.Fill(a, -1);
        Array.Fill(b, -1);
        foreach (var (mason, darius) in Same.Concat(Followers.Select(f => (f.Mason, f.Darius))))
        {
            int d = Find(to, darius), m = Find(from, mason);
            if (d >= 0 && m >= 0) (drive[d], a[d]) = (Drive.Turn, m);
        }
        foreach (var (darius, first, second) in Halfway)
        {
            int d = Find(to, darius), m1 = Find(from, first), m2 = Find(from, second);
            if (d >= 0 && m1 >= 0 && m2 >= 0) (drive[d], a[d], b[d]) = (Drive.Halfway, m1, m2);
        }
        var placed = new (int Mason, int FromMason, int FromDarius)?[n];
        foreach (var (darius, mason, fromMason, fromDarius) in Placed)
        {
            int d = Find(to, darius), m = Find(from, mason);
            if (d < 0 || m < 0) continue;
            placed[d] = (m, fromMason == null ? -1 : Find(from, fromMason), fromDarius == null ? -1 : Find(to, fromDarius));
        }
        // Each limb bone's turn from where Darius's points at rest to where Mason's does.
        var aim = new Matrix4x4[n];
        Array.Fill(aim, Matrix4x4.Identity);
        foreach (var (bone, pointsAt) in Aims)
        {
            int d = Find(to, bone), dc = Find(to, pointsAt);
            if (d < 0 || dc < 0 || drive[d] != Drive.Turn || drive[dc] != Drive.Turn) continue;
            Vector3 dariusWay = to.Bones[dc].Position - to.Bones[d].Position, masonWay = from.Bones[a[dc]].Position - from.Bones[a[d]].Position;
            aim[d] = Matrix4x4.CreateFromQuaternion(FromTo(dariusWay, masonWay));
        }
        foreach (var (bone, @as) in AimedAs)
        {
            int d = Find(to, bone), limb = Find(to, @as);
            if (d >= 0 && limb >= 0) aim[d] = aim[limb];
        }

        int masonHips = Find(from, "pelvis"), dariusHips = Find(to, "Pelvis");
        float scale = masonHips >= 0 && dariusHips >= 0 && from.Bones[masonHips].Position.Y > 0.1f
            ? to.Bones[dariusHips].Position.Y / from.Bones[masonHips].Position.Y : 1;

        // Which of Darius's bones get keys: the ones whose Mason bones the animation moves. One that moves
        // only part of the body (the upper body, say) is played over another, and the bones it leaves
        // alone keep that other's pose; each keyed bone also takes its Mason bone's weight curve.
        var masonTracks = source.Tracks.ToDictionary(t => t.Bone);
        bool Moved(int masonBone) => masonBone >= 0 && masonTracks.ContainsKey(masonBone);
        var keyed = new bool[n];
        var weightFrom = new int[n];
        for (int i = 0; i < n; i++)
        {
            int moved = drive[i] switch
            {
                Drive.Turn when Moved(a[i]) => a[i],
                Drive.Halfway when Moved(a[i]) || Moved(b[i]) => Moved(a[i]) ? a[i] : b[i],
                _ => placed[i] is { } p && Moved(p.Mason) ? p.Mason : -1,
            };
            keyed[i] = moved >= 0;
            weightFrom[i] = moved;
        }

        // Every frame posed on Mason and carried over.
        var rotations = new List<AnimationKey<Quaternion>>[n];
        var positions = new List<AnimationKey<Vector3>>[n];
        for (int i = 0; i < n; i++) { rotations[i] = []; positions[i] = []; }
        var worldTurn = new Matrix4x4[n];
        var worldPlace = new Vector3[n];
        for (int frame = 0; frame <= source.Frames; frame++)
        {
            Matrix4x4[] mason = source.Pose(from, frame);
            for (int i = 0; i < n; i++)
            {
                RigBone bone = to.Bones[i];
                Matrix4x4 parentTurn = bone.Parent >= 0 ? worldTurn[bone.Parent] : Matrix4x4.Identity;
                Vector3 parentPlace = bone.Parent >= 0 ? worldPlace[bone.Parent] : Vector3.Zero;
                Matrix4x4 turn = drive[i] switch
                {
                    Drive.Turn => aim[i] * Turn(mason[a[i]]),
                    Drive.Halfway => Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(Q(mason[a[i]]), Q(mason[b[i]]), 0.5f)),
                    _ => parentTurn,
                };
                Matrix4x4 local = turn * Matrix4x4.Transpose(parentTurn);  // the turn from the parent's
                Quaternion localTurn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(local));
                worldTurn[i] = Matrix4x4.CreateFromQuaternion(localTurn) * parentTurn;

                Vector3 place = parentPlace + Vector3.Transform(bone.Offset, parentTurn);
                if (placed[i] is { } p)
                {
                    place = p.FromMason >= 0 && p.FromDarius >= 0
                        ? worldPlace[p.FromDarius] + (mason[p.Mason].Translation - mason[p.FromMason].Translation)
                        : bone.Position + (mason[p.Mason].Translation - from.Bones[p.Mason].Position) * scale;
                    Vector3 offset = Vector3.Transform(place - parentPlace, Matrix4x4.Transpose(parentTurn));
                    positions[i].Add(new(frame, offset - bone.Offset));
                }
                worldPlace[i] = place;
                if (drive[i] != Drive.None) rotations[i].Add(new(frame, localTurn));
            }
        }
        var tracks = Enumerable.Range(0, n)
            .Where(i => keyed[i] && (rotations[i].Count > 0 || positions[i].Count > 0))
            .Select(i => new AnimationTrack(i, rotations[i], positions[i], [], masonTracks[weightFrom[i]].Weights))
            .ToList();
        var root = new AnimationTrack(-1, source.Root.Rotations, source.Root.Positions, [], source.Root.Weights);
        return AnimationWriter.Write(animation.AsSpan(0, 0x30), source.Frames, n, tracks, root);
    }

    /// <summary>
    /// One of Mason's additive animations (played on top of another, such as a flinch in a seat; its
    /// anim_files.xtbl flag Additive) as Darius's. Its keys are turns added to whatever the person is
    /// doing, not a pose, so they can't be posed and carried over as <see cref="Retarget"/> does; both
    /// rest poses have no rotations, so each of Mason's bones' turns is given to Darius's same bone (and
    /// his twist bones their twist segments') as it is, and the hips' movement scaled by the skeletons'
    /// hip heights.
    /// </summary>
    public static byte[] RetargetAdditive(byte[] animation, Rig from, Rig to)
    {
        var source = Animation.ReadGuerrilla(animation);
        if (source.SkeletonBones != from.Bones.Count)
            throw new InvalidDataException($"the animation is for a skeleton of {source.SkeletonBones} bones, not {from.Bones.Count}");
        int Find(Rig rig, string name)
        {
            for (int i = 0; i < rig.Bones.Count; i++)
                if (rig.Bones[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
        int masonHips = Find(from, "pelvis"), dariusHips = Find(to, "Pelvis");
        float scale = masonHips >= 0 && dariusHips >= 0 && from.Bones[masonHips].Position.Y > 0.1f
            ? to.Bones[dariusHips].Position.Y / from.Bones[masonHips].Position.Y : 1;
        var tracks = new List<AnimationTrack>();
        foreach (var (mason, darius) in Same.Concat(Followers.Select(f => (f.Mason, f.Darius))))
        {
            int m = Find(from, mason), d = Find(to, darius);
            if (m < 0 || d < 0 || source.Tracks.FirstOrDefault(t => t.Bone == m) is not { } track) continue;
            var positions = d == dariusHips ? track.Positions.Select(k => new AnimationKey<Vector3>(k.Frame, k.Value * scale)).ToList() : [];
            tracks.Add(new AnimationTrack(d, track.Rotations, positions, [], track.Weights));
        }
        tracks.Sort((x, y) => x.Bone.CompareTo(y.Bone));
        var root = new AnimationTrack(-1, source.Root.Rotations, source.Root.Positions, [], source.Root.Weights);
        return AnimationWriter.Write(animation.AsSpan(0, 0x30), source.Frames, to.Bones.Count, tracks, root);
    }

    /// <summary>
    /// One of Guerrilla's animations for a skeleton that is the same in Armageddon (a vehicle's own: a
    /// turret's, a flyer's, a walker's legs, carried over with the vehicle), written in Armageddon's
    /// packing with every key as it is. Guerrilla packs rotation keys differently (see
    /// <see cref="Animation.ReadGuerrilla"/>), so even an animation that needs no other change can't be
    /// copied as it is.
    /// </summary>
    public static byte[] Recode(byte[] animation)
    {
        var source = Animation.ReadGuerrilla(animation);
        var root = new AnimationTrack(-1, source.Root.Rotations, source.Root.Positions, source.Root.Scales, source.Root.Weights);
        return AnimationWriter.Write(animation.AsSpan(0, 0x30), source.Frames, source.SkeletonBones, source.Tracks, root);
    }

    static Matrix4x4 Turn(Matrix4x4 m) => Matrix4x4.CreateFromQuaternion(Q(m));

    /// <summary>The shortest turn taking direction <paramref name="from"/> to <paramref name="to"/> (as rows: v × the turn's matrix).</summary>
    static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        Vector3 axis = Vector3.Cross(from, to);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (axis.LengthSquared() < 1e-10f) return Quaternion.Identity;
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(dot));
    }

    static Quaternion Q(Matrix4x4 m)
    {
        m.Translation = Vector3.Zero;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}
