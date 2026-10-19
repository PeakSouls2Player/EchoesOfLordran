using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>Session 11 (npc12) research probe: FaceGen files of DS1 (temporary, for the head bake).</summary>
public static class NpcHeadProbe
{
    public static int Run(string[] args)
    {
        string root = Program.DefaultDsrRoot;
        var fg = BND3.Read(Path.Combine(root, "facegen", "FaceGen.fgbnd"));
        byte[] b = fg.Files.First(f => f.Name.EndsWith("face.flver")).Bytes;
        var flv = Build.Chr.Ds1FaceGen.ReadFlver(b);
        for (int i = 0; i < flv.Meshes.Count; i++)
        {
            var m = flv.Meshes[i];
            var v = m.Vertices[0];
            Console.WriteLine($"mesh {i} verts {m.Vertices.Count} pos0 {v.Position} uvs {v.UVs.Count} {string.Join(" ", v.UVs)} bones {string.Join(",", m.BoneIndices)} bi {v.BoneIndices[0]},{v.BoneIndices[1]} bw {v.BoneWeights[0]},{v.BoneWeights[1]} colors {v.Colors.Count} {(v.Colors.Count > 0 ? v.Colors[0].ToString() : "")}");
            var mn = m.Vertices.Aggregate(new Vector3(9), (a, x) => Vector3.Min(a, x.Position));
            var mx = m.Vertices.Aggregate(new Vector3(-9), (a, x) => Vector3.Max(a, x.Position));
            Console.WriteLine($"   bbox {mn} .. {mx}");
        }
        foreach (var n in flv.Nodes) Console.WriteLine($"node {n.Name} parent {n.ParentIndex} t {n.Translation} r {n.Rotation}");
        var face = Build.Chr.Ds1FaceGen.Load(root);
        Console.WriteLine(face.Describe());
        if (args.Length > 1)
        {
            string outDir = args[1];
            Directory.CreateDirectory(outDir);
            var ds1 = Formats.ParamStore.LoadDsr(root);
            var row = ds1.Params["FaceGenParam"][args.Length > 2 ? int.Parse(args[2]) : 6270]!;
            var geo = Enumerable.Range(0, 50).Select(i => Convert.ToInt32(row[$"faceGeoData{i:D2}"].Value)).ToList();
            var tx = Enumerable.Range(0, 50).Select(i => Convert.ToInt32(row[$"faceTexData{i:D2}"].Value)).ToList();
            Textures.Png.Save(face.MeanMale, Path.Combine(outDir, "mean_m.png"));
            Textures.Png.Save(face.FaceColour(tx, false), Path.Combine(outDir, "gen.png"));
            foreach (float g in new[] { 0.1f, 0.25f, 0.5f })
                Textures.Png.Save(face.FaceColour(tx, false, g), Path.Combine(outDir, $"gen_{g}.png"));
            // gen for several known rows side by side, at gain 1 (Oscar 6020, Petrus 6080, Common Face 1000, prototypes 1, 20)
            var rowsAll = new[] { 6020, 6080, 1000, 1, 20, 300 }.Select(id => ds1.Params["FaceGenParam"][id]).Where(r => r != null).ToList();
            Textures.Png.Save(Commands.OutfitCheck.Strip(rowsAll.Select(r => face.FaceColour(Enumerable.Range(0, 50).Select(i => Convert.ToInt32(r![$"faceTexData{i:D2}"].Value)).ToList(), false)).ToList()), Path.Combine(outDir, "gens.png"));
            Console.WriteLine("tex bytes 1000: " + string.Join(" ", Enumerable.Range(0, 50).Select(i => Convert.ToInt32(ds1.Params["FaceGenParam"][1000]![$"faceTexData{i:D2}"].Value))));
            Console.WriteLine("geo bytes 1000: " + string.Join(" ", Enumerable.Range(0, 50).Select(i => Convert.ToInt32(ds1.Params["FaceGenParam"][1000]![$"faceGeoData{i:D2}"].Value))));
            foreach (string t in new[] { "FG_m_face_org_d", "FG_face_gen_t", "FG_face_t", "FG_eye" })
                if (face.Textures.TryGetValue(t, out var tt)) Textures.Png.Save(Textures.Dds.Read(tt.Headerize()).DecodeTop(), Path.Combine(outDir, t + ".png"));
            // UV wireframes of mesh 0 (both UV sets) over org_d and gen
            foreach (int set in new[] { 0, 1 })
            {
                var bg = Textures.Dds.Read(face.Textures["FG_m_face_org_d"].Headerize()).DecodeTop();
                var m = flv.Meshes[0];
                var tris = m.FaceSets[0].Triangulate(true);
                for (int i = 0; i + 2 < tris.Count; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        var a = m.Vertices[tris[i + e]].UVs[set]; var c = m.Vertices[tris[i + (e + 1) % 3]].UVs[set];
                        for (int s = 0; s <= 40; s++)
                        {
                            float u = a.X + (c.X - a.X) * s / 40, v = a.Y + (c.Y - a.Y) * s / 40;
                            int px = (int)(u * bg.Width), py = (int)(v * bg.Height);
                            if (px >= 0 && py >= 0 && px < bg.Width && py < bg.Height) bg[px, py] = new Textures.Rgba(0, 255, 0, 255);
                        }
                    }
                Textures.Png.Save(bg, Path.Combine(outDir, $"uv{set}_on_org.png"));
            }
            Console.WriteLine($"mean_m avg {face.MeanMale.Pixels.Average(p => p.R):0} {face.MeanMale.Pixels.Average(p => p.G):0} {face.MeanMale.Pixels.Average(p => p.B):0}; org_d avg " +
                string.Join(" ", new Func<Textures.Rgba, int>[] { p => p.R, p => p.G, p => p.B }.Select(fn => Textures.Dds.Read(face.Textures["FG_m_face_org_d"].Headerize()).DecodeTop().Pixels.Average(fn).ToString("0"))));
            Console.WriteLine("egt scales " + string.Join(" ", face.Egt.Take(10).Select(e => e.Scale.ToString("g3"))) + " ; max|d| mode0 " + face.Egt[0].R.Max(x => Math.Abs((int)x)));
            Console.WriteLine("geo coeffs " + string.Join(" ", geo.Take(10).Select(g => Build.Chr.Ds1FaceGen.Coefficient(g).ToString("0.0"))));
            foreach (string hr in new[] { "HR_M_0002", "HR_M_0009" })
            {
                var hb = BND3.Read(Path.Combine(root, "parts", hr + ".partsbnd.dcx"));
                foreach (var t in TPF.Read(hb.Files.First(f => f.Name.EndsWith(".tpf")).Bytes).Textures)
                {
                    var im = Textures.Dds.Read(t.Headerize()).DecodeTop();
                    Textures.Png.Save(im, Path.Combine(outDir, t.Name + ".png"));
                    Console.WriteLine($"{t.Name}: fmt {t.Format} {im.Width}x{im.Height} mean rgba {im.Pixels.Average(p => p.R):0} {im.Pixels.Average(p => p.G):0} {im.Pixels.Average(p => p.B):0} {im.Pixels.Average(p => p.A):0}");
                }
            }
            var morphed = face.Morph(geo, out double shift);
            Console.WriteLine($"morph 6270: max shift {shift:0.0} mm");
        }
        return 0;
    }
}
