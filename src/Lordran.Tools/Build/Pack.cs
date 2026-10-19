using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Build;

/// <summary>
/// The Lordran content pack: files laid out as &lt;device&gt;/&lt;path&gt; (e.g. gamedata/model/parts/weapon/...,
/// title/enc_regulation.bnd.dcx) that lordran.dll serves in place of DS2's own files.
/// Always built from vanilla DS2 data plus the converted content, so rebuilding is deterministic.
/// </summary>
public sealed class Pack
{
    public const string DefaultOut = @"C:\EchoesOfLordran\data\lordran\pack";

    /// <summary>The agent sandboxes (session 8: parallel agents each work in data\sandbox\NAME; data\sandbox\_base is the
    /// shared source snapshot they copy from).</summary>
    public static readonly string SandboxRoot = Path.GetFullPath(Path.Combine(DefaultOut, "..", "..", "sandbox"));

    /// <summary>
    /// An agent sandbox's pack folder (session 8: parallel agents build there, never into the installed pack; the game
    /// never reads it): exactly data\sandbox\NAME\LEAF, LEAF ending in "pack" (pack, review-pack, ...), NAME not starting
    /// with '_'. Write clears the folder, so the sandbox itself (data\sandbox\NAME holds the agent's src, tools and notes),
    /// anything deeper (NAME\src\pack), other folders (NAME\tools) and the _base snapshot never count. Session 8 review:
    /// the first version tested for a "\data\sandbox\" substring, so a mistyped --out data\sandbox\travel would have
    /// wiped that whole sandbox.
    /// </summary>
    public static bool IsSandboxPack(string outDir)
    {
        string full = Path.GetFullPath(outDir).TrimEnd('\\', '/');
        string? sandbox = Path.GetDirectoryName(full);
        return sandbox != null
            && string.Equals(Path.GetDirectoryName(sandbox), SandboxRoot, StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(sandbox).StartsWith('_')
            && Path.GetFileName(full).EndsWith("pack", StringComparison.OrdinalIgnoreCase);
    }

    public string Ds2Game { get; }
    public string Ds2Data { get; }

    readonly byte[] regulationFile;
    readonly BND4 regulation;
    readonly Dictionary<string, (BinderFile File, PARAM Param)> regulationParams = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> touchedParams = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, FMG> text = new(StringComparer.OrdinalIgnoreCase);
    readonly SortedDictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> log = new();

    public Pack(string ds2Game, string ds2Data)
    {
        Ds2Game = ds2Game;
        Ds2Data = ds2Data;
        regulationFile = File.ReadAllBytes(Path.Combine(ds2Game, "enc_regulation.bnd.dcx"));
        regulation = BND4.Read(Ds2RegulationCipher.Decrypt(regulationFile));
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml")
            .Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        foreach (var file in regulation.Files)
        {
            try
            {
                var param = PARAM.Read(file.Bytes);
                if (param.ApplyParamdefCarefully(defs))
                    regulationParams[Path.GetFileNameWithoutExtension(file.Name)] = (file, param);
            }
            catch (InvalidDataException) { } // language marker files
        }
    }

    /// <summary>Whether the regulation has this param (without marking it for re-serialisation).</summary>
    public bool HasParam(string name) => regulationParams.ContainsKey(name);

    /// <summary>A regulation param, marked for re-serialisation.</summary>
    public PARAM Param(string name)
    {
        touchedParams.Add(name);
        return regulationParams[name].Param;
    }

    /// <summary>Copies <paramref name="template"/> as a new row (replacing any row with that id), keeping rows sorted.</summary>
    public PARAM.Row AddRow(string param, PARAM.Row template, int id, string name)
    {
        var p = Param(param);
        // DS2 ships unnamed rows; keep ours unnamed too so the param's string table matches vanilla's shape.
        var row = new PARAM.Row(template) { ID = id, Name = template.Name };
        p.Rows.RemoveAll(r => r.ID == id);
        p.Rows.Add(row);
        p.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        log.Add($"param {param} row {id} ({name}) from template {template.ID}");
        return row;
    }

    readonly Dictionary<string, (BinderFile File, EMEVD Script)> regulationScripts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// An EMEVD script inside the regulation, marked for re-serialisation. DS2 keeps item special effects this way
    /// (SpEffectWeapon, SpEffectArmor, SpEffectRing, …: one event per item, event id = item id; see SpecialsPorter).
    /// </summary>
    public EMEVD RegulationScript(string name)
    {
        if (!regulationScripts.TryGetValue(name, out var entry))
        {
            var file = regulation.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
            regulationScripts[name] = entry = (file, EMEVD.Read(file.Bytes));
        }
        return entry.Script;
    }

    /// <summary>An English text file from DS2 (menu/text/english/NAME.fmg), loaded on first use.</summary>
    public FMG Text(string name)
    {
        if (!text.TryGetValue(name, out var fmg))
            text[name] = fmg = FMG.Read(Path.Combine(Ds2Data, "menu", "text", "english", name + ".fmg"));
        return fmg;
    }

    public void SetText(string fmgName, int id, string value)
    {
        var fmg = Text(fmgName);
        fmg.Entries.RemoveAll(e => e.ID == id);
        fmg.Entries.Add(new FMG.Entry(id, value));
        fmg.Entries.Sort((a, b) => a.ID.CompareTo(b.ID));
    }

    public void AddFile(string device, string path, byte[] bytes)
    {
        string key = $"{device}/{path.TrimStart('/').Replace('\\', '/')}";
        files[key] = bytes;
        log.Add($"file {key} ({bytes.Length:N0} bytes)");
    }

    /// <summary>A file already added to the pack (to edit it further).</summary>
    public byte[] GetFile(string device, string path) => files[$"{device}/{path.TrimStart('/').Replace('\\', '/')}"];

    /// <summary>A file already added to the pack, or null (session 8: the DS1 sound projects of several maps accumulate
    /// in one fevbnd).</summary>
    public byte[]? TryGetFile(string device, string path) => files.GetValueOrDefault($"{device}/{path.TrimStart('/').Replace('\\', '/')}");

    /// <summary>The files added so far under <paramref name="device"/>/<paramref name="prefix"/> (path relative to the
    /// device, bytes); chrint stage-2 fix pass: build-pack's Morpheme id check runs on them before the pack is written.</summary>
    public IEnumerable<(string Path, byte[] Bytes)> Files(string device, string prefix)
    {
        string p = $"{device}/{prefix.TrimStart('/').Replace('\\', '/')}";
        return files.Where(kv => kv.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key[(device.Length + 1)..], kv.Value));
    }
    /// <summary>Session 10 (asy-events fix pass e2): the "device/path" keys of the files added so far (a copy).</summary>
    public List<string> FileKeys => [.. files.Keys];

