using ModLaunch.Creator;
using ModScriptX;

// ModScript X — консольный инжектор .modx. Сообщения на английском: инструмент международный.
return Cli.Run(args);

static class Cli
{
    const string Usage = """
        ModScript X 1.0 — one .modx script, every game scripting language.

          modscript inspect <gameDir>                       detect the engine and show the game's scripts and variables
          modscript inject  <file.modx> --game <gameDir>    translate and install into the game
          modscript watch   <file.modx> --game <gameDir>    re-inject on every save (live editing)
          modscript build   <file.modx> [--out dir]         translate only, without touching a game
          modscript eject   <modId> --game <gameDir>        remove a mod installed by ModScript X
          modscript list    <gameDir>                       mods installed by ModScript X
          modscript targets                                 supported engines
          modscript selftest                                built-in checks

        Options: --target renpy|rpgmaker|unity|godot|lua (overrides 'engine' in the file and auto-detection).
        For single-player games only: games with anti-cheat are refused.
        """;

    public static int Run(string[] args)
    {
        string? Opt(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var pos = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !args[i - 1].StartsWith("--"))).ToArray();
        var cmd = pos.FirstOrDefault();
        var arg = pos.ElementAtOrDefault(1);
        var game = Opt("--game");
        var target = Opt("--target");
        try
        {
            switch (cmd)
            {
                case "targets":
                    foreach (var t in Target.All) Console.WriteLine($"{t.Id,-9} {t.Title} — {t.Language}; hooks: {string.Join(", ", t.Events)}");
                    return 0;
                case "inspect" when arg != null:
                {
                    var t = target != null ? Target.Find(target) : Engines.Detect(arg);
                    if (t == null) { Console.Error.WriteLine("could not detect the game engine; pass --target"); return 1; }
                    Engines.Inspect(arg, t).ForEach(Console.WriteLine);
                    var ac = Engines.FindAntiCheat(arg);
                    if (ac != null) Console.WriteLine($"warning: {ac} found — inject is disabled for this game");
                    return 0;
                }
                case "build" when arg != null:
                    return Report(Injector.Build(File.ReadAllText(arg), Opt("--out") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(arg))!, "build"), target));
                case "inject" when arg != null && game != null:
                    return Report(Injector.Inject(File.ReadAllText(arg), game, target));
                case "watch" when arg != null && game != null:
                    return Watch(arg, game, target);
                case "eject" when arg != null && game != null:
                {
                    var removed = Injector.Eject(arg, game);
                    Console.WriteLine(removed.Count == 0 ? $"'{arg}' is not installed" : "removed: " + string.Join(", ", removed));
                    return removed.Count == 0 ? 1 : 0;
                }
                case "list" when arg != null:
                    foreach (var m in Injector.Installed(arg)) Console.WriteLine(m);
                    return 0;
                case "selftest":
                    return SelfTest.Run();
                default:
                    Console.WriteLine(Usage);
                    return cmd == null || cmd is "help" or "--help" ? 0 : 1;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 1;
        }
    }

    static string Explain(string key) => key switch
    {
        "err.noName" => "missing mod name: add  mod \"My mod\"",
        "err.version" => "version must look like 1.0.0",
        "err.openBrace" => "block is not closed with }",
        "err.extraBrace" => "extra }",
        _ => key,
    };

    static int Report(Injector.Result r)
    {
        foreach (var d in r.Doc.Diags)
            Console.WriteLine($"{(d.Warning ? "warning" : "error")}{(d.Line > 0 ? " line " + d.Line : "")}: {Explain(d.Message)}");
        if (!r.Doc.Ok || r.Target == null) return 1;
        Console.WriteLine($"{r.Doc.Name} → {r.Target.Title} ({r.Target.Language})");
        foreach (var f in r.Files) Console.WriteLine("  " + f);
        if (r.Note != null) Console.WriteLine(r.Note);
        return 0;
    }

    /// <summary>Живое редактирование: на каждое сохранение файла — заново перевести и поставить.</summary>
    static int Watch(string file, string game, string? target)
    {
        var full = Path.GetFullPath(file);
        void Once()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] inject");
            try { Report(Injector.Inject(File.ReadAllText(full), game, target)); }
            catch (IOException e) { Console.WriteLine("error: " + e.Message); }
        }
        Once();
        using var w = new FileSystemWatcher(Path.GetDirectoryName(full)!, Path.GetFileName(full)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
        var timer = new Timer(_ => Once());
        // Редакторы пишут файл несколькими событиями подряд — ждём паузу.
        w.Changed += (_, _) => timer.Change(300, Timeout.Infinite);
        Console.WriteLine("watching for changes, Ctrl+C to stop. Reload in game: Ren'Py Shift+R, RPG Maker F5.");
        var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
        stop.Wait();
        return 0;
    }
}
