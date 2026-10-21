using Avalonia.Controls;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 12.0, Creator Hub: мастер нового проекта (пустой, шаблон для игры, импорт мода, по-английски),
/// поиск по своим проектам с Обзора и палитра команд с вкладками и проектами.
/// </summary>
public static class V12HubNavWizardShots
{
    /// <summary>Пара проектов, чтобы «Мои проекты» и палитра не были пустыми (один раз за прогон).</summary>
    static void SeedProjects()
    {
        if (Projects.List().Count >= 3) return;
        foreach (var id in new[] { "sdv-seeds", "sdv-fish", "valheim-builder" })
            Projects.Create(I18n.T($"cr.ex.{id}"), Templates.ById(id)!.Code);
    }

    static void Wizard(Shots s, CreatorPage.NewProjectState state, string name, int wait = 700)
    {
        s.Window.Navigate(() => new CreatorPage("home"));
        s.Pump(500);
        CreatorPage.ShowNewProject(state);
        s.Pump(wait);
        s.Save(name);
        s.Window.CloseDialog();
    }

    [DemoShots]
    static void HubNavWizard1Blank(Shots s) => Wizard(s, new CreatorPage.NewProjectState(), "v12-hnw-1-new-blank");

    [DemoShots]
    static void HubNavWizard2Template(Shots s) =>
        Wizard(s, new CreatorPage.NewProjectState { Source = "template", Game = "stardew-valley", TemplateId = "sdv-fish" }, "v12-hnw-2-new-template");

    [DemoShots]
    static void HubNavWizard3Import(Shots s)
    {
        // Готовый мод Thunderstore в папке: манифест с зависимостями, значок, плагин и настройки.
        var dir = Path.Combine(Path.GetTempPath(), "modlaunch-shots-" + Environment.ProcessId, "More_Slots");
        Directory.CreateDirectory(Path.Combine(dir, "plugins"));
        Directory.CreateDirectory(Path.Combine(dir, "config"));
        File.WriteAllText(Path.Combine(dir, "manifest.json"), "{\"name\":\"More_Slots\",\"version_number\":\"1.4.0\",\"website_url\":\"https://example.com\"," +
            "\"description\":\"Eight more quick slots\",\"dependencies\":[\"denikson-BepInExPack_Valheim-5.4.2202\",\"RandyKnapp-EquipmentAndQuickSlots-2.1.0\",\"ValheimModding-Jotunn-2.20.0\"]}");
        File.WriteAllText(Path.Combine(dir, "README.md"), "# More Slots");
        File.WriteAllBytes(Path.Combine(dir, "icon.png"), [137, 80, 78, 71]);
        File.WriteAllText(Path.Combine(dir, "plugins", "MoreSlots.dll"), new string('x', 48_000));
        File.WriteAllText(Path.Combine(dir, "config", "more.slots.cfg"), "[General]\nSlots = 8\n");
        Wizard(s, new CreatorPage.NewProjectState { Source = "import", Game = "valheim", ImportPath = dir }, "v12-hnw-3-new-import", 1200);
    }

    [DemoShots]
    static void HubNavWizard4English(Shots s)
    {
        I18n.Set("en");
        try { Wizard(s, new CreatorPage.NewProjectState { Source = "template", Game = "valheim" }, "v12-hnw-4-new-en"); }
        finally { I18n.Set("ru"); }
    }

    [DemoShots]
    static void HubNavWizard5Search(Shots s)
    {
        SeedProjects();
        s.Window.Navigate(() => new CreatorPage("home"));
        s.Pump(500);
        // Поиск с Обзора — по своим проектам (раньше уводил в старый вид Hub).
        (s.Window.CurrentPage as CreatorPage)?.Search(I18n.T("cr.ex.sdv-seeds").Split(' ')[0]);
        s.Pump(500);
        s.Save("v12-hnw-5-search-projects");
    }

    [DemoShots]
    static void HubNavWizard6Palette(Shots s)
    {
        SeedProjects();
        s.Window.Navigate(() => new CreatorPage("home"));
        s.Pump(400);
        s.Window.CommandPalette();
        s.Pump(300);
        if (s.Window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.Watermark == I18n.T("cmd.hint")) is { } box) box.Text = I18n.Lang == "ru" ? "проект" : "project";
        s.Pump(400);
        s.Save("v12-hnw-6-palette");
        s.Window.CloseDialog();
    }
}
