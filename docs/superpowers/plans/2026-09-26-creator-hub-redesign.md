# Новый Creator Hub — план работ

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Новые экраны Creator Hub в минималистичном стиле: логотип A, стартовая «Главная», «Мои моды» вместе с публикациями, понятный редактор, шаблоны и справка с оглавлением.

**Architecture:** Только слой `Views`. `CreatorPage` (partial class) делится на файлы по экранам; «начинка» (`Creator/*.cs`) не меняется. Общие элементы — `Ui.TabBar`, `Ui.Thumb`, `Ui.GameImage`, новый `CreatorLogo`.

**Tech Stack:** C# / .NET 8, Avalonia 11.3; интерфейс собирается в коде C#, стили в `App.axaml`, тексты в `Assets/strings5.json`.

**Spec:** `docs/superpowers/specs/2026-09-26-creator-hub-redesign-design.md`

## Global Constraints

- Все пути относительно `desktop/ModLaunch/`.
- `Creator/*.cs` не меняются.
- Id вкладок: `home`, `hub`, `mine`, `examples`, `docs`. `published` — псевдоним: открывает `mine` на части «Опубликованные».
- `new CreatorPage()` открывает `home`. Вызовы из `Program.cs` (`"mine"`, `"examples"`, `"docs"`, `"hub"`) должны работать как раньше.
- Логотип: значок кубика с искрой, градиент `#9D85FF` → `#EC4899`, скругление 27% стороны.
- Акцентная кнопка на экране одна: главное действие. Всё остальное — обычные кнопки или `ghost`.
- Тексты на двух языках (ru и en в `Assets/strings5.json`).
- Проекта с тестами нет, git нет. Проверка = сборка (`Ошибок: 0`) + снимки экранов + `--selfcheck`.
- Сборка и снимки: `& "<scratchpad>\shoot.ps1" <папка>` (из `desktop\ModLaunch`). Перед сборкой закрыть ModLaunch, **предварительно предупредив пользователя**.

## Review Focus

1. **Нет интернета / ошибка Hub** на «Главной»: вместо «Популярного» одна строка текста, остальная страница работает.
2. **Ноль проектов** — «Продолжить работу» не показывается, в «Моих модах» пустая карточка с кнопками.
3. **Не вошёл в аккаунт** — «Опубликованные» показывают кнопку входа, «Опубликовать мод» на «Главной» ведёт в окно публикации (оно само просит войти).
4. **Старые ссылки** `new CreatorPage("published")` и поиск из шапки (Search) не ломаются: поиск с «Главной» и «Моих модов» переводит на Hub.
5. **Несохранённый код**: переключение вкладок из редактора сохраняет проект, как раньше.

---

### Task 0: Резервная копия

- [ ] **Step 1:**

```powershell
Compress-Archive -Path Views\CreatorPage.cs, Views\HubViews.cs, Views\MainWindow.cs, Views\Ui.cs, App.axaml, Assets\strings5.json, Program.cs -DestinationPath ..\..\backups\creator-before-redesign.zip -Force
```

---

### Task 1: Логотип и тексты

**Files:**
- Modify: `Views/Ui.cs` (класс `Icons`: добавить `Creator`)
- Create: `Views/CreatorLogo.cs`
- Modify: `Views/MainWindow.cs:97` (`Icons.Tools` → `Icons.Creator`), `:587` (`Icons.Code` → `Icons.Creator`)
- Modify: `Assets/strings5.json`

**Interfaces:**
- Produces: `Icons.Creator` (string path), `CreatorLogo.Tile(double size) : Control`.

- [ ] **Step 1: Значок** — в `Icons` после `Tools`:

```csharp
/// <summary>Кубик мода с искрой — логотип Creator Hub.</summary>
public const string Creator = "M11 3.5l-7 4v8l7 4 7-4v-8z M4 7.5l7 4 7-4 M11 11.5v8 M19.5 1.5v4 M17.5 3.5h4";
```