    public void Note(string line) => log.Add(line);

    static readonly string[] SourceFolderNames = ["src", "tools", ".git", "_base"];
    static readonly string[] SourceExtensions = [".cs", ".csproj", ".sln", ".c", ".h", ".cpp", ".py"];

    /// <summary>
    /// Why <see cref="Write"/> must not clear <paramref name="outDir"/>, or null when it may. Write deletes every subfolder
    /// and every file but loader.ini in the output folder, so only a pack folder passes:
    /// - it lies below data\lordran (the installed pack is data\lordran\pack), or at least two levels below data\sandbox
    ///   (data\sandbox\NAME\pack, inside one agent's sandbox), and not inside a sandbox folder whose name starts with '_'
    ///   (data\sandbox\_base is the shared source snapshot all agents copy);
    /// - the output folder, or a folder above it below data\lordran or data\sandbox\NAME, has "pack" in its name (pack,
    ///   review-pack, pack-test\fresh). That refuses data\lordran itself, data\lordran\cache and travel, data\sandbox,
    ///   data\sandbox\NAME and a sandbox's src, tools and out folders by name alone;
    /// - an existing folder holds no source tree: no src, tools, .git or _base folder and no .cs/.csproj/.sln/.c/.h/.cpp/.py
    ///   file anywhere inside it. A pack holds only game files, build.log and loader.ini; on 2026-10-03 the installed
    ///   pack and all 11 sandbox pack folders passed this check (0 hits in about 8,200 files).
    /// Session 8: the first sandbox guard accepted any path containing \data\sandbox\, so a mistaken --out S
    /// (data\sandbox\tools) would have deleted that sandbox's src, tools, out and pack, and --out data\sandbox\ would have
    /// deleted _base and every parallel agent's sandbox (review of the tools track). The older data\lordran guard had the same
    /// weakness for data\lordran itself (cache, travel, item-map.tsv) and is tightened by the same rule. build-pack checks
    /// --out before it starts building; Write checks again before it deletes anything.
    /// </summary>
    public static string? OutputFolderProblem(string outDir)
    {
        string full;
        try { full = Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"not a usable path ({e.Message})";
        }
        var cmp = StringComparison.OrdinalIgnoreCase;
        string[] parts = full.Split(Path.DirectorySeparatorChar);
        int root = -1; // index of "lordran" / "sandbox" in ...\data\lordran or ...\data\sandbox (the innermost one)
        for (int i = parts.Length - 2; i >= 0 && root < 0; i--)
            if (parts[i].Equals("data", cmp) && (parts[i + 1].Equals("lordran", cmp) || parts[i + 1].Equals("sandbox", cmp)))
                root = i + 1;
        if (root < 0)
            return "packs are written below data\\lordran (data\\lordran\\pack) or inside one sandbox (data\\sandbox\\NAME\\pack)";
        bool sandbox = parts[root].Equals("sandbox", cmp);
        int first = root + (sandbox ? 2 : 1); // first folder that may be (part of) the pack folder
        if (parts.Length <= first)
            return sandbox
                ? "a sandbox pack goes inside one sandbox (data\\sandbox\\NAME\\pack), never data\\sandbox or a whole sandbox folder"
                : "a pack goes below data\\lordran (data\\lordran\\pack), never data\\lordran itself";
        if (sandbox && parts[root + 1].StartsWith('_'))
            return $"data\\sandbox\\{parts[root + 1]} is shared by every agent (a '_' folder), never a pack folder";
        if (!parts.Skip(first).Any(p => p.Contains("pack", cmp)))
            return $"neither {parts[^1]} nor a folder above it below data\\{parts[root]}{(sandbox ? "\\" + parts[root + 1] : "")} has \"pack\" in its name (use e.g. {(sandbox ? "data\\sandbox\\NAME\\pack" : "data\\lordran\\pack")})";
        if (Directory.Exists(full))
        {
            string? dir = Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories)
                .FirstOrDefault(d => SourceFolderNames.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase));
            if (dir != null)
                return $"it holds a source folder ({dir}); a pack folder holds only game files, build.log and loader.ini";
            string? file = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .FirstOrDefault(f => SourceExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            if (file != null)
                return $"it holds a source file ({file}); a pack folder holds only game files, build.log and loader.ini";
        }
        return null;
    }

    public void Write(string outDir)
    {
        string full = Path.GetFullPath(outDir);
        // session 8: parallel agents build into their own sandbox (data\sandbox\NAME\pack), never the installed pack; the
        // guard refuses any folder that is not a pack folder (OutputFolderProblem), because everything in it is deleted
        if (OutputFolderProblem(outDir) is string problem)
            throw new InvalidOperationException($"refusing to clear {full}: {problem}");
        // loader.ini holds the loader's experiment switches; it survives rebuilds and is never deleted (a rebuild that fails
        // half-way, e.g. because the game still has pack files open, must not lose it). Fallback: data\lordran\loader.ini.template.
        string iniPath = Path.Combine(full, "loader.ini");
        Directory.CreateDirectory(full);
        foreach (string dir in Directory.GetDirectories(full))
            Directory.Delete(dir, recursive: true);
        foreach (string file in Directory.GetFiles(full).Where(f => !f.Equals(iniPath, StringComparison.OrdinalIgnoreCase)))
            File.Delete(file);
        // marks the folder as a pack folder at once, even if this write fails half-way; replaced at the end
        File.WriteAllText(Path.Combine(full, "build.log"), "pack write in progress (if this stays, the write failed)\r\n");
        string template = Path.Combine(Path.GetDirectoryName(full.TrimEnd('\\'))!, "loader.ini.template");
        // session 8 (track potroll review): a sandbox pack has no template beside it, so it got no loader.ini and installing
        // it by replacing the folder dropped the installed one's switches. It now starts from the installed pack's
        // loader.ini (read only: its current switches), else data\lordran's template.
        if (IsSandboxPack(full))
            template = new[] { Path.Combine(DefaultOut, "loader.ini"), Path.Combine(Path.GetDirectoryName(DefaultOut)!, "loader.ini.template") }
                .FirstOrDefault(File.Exists) ?? template;
        if (!File.Exists(iniPath) && File.Exists(template))
        {
            File.Copy(template, iniPath);
            log.Add($"loader.ini copied from {template}");
        }

        foreach (string name in touchedParams)
        {
            var (file, param) = regulationParams[name];
            file.Bytes = param.Write();
        }
        foreach (var (file, script) in regulationScripts.Values)
            file.Bytes = script.Write();
        if (touchedParams.Count > 0 || regulationScripts.Count > 0)
            AddFile("title", "enc_regulation.bnd.dcx", Ds2RegulationCipher.Encrypt(Ds2RegulationCipher.WriteBnd(regulation), regulationFile));
        foreach (var (name, fmg) in text)
            AddFile("gamedata", $"menu/text/english/{name}.fmg", fmg.Write());

        foreach (var (key, bytes) in files)
        {
            string target = Path.Combine(full, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        File.WriteAllLines(Path.Combine(full, "build.log"), log);
        Console.WriteLine($"pack written to {full}: {files.Count} files");
    }
}
