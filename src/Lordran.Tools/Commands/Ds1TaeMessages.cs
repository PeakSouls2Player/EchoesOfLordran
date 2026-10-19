using Lordran.Tools.Build.Chr;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track chrai11), rule EM1 survey: <c>ds1-tae-msgs CHR... [--jt ID[,ID…]]</c> — every DS1 TAE event 231 "Send Ez State
/// Request" (TAE.Template.DS1.xml; the message a map event reads with 4:8 IF Character Has Event Message) of the characters'
/// anibnds: animation id, message id, start / end seconds; with --jt also each animation's JumpTable windows of those ids (27 Ignore
/// Gravity, 19 Disable Map Collision: what an event-forced clip carries itself, rule FA1). Read-only (DSR install).
/// </summary>
public static class Ds1TaeMessages
{
    public static int Run(string[] args)
    {
        var jts = args.SkipWhile(a => a != "--jt").Skip(1).Take(1).SelectMany(a => a.Split(',')).Select(int.Parse).ToList();
        if (args.Contains("--fa1"))   // rule FA1: every Burg boss spec's layout
        {
            ChrPorter.Burg = true;
            foreach (var s in Ds1Boss.All)
            {
                var l = Build.Ai.Boss.EventMessages.Layout(Program.DefaultDsrRoot, s);
                Console.WriteLine($"{s.Ds1Chr}: {(l == null ? "no forced clips" : l.Note)}");
            }
            return 0;
        }
        foreach (string chr in args.Skip(1).TakeWhile(a => !a.StartsWith("--")))
        {
            var src = Ds1ChrSource.Load(Program.DefaultDsrRoot, chr);
            foreach (var (anim, msg, s, e) in Build.Ai.Boss.EventMessages.Of(src))
                Console.WriteLine($"{chr} a{anim / 10000:D2}_{anim % 10000:D4} ({anim}) message {msg} {s:0.###}-{e:0.###} s");
            if (jts.Count == 0 || src.Tae == null) continue;
            foreach (var a in src.Tae.Animations.OrderBy(a => a.ID))
                foreach (int jt in jts)
                {
                    var w = Ds1RigAnims.JumpTable(a, jt);
                    if (w.Count > 0) Console.WriteLine($"{chr} a{a.ID / 10000:D2}_{a.ID % 10000:D4} JT {jt}: {string.Join(" ", w.Select(x => $"{x.S:0.###}-{x.E:0.###}"))}");
                }
        }
        return 0;
    }
}