- [ ] **Step 2: `Views/CreatorLogo.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ModLaunch.Views;

/// <summary>Логотип Creator Hub: белый кубик с искрой на фиолетово-розовой плитке.</summary>
public static class CreatorLogo
{
    public static Control Tile(double size) => new Border
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.27),
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#9D85FF"), 0), new GradientStop(Color.Parse("#EC4899"), 1) },
        },
        Child = Ui.Icon(Icons.Creator, size * 0.55, Brushes.White),
    };
}
```

- [ ] **Step 3: Боковая панель и палитра** — `MainWindow.cs:97` `RailIcon(Icons.Tools, …"Creator Hub")` → `RailIcon(Icons.Creator, …)`; `:587` `("Creator Hub", Icons.Code, …)` → `("Creator Hub", Icons.Creator, …)`.

- [ ] **Step 4: Тексты** — в `strings5.json`, раздел `ru`:

```json
"cr.subtitle": "Моды сообщества и мастерская",
"cr.tab.mine": "Мои моды",
"cr.tab.examples": "Шаблоны",
```

и сразу после `"cr.tab.docs": "Справка",` добавить:

```json
"cr.tab.home": "Главная",
"cr.home.hello": "Делайте свои моды и находите моды сообщества",
"cr.home.create": "Создать мод",
"cr.home.create.text": "Чистый лист с подсказками внутри",
"cr.home.publish": "Опубликовать мод",
"cr.home.publish.text": "Выложите архив мода в ModLaunch Hub",
"cr.home.continue": "Продолжить работу",
"cr.home.popular": "Популярное в Hub",
"cr.home.templates": "Начни с шаблона",
"cr.home.hubEmpty": "В Hub пока нет модов — станьте первым автором",
"cr.mine.projects": "Проекты",
"cr.mine.published": "Опубликованные",
"cr.docs.toc": "Разделы",
```

Раздел `en`: `cr.subtitle` → `"Community mods and workshop"`, `cr.tab.mine` → `"My mods"`, `cr.tab.examples` → `"Templates"`; после `"cr.tab.docs": "Reference",`:

```json
"cr.tab.home": "Home",
"cr.home.hello": "Make your own mods and discover community mods",
"cr.home.create": "Create a mod",
"cr.home.create.text": "A blank page with hints inside",
"cr.home.publish": "Publish a mod",
"cr.home.publish.text": "Upload a mod archive to ModLaunch Hub",
"cr.home.continue": "Continue working",
"cr.home.popular": "Popular in Hub",
"cr.home.templates": "Start from a template",
"cr.home.hubEmpty": "No mods in Hub yet — be the first author",
"cr.mine.projects": "Projects",
"cr.mine.published": "Published",
"cr.docs.toc": "Sections",
```

- [ ] **Step 5: Собрать** (`shots-cr1`). Ожидается: `Ошибок: 0`; на любом снимке у кнопки Creator Hub на боковой панели — кубик с искрой; на `10c` вкладка называется «Шаблоны».

---

### Task 2: Разделить `CreatorPage.cs` на файлы (без изменения поведения)

**Files:**
- Create: `Views/CreatorPage.Editor.cs` — методы `Editor`, `Insert`, `GoToLine`, `Msg`, `RenderCheck`, `Delete`, `BuildOpen`, `InstallOpen`, `InstallBuilt`, `Export`, `Publish`
- Create: `Views/CreatorPage.Templates.cs` — поле `_exCategory`, методы `Examples`, `Preview`
- Create: `Views/CreatorPage.Docs.cs` — метод `Docs`
- Create: `Views/CreatorPage.Mine.cs` — метод `Mine`
- Modify: `Views/CreatorPage.cs` — остаются поля, конструктор, `Search`, `Open`, `Save`, `Build`

Каждый новый файл:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // перенесённые методы — дословно
}
```

- [ ] **Step 1:** перенести методы дословно (скриптом по границам разделов `// ---- …`), удалить их из `CreatorPage.cs`.
- [ ] **Step 2: Собрать** (`shots-cr2`). Ожидается: `Ошибок: 0`, снимки `10a`–`10d`, `11b` не отличаются от `shots-cr1`.

---

### Task 3: Новая шапка, вкладки, «Главная» по умолчанию

