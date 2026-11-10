using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 26.0 (MODLAUNCH_SHOTS=V26Design): проход по облику Creator Hub — «Мои моды» (плитки на фоне игры без логотипа,
/// одна главная кнопка в шапке), «Скрипты», «Опубликованные», «Новый мод» по шагам (кнопки под карточкой справа,
/// быстрый старт из блоков для первого мода), «Конструктор в играх», справочник, среда ModScript, подтверждение
/// удаления и светлая тема.
/// </summary>
public static class V26DesignShots
{
    static void Shot(Shots s, string name, Func<Page> page, int ms = 1300)
    {
        s.Window.Navigate(page);
        s.Pump(ms);
        s.Save(name);
    }

    [DemoShots]
    static void V26Design(Shots s)
    {
        var w = s.Window;
        // Уведомление об уровне при старте закрыло бы низ снимков — даём ему уйти.
        s.Pump(4500);
        XStudio.DemoProject();
        if (Projects.List().Count < 2)
            foreach (var id in new[] { "sdv-seeds", "valheim-builder" })
                Projects.Create(I18n.T($"cr.ex.{id}"), Templates.ById(id)!.Code);
        if (!Features.Inject.All().Any(i => i.Source == "import")) Features.Inject.Demo();

        Shot(s, "v26d-01-mods", () => new CreatorPage("home"));
        Shot(s, "v26d-02-scripts", () => new CreatorPage("scripts"));
        Shot(s, "v26d-03-published", () => new CreatorPage("published"));

        // «Новый мод»: первый раз (быстрый старт из блоков над анкетой) и обычные шаги.
        CreatorPage.DemoFirstTime = true;
        Shot(s, "v26d-04-new-first", () => CreatorPage.DemoForm("code", 0));
        V23Shots.Scroll(s, 1);
        s.Save("v26d-05-new-first-bottom");
        CreatorPage.DemoFirstTime = null;
        foreach (var (what, step, name, shot) in new[]
        {
            ("code", 0, "", "v26d-06-new-what"), ("code", 1, "", "v26d-07-new-game"), ("code", 2, "", "v26d-08-new-does"),
            ("code", 3, "", "v26d-09-new-name-empty"), ("files", 2, "Вечер с друзьями", "v26d-10-new-files-name"), ("world", 1, "", "v26d-11-new-world"),
        })
            Shot(s, shot, () => CreatorPage.DemoForm(what, step, name));
        // Шаг названия с раскрытым «Автор, версия и цвет ярлыка» — вид Expander в хабе.
        Shot(s, "v26d-12-new-name-more", () => CreatorPage.DemoFormMore("code", 3, "Золото за волны"));

        Shot(s, "v26d-13-devgames", () => new CreatorPage("ingame"), 9000);
        V23Shots.Scroll(s, 0.5);
        s.Pump(2000);
        s.Save("v26d-14-devgames-more");

        Shot(s, "v26d-15-guide", () => new CreatorPage("guide"));
        Shot(s, "v26d-16-guide-files", () => new CreatorPage("commands"));
        Shot(s, "v26d-17-guide-functions", () => new CreatorPage("functions"));

        // Среда ModScript: проект правок файлов (панель «Файлы» с «+» в заголовке, вкладки нижней панели).
        var classic = Projects.List().First();
        var x = XProjects.Adopt(classic);
        Shot(s, "v26d-18-studio", () => new XStudio(x.Id, "files", "problems", "code"), 1500);
        if (w.CurrentPage is XStudio studio)
        {
            studio.DemoRun();
            s.Pump(800);
            s.Save("v26d-19-studio-checked");
        }

        // Подтверждение удаления — красная кнопка «danger», а не янтарная главная.
        w.Navigate(() => new CreatorPage("home"));
        s.Pump(900);
        if (w.CurrentPage is CreatorPage mine) mine.DemoDeleteX();
        s.Pump(500);
        s.Save("v26d-20-delete");
        w.CloseDialog();

        // «Из шаблона» (другой способ начать): сетка по краю шапки, «Попробовать» янтарём.
        Shot(s, "v26d-21-templates", () => new CreatorPage("examples"));

        // Быстрый старт из блоков работает: проект создаётся и открывается в блоках.
        CreatorPage.QuickStart("gold");
        s.Pump(1500);
        s.Save("v26d-22-quickstart-blocks");

        // Светлая тема: те же экраны на токенах HubLook.
        var was = Settings.Data.Str("theme") ?? "store";
        Look.SetTheme("light");
        try
        {
            Shot(s, "v26d-23-light-mods", () => new CreatorPage("home"));
            Shot(s, "v26d-24-light-new", () => CreatorPage.DemoFormMore("code", 3, "Золото за волны"));
            Shot(s, "v26d-25-light-devgames", () => new CreatorPage("ingame"), 4000);
            Shot(s, "v26d-26-light-studio", () => new XStudio(x.Id, "files", "problems", "code"), 1500);
        }
        finally { Look.SetTheme(was); }
    }
}

public sealed partial class CreatorPage
{
    /// <summary>Для снимков: анкета с раскрытым блоком «Автор, версия и цвет ярлыка».</summary>
    internal static CreatorPage DemoFormMore(string what, int step, string name)
    {
        var page = DemoForm(what, step, name);
        _form.More = true;
        return page;
    }

    /// <summary>Для снимков: подтверждение удаления первого скрипта ModScript.</summary>
    internal void DemoDeleteX()
    {
        if (XProjects.List().FirstOrDefault() is { } p) DeleteX(p);
    }
}
