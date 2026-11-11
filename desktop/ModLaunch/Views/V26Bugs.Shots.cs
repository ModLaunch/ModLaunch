using Avalonia.Controls;
using Avalonia.Interactivity;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 26.0 (MODLAUNCH_SHOTS=V26Bugs): исправления публикации и совместной работы в Creator Hub — «Новая версия»
/// из проекта на этом компьютере (код с диска, карточка не стирается), без проекта (честный выбор), версия не выше
/// выложенной (ошибка у кнопок и «Поставить 1.2.1»), незнакомая игра в коде, мод с файлами — только пакетом,
/// «Обновить у себя» в команде с разницей строк и файл, изменённый на диске, в среде ModScript; часть — в светлой теме.
/// </summary>
public static class V26BugsShots
{
    const string Slots = "mod \"Больше слотов\"\nversion 1.2.0\nauthor \"Kira\"\nabout \"Ещё 8 быстрых слотов\"\ngame valheim\nconfig \"BepInEx.cfg\" [Slots] Extra = 8\n";

    static HubMod Card(string id, string name, string game, string version, string kind, string code) =>
        new(id, "demo", "Kira", name, "Ещё 8 быстрых слотов на панели", "Восемь дополнительных слотов на панели быстрого доступа.\n\n## Совместимость\n- работает с Quick Stack",
            game, version, kind, code, ["qol", "ui"], ["https://cdn.modlaunch.app/demo/slots-1.png", "https://cdn.modlaunch.app/demo/slots-2.png"],
            kind == "package" ? 900_000 : 0, "", kind == "package" ? 1 : 0, kind == "package" ? "MoreSlots.zip" : "", "", 41, 1280, 6,
            DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(-3));

    /// <summary>Проект на этом компьютере с этим кодом (тот же id при повторном запуске), привязанный к карточке.</summary>
    static Project Local(string name, string code, string? hubId)
    {
        var p = Projects.List().FirstOrDefault(x => x.Name == name) ?? Projects.Create(name, code);
        Projects.Save(p, code);
        if (hubId is not null) ProjectMeta.Update(p.Dir, m => m.HubId = hubId);
        return Projects.Get(p.Id) ?? p;
    }

    static CreatorPage Published(Shots s)
    {
        s.Window.CloseDialog();
        s.Window.Navigate(() => new CreatorPage("published"));
        s.Pump(900);
        return (CreatorPage)s.Window.CurrentPage!;
    }

    static void Press(Button? b) => b?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [DemoShots]
    static void V26Bugs(Shots s)
    {
        var w = s.Window;
        // Уведомление об уровне при старте закрыло бы низ снимков — даём ему уйти.
        s.Pump(4500);

        // 1. «Новая версия» мода, проект которого есть на этом компьютере: код и версия — из main.mls, карточка — из Hub.
        var slots = Card("demo_bolshe_slotov", "Больше слотов", "valheim", "1.1.0", "script", "mod \"Больше слотов\"\nversion 1.1.0\ngame valheim\n");
        Local("Больше слотов", Slots, slots.Id);
        Published(s).DemoNewVersion(slots);
        s.Pump(700);
        s.Save("v26b-01-new-version-project");

        // 2. Версия не выше выложенной: ошибка у кнопок и «Поставить 1.2.1».
        w.CloseDialog();
        var project = Projects.List().First(p => p.Name == "Больше слотов");
        HubPublish.ShowProject(new HubDraft { Id = slots.Id, Name = slots.Name, Summary = slots.Summary, Description = slots.Description, Game = "valheim", Version = "1.2.0",
            Tags = [.. slots.Tags], Images = [.. slots.Images] }, project, slots with { Version = "1.2.0" }, lockName: true);
        s.Pump(400);
        Press(HubPublish.LastGo);
        s.Pump(500);
        s.Save("v26b-02-version-not-higher");

        // 3. Без проекта на этом компьютере: честно — тот же код из Hub или готовый архив.
        var stored = Card("demo_kompas", "Компас викинга", "valheim", "2.0.3", "script", "mod \"Компас викинга\"\nversion 2.0.3\ngame valheim\n");
        Published(s).DemoNewVersion(stored);
        s.Pump(700);
        s.Save("v26b-03-new-version-no-project");

        // 4. Мод берёт файлы проекта (copy): исходником нельзя — только пакетом.
        var arena = Card("demo_arena_skins", "Скины арены", "valheim", "1.0.0", "script", "");
        Local("Скины арены", "mod \"Скины арены\"\nversion 1.0.0\ngame valheim\ncopy \"arena.png\" to \"plugins/ArenaSkins/arena.png\"\n", arena.Id);
        Published(s).DemoNewVersion(arena);
        s.Pump(700);
        s.Save("v26b-04-files-package-only");

        // 5. Незнакомая игра в коде: ошибка с подсказкой, «Опубликовать» недоступна (раньше становилась Minecraft).
        var silk = Card("demo_silk_hp", "Больше маски", "hollow-knight-silksong", "1.0.0", "script", "");
        Local("Больше маски", "mod \"Больше маски\"\nversion 1.0.0\ngame silksong\nconfig \"BepInEx.cfg\" [Mask] Extra = 2\n", silk.Id);
        Published(s).DemoNewVersion(silk);
        s.Pump(700);
        s.Save("v26b-05-unknown-game");

        // 6. «Обновить у себя» в команде: чья версия, +/− строк и какие, своя версия — в историю.
        w.CloseDialog();
        var mine = Local("Арена волн", "mod \"Арена волн\"\nversion 1.3.0\ngame valheim\nconfig \"BepInEx.cfg\" [Arena] Waves = 12\nconfig \"BepInEx.cfg\" [Arena] Boss = true\n# моя правка: награда за волну\nconfig \"BepInEx.cfg\" [Arena] Reward = 50\n", null);
        var team = new TeamProject(Teams.ProjectId(mine), "Арена волн", "valheim", "1.2.0", "u2", "Nox",
            "mod \"Арена волн\"\nversion 1.2.0\ngame valheim\nconfig \"BepInEx.cfg\" [Arena] Waves = 10\nconfig \"BepInEx.cfg\" [Arena] Boss = true\n", DateTime.UtcNow.AddHours(-20));
        w.Navigate(() => new CreatorPage("home"));
        s.Pump(900);
        ((CreatorPage)w.CurrentPage!).DemoPull(team);
        s.Pump(500);
        s.Save("v26b-06-team-pull");

        // 7. Файл с несохранёнными правками поменяли на диске — среда спрашивает, что оставить.
        w.CloseDialog();
        var x = XProjects.Adopt(mine);
        w.Navigate(() => new CreatorPage("home"));
        s.Pump(600);
        w.Navigate(() => XStudio.DemoDiskConflict(x.Id));
        s.Pump(1500);
        s.Save("v26b-07-disk-changed");
        w.CloseDialog();

        // Светлая тема: новая версия из проекта и «Обновить у себя».
        var was = Settings.Data.Str("theme") ?? "store";
        Look.SetTheme("light");
        try
        {
            Published(s).DemoNewVersion(slots);
            s.Pump(700);
            s.Save("v26b-08-light-new-version");
            w.CloseDialog();
            w.Navigate(() => new CreatorPage("home"));
            s.Pump(900);
            ((CreatorPage)w.CurrentPage!).DemoPull(team);
            s.Pump(500);
            s.Save("v26b-09-light-team-pull");
            w.CloseDialog();
        }
        finally { Look.SetTheme(was); }
    }
}