**Files:**
- Modify: `Views/CreatorPage.cs` (поля, конструктор, `Search`, `Build`)
- Modify: `Views/HubViews.cs` (`PublishArchive`: `done`), `Views/CreatorPage.Editor.cs` (`Publish`: `done`)

**Interfaces:**
- Produces: поле `string _minePart = "projects";` (`"projects"` | `"published"`); метод `void Go(string tab)`.

- [ ] **Step 1: Поля и конструктор**

```csharp
string _minePart = "projects";

public CreatorPage(string tab = "home", string? project = null)
{
    _tab = tab;
    if (tab == "published") { _tab = "mine"; _minePart = "published"; }
    if (project is not null) { _tab = "mine"; Open(Projects.Get(project)); }
    // … остальное без изменений
}

public override void Search(string text) { _filter = text.Trim(); if (_open is null && _tab is "mine" or "home") _tab = "hub"; Build(); }

/// <summary>Перейти на вкладку; несохранённый код сохраняется.</summary>
void Go(string tab)
{
    if (_dirty) Save(quiet: true);
    _tab = tab;
    if (tab != "mine") _open = null;
    Build();
}
```

- [ ] **Step 2: `Build()`**

```csharp
public override void Build()
{
    var content = new StackPanel { Spacing = 24, Margin = new Thickness(40, 26, 40, 40), MaxWidth = 1640 };
    content.Children.Add(Ui.Row(14, CreatorLogo.Tile(44),
        Ui.Col(2, Ui.Text("Creator Hub", "h2"), Ui.Text(I18n.T("cr.subtitle"), "small muted"))));
    var tabs = new List<Control>();
    foreach (var (id, key, icon) in new[] { ("home", "cr.tab.home", Icons.Home), ("hub", "hub.tab", Icons.Globe), ("mine", "cr.tab.mine", Icons.Edit), ("examples", "cr.tab.examples", Icons.Wand), ("docs", "cr.tab.docs", Icons.Book) })
    {
        var tab = id;
        var b = Ui.Button(I18n.T(key), () => Go(tab), "tab", icon);
        if (_tab == id) b.Classes.Add("active");
        tabs.Add(b);
    }
    content.Children.Add(Ui.TabBar(tabs.ToArray()));
    content.Children.Add(_tab switch
    {
        "home" => Home(),
        "examples" => Examples(),
        "hub" => HubView(),
        "docs" => Docs(),
        _ => _open is null ? Mine() : Editor(),
    });
    Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}
```

Временно (до Task 4) в `CreatorPage.Home.cs`: `Control Home() => Mine();` — чтобы собиралось.

- [ ] **Step 3: Куда вести после публикации** — в `HubViews.PublishArchive` и `CreatorPage.Publish` заменить `_tab = "published";` на `_tab = "mine"; _minePart = "published";`. Вкладка `"published"` в `Build` больше не нужна: `PublishedView()` вызывается из `Mine` (Task 5).

- [ ] **Step 4: Собрать** (`shots-cr3`). Ожидается: `Ошибок: 0`; на `10a`–`10d`, `11b` — логотип 44 px и вкладки с подчёркиванием под шапкой.

---

### Task 4: «Главная»

**Files:**
- Create: `Views/CreatorPage.Home.cs`
- Modify: `Views/CreatorPage.Mine.cs` (добавить `NewProject`, `ProjectCard`), `Views/CreatorPage.Templates.cs` (выделить `TemplateCard`, `TryTemplate`)
- Modify: `Program.cs` (снимки `11i-creator-home`, `11j-creator-home-light`)

**Interfaces:**
- Produces: `void NewProject()`, `Control ProjectCard(Project p)`, `Control TemplateCard(Template t)`, `void TryTemplate(Template t)`.
- Consumes: `CreatorLogo.Tile`, `Grid3`, `Skeleton`, `LoadHub`, `PublishArchive`, `_hub`, `_hubLoading`, `_hubError` из `HubViews.cs`.

- [ ] **Step 1: `CreatorPage.Home.cs`**

