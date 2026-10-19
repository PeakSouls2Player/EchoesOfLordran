using System.Numerics;
using System.Text;
using Lordran.Tools.Commands;
using Lordran.Tools.Formats.Havok;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// The model half of a 1:1 DS1 character (session 8, track chr step 2; user directive: DS1 enemies 1:1, DS1 skeleton,
/// no DS2 stand-in body): DS1's own FLVER as a DS2 chr model skinned to DS1's own bones, DS1's ragdoll converted, and
/// the per-model side files DS2 needs. Built for the Asylum's unarmed Hollow (DS1 c2500 → our c2501).
/// <para><b>DS2 character archive</b> (model/chr/c1030.bnd, studied for this): <c>cNNNN.acb</c> (asset list: textures,
/// the two sib models, <c>model_hkt/cNNNN_Ragdoll.hkt</c>, two <c>.dspwv.xml</c> sources), <c>cNNNN.flv</c> + <c>_s.flv</c>,
/// <c>cNNNN_Ragdoll.hkx</c> (Havok 2014 packfile: an animation skeleton "Character" = the Morpheme rig minus
/// CharacterWorldSpaceTM, in FLVER space (Y up; c1030 Root at (0, 0.887, 0) rotated 90° about Y = its FLVER Root node), a
/// ragdoll skeleton, 19 rigid bodies (16 capsules, 3 boxes), 36 constraints, a ragdoll instance and two skeleton mappers), <c>cNNNN_Ragdoll.hkxpwv</c>
/// and <c>cNNNN.flvpwv</c>. Textures in <c>cNNNN.texbnd</c>.</para>
/// <para><b>FLVER ↔ rig</b>: DS2 binds the Morpheme rig to FLVER nodes by name. Evidence (c1030): the FLVER lists mesh
/// nodes first and eight twist bones the rig lacks, the rig has CharacterWorldSpaceTM and two toe nubs the FLVER lacks,
/// so indices differ for every bone, yet all 52 shared bones have identical bind world positions (0.0 mm, chr-inspect
/// --rig). Our FLVER puts the rig bones first in rig order (node i = rig bone i+1 = Havok skeleton bone i), so a by-name
/// and a by-index binding both hold, then DS1's non-bone nodes.</para>
/// <para><b>Bind pose</b>: node world transforms are DS1's FLVER bind (DS1 skins with its FLVER bind, animates with the
/// Havok reference pose; keeping both is DS1's own behaviour). DS1 FLVER nodes flagged Disabled (Master, the nubs, seeld)
/// get their local transform under their rig parent. c2500: FLVER bind vs Havok reference pose ≤ 1.2 mm on connected
/// bones (unlike c2232's ~1.15× noted in NSA-FORMAT.md §4); checked per build (report).</para>
/// <para><b>flvpwv</b> = per-FLVER-node twist driver (decoded session 8 from c1030): header u32 1, u16 0, u16 node count,
/// u16 20 (header size), u16 148 (record size), u64 0; per node {UTF-16 name, 128 bytes; u16 src, u16 src2, f32 weight,
/// u8 driven, 0x64 0x64 0xF8, u32 1, u32 0}. c1030 drives its eight rig-less twist bones (L_ForeTwist = 0.5 × L_Forearm→
/// L_Hand, LUpArmTwist = −0.5 × L_UpperArm, LCalfTwist = 0.5 × L_Calf→L_Foot, LThighTwist = 0.5 × L_Thigh); every other
/// node is (0, 0, 1.0, 0). DS1's twist bones are rig bones animated by DS1's clips, so every record is undriven.</para>
/// <para><b>Dummies</b>: DS2 stores them bone-relative (ParentBoneIndex = bone, AttachBoneIndex = −1, position/direction
/// in the bone's bind frame, unit vectors; all 25 of c1030), DS1 model-space (ParentBoneIndex = a "Model_Dmy"/"sfx_dummy"
/// node, AttachBoneIndex = bone). DS1's dummies are converted with their ids; DS1's bone-less ones (固定dmy) go on the
/// trajectory bone like DS2's dummy 9 on Master; DS2-only body ids 9, 249, 255 are added at c1030's bind world positions.</para>
/// </summary>
public static class Ds1RigModel
{
    public sealed class Result
    {
        public FLVER2 Flver = null!;
        public byte[] Flvpwv = [], Ragdoll = [], RagdollPwv = [];
        public Dictionary<string, string> TexUse = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Report = [];
        public double MaxBindVsRig;
        /// <summary>DS1 weapon material → culling id of the template material it took over (track enemies).</summary>
        public Dictionary<string, int> WeaponCulling = [];
    }

    static readonly Vector3 Empty = new(float.MaxValue), EmptyMax = new(float.MinValue);

    /// <summary>An extra dummy (session 8, track boss: the hit-capsule dummies of a DS1 attack): DS1 model-space position and
    /// forward on the DS1 FLVER node <c>AttachNode</c>; converted bone-relative like DS1's own dummies.</summary>
    public sealed record ExtraDummy(int RefId, int AttachNode, Vector3 Position, Vector3 Forward);

    /// <summary>Morpheme (Z up) → FLVER (Y up): swap Y and Z (NSA-FORMAT.md §2.2).</summary>
    static Vector3 ToFlver(double x, double y, double z) => new((float)x, (float)z, (float)y);

