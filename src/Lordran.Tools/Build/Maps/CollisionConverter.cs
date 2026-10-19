using Lordran.Tools.Formats.Havok;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1 (DSR) map collision → DS2 map collision. Both games use the same Havok classes (hkpRigidBody →
/// hkpMoppBvTreeShape → FromSoft's CustomParamStorageExtendedMeshShape with one storage subpart and one
/// CustomMeshParameter per hit material), so this is a container conversion: the DSR 2015 tagfile is read, and a DS2
/// 2014 packfile is written from a DS2 collision file used as template. The MOPP code (Havok's BV-tree bytecode) and its
/// quantisation info are copied unchanged; the geometry goes into new storage subparts.
/// Materials: DS1 keeps the HitMtrlParam id in CustomMeshParameter.materialNameData (hundreds = variant); DS2 keeps
/// its MaterialParam id in primitiveDataBuffer = [0, material, 0, 11].
/// </summary>
public static class CollisionConverter
{
    /// <summary>DS1 HitMtrlParam → DS2 MaterialParam (by name: 19 Stone, 20 Soil, 7 Wood, 22 Grass, 2 Iron, …).</summary>
    static readonly Dictionary<int, int> MaterialMap = new()
    {
        [0] = 19, [1] = 19, [2] = 19, [3] = 20, [4] = 7, [5] = 22, [6] = 23, [7] = 19, [8] = 4, [9] = 6, [10] = 22,
        [11] = 2, [12] = 8, [14] = 12, [15] = 26, [17] = 19, [19] = 3, [20] = 5, [21] = 5, [22] = 5, [23] = 27, [24] = 27,
        [25] = 27, [26] = 27, [27] = 30, [28] = 21, [29] = 1, [34] = 19, [40] = 19,
    };

    public static int Ds2Material(uint ds1MaterialNameData) =>
        MaterialMap.TryGetValue((int)(ds1MaterialNameData % 100), out int m) ? m : 19;

    public sealed record Result(byte[] Bytes, int Triangles, int Subparts, string Materials);

    public static Result Convert(byte[] dsrTagfile, HkPackfile template, string name)
    {
        var tag = new HkTagfile(dsrTagfile);
        static HkTagfile.HkObj O(object? v) => (HkTagfile.HkObj)v!;
        static List<object?> L(object? v) => (List<object?>)v!;
        static float[] F(object? v) => L(v).Select(x => System.Convert.ToSingle(x)).ToArray();

        var srcRb = O(L(O(L(O(O(L(tag.Root!["namedVariants"])[0])["variant"])["systems"])[0])["rigidBodies"])[0]);
        var srcMopp = O(O(srcRb["collidable"])["shape"]);
        var srcCode = O(srcMopp["code"]);
        var srcMesh = O(O(srcMopp["child"])["childShape"]);
        if (srcMopp.Type.Name != "hkpMoppBvTreeShape" || srcMesh.Type.Name != "CustomParamStorageExtendedMeshShape")
            throw new NotSupportedException($"collision shape {srcMopp.Type.Name}/{srcMesh.Type.Name}");

        // Template graph (DS2): root → physics data → system → rigid body → MOPP shape → code + mesh.
        var tRoot = template.ReadRoot();
        var tVariant = (HkPObj)tRoot.List("namedVariants")[0]!;
        var tPhys = (HkPObj)tVariant["variant"]!;
        var tSys = (HkPObj)tPhys.List("systems")[0]!;
        var tRb = (HkPObj)tSys.List("rigidBodies")[0]!;
        var tMopp = (HkPObj)tRb.Get("collidable")["shape"]!;
        var tCode = (HkPObj)tMopp["code"]!;
        var tMesh = (HkPObj)tMopp.Get("child")["childShape"]!;
        var tStorage = (HkPObj)tMesh.List("meshstorage")[0]!;
        var tParam = (HkPObj)tMesh.List("materialArray")[0]!;
        var tSubpart = (HkPObj)tMesh.List("trianglesSubparts")[0]!;

        var code = tCode.Clone();
        code.Get("info")["offset"] = F(O(srcCode["info"])["offset"]);
        code["data"] = L(srcCode["data"]).Select(x => System.Convert.ToByte(x)).ToArray();
        code["buildType"] = System.Convert.ToInt64(srcCode["buildType"] ?? 0);

        var mesh = tMesh.Clone();
        var srcSubparts = L(srcMesh["trianglesSubparts"]).Cast<HkTagfile.HkObj>().ToList();
        var srcStorage = L(srcMesh["meshstorage"]).Cast<HkTagfile.HkObj>().ToList();
        var srcParams = L(srcMesh["materialArray"]).Cast<HkTagfile.HkObj>().ToList();
        var subparts = new List<object?>();
        var storages = new List<object?>();
        var materials = new List<object?>();
        int triangles = 0;
        float maxRadius = 0;
        var used = new List<string>();
        for (int i = 0; i < srcSubparts.Count; i++)
        {
            var ss = srcSubparts[i];
            var sp = tSubpart.Clone();
            foreach (string f in new[] { "numTriangleShapes", "numVertices", "vertexStriding", "indexStriding", "stridingType", "flipAlternateTriangles" })
                sp[f] = System.Convert.ToInt64(ss[f]);
            sp["triangleOffset"] = 0L;
            sp["extrusion"] = F(ss["extrusion"]);
            subparts.Add(sp);
            triangles += System.Convert.ToInt32(ss["numTriangleShapes"]);

            var st = tStorage.Clone();
            var src = srcStorage[i];
            var vertices = L(src["vertices"]).Select(v => (object?)F(v)).ToList();
            foreach (float[] v in vertices.Cast<float[]>()) maxRadius = Math.Max(maxRadius, MathF.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]));
            st["vertices"] = vertices;
            st["indices8"] = L(src["indices8"]).Select(x => (object?)System.Convert.ToByte(x)).ToList();
            st["indices16"] = L(src["indices16"]).Select(x => (object?)System.Convert.ToUInt16(x)).ToList();
            st["indices32"] = L(src["indices32"]).Select(x => (object?)System.Convert.ToUInt32(x)).ToList();
            foreach (string f in new[] { "materialIndices", "materials", "namedMaterials", "materialIndices16" }) st[f] = new List<object?>();
            storages.Add(st);

            var mp = tParam.Clone();
            uint ds1Mat = i < srcParams.Count ? System.Convert.ToUInt32(srcParams[i]["materialNameData"] ?? 0u) : 0u;
            int ds2Mat = Ds2Material(ds1Mat);
            mp["vertexDataBuffer"] = new List<object?>();
            mp["vertexDataStride"] = 0L;
            mp["primitiveDataBuffer"] = new List<object?> { (byte)0, (byte)ds2Mat, (byte)0, (byte)11 };
            mp["materialNameData"] = 0L;
            materials.Add(mp);
            used.Add($"{ds1Mat}→{ds2Mat}");
        }
        mesh["embeddedTrianglesSubpart"] = ((HkPObj)subparts[0]!).Clone();
        mesh["trianglesSubparts"] = subparts;
        mesh["meshstorage"] = storages;
        mesh["materialArray"] = materials;
        mesh["shapesSubparts"] = new List<object?>();
        mesh["shapestorage"] = new List<object?>();
        mesh["weldingInfo"] = new List<object?>();
        mesh["aabbHalfExtents"] = F(srcMesh["aabbHalfExtents"]);
        mesh["aabbCenter"] = F(srcMesh["aabbCenter"]);
        mesh["cachedNumChildShapes"] = (long)triangles;
        mesh["triangleRadius"] = System.Convert.ToSingle(srcMesh["triangleRadius"] ?? 0f);
        mesh["numBitsForSubpartIndex"] = System.Convert.ToInt64(srcMesh["numBitsForSubpartIndex"] ?? 12);

        var mopp = tMopp.Clone();
        mopp["code"] = code;
        mopp.Get("child")["childShape"] = mesh;

        var rb = tRb.Clone();
        rb["name"] = name;
        rb.Get("collidable")["shape"] = mopp;
        rb.Get("motion").Get("motionState")["objectRadius"] = maxRadius;

        var sys = tSys.Clone();
        sys["rigidBodies"] = new List<object?> { rb };
        var phys = tPhys.Clone();
        phys["systems"] = new List<object?> { sys };
        var root = tRoot.Clone();
        var variant = tVariant.Clone();
        variant["variant"] = phys;
        root["namedVariants"] = new List<object?> { variant };

        return new Result(new HkPackWriter(template).Write(root), triangles, subparts.Count, string.Join(" ", used));
    }
}