```csharp
// (using — как в Task 2)
public sealed partial class CreatorPage
{
    /// <summary>Главная Creator Hub: создать или опубликовать мод, свои проекты, популярное в Hub, шаблоны.</summary>
    Control Home()
    {
        var col = new StackPanel { Spacing = 30 };

        var hello = Ui.Col(4, Ui.Text("Creator Hub", "h1"), Ui.Text(I18n.T("cr.home.hello"), "muted"));
        hello.VerticalAlignment = VerticalAlignment.Center;
        col.Children.Add(Ui.Row(20, CreatorLogo.Tile(72), hello));

        var actions = new UniformGrid { Columns = 2 };
        actions.Children.Add(BigAction(Icons.Plus, I18n.T("cr.home.create"), I18n.T("cr.home.create.text"), NewProject, primary: true));
        actions.Children.Add(BigAction(Icons.Upload, I18n.T("cr.home.publish"), I18n.T("cr.home.publish.text"), PublishArchive, primary: false));
        col.Children.Add(actions);

        var recent = Projects.List().OrderByDescending(p => p.Updated).Take(4).ToList();
        if (recent.Count > 0)
        {
            var grid = new UniformGrid { Columns = 4 };
            foreach (var p in recent) grid.Children.Add(ProjectCard(p));
            col.Children.Add(Section(I18n.T("cr.home.continue"), null, grid));
        }

        if (_hub is null && !_hubLoading && !Program.Screenshot) _ = LoadHub();
        Control popular;
        if (_hubLoading && _hub is null)
        {
            var sk = new UniformGrid { Columns = 3 };
            for (var i = 0; i < 3; i++) sk.Children.Add(Skeleton());
            popular = sk;
        }
        else if (_hubError is not null) popular = Ui.Text(I18n.T("hub.error"), "muted");
        else if ((_hub ?? []).Count == 0) popular = Ui.Text(I18n.T("cr.home.hubEmpty"), "muted");
        else popular = Grid3(Hub.Sort(_hub!, "trending").Take(6));
        col.Children.Add(Section(I18n.T("cr.home.popular"), () => Go("hub"), popular));

        var templates = new UniformGrid { Columns = 3 };
        foreach (var t in Templates.All.Take(3)) templates.Children.Add(TemplateCard(t));
        col.Children.Add(Section(I18n.T("cr.home.templates"), () => Go("examples"), templates));
        return col;
    }

    /// <summary>Заголовок раздела со ссылкой «Все» справа (если есть куда вести).</summary>
    static Control Section(string title, Action? all, Control body)
    {
        var head = new DockPanel();
        if (all is not null)
        {
            var more = Ui.Button(I18n.T("home.all"), all, "ghost");
            more.Padding = new Thickness(8, 4);
            DockPanel.SetDock(more, Dock.Right);
            head.Children.Add(more);
        }
        head.Children.Add(Ui.Text(title, "h2"));
        return Ui.Col(14, head, body);
    }

    /// <summary>Большая карточка-действие; акцентная — только одна на экране.</summary>
    static Control BigAction(string icon, string title, string text, Action click, bool primary)
    {
        var circle = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(24),
            Background = primary ? new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)) : Ui.Res("Surface3"),
            Child = Ui.Icon(icon, 22, primary ? Brushes.White : Ui.Res("Text")),
        };
        var sub = Ui.Text(text, "small", wrap: true);
        sub.Opacity = 0.8;
        var words = Ui.Col(3, Ui.Text(title, "h3"), sub);
        words.VerticalAlignment = VerticalAlignment.Center;
        var b = new Button
        {
            Padding = new Thickness(22), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = Ui.Row(16, circle, words),
        };
        b.Classes.Add(primary ? "primary" : "card-btn");
        b.Click += (_, _) => click();
        return b;
    }
}
```

- [ ] **Step 2: `NewProject` и `ProjectCard`** в `CreatorPage.Mine.cs`:

