using System.IO.Compression;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 12.0, Creator Hub: «Мои проекты» с точками статуса и версиями, «Собрать и проверить» в редакторе
/// (идёт игра, итог с логом мода) и обзор проекта. Демо-проекты после снимков удаляются — другие снимки их не видят.
/// </summary>
public sealed partial class CreatorPage
{
    [DemoShots]
    static void V12HubBuildTestShots(Shots s)
    {
        var made = new List<Project>();
        var now = DateTime.UtcNow;
        Project Make(string template, string name, string version, DateTime edited, Action<ProjectMeta>? history = null, params string[] builds)
        {
            var code = Templates.ById(template)!.Code;
            code = Regex.Replace(code, @"(?m)^(\s*)mod\s+""[^""]*""", $"$1mod \"{name}\"");
            code = Regex.Replace(code, @"(?m)^(\s*)version\s+\S+", $"$1version {version}");
            var p = Projects.Create(name, code);
            made.Add(p);
            File.SetLastWriteTimeUtc(p.Script, edited);
            var meta = ProjectMeta.Update(p.Dir, m => history?.Invoke(m));
            // Архивы сборок по версиям — как их оставляет Projects.Pack (builds — свежие первыми, последняя — из истории).
            var last = meta.LastBuild?.Time ?? edited;
            for (var i = 0; i < builds.Length; i++)
            {
                var zip = Path.Combine(Directory.CreateDirectory(Projects.BuildsDir(p.Id)).FullName, $"{Projects.PackageName(name)}-{builds[i]}.zip");
                // Несжимаемые байты — чтобы размер архива был похож на настоящий мод (десятки КБ).
                var bytes = new byte[18_000 + i * 3_100 + name.Length * 97];
                new Random(i + name.Length).NextBytes(bytes);
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                using (var w = archive.CreateEntry("content.json").Open()) w.Write(bytes);
                File.SetLastWriteTimeUtc(zip, i == 0 ? last : last.AddDays(-3 * i - 1));
            }
            if (meta.LastBuild is { Ok: true, Zip: null } b && Projects.Builds(p.Id).FirstOrDefault() is { } newest)
                ProjectMeta.Update(p.Dir, m => m.LastBuild = b with { Zip = newest.FullName });
            return Projects.Get(p.Id)!;
        }

        try
        {
            var t0 = now.AddMinutes(-26);
            var seeds = Make("sdv-seeds", "Дешёвые семена", "1.2.0", t0, m =>
            {
                m.HubId = "demo_cheap_seeds";
                m.LastBuild = new BuildNote(t0.AddMinutes(2), true, 0, null, "1.2.0");
                m.LastInstall = new InstallNote(t0.AddMinutes(2), "stardew-valley", "1.2.0");
                m.LastTest = new TestNote(t0.AddMinutes(9), true, 0, 412, false, "1.2.0",
                    ["[12:00:03 INFO  SMAPI] Loaded 1 content pack: [CP] Дешёвые семена 1.2.0 by ModLaunch | Весенние семена стоят вдвое меньше"]);
                foreach (var (v, days) in new[] { ("1.0.0", 30), ("1.1.0", 17), ("1.1.1", 9), ("1.2.0", 2) }) m.NoteRelease(v, "hub", now.AddDays(-days));
                var total = 120L;
                for (var d = 13; d >= 0; d--) m.NoteDownloads(total += 14 + (13 - d) * 5 + (d % 3) * 9, now.AddDays(-d));
            }, "1.2.0", "1.1.2", "1.1.1", "1.1.0", "1.0.0");
            _projStats[seeds.Id] = new ProjectStats(
                new HubMod("demo_cheap_seeds", "demo", "Mira", "Дешёвые семена", "", "", "stardew-valley", "1.2.0", "script", "", [], [], 2400, "", 1, "",
                    "", 48, 1264, 12, now.AddDays(-30), now.AddDays(-2)),
                null,
                new Listing("demo_seeds", "demo", "Mira", "Дешёвые семена", "", "", "pack", "stardew-valley", 4900, "personal", "1.2.0", "", [], [], 2400, "", 1,
                    "seeds.zip", "active", 37, now.AddDays(-20), now.AddDays(-2)),
                37, 37 * (4900 - Market.Fee(4900)), null);

            var greet = Make("sdv-dialogue", "Весенние приветствия", "1.0.1", now.AddMinutes(-40), m =>
            {
                m.LastBuild = new BuildNote(now.AddMinutes(-38), true, 0, null, "1.0.1");
                m.LastInstall = new InstallNote(now.AddMinutes(-38), "stardew-valley", "1.0.1");
                m.LastTest = new TestNote(now.AddMinutes(-35), false, 0, 94, false, "1.0.1",
                [
                    "[12:04:11 INFO  SMAPI] Loaded 1 content pack: [CP] Весенние приветствия 1.0.1 by ModLaunch",
                    "[12:04:15 ERROR Content Patcher] [CP] Весенние приветствия > Changes > entry #2: unknown token 'Weathr'.",
                    "[12:04:15 WARN  Content Patcher] [CP] Весенние приветствия > entry #2 was skipped.",
                ], Errors: 1);
            }, "1.0.1", "1.0.0");
            Make("valheim-builder", "Строитель без границ", "0.3.0", now.AddHours(-5), m =>
                m.LastBuild = new BuildNote(now.AddHours(-4), true, 1, null, "0.3.0"), "0.3.0");
            Make("bepinex-quiet", "Тихий лог", "1.0.4", now.AddMinutes(-3), m =>
            {
                m.LastBuild = new BuildNote(now.AddHours(-2), true, 0, null, "1.0.3");
                m.LastTest = new TestNote(now.AddHours(-2).AddMinutes(4), true, null, 233, true, "1.0.3", []);
            }, "1.0.3", "1.0.2", "1.0.1");
            var fresh = Make("blank", "Мой мод", "1.0.0", now.AddMinutes(-1));

            s.Window.Navigate(() => new CreatorPage("mine"));
            s.Pump(900);
            s.Save("v12-hub-1-projects");

            // Итог проверки: игра закрылась, загрузчик ругался на мод — строки лога прямо у кода.
            s.Window.Navigate(() => new CreatorPage("mine", greet.Id));
            s.Pump(900);
            s.Save("v12-hub-2-editor-result");

            // Проверка идёт: шаги пройдены, игра запущена полторы минуты назад, «Остановить» вместо «Собрать и проверить».
            Playtest.Demo(seeds);
            s.Window.Navigate(() => new CreatorPage("mine", seeds.Id));
            s.Pump(900);
            s.Save("v12-hub-3-editor-running");
            Playtest.Demo(seeds, Playtest.Stage.Needs);
            s.Window.Navigate(() => new CreatorPage("mine", seeds.Id));
            s.Pump(700);
            s.Save("v12-hub-4-editor-steps");
            Playtest.Demo(null);

            s.Window.Navigate(() => new CreatorPage("mine", seeds.Id) { _pane = "overview" });
            s.Pump(1000);
            s.Save("v12-hub-5-overview");
            s.Window.Navigate(() => new CreatorPage("mine", fresh.Id) { _pane = "overview" });
            s.Pump(800);
            s.Save("v12-hub-6-overview-new");

            I18n.Set("en");
            s.Window.Navigate(() => new CreatorPage("mine", greet.Id) { _pane = "overview" });
            s.Pump(1000);
            s.Save("v12-hub-7-overview-en");
        }
        finally
        {
            I18n.Set("ru");
            Playtest.Demo(null);
            foreach (var p in made) { ForgetStats(p.Id); Projects.Delete(p); }
        }
    }
}