    /// <param name="rig">the network's rig (DS2 bone names, <see cref="Ds1RigAnims.Ds2BoneName"/>)</param>
    /// <param name="ds1Names">DS1's name of each rig bone (to find the DS1 FLVER node)</param>
    /// <param name="weaponMeshes">track enemies (session 8): DS1 weapon/shield materials ("#00#", ...) to keep → the template
    /// material each takes over. A DS2 chr material's GX list item 1 starts with a culling id (c1030: WP1 20, WP2 21, WP_1010_M
    /// 22, WP_4200_M 23, "SD cull24" 24, body 0 = always drawn); EnemyEquipWeaponParam.cullingId00/01 of the equipped weapon
    /// switches those meshes on (10302000 sword+shield: 20, 24), so one model serves every armed variant of a DS1 character as
    /// DS1's NpcParam.modelDispMask does. The meshes stay on DS1's own weapon bones (R_Weapon / L_shield / L_Weapon), as DS1
    /// draws them.</param>
    /// <param name="extraDummies">dummies to add (track boss: DS1 hit-capsule ends, see <see cref="Ds1Boss"/>)</param>
    /// <param name="maxPerDummyId">track boss: at most this many DS1 dummies per reference id (DS1's scatter sets)</param>
    /// <param name="ds1TextureNames">track boss: every DS1 material gets texture names of its own (<c>n_DS1NAME_d/_n/_s</c>,
    /// recorded in <see cref="Result.TexUse"/> as our name → DS1 texture). With several DS1 materials on one template
    /// material (the Asylum Demon's body and hammer both take BD_LG) the template's names would make them share one
    /// texture set.</param>
    /// <remarks>Merge stage 5: the enemies track's <paramref name="weaponMeshes"/> stays the 8th parameter; the boss track's
    /// three follow it (Ds1Boss passes <c>extraDummies</c> by name).</remarks>
    public static Result Build(FLVER2 ds1, FLVER2 template, MorphemeSkeleton rig, IReadOnlyList<string> ds1Names, ChrPorter.Options o, string t, string n,
        IReadOnlyDictionary<string, string>? weaponMeshes = null,
        IReadOnlyList<ExtraDummy>? extraDummies = null, bool ds1TextureNames = false, int maxPerDummyId = int.MaxValue,
        IReadOnlyDictionary<string, int>? cullingOverride = null)
    {
        var res = new Result();
        var cull = weaponMeshes?.ToDictionary(kv => kv.Key, kv => CullingOf(template, kv.Value)) ?? [];
        res.WeaponCulling = cull;
        var w1 = ChrCompare.World(ds1);
        var ds1ByName = new Dictionary<string, int>();
        for (int i = 0; i < ds1.Nodes.Count; i++) ds1ByName.TryAdd(ds1.Nodes[i].Name.Trim(), i);
        bool Connected(int i) => !ds1.Nodes[i].Flags.HasFlag(FLVER.Node.NodeFlags.Disabled);

        // ---- nodes: rig bones 1..N-1 (CharacterWorldSpaceTM is not a FLVER node in DS2), then DS1's non-bone nodes
        int nb = rig.Names.Count - 1;
        var world = new Matrix4x4[nb];
        var nodes = new List<FLVER.Node>();
        var ds1ToNew = Enumerable.Repeat(-1, ds1.Nodes.Count).ToArray();
        var rigWorld = MorphemePose.From(rig).BindWorld();
        for (int i = 0; i < nb; i++)
        {
            int ri = i + 1;
            string name = rig.Names[ri], dsName = ds1Names[ri];
            int parent = rig.Parents[ri] - 1;                    // Character (rig 1) has parent CharacterWorldSpaceTM → root
            Matrix4x4 w;
            if (ds1ByName.TryGetValue(dsName, out int d))
            {
                ds1ToNew[d] = i;
                // connected DS1 node: its FLVER bind world; disabled one (Master, nubs, seeld): its local under the rig parent
                w = Connected(d) ? w1[d] : ds1.Nodes[d].ComputeLocalTransform() * (parent >= 0 ? world[parent] : Matrix4x4.Identity);
            }
            else w = parent >= 0 ? world[parent] : Matrix4x4.Identity;  // Character, Trajectory: identity
            world[i] = w;
            var local = parent >= 0 && Matrix4x4.Invert(world[parent], out var inv) ? w * inv : w;
            Matrix4x4.Decompose(local, out var scale, out _, out var tr);
            var node = new FLVER.Node
            {
                Name = name, ParentIndex = (short)parent, Translation = tr, Rotation = EulerXZY(local), Scale = Vector3.One,
                BoundingBoxMin = ds1ByName.ContainsKey(dsName) ? ds1.Nodes[d].BoundingBoxMin : Empty,
                BoundingBoxMax = ds1ByName.ContainsKey(dsName) ? ds1.Nodes[d].BoundingBoxMax : EmptyMax,
                Flags = 0,
            };
            nodes.Add(node);
            // bind check: FLVER world vs rig world (Morpheme → FLVER space)
            var rw = rigWorld[ri];
            double dist = Vector3.Distance(w.Translation, ToFlver(rw.X, rw.Y, rw.Z));
            if (ds1ByName.TryGetValue(dsName, out int dd) && Connected(dd)) res.MaxBindVsRig = Math.Max(res.MaxBindVsRig, dist);
        }
        foreach (var (node, d) in ds1.Nodes.Select((x, k) => (x, k)).Where(x => ds1ToNew[x.k] < 0))
        {
            // DS1 nodes that are not rig bones: mesh / dummy-owner nodes ("c2500", "Model_Dmy", "sfx_dummy", 固定dmy, ...)
            ds1ToNew[d] = nodes.Count;
            nodes.Add(new FLVER.Node(node) { ParentIndex = -1, Flags = 0 });
        }
        Relink(nodes);
        var wNew = new Matrix4x4[nodes.Count];
        for (int i = 0; i < nodes.Count; i++) wNew[i] = i < nb ? world[i] : nodes[i].ComputeLocalTransform();
        // recomposition check (the Euler XZY decomposition must reproduce the bind pose)
        var wCheck = ChrCompare.World(new FLVER2 { Nodes = nodes });
        double recomposed = 0;
        for (int i = 0; i < nb; i++) recomposed = Math.Max(recomposed, Vector3.Distance(wCheck[i].Translation, world[i].Translation));
        res.Report.Add($"nodes: {nb} rig bones (rig order) + {nodes.Count - nb} DS1 non-bone nodes; bind vs rig (Havok reference pose) max {res.MaxBindVsRig * 1000:0.00} mm on connected bones; Euler recomposition error {recomposed * 1000:0.000} mm");

        // ---- materials (template's DS2 materials with DS1 textures) and meshes (DS1's, palettes remapped)
        var dst = template;
        var tBody = dst.Meshes.First(m => dst.Materials[m.MaterialIndex].Name == o.BodyMaterial);
        int normalW = tBody.Vertices.Count > 0 ? tBody.Vertices[0].NormalW : 127;
        int layout = tBody.VertexBuffers[0].LayoutIndex;
        var mats = new List<FLVER2.Material>();
        var matIndex = new Dictionary<(string, int), int>();
        var meshes = new List<FLVER2.Mesh>();
        int verts = 0, dropped = 0;
        foreach (var sm in ds1.Meshes)
        {
            var sMat = ds1.Materials[sm.MaterialIndex];
            string tMat;
            if (sMat.Name.StartsWith('#'))
            {
                // weapon meshes: the unarmed variant shows none (NpcParam 250020 modelDispMask 0); armed models keep the listed ones
                if (weaponMeshes == null || !weaponMeshes.TryGetValue(sMat.Name, out var wm)) { dropped++; continue; }
                tMat = wm;
            }
            else tMat = sMat.MTD.Contains("_Edge", StringComparison.OrdinalIgnoreCase) || sMat.MTD.Contains("Alp", StringComparison.OrdinalIgnoreCase) ? o.EdgeMaterial : o.BodyMaterial;
            if (!matIndex.TryGetValue((tMat, sm.MaterialIndex), out int mi))
            {
                if (!ds1TextureNames) mats.Add(ChrPorter.MaterialFromTemplate(dst, tMat, sMat, t, n, res.TexUse));
                else
                {
                    // the template material's slots and shader, each slot named after the DS1 texture it takes
                    var tm = ChrPorter.MaterialFromTemplate(dst, tMat, sMat, t, n, []);
                    tm.Name = $"{tm.Name}_{sm.MaterialIndex}";
                    foreach (var tex in tm.Textures)
                    {
                        string slot = tex.ParamName switch { "g_DiffuseTexture" => "g_Diffuse", "g_SpecularTexture" => "g_Specular", "g_BumpmapTexture" => "g_Bumpmap", _ => "" };
                        string kind = tex.ParamName switch { "g_DiffuseTexture" => "_d", "g_SpecularTexture" => "_s", "g_BumpmapTexture" => "_n", _ => "" };
                        var s = sMat.Textures.FirstOrDefault(x => x.ParamName == slot && !string.IsNullOrEmpty(x.Path));
                        if (s == null || kind == "" || string.IsNullOrEmpty(tex.Path)) continue;
                        string ds1Stem = Path.GetFileNameWithoutExtension(s.Path.Replace('\\', '/'));
                        string baseName = ds1Stem.Length > 2 && ds1Stem[^2] == '_' && "dns".Contains(char.ToLowerInvariant(ds1Stem[^1])) ? ds1Stem[..^2] : ds1Stem;
                        string ours = ChrPorter.UniqueTexName($"{baseName}{kind}", n);
                        int cut = Math.Max(tex.Path.LastIndexOf('\\'), tex.Path.LastIndexOf('/')) + 1;
                        string ext = Path.GetExtension(tex.Path);
                        tex.Path = tex.Path[..cut] + ours + ext;
                        res.TexUse[ours] = ds1Stem;
                    }
                    mats.Add(tm);
                }
                // session 11 (track chrai11), rule TC1-M: a DS1 display group cut off at a part break (the Hellkite's tail #00#) draws
                // under its own culling id (a copy of the material's GX list with item "1" = that id), so BossPartsModelParam can swap it
                if (cullingOverride != null && cullingOverride.TryGetValue(sMat.Name, out int cullId) && mats[^1].GXIndex >= 0 && mats[^1].GXIndex < dst.GXLists.Count)
                {
                    var gx = new FLVER2.GXList { TerminatorID = dst.GXLists[mats[^1].GXIndex].TerminatorID, TerminatorLength = dst.GXLists[mats[^1].GXIndex].TerminatorLength };
                    foreach (var it in dst.GXLists[mats[^1].GXIndex]) gx.Add(new FLVER2.GXItem(it.ID, it.Unk04, (byte[])it.Data.Clone()));
                    if (gx.FirstOrDefault(x => x.ID == "1") is { Data.Length: >= 4 } one)
                    {
                        BitConverter.GetBytes(cullId).CopyTo(one.Data, 0);
                        dst.GXLists.Add(gx);
                        mats[^1].GXIndex = dst.GXLists.Count - 1;
                        res.Report.Add($"rule TC1-M: DS1 {sMat.Name} meshes on {tMat} draw under culling id {cullId} (own GX list {mats[^1].GXIndex})");
                    }
                }
                matIndex[(tMat, sm.MaterialIndex)] = mi = mats.Count - 1;
            }
            var palette = sm.BoneIndices.Select(b => ds1ToNew[b]).ToList();
            if (palette.Any(b => b < 0 || b >= nb)) throw new InvalidDataException($"DS1 mesh skinned to a node that is not a rig bone: {string.Join(",", sm.BoneIndices.Where(b => ds1ToNew[b] < 0 || ds1ToNew[b] >= nb).Select(b => ds1.Nodes[b].Name))}");
            var newVerts = new List<FLVER.Vertex>();
            foreach (var v in sm.Vertices)
            {
                var nv = new FLVER.Vertex(v) { NormalW = normalW };
                while (nv.Colors.Count < 2) nv.Colors.Add(new FLVER.VertexColor(1f, 1f, 1f, 1f));
                if (nv.Tangents.Count == 0) nv.Tangents.Add(new Vector4(1, 0, 0, 1));
                newVerts.Add(nv);
            }
            var fs = sm.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? sm.FaceSets[0];
            var mesh = new FLVER2.Mesh
            {
                MaterialIndex = mi, NodeIndex = ds1ToNew[sm.NodeIndex], BoneIndices = palette, Dynamic = 1, UseBoneWeights = tBody.UseBoneWeights,
                VertexBuffers = [new FLVER2.VertexBuffer(layout)],
                FaceSets = [new FLVER2.FaceSet(FLVER2.FaceSet.FSFlags.None, fs.TriangleStrip, fs.CullBackfaces, fs.Unk06, new List<int>(fs.Indices))],
                Vertices = newVerts,
            };
            if (tBody.BoundingBox != null) mesh.BoundingBox = new FLVER2.Mesh.BoundingBoxes { Min = Min(newVerts), Max = Max(newVerts), Unk = tBody.BoundingBox.Unk };
            meshes.Add(mesh);
            verts += newVerts.Count;
        }
        foreach (var m in mats)
            foreach (var x in m.Textures.Where(x => !string.IsNullOrEmpty(x.Path)))
            {
                // track boss: DS2's engine texture "dummy128" (in c1030's and c3050's materials, in no texbnd: a global
                // texture) keeps its name; renamed it would point at nothing
                if (ds1TextureNames && Path.GetFileNameWithoutExtension(x.Path.Replace('\\', '/')).StartsWith("dummy", StringComparison.OrdinalIgnoreCase)) continue;
                x.Path = ChrPorter.UniqueTexPath(x.Path, n);
            }

        // ---- dummies: DS1's, bone-relative (DS2 layout); DS2-only body ids from c1030 at their bind world positions
        var dummies = new List<FLVER.Dummy>();
        int traj = 1; // node of rig bone 2 (Trajectory) = DS2's Master
        var perId = new Dictionary<int, int>();
        int thinned = 0;
        foreach (var d in ds1.Dummies)
        {
            // track boss: DS1's scatter sets (c2232: 329 dummies with id 200, 184 × 150, 31 × 100, 16 × 103, 7 × 102: body
            // surface points for DS1's hit effects) are thinned to maxPerDummyId per id: vanilla DS2 characters hold at most 48
            // dummies (c6030, c6191; c6020 45, c6000 32, c3050 15)
            if ((perId[d.ReferenceID] = perId.GetValueOrDefault(d.ReferenceID) + 1) > maxPerDummyId) { thinned++; continue; }
            int bone = d.AttachBoneIndex >= 0 ? ds1ToNew[d.AttachBoneIndex] : traj;
            if (bone < 0 || bone >= nb) bone = traj;
            dummies.Add(BoneRelative(d.ReferenceID, bone, wNew[bone], d.Position, d.Forward, d.Upward, d.UseUpwardVector));
        }
        var w2 = ChrCompare.World(template);
        var have = dummies.Select(x => (int)x.ReferenceID).ToHashSet();
        foreach (var d in template.Dummies.Where(x => x.ReferenceID is 9 or 249 or 255 && !have.Contains(x.ReferenceID)))
        {
            string tname = template.Nodes[d.ParentBoneIndex].Name;
            int bone = tname == "Master" ? traj : nodes.FindIndex(x => Norm(x.Name) == Norm(tname));
            if (bone < 0 || bone >= nb) continue;
            var pw = Vector3.Transform(d.Position, w2[d.ParentBoneIndex]);
            var fw = Vector3.TransformNormal(d.Forward, w2[d.ParentBoneIndex]); var uw = Vector3.TransformNormal(d.Upward, w2[d.ParentBoneIndex]);
            dummies.Add(BoneRelative(d.ReferenceID, bone, wNew[bone], pw, fw, uw, d.UseUpwardVector));
        }
        // track enemies (session 8): an armed model also gets the template's weapon dummies (ids 100-199 on R_Weapon / L_Weapon /
        // L_shield: c1030 100/105/106/110/160/161/165/166/170/171 on R_Weapon, 115/130 on L_Weapon; c1020 the same set) that
        // DS1 lacks, kept bone-relative as in the template (DS2 stores dummies relative to their bone; the DS1 weapon sits on the
        // bone the same way: grip at the origin). DS2's TimeAct bullet event carries no dummy (all 1287 2300 events: only the ID),
        // so its bullets start from DS2's own weapon dummies; DS1's hit capsules keep using DS1's dummies (1 → 11) (P).
        int weaponDummies = 0;
        if (weaponMeshes is { Count: > 0 })
            foreach (var d in template.Dummies.Where(x => x.ReferenceID is >= 100 and < 200 && !have.Contains(x.ReferenceID)))
            {
                string tname = template.Nodes[d.ParentBoneIndex].Name;
                int bone = nodes.FindIndex(x => Norm(x.Name) == Norm(tname));
                if (bone < 0 || bone >= nb || !System.Text.RegularExpressions.Regex.IsMatch(tname, "weapon|shield", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                dummies.Add(new FLVER.Dummy
                {
                    ReferenceID = d.ReferenceID, ParentBoneIndex = (short)bone, AttachBoneIndex = -1, Position = d.Position, Forward = d.Forward,
                    Upward = d.Upward, UseUpwardVector = d.UseUpwardVector, Flag1 = d.Flag1, Color = d.Color,
                });
                have.Add(d.ReferenceID);
                weaponDummies++;
            }
        if (weaponDummies > 0) res.Report.Add($"{weaponDummies} DS2 weapon dummies (100-199) from the template on our weapon bones");

        foreach (var e in extraDummies ?? [])
        {
            int bone = e.AttachNode >= 0 && e.AttachNode < ds1ToNew.Length ? ds1ToNew[e.AttachNode] : traj;
            if (bone < 0 || bone >= nb) bone = traj;
            dummies.Add(BoneRelative(e.RefId, bone, wNew[bone], e.Position, e.Forward, new Vector3(0, 1, 0), false));
        }

        dst.Nodes = nodes; dst.Materials = mats; dst.Meshes = meshes; dst.Dummies = dummies;
        var all = meshes.SelectMany(m => m.Vertices).ToList();
        dst.Header.BoundingBoxMin = Min(all); dst.Header.BoundingBoxMax = Max(all);
        res.Flver = dst;
        res.Report.Add($"{meshes.Count} meshes ({dropped} DS1 weapon meshes left out{(weaponMeshes is { Count: > 0 } ? $"; weapon meshes kept: {string.Join(", ", weaponMeshes.Select(kv => $"{kv.Key} on {kv.Value} (culling {cull[kv.Key]})"))}" : "")}), {verts} vertices, {mats.Count} materials, {dummies.Count} dummies (DS1 {ds1.Dummies.Count}{(thinned > 0 ? $" minus {thinned} beyond {maxPerDummyId} per id" : "")} + DS2-only body ids{(extraDummies?.Count > 0 ? $" + {extraDummies.Count} hit-capsule dummies" : "")})");
        res.Flvpwv = Flvpwv(nodes);
        return res;
    }

    static string Norm(string s) => s.Replace(' ', '_').Trim().ToLowerInvariant();

    /// <summary>Culling id of a template material: first u32 of its GX list's item "1" (0 = always drawn).</summary>
    public static int CullingOf(FLVER2 f, string material)
    {
        int mi;
        try { mi = ChrPorter.TemplateMaterial(f, material, ""); } catch (InvalidDataException) { return -1; }
        var m = f.Materials[mi];
        if (m.GXIndex < 0 || m.GXIndex >= f.GXLists.Count) return -1;
        var it = f.GXLists[m.GXIndex].FirstOrDefault(x => x.ID == "1");
        return it != null && it.Data.Length >= 4 ? BitConverter.ToInt32(it.Data, 0) : -1;
    }

    static FLVER.Dummy BoneRelative(int refId, int bone, Matrix4x4 boneWorld, Vector3 pos, Vector3 fwd, Vector3 up, bool useUp)
    {
        Matrix4x4.Invert(boneWorld, out var inv);
        var p = Vector3.Transform(pos, inv);
        var f = Vector3.TransformNormal(fwd, inv); var u = Vector3.TransformNormal(up, inv);
        return new FLVER.Dummy
        {
            ReferenceID = (short)refId, ParentBoneIndex = (short)bone, AttachBoneIndex = -1, Position = p,
            Forward = f.LengthSquared() > 1e-12f ? Vector3.Normalize(f) : new Vector3(0, 0, -1),
            Upward = u.LengthSquared() > 1e-12f ? Vector3.Normalize(u) : new Vector3(0, 1, 0),
            UseUpwardVector = useUp, Flag1 = false, Color = System.Drawing.Color.White,
        };
    }

    /// <summary>First-child / sibling links from the parent indices (FLVER node tree).</summary>
    static void Relink(List<FLVER.Node> nodes)
    {
        foreach (var x in nodes) { x.FirstChildIndex = -1; x.NextSiblingIndex = -1; x.PreviousSiblingIndex = -1; }
        var last = new Dictionary<int, int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            int p = nodes[i].ParentIndex;
            if (last.TryGetValue(p, out int prev)) { nodes[prev].NextSiblingIndex = (short)i; nodes[i].PreviousSiblingIndex = (short)prev; }
            else if (p >= 0) nodes[p].FirstChildIndex = (short)i;
            last[p] = i;
        }
    }

    /// <summary>
    /// Euler angles (x, y, z) for FLVER's rotation order: local = Rx(x)·Rz(z)·Ry(y) (System.Numerics row vectors, as
    /// <see cref="ChrCompare.World"/> composes). From R = Rx·Rz·Ry: M12 = sin z, M11 = cos z cos y, M13 = −cos z sin y,
    /// M22 = cos x cos z, M32 = −sin x cos z; at cos z ≈ 0 y is set to 0 and x = atan2(M23, −M21 / sin z).
    /// </summary>
    /// <summary>Session 11 (dchr11), rule SL-E's switch: on for the Depths' boss-pipeline builds only (Ds1Boss.Build, as rule C11d's AllowEvenSync),
    /// so every earlier character's FLVER stays byte-identical (the Asylum / Stray Demon have near-gimbal nodes the fallback would re-express).</summary>
    public static bool GimbalFix;

    public static Vector3 EulerXZY(Matrix4x4 m)
    {
        Matrix4x4.Decompose(m, out _, out var q, out _);
        var r = Matrix4x4.CreateFromQuaternion(q);
        double s = Math.Clamp(r.M12, -1f, 1f);
        double z = Math.Asin(s), cz = Math.Cos(z);
        double x, y;
        if (cz > 1e-6) { y = Math.Atan2(-r.M13, r.M11); x = Math.Atan2(-r.M32, r.M22); }
        else { y = 0; x = Math.Atan2(r.M23, -r.M21 / s); }
        // session 11 (dchr11, rule SL-E): near the gimbal (the Slime c3200's master: X 180°, Z 90°, cos z ≈ 3e-4 in float) the two atan2
        // above lose x + y; the gimbal form (y = 0, x = atan2(M23, M33)) is taken when it recomposes the rotation strictly better
        if (cz < 1e-2 && GimbalFix)
        {
            static double Err(Matrix4x4 r, double x, double y, double z)
            {
                var c = Matrix4x4.CreateRotationX((float)x) * Matrix4x4.CreateRotationZ((float)z) * Matrix4x4.CreateRotationY((float)y);
                return Math.Abs(c.M11 - r.M11) + Math.Abs(c.M12 - r.M12) + Math.Abs(c.M13 - r.M13) + Math.Abs(c.M21 - r.M21) + Math.Abs(c.M22 - r.M22)
                    + Math.Abs(c.M23 - r.M23) + Math.Abs(c.M31 - r.M31) + Math.Abs(c.M32 - r.M32) + Math.Abs(c.M33 - r.M33);
            }
            double gx = Math.Atan2(r.M23, r.M33);
            // only where the analytic form actually fails (matrix error > 0.1, a lost axis flip: the Slime's master; the demons' near-gimbal bones, errors < 0.1, stay byte-identical)
            double e0 = Err(r, x, y, z);
            if (e0 > 0.1 && Err(r, gx, 0, z) + 1e-6 < e0) { x = gx; y = 0; }
        }
        return new Vector3((float)x, (float)y, (float)z);
    }

    static Vector3 Min(IEnumerable<FLVER.Vertex> vs) => vs.Aggregate(new Vector3(float.MaxValue), (a, v) => Vector3.Min(a, v.Position));
    static Vector3 Max(IEnumerable<FLVER.Vertex> vs) => vs.Aggregate(new Vector3(float.MinValue), (a, v) => Vector3.Max(a, v.Position));

    /// <summary>flvpwv for our FLVER: one undriven record per node (see the class comment for the layout).</summary>
    public static byte[] Flvpwv(List<FLVER.Node> nodes)
    {
        var w = new List<byte>();
        void U16(int v) { w.Add((byte)v); w.Add((byte)(v >> 8)); }
        void U32(uint v) { for (int i = 0; i < 4; i++) w.Add((byte)(v >> (8 * i))); }
        U32(1); U16(0); U16(nodes.Count); U16(20); U16(148); U32(0); U32(0);
        foreach (var x in nodes)
        {
            var name = new byte[128];
            var enc = Encoding.Unicode.GetBytes(x.Name);
            Array.Copy(enc, name, Math.Min(enc.Length, 126));
            w.AddRange(name);
            U16(0); U16(0); U32(BitConverter.SingleToUInt32Bits(1f));
            w.Add(0); w.Add(0x64); w.Add(0x64); w.Add(0xF8);
            U32(1); U32(0);
        }
        return w.ToArray();
    }

    // ------------------------------------------------------------------ ragdoll
    /// <summary>hkpConeLimitConstraintAtom.memOffsetToAngleOffset of a ragdoll constraint: DSR's Havok 2015 runtime layout vs
    /// DS2's Havok 2014 one (every DS2 ragdoll cone limit).</summary>
    const int Dsr2015ConeAngleOffset = 160, Ds2ConeAngleOffset = 56;

    /// <summary>
    /// DS1's ragdoll (the chrbnd's cNNNN.hkx, a DSR 2015 tagfile: animation skeleton "Master" with DS1's 52 bones, ragdoll
    /// skeleton, 19 rigid bodies, constraints, ragdoll instance, two skeleton mappers) as a DS2 2014 packfile
    /// (<see cref="HkTagToPack"/> with c1030_Ragdoll.hkx as the class template), with DS2's animation-skeleton convention:
    /// "Character" + Trajectory + DS1's bones (= our rig minus CharacterWorldSpaceTM, FLVER space), so every mapper index
    /// on the animation side moves by 2 and the two new bones are unmapped in the ragdoll → animation mapper. The rigid
    /// bodies' collision filter infos already use DS2's scheme (c1030 and c2500 share 16 of 18 distinct values).
    /// Returns the packfile and the hkxpwv (DS2 layout: header u32 1, u16 0, u16 bodies, u16 animation bones, u16 ragdoll
    /// bones, u16 24, u16 132, u16 132, u16 136, u32 0; then per body {UTF-16 name 128 B, i16, u16}, per animation bone
    /// {name, u32 0}, per ragdoll bone {name, u32 flags, u32 0}). Body values are DS1's own hkxpwv's (−1/0 like c1030);
    /// ragdoll-bone flags follow DS2's c1030 roles (pelvis 1, upper spine 2, others 0; DS1's file has 7 on the head bone,
    /// meaning unknown).
    /// </summary>
    /// <param name="hitParts">session 11 (track chrai11), rule TC1-H: ragdoll body name → DS2 hit part (BOSS_HIT_PARTS_TYPE: 7 = tail,
    /// the u16 DS2's own _h.hkxpwv records carry per hit body, c2120: Tail2-5 → 7, the BossPartsParam row EnemyParam·100 + 7); our
    /// bosses' hurt bodies are the ragdoll's (no _h file, rule C16), so the ragdoll's records carry it (P)</param>
    public static (byte[] Hkx, byte[] Pwv, List<string> Report) Ragdoll(byte[] ds1Hkx, byte[] ds1Pwv, HkPackfile template, MorphemeSkeleton rig, IReadOnlyList<string> ds1Names,
        IReadOnlyDictionary<string, ushort>? hitParts = null)
    {
        var report = new List<string>();
        // Track boss (session 8): DS1's c2232 ragdoll uses plain hinges (hkpHingeConstraintData); no DS2 character ragdoll has
        // that class, DS2's own hinges are hkpLimitedHingeConstraintData (c3050: 18). A plain hinge is a limited hinge whose
        // limit spans the full circle: the shared atoms (transforms, setupStabilization, 2dAng, ballSocket) keep DS1's values,
        // the limited hinge's extra atoms get DS2's own defaults (c3050_Ragdoll: angMotor type 0x12 off, angFriction 0x11 on
        // with 1 axis and 0 torque, angLimit 0x0E on, axis 0, tau 1) with the limit at ±π.
        int hinges = 0;
        int balls = 0;
        void HingeFixup(HkPObj p)
        {
            if (p.Class != "hkpLimitedHingeConstraintDataAtoms" || p["angLimit"] != null) return;
            // session 11 (burgchr11): DS1's ball-and-socket joints (the Hellkite's 28; no DS2 ragdoll has the class) become limited hinges
            // free over ±π: DS1's pivots are the transforms' translations (identity frames), a 2dAng atom on axis 0 (P: the joint keeps
            // its pivot and its bodies, one rotation axis instead of three; the hurt capsules are the bodies, rule C16)
            if (p["transforms"] == null && HkTagToPack.Sources.TryGetValue(p, out var src) && src.Fields.TryGetValue("pivots", out var pvo) && pvo is HkTagfile.HkObj pv)
            {
                static float[] Fl(object? v) => v switch { float[] fa => fa, System.Collections.IEnumerable e and not string => [.. e.Cast<object?>().Select(x => System.Convert.ToSingle(x))], _ => [] };
                static float[] Frame(object? t) { var l = new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0 }; var v = Fl(t); for (int i = 0; i < 3 && i < v.Length; i++) l[12 + i] = v[i]; return l; }
                var tr = new HkPObj("hkpSetLocalTransformsConstraintAtom"); tr["type"] = 2L;
                tr["transformA"] = Frame(pv.Fields.GetValueOrDefault("translationA")); tr["transformB"] = Frame(pv.Fields.GetValueOrDefault("translationB"));
                var ang = new HkPObj("hkp2dAngConstraintAtom"); ang["type"] = 12L; ang["freeRotationAxis"] = 0L;
                p["transforms"] = tr; p["2dAng"] = ang;
                balls++;
            }
            var motor = new HkPObj("hkpAngMotorConstraintAtom"); motor["type"] = 0x12L; motor["isEnabled"] = false; motor["motorAxis"] = 0L;
            var fr = new HkPObj("hkpAngFrictionConstraintAtom"); fr["type"] = 0x11L; fr["isEnabled"] = 1L; fr["firstFrictionAxis"] = 0L; fr["numFrictionAxes"] = 1L; fr["maxFrictionTorque"] = 0f;
            var lim = new HkPObj("hkpAngLimitConstraintAtom"); lim["type"] = 0x0EL; lim["isEnabled"] = 1L; lim["limitAxis"] = 0L; lim["minAngle"] = (float)-Math.PI; lim["maxAngle"] = (float)Math.PI; lim["angularLimitsTauFactor"] = 1f;
            p["angMotor"] = motor; p["angFriction"] = fr; p["angLimit"] = lim;
            hinges++;
        }
        byte[] conv = HkTagToPack.Convert(ds1Hkx, template, fixup: HingeFixup,
            substitute: new Dictionary<string, string> { ["hkpHingeConstraintData"] = "hkpLimitedHingeConstraintData", ["hkpHingeConstraintDataAtoms"] = "hkpLimitedHingeConstraintDataAtoms",
                ["hkpBallAndSocketConstraintData"] = "hkpLimitedHingeConstraintData", ["hkpBallAndSocketConstraintDataAtoms"] = "hkpLimitedHingeConstraintDataAtoms" });
        if (hinges > 0) report.Add($"ragdoll: {hinges} DS1 plain hinges{(balls > 0 ? $" ({balls} of them DS1 ball-and-socket joints)" : "")} as DS2 limited hinges (limit ±π)");
        var hk = new HkPackfile(conv);
        var root = hk.ReadRoot();
        var objs = hk.Objects.Select(x => hk.ReadObject(x.Offset)).ToList();
        var skels = objs.Where(x => x.Class == "hkaSkeleton").ToList();
        var anim = skels.FirstOrDefault(s => ((System.Collections.IEnumerable)s["bones"]!).Cast<HkPObj>().Any(b => (b["name"] as string)?.Trim() == "Pelvis"))
            // session 11 (dchr11, rule SL-F): a rig without a Pelvis (the Slime c3200): the skeleton whose first bone is the rig's first DS1 bone
            ?? skels.First(s => ((System.Collections.IEnumerable)s["bones"]!).Cast<HkPObj>().FirstOrDefault() is { } b0 && (b0["name"] as string)?.Trim() == ds1Names.Skip(3).FirstOrDefault());
        var ragSkel = skels.First(s => s != anim);
        var bones = anim.List("bones"); var parents = anim.List("parentIndices"); var refPose = anim.List("referencePose");
        int ds1Count = bones.Count;
        var expect = ds1Names.Skip(3).ToList();
        var names = bones.Cast<HkPObj>().Select(b => (b["name"] as string ?? "").Trim()).ToList();
        // session 11 (burgchr11): a non-ASCII DS1 bone name (c2250's "master エクストラ01Nub", Shift-JIS) decodes differently in the Havok
        // tag file and the Morpheme rig: compared with every non-ASCII character as '?' (the tag file decodes the bytes as ASCII '?')
        static string Ascii(string x) => new([.. x.Select(c => c >= 128 ? '?' : c)]);
        if (!names.Select(Ascii).SequenceEqual(expect.Select(Ascii)))
            throw new InvalidDataException($"DS1 ragdoll animation skeleton differs from the rig's DS1 bones (first: {names.Zip(expect).Select((p2, i) => (p2, i)).Where(x => Ascii(x.p2.First) != Ascii(x.p2.Second)).Select(x => $"#{x.i} '{x.p2.First}' vs '{x.p2.Second}'").FirstOrDefault() ?? $"count {names.Count} vs {expect.Count}"})");
        // the Havok skeleton carries the rig's (DS2) bone names, like c1030's "Character" skeleton carries its rig's
        for (int i = 0; i < bones.Count; i++) ((HkPObj)bones[i]!)["name"] = rig.Names[i + 3];
        HkPObj Bone(string name) { var b = new HkPObj("hkaBone"); b["name"] = name; b["lockTranslation"] = false; return b; }
        float[] Ident() => [0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 0];
        bones.InsertRange(0, [Bone(rig.Names[1]), Bone(rig.Names[2])]);
        var newParents = new List<object?> { (short)-1, (short)0 };
        newParents.AddRange(parents.Select(p => (object?)(short)((short)p! < 0 ? 0 : (short)p! + 2)));
        anim["parentIndices"] = newParents;
        refPose.InsertRange(0, [Ident(), Ident()]);
        anim["name"] = "Character";
        int shifted = 0;
        var ragToAnim = new Dictionary<int, HashSet<string>>();   // rule TC1-H: ragdoll bone index → the animation bones (DS1 and DS2 names) it maps to
        foreach (var mp in objs.Where(x => x.Class == "hkaSkeletonMapper"))
        {
            var d = mp.Get("mapping");
            bool aIsAnim = ReferenceEquals(d["skeletonA"], anim), bIsAnim = ReferenceEquals(d["skeletonB"], anim);
            foreach (var sm in d.List("simpleMappings").Cast<HkPObj>())
            {
                if (aIsAnim != bIsAnim)
                {
                    int ai = (short)(aIsAnim ? sm["boneA"]! : sm["boneB"]!), ri = (short)(aIsAnim ? sm["boneB"]! : sm["boneA"]!);
                    if (ai >= 0 && ai < names.Count) { var set = ragToAnim.TryGetValue(ri, out var x) ? x : ragToAnim[ri] = []; set.Add(names[ai]); set.Add(rig.Names[ai + 3]); }
                }
                if (aIsAnim) { sm["boneA"] = (short)((short)sm["boneA"]! + 2); shifted++; }
                if (bIsAnim) { sm["boneB"] = (short)((short)sm["boneB"]! + 2); shifted++; }
            }
            foreach (var cm in d.List("chainMappings").Cast<HkPObj>())
                foreach (var f in new[] { "startBoneA", "endBoneA", "startBoneB", "endBoneB" })
                    if ((aIsAnim && f.EndsWith('A')) || (bIsAnim && f.EndsWith('B'))) { cm[f] = (short)((short)cm[f]! + 2); shifted++; }
            if (bIsAnim)
            {
                var um = d.List("unmappedBones");
                var nu = new List<object?> { (short)0, (short)1 };
                nu.AddRange(um.Select(u => (object?)(short)((short)u! + 2)));
                d["unmappedBones"] = nu;
            }
        }
        // DS2 Havok 2014 values the DSR 2015 tagfile doesn't carry (session 8, c2501 crash diagnosis; all 198 DS2 chr
        // ragdolls surveyed in data\sandbox\chr\crash\diff\survey_summary.tsv):
        //  - hkpConeLimitConstraintAtom.memOffsetToAngleOffset = where the solver keeps the cone angle offset inside the
        //    constraint's runtime block. DSR writes 160 (its 2015 hkpRagdollConstraintData::Runtime layout); all 3288 DS2 cone
        //    limits have 56 (planes limits 0 on both sides). DS2 sizes the runtime block for its own layout, so 160 would
        //    read/write past it once the ragdoll simulates (death).
        //  - hkpEntity.npData 0xFFFFFFFF (absent from the tagfile, so written 0) and hkcdShape.dispatchType 4 (DSR 0): DS2's
        //    value on every ragdoll body and shape (the rule ObjPorter applies to objects).
        int cones = 0, npFixed = 0, dispFixed = 0;
        foreach (var rc in objs.Where(x => x.Class == "hkpRagdollConstraintData"))
        {
            var cone = rc.Get("atoms").Get("coneLimit");
            long off = System.Convert.ToInt64(cone["memOffsetToAngleOffset"] ?? 0L);
            if (off == Dsr2015ConeAngleOffset) { cone["memOffsetToAngleOffset"] = (long)Ds2ConeAngleOffset; cones++; }
            else if (off != Ds2ConeAngleOffset)
                throw new InvalidDataException($"ragdoll cone limit memOffsetToAngleOffset {off}: expected DSR {Dsr2015ConeAngleOffset} or DS2 {Ds2ConeAngleOffset}");
        }
        foreach (var o in objs)
        {
            if (o.Class == "hkpRigidBody" && System.Convert.ToInt64(o["npData"] ?? 0L) == 0) { o["npData"] = 0xFFFFFFFFL; npFixed++; }
            if (o["dispatchType"] is { } dt and not string && System.Convert.ToInt64(dt) == 0) { o["dispatchType"] = 4L; dispFixed++; }
        }
        // track enemies (session 8): DS1 c2900's ragdoll constraints carry no motors (motors [null, null, null] in DS1's own
        // tagfile, read with HkTagfile), unlike c2500 / c2540 / c2550; every DS2 ragdoll constraint has 3, all pointing at its
        // file's one hkpPositionConstraintMotor (c1030, c1320: "x1"). When DS1's file has none, the converted constraints get a
        // copy of the template's motor (shared, as DS2 does). Decided on the DS1 side: the 2014 packfile reader returns these
        // C-array pointers as null even where they are written, so it cannot tell.
        int motorsFilled = 0;
        var ds1Tag = new HkTagfile(ds1Hkx);
        var tagRag = new List<HkTagfile.HkObj>();
        void Walk(HkTagfile.HkObj o, HashSet<HkTagfile.HkObj> seen)
        {
            if (!seen.Add(o)) return;
            if (o.Type.Name == "hkpRagdollConstraintData") tagRag.Add(o);
            foreach (var v in o.Fields.Values)
                foreach (var c in v is HkTagfile.HkObj h ? [h] : v is System.Collections.IEnumerable e and not string and not byte[] ? e.OfType<HkTagfile.HkObj>() : [])
                    Walk(c, seen);
        }
        if (ds1Tag.Root != null) Walk(ds1Tag.Root, new(ReferenceEqualityComparer.Instance));
        bool Motorless(HkTagfile.HkObj rc) => rc["atoms"] is HkTagfile.HkObj at && at["ragdollMotors"] is HkTagfile.HkObj rm
            && (rm["motors"] is not System.Collections.IEnumerable ms || ms.Cast<object?>().All(x => x is not HkTagfile.HkObj));
        int ds1Motorless = tagRag.Count(Motorless);
        // c2500 / c2540 / c2550 hold two copies of each joint (physics system and ragdoll instance), only the instance's with
        // motors: those files keep DS1's own motors; only a file without any motor (c2900) is filled
        if (tagRag.Count > 0 && ds1Motorless == tagRag.Count)
        {
            var motor = template.Objects.Where(x => x.Class == "hkpPositionConstraintMotor").Select(x => template.ReadObject(x.Offset).Clone()).FirstOrDefault()
                ?? throw new InvalidDataException("the ragdoll template has no hkpPositionConstraintMotor to fill DS1's empty motor slots");
            foreach (var rc in objs.Where(x => x.Class == "hkpRagdollConstraintData"))
            {
                rc.Get("atoms").Get("ragdollMotors")["motors"] = new List<object?> { motor, motor, motor };
                motorsFilled++;
            }
        }
        report.Add($"ragdoll DS2 conventions: cone limit memOffsetToAngleOffset {Dsr2015ConeAngleOffset} -> {Ds2ConeAngleOffset} on {cones} constraints, npData 0xFFFFFFFF on {npFixed} bodies, dispatchType 4 on {dispFixed} shapes{(motorsFilled > 0 ? $"; {motorsFilled} ragdoll constraints without DS1 motors got the template's position motor" : "")}");
        byte[] outHkx = new HkPackWriter(hk).Write(root);
        // check: re-read, skeleton = rig minus CharacterWorldSpaceTM
        var back = new HkPackfile(outHkx);
        var bs = back.Objects.Where(x => x.Class == "hkaSkeleton").Select(x => back.ReadObject(x.Offset)).ToList();
        var bn = ((System.Collections.IEnumerable)bs[0]["bones"]!).Cast<HkPObj>().Select(b => b["name"] as string).ToList();
        report.Add($"ragdoll: DS1 c-hkx converted ({conv.Length} → {outHkx.Length} bytes), animation skeleton {ds1Count} → {bn.Count} bones (rig minus CharacterWorldSpaceTM: {(bn.SequenceEqual(rig.Names.Skip(1)) ? "yes" : "NO")}), {shifted} mapper indices shifted, rigid bodies {objs.Count(x => x.Class == "hkpRigidBody")}");

        // ---- hkxpwv in DS2's layout
        var bodies = objs.Where(x => x.Class == "hkpRigidBody").Select(b => b["name"] as string ?? "").ToList();
        var physSys = objs.FirstOrDefault(x => x.Class == "hkpPhysicsSystem");
        if (physSys != null) bodies = physSys.List("rigidBodies").Cast<HkPObj>().Select(b => b["name"] as string ?? "").ToList();
        var ragNames = ragSkel.List("bones").Cast<HkPObj>().Select(b => b["name"] as string ?? "").ToList();
        int dsBodies = BitConverter.ToUInt16(ds1Pwv, 6), dsAnim = BitConverter.ToUInt16(ds1Pwv, 8), dsRag = BitConverter.ToUInt16(ds1Pwv, 10);
        if (dsBodies != bodies.Count || dsAnim != ds1Count || dsRag != ragNames.Count)
            report.Add($"WARNING DS1 hkxpwv counts {dsBodies}/{dsAnim}/{dsRag} differ from the ragdoll's {bodies.Count}/{ds1Count}/{ragNames.Count}");
        var w = new List<byte>();
        void U16(int v) { w.Add((byte)v); w.Add((byte)(v >> 8)); }
        void U32(uint v) { for (int i = 0; i < 4; i++) w.Add((byte)(v >> (8 * i))); }
        void Name(string s) { var nm = new byte[128]; var e = Encoding.Unicode.GetBytes(s); Array.Copy(e, nm, Math.Min(e.Length, 126)); w.AddRange(nm); }
        U32(1); U16(0); U16(bodies.Count); U16(bn.Count); U16(ragNames.Count); U16(24); U16(132); U16(132); U16(136); U32(0);
        var partBodies = new List<string>();
        // rule TC1-H: a body is on the part when its name is "Ragdoll_<bone>" of a part bone, or its ragdoll bone (same name) maps to a
        // part bone (DS1 ragdolls number their own bones: c3430's Ragdoll_Tail01-06 drive Tail1-12)
        ushort? PartOf(string body)
        {
            if (hitParts == null) return null;
            if (hitParts.TryGetValue(body, out ushort p)) return p;
            int ri = ragNames.FindIndex(r => r.Trim() == body.Trim());
            if (ri >= 0 && ragToAnim.TryGetValue(ri, out var mapped))
                foreach (var m in mapped) if (hitParts.TryGetValue("Ragdoll_" + m.Trim(), out p)) return p;
            return null;
        }
        for (int i = 0; i < bodies.Count; i++)
        {
            Name(bodies[i]);
            int o = 0x20 + 4 * i; // DS1 body records: i16, u16 after a 32-byte header
            if (o + 4 <= ds1Pwv.Length) w.AddRange(ds1Pwv.AsSpan(o, 4).ToArray()); else { U16(0xFFFF); U16(0); }
            if (PartOf(bodies[i]) is ushort part) { w[^2] = (byte)part; w[^1] = (byte)(part >> 8); partBodies.Add($"{bodies[i]}={part}"); }   // rule TC1-H
        }
        if (hitParts is { Count: > 0 }) report.Add($"rule TC1-H: ragdoll bodies with a hit part: {(partBodies.Count > 0 ? string.Join(", ", partBodies) : "NONE (no body on the part's bones)")}");
        foreach (var s in bn) { Name(s!); U32(0); }
        foreach (var s in ragNames)
        {
            Name(s);
            uint flag = s.Contains("Pelvis") ? 1u : s.Contains("Spine02") ? 2u : 0u;
            U32(flag); U32(0);
        }
        report.Add($"ragdoll hkxpwv: {bodies.Count} bodies, {bn.Count} animation bones, {ragNames.Count} ragdoll bones, {w.Count} bytes");
        return (outHkx, w.ToArray(), report);
    }
}