```csharp
void NewProject()
{
    var p = Projects.Create("Мой мод", Templates.ById("blank")!.Code);
    _tab = "mine";
    Open(p);
    Build();
}

/// <summary>Карточка проекта: картинка игры, название, игра, когда изменён.</summary>
Control ProjectCard(Project p)
{
    var game = GameCatalog.ById(p.Game);
    Control art = game is not null
        ? Ui.GameImage(game, 400)
        : new Border { Background = Ui.Res("Surface3"), Child = Ui.Icon(Icons.Creator, 30, Ui.Res("Faint")) };
    var top = new Border { Height = 96, ClipToBounds = true, CornerRadius = new CornerRadius(16, 16, 0, 0), Child = art };
    var body = Ui.Col(3,
        Ui.Text(p.Name, "h3"),
        Ui.Text(game?.Name ?? p.Game, "small muted"),
        Ui.Text(I18n.T("cr.edited", ("when", Ui.Ago(p.Updated))), "small muted"));
    body.Margin = new Thickness(14, 12, 14, 14);
    var card = new Button
    {
        Classes = { "tile" }, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Content = new StackPanel { Children = { top, body } },
    };
    card.Click += (_, _) => { _tab = "mine"; Open(p); Build(); };
    return card;
}
```

- [ ] **Step 3: `TemplateCard` / `TryTemplate`** в `CreatorPage.Templates.cs`: тело цикла в `Examples()` вынести в метод. Карточка целиком — кнопка `card-btn`; внутренней кнопки «Попробовать» нет (вложенная кнопка передала бы нажатие наружу и создала бы проект дважды), вместо неё строка-подсказка.

```csharp
void TryTemplate(Template t)
{
    var p = Projects.Create(I18n.T($"cr.ex.{t.Id}"), t.Code);
    _tab = "mine";
    Open(p);
    Build();
}

Control TemplateCard(Template t)
{
    var game = GameCatalog.ById(t.Game);
    var preview = new TextBlock
    {
        Text = Preview(t.Code),
        FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis,
    };
    var card = new Button
    {
        Classes = { "card-btn" }, Padding = new Thickness(18), Margin = new Thickness(0, 0, 14, 14),
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        Content = Ui.Col(10,
            Ui.Row(10, Ui.Thumb(game?.ArtUrl, game?.Name ?? t.Game, 36, 9), Ui.Col(2, Ui.Text(I18n.T($"cr.ex.{t.Id}"), "h3"), Ui.Text(game?.Name ?? t.Game, "small muted"))),
            Ui.Text(I18n.T($"cr.ex.{t.Id}.text"), "small muted", wrap: true),
            new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10), Child = preview },
            Ui.Row(6, Ui.Icon(Icons.Wand, 14, Ui.Res("Brand2")), Ui.Text(I18n.T("cr.ex.try"), "small brand"))),
    };
    card.Click += (_, _) => TryTemplate(t);
    return card;
}
```

В `Examples()` цикл становится: фильтр по `_filter` как был, затем `grid.Children.Add(TemplateCard(t));`.

- [ ] **Step 4: Снимки** — в `Program.cs` сразу после `Save("11b-hub");`:

```csharp
window.Navigate(() => new CreatorPage());
Save("11i-creator-home");
Look.SetTheme("light");
window.Navigate(() => new CreatorPage());
Save("11j-creator-home-light");
Look.SetTheme("dark");
```

- [ ] **Step 5: Собрать** (`shots-cr4`). Ожидается: `Ошибок: 0`. `11i`: логотип 72 px, две карточки (левая фиолетовая), «Продолжить работу» с «Дешёвыми семенами», 6 модов Hub, 3 шаблона. `11j`: то же в светлой теме, всё читается. `11a` (пустой Hub) не сломан.

---

### Task 5: «Мои моды» — проекты и опубликованные

**Files:**
- Modify: `Views/CreatorPage.Mine.cs` (`Mine`)
- Modify: `App.axaml` (стиль пунктирной карточки, если `Button.card-btn.dashed` не подходит)

- [ ] **Step 1: `Mine()`**

```csharp
Control Mine()
{
    var col = new StackPanel { Spacing = 16 };
    var parts = Ui.Row(6);
    foreach (var (id, key) in new[] { ("projects", "cr.mine.projects"), ("published", "cr.mine.published") })
    {
        var part = id;
        var chip = Ui.Button(I18n.T(key), () => { _minePart = part; Build(); }, "chip");
        if (_minePart == id) chip.Classes.Add("active");
        parts.Children.Add(chip);
    }
    var bar = new DockPanel();
    if (_minePart == "projects")
    {
        var folder = Ui.Button(I18n.T("cr.folder"), () => Actions.OpenFolder(Projects.Root), "ghost", Icons.Folder);
        DockPanel.SetDock(folder, Dock.Right);
        bar.Children.Add(folder);
    }
    bar.Children.Add(parts);
    col.Children.Add(bar);
    if (_minePart == "published") { col.Children.Add(PublishedView()); return col; }

    var projects = Projects.List().Where(p => _filter == "" || p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
    if (projects.Count == 0)
    {
        col.Children.Add(Ui.Card(Ui.Col(10,
            Ui.Text(I18n.T("cr.empty"), "h3"),
            Ui.Text(I18n.T("cr.empty.text"), "muted", wrap: true),
            Ui.Row(8, Ui.Button(I18n.T("cr.home.create"), NewProject, "primary", Icons.Plus), Ui.Button(I18n.T("cr.tab.examples"), () => Go("examples"), "", Icons.Wand))), 28));
        return col;
    }
    var grid = new UniformGrid { Columns = 4 };
    var add = new Button
    {
        Classes = { "card-btn", "dashed" }, Margin = new Thickness(0, 0, 14, 14), MinHeight = 180,
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        Content = Ui.Col(8, new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Ui.Res("Surface3"), Child = Ui.Icon(Icons.Plus, 20) }, Ui.Text(I18n.T("cr.new"), "h3")),
    };
    ((StackPanel)add.Content!).HorizontalAlignment = HorizontalAlignment.Center;
    add.Click += (_, _) => NewProject();
    grid.Children.Add(add);
    foreach (var p in projects) grid.Children.Add(ProjectCard(p));
    col.Children.Add(grid);
    return col;
}
```

- [ ] **Step 2: Собрать** (`shots-cr5`) и добавить в `Program.cs` после `Save("10b-creator-mine");`:

```csharp
window.Navigate(() => new CreatorPage("published"));
Save("10b2-creator-published");
```

Ожидается: `Ошибок: 0`. `10b`: чипы «Проекты / Опубликованные», пунктирная карточка «+ Новый мод» и карточка «Дешёвые семена» с картинкой Stardew. `10b2`: выбран чип «Опубликованные», внутри — просьба войти (в демо аккаунта нет).

---

### Task 6: Редактор и шаблоны — понятные кнопки

**Files:**
- Modify: `App.axaml` (после стилей `Button.chip.active…`)
- Modify: `Views/CreatorPage.Editor.cs` (класс чипов вставки)

- [ ] **Step 1: Стиль чипа-вставки**

```xml
<!-- Чипы-вставки в редакторе: всегда с фоном, чтобы было видно, что это кнопки -->
<Style Selector="Button.chip.snippet">
  <Setter Property="Background" Value="{StaticResource Surface2}"/>
  <Setter Property="Foreground" Value="{StaticResource Text}"/>
</Style>
```

- [ ] **Step 2:** в `Editor()` `Ui.Button("+ " + label, () => Insert(t), "chip")` → `… "chip snippet")`. `ScrollViewer` вокруг чипов: `HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden`.

- [ ] **Step 3: Собрать** (`shots-cr6`). `10a`: чипы `+ needs` … `+ copy` на сером фоне, видно, что это кнопки. `10c`: в шаблонах нет фиолетовых кнопок, внизу карточек фиолетовая строка «✦ Попробовать».

---

### Task 7: Hub — хэштеги с прокруткой, простые цифры

**Files:**
- Modify: `Views/HubViews.cs` (`HubView`, `Stat`, `PublishedView`)

- [ ] **Step 1:** у `ScrollViewer` с тегами `HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden`.
- [ ] **Step 2: `Stat` без плитки-иконки** — заменить метод и убрать первый аргумент во всех 8 вызовах (`Stat(Icons.X, a, b)` → `Stat(a, b)`):

```csharp
static Control Stat(string value, string label) => Ui.Col(0, Ui.Text(value, "h2"), Ui.Text(label, "small muted"));
```

В двух местах, где стоят `Ui.Row(22, Stat…)`, заменить промежуток на `Ui.Row(36, …)`.

- [ ] **Step 3: Собрать** (`shots-cr7`). `11b`: над модами четыре числа с подписями без фиолетовых квадратиков; строка тегов без серой полосы прокрутки.

---

### Task 8: Справка с оглавлением

**Files:**
- Modify: `Views/CreatorPage.Docs.cs`

- [ ] **Step 1:**

```csharp
static Control Docs()
{
    var sections = new StackPanel { Spacing = 12 };
    var toc = new StackPanel { Spacing = 2 };
    var intro = Ui.Card(Ui.Col(8, Ui.Text(I18n.T("cr.docs.title"), "h3"), Ui.Text(I18n.T("cr.docs.intro"), "muted", wrap: true)), 20);
    sections.Children.Add(intro);
    toc.Children.Add(TocItem(I18n.T("cr.docs.title"), intro));
    foreach (var group in new[]
    {
        ("cr.docs.basics", new[] { "mod", "version", "author", "about", "game", "icon", "needs" }),
        ("cr.docs.logic", new[] { "let", "if", "else", "for", "when", "print" }),
        ("cr.docs.stardew", new[] { "edit", "entry", "dialogue", "mail", "image" }),
        ("cr.docs.bepinex", new[] { "config", "ini", "copy", "write", "json" }),
    })
    {
        var rows = new StackPanel { Spacing = 10 };
        foreach (var cmd in group.Item2)
            rows.Children.Add(Ui.Col(3,
                new SelectableTextBlock { Text = I18n.T($"cr.doc.{cmd}.syntax"), FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 13, Foreground = Ui.Res("Brand2") },
                Ui.Text(I18n.T($"cr.doc.{cmd}"), "small muted", wrap: true)));
        var card = Ui.Card(Ui.Col(12, Ui.Text(I18n.T(group.Item1), "h3"), rows), 20);
        sections.Children.Add(card);
        toc.Children.Add(TocItem(I18n.T(group.Item1), card));
    }
    var left = Ui.Col(8, Ui.Text(I18n.T("cr.docs.toc"), "small muted"), toc);
    var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), ColumnSpacing = 24 };
    grid.Children.Add(left);
    Grid.SetColumn(sections, 1);
    grid.Children.Add(sections);
    return grid;
}

/// <summary>Пункт оглавления: прокручивает страницу к разделу.</summary>
static Control TocItem(string title, Control target)
{
    var b = Ui.Button(title, () => target.BringIntoView(), "tab");
    b.HorizontalAlignment = HorizontalAlignment.Stretch;
    b.HorizontalContentAlignment = HorizontalAlignment.Left;
    return b;
}
```

- [ ] **Step 2: Собрать** (`shots-cr8`). `10d`, `11h`: слева «Разделы» и пять пунктов, справа карточки справки.

---

### Task 9: Итоговая проверка и новый установщик

- [ ] **Step 1:** полный набор снимков (`shots-cr-after`); сравнить с `shots-fix` (последние «до»): `10a`, `10b`, `10b2`, `10c`, `10d`, `10k-black-green`, `11a`, `11b`, `11i`, `11j`, `11h`.
- [ ] **Step 2:** `--selfcheck` → `ALL OK`.
- [ ] **Step 3:** Review Focus 1–5 — проверить по коду (ветки `_hubError`, пустой `Projects.List()`, `SignedIn`, конструктор с `"published"`, `Search`, `Go` → `Save`).
- [ ] **Step 4:** `dotnet publish -c Release -r win-x64 -o "$env:TEMP\mlpublish"`, скопировать `ModLaunch.exe` на рабочий стол как `ModLaunch-Setup (от Claude Code).exe` (заменить старый), `--selfcheck` на опубликованном exe.
