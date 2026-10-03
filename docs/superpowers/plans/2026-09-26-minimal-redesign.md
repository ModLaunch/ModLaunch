# Минималистичный редизайн — план работ

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Сделать интерфейс C#-версии ModLaunch спокойным и минималистичным: меньше рамок, больше воздуха, ничего не обрезается криво и не повторяется.

**Architecture:** Основная часть — общие стили в `App.axaml` и общие элементы в `Views/Ui.cs`, так что обновляются все экраны сразу. Дальше точечные правки: полоса вкладок (новый `Ui.TabBar`), правая панель (`Aside.cs`, `MainWindow.cs`), заглушка иконки (`Ui.Thumb`), пути в настройках, отступы страниц.

**Tech Stack:** C# / .NET 8, Avalonia UI 11.3 (Fluent), интерфейс собирается в коде C#, стили в `App.axaml`.

**Spec:** `docs/superpowers/specs/2026-09-26-minimal-redesign-design.md`

## Global Constraints

- Все пути ниже указаны относительно `desktop/ModLaunch/`.
- Функции, тексты и порядок экранов не меняются, меняется только вид.
- Акцент `#7C5CFF` и тёмная тема по умолчанию остаются. Темы `dark`, `black`, `light` и выбор акцента должны работать.
- Electron-версию (`src/`), Big Picture (`bp-*`) и оверлей не трогаем.
- Цвета — только через ресурсы (`{StaticResource ...}` / `Ui.Res(...)`), без новых жёстко заданных цветов (кроме `Transparent`).
- Проекта с тестами нет. Проверка = сборка без ошибок + снимки экранов + `--selfcheck`.
- Папка не git-репозиторий: вместо коммитов в конце каждой задачи делаем снимки экранов.

**Сборка и снимки (используются в каждой задаче):**

```powershell
# из папки desktop\ModLaunch; ModLaunch.exe перед этим должен быть закрыт
dotnet build -c Debug -o "$env:TEMP\mlbuild"
$env:DOTNET_ROLL_FORWARD = "Major"   # у пользователя стоит .NET 9, а не 8
& "$env:TEMP\mlbuild\ModLaunch.exe" --screenshot "<scratchpad>\shots-taskN"
```

Ожидается: `Ошибок: 0` в конце сборки и строки `saved 1-home` … `saved 8-home-en`.

## Review Focus

1. **Светлая тема** (`10j-home-light`): без обводок карточки и поля ввода должны оставаться различимыми на светлом фоне, а активный чип — заметным.
2. **Узкое окно:** если вкладки не влезают, они прокручиваются вбок (колесом мыши тоже) и не переносятся на вторую строку.
3. **Очень длинные названия модов** в правой панели и списках обрезаются с «…» и не наезжают на кнопку.
4. **Пользователь, который уже открыл панель** (`asideOpen = true` в настройках), видит её открытой и после обновления.
5. **Фокус с клавиатуры (Tab)** остаётся видимым на кнопках без обводки.

Каждый пункт проверяется в задаче, к которой он относится (см. шаги «Проверить»).

---

### Task 0: Резервная копия и снимки «до»

**Files:**
- Create: `../../backups/desktop-before-redesign.zip`

- [ ] **Step 1: Сделать архив исходников**

```powershell
New-Item -ItemType Directory -Force ..\..\backups | Out-Null
Compress-Archive -Path * -DestinationPath ..\..\backups\desktop-before-redesign.zip -Force
```

Ожидается: архив размером несколько МБ (папки `bin`/`obj`, если есть, тоже попадут — это нормально).

- [ ] **Step 2: Сохранить снимки «до»**

Уже сняты в `<scratchpad>\shots`. Переименовать в `<scratchpad>\shots-before`.

---

### Task 1: Общие стили — меньше рамок, спокойные чипы, крупнее заголовки

**Files:**
- Modify: `App.axaml` (стили `TextBlock.h1/h2`, `Border.card`, `Button`, `Button:pointerover`, `Button.chip*`, `Button.friends-pill`, `Button.tile*`, `TextBox*`, `ComboBox`, `Button.card-btn*`)

**Interfaces:**
- Produces: стили без обводок; активный `Button.chip.active` — фон `Surface3`, текст `Text`.

- [ ] **Step 1: Заголовки крупнее**

```xml
<Style Selector="TextBlock.h1"><Setter Property="FontSize" Value="32"/><Setter Property="FontWeight" Value="Bold"/></Style>
<Style Selector="TextBlock.h2"><Setter Property="FontSize" Value="22"/><Setter Property="FontWeight" Value="Bold"/></Style>
```

- [ ] **Step 2: Карточка без обводки**

```xml
<Style Selector="Border.card">
  <Setter Property="Background" Value="{StaticResource Surface}"/>
  <Setter Property="BorderThickness" Value="0"/>
  <Setter Property="CornerRadius" Value="18"/>
</Style>
```

- [ ] **Step 3: Кнопки без обводки**

В `<Style Selector="Button">` заменить `BorderThickness` `1` на `0`. В `Button:pointerover /template/ ContentPresenter` заменить `BorderBrush` `#3A4150` на `Transparent`.

- [ ] **Step 4: Чипы спокойнее**

```xml
<Style Selector="Button.chip">
  <Setter Property="CornerRadius" Value="999"/>
  <Setter Property="Padding" Value="14,7"/>
  <Setter Property="FontSize" Value="13"/>
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="Foreground" Value="{StaticResource Muted}"/>
</Style>
<Style Selector="Button.chip.active">
  <Setter Property="Background" Value="{StaticResource Surface3}"/>
  <Setter Property="BorderBrush" Value="Transparent"/>
  <Setter Property="Foreground" Value="{StaticResource Text}"/>
</Style>
<Style Selector="Button.chip.active:pointerover /template/ ContentPresenter">
  <Setter Property="Background" Value="{StaticResource Surface3}"/>
  <Setter Property="Foreground" Value="{StaticResource Text}"/>
</Style>
```

- [ ] **Step 5: Плитки, кнопки-карточки, «Друзья» без обводки**

- `Button.friends-pill`: заменить `BorderBrush` на `<Setter Property="BorderThickness" Value="0"/>`.
- `Button.tile`: `BorderThickness` → `0`. Стиль `Button.tile:pointerover /template/ ContentPresenter` заменить на:
  ```xml
  <Style Selector="Button.tile:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="{StaticResource Surface2}"/></Style>
  ```
- Первый `Button.card-btn`: `BorderThickness` → `0`. В `Button.card-btn:pointerover /template/ ContentPresenter` заменить `BorderBrush` `#3A4150` на `Transparent`.

- [ ] **Step 6: Поля ввода без рамки (рамка появляется только при фокусе)**

```xml
<Style Selector="TextBox">
  <Setter Property="CornerRadius" Value="12"/>
  <Setter Property="Background" Value="{StaticResource Surface}"/>
  <Setter Property="BorderBrush" Value="Transparent"/>
  <Setter Property="Padding" Value="12,9"/>
  <Setter Property="VerticalContentAlignment" Value="Center"/>
</Style>
<Style Selector="TextBox:pointerover /template/ Border#PART_BorderElement">
  <Setter Property="Background" Value="{StaticResource Surface2}"/>
  <Setter Property="BorderBrush" Value="Transparent"/>
</Style>
```

Стиль `TextBox:focus` (рамка `Brand`) оставить. В `ComboBox` заменить `BorderBrush` на `Transparent`.

- [ ] **Step 7: Собрать и снять экраны**

Команды из Global Constraints, папка `shots-task1`. Ожидается: `Ошибок: 0`.

- [ ] **Step 8: Проверить**

Открыть `1-home`, `2-installed`, `4-catalog`, `9a-mod`, `10j-home-light`, `10k-black-green`. Должно быть:
- у карточек и кнопок нет светлой обводки;
- активный чип («Все», «Thunderstore») серо-светлый, а не фиолетовый;
- кнопки «Играть» и «Установить» по-прежнему фиолетовые;
- в светлой теме карточки видны на фоне, поле поиска различимо (Review Focus 1).

Если в светлой теме поле поиска сливается с фоном, поставить в `TextBox` `Background` = `{StaticResource Surface2}`.

---

### Task 2: Вкладки одной строкой с подчёркиванием

**Files:**
- Modify: `App.axaml` (добавить стили `Button.tab.line` после `Button.tab.active`)
- Modify: `Views/Ui.cs` (добавить `Ui.TabBar`)
- Modify: `Views/GamePage.cs:204-212`, `Views/ModPage.Tabs.cs:37-49`, `Views/ModsCenterPage.cs:44-55`

**Interfaces:**
- Produces: `public static Control Ui.TabBar(params Control[] tabs)` — добавляет каждой кнопке класс `line` и кладёт их в строку с прокруткой вбок и тонкой линией снизу.
- Не трогаем: вертикальные списки с классом `tab` (настройки, палитра команд, Creator) — у них нет класса `line`.

- [ ] **Step 1: Стили подчёркнутых вкладок в `App.axaml`**

Сразу после `<Style Selector="Button.tab.active">…</Style>`:

```xml
<!-- Вкладки-строка: без фона, активная подчёркнута акцентом -->
<Style Selector="Button.tab.line">
  <Setter Property="CornerRadius" Value="0"/>
  <Setter Property="Padding" Value="2,12"/>
  <Setter Property="Margin" Value="0,0,24,0"/>
  <Setter Property="BorderThickness" Value="0,0,0,2"/>
  <Setter Property="BorderBrush" Value="Transparent"/>
</Style>
<Style Selector="Button.tab.line:pointerover /template/ ContentPresenter">
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="BorderBrush" Value="{StaticResource Line}"/>
  <Setter Property="Foreground" Value="{StaticResource Text}"/>
</Style>
<Style Selector="Button.tab.line.active">
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="BorderBrush" Value="{StaticResource Brand}"/>
  <Setter Property="Foreground" Value="{StaticResource Text}"/>
</Style>
<Style Selector="Button.tab.line.active:pointerover /template/ ContentPresenter">
  <Setter Property="BorderBrush" Value="{StaticResource Brand}"/>
</Style>
```

И после строки `Window.juicy Button.tab:pointerover, Window.juicy Button.chip:pointerover` — чтобы подчёркнутые вкладки не «прыгали»:

```xml
<Style Selector="Window.juicy Button.tab.line:pointerover"><Setter Property="RenderTransform" Value="translateY(0px)"/></Style>
```

- [ ] **Step 2: `Ui.TabBar` в `Views/Ui.cs`** (после метода `Card`)

```csharp
/// <summary>Вкладки одной строкой с подчёркиванием; если не влезают — строка прокручивается вбок.</summary>
public static Control TabBar(params Control[] tabs)
{
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    foreach (var t in tabs)
    {
        if (t is Button b) b.Classes.Add("line");
        row.Children.Add(t);
    }
    return new Border
    {
        BorderBrush = Res("Line"),
        BorderThickness = new Thickness(0, 0, 0, 1),
        Child = new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        },
    };
}
```

- [ ] **Step 3: `GamePage.Tabs()`** — заменить `var bar = new WrapPanel { Children = { … } };` и `return new Border { Classes = { "card" }, … Child = bar … };` на:

```csharp
return Ui.TabBar(
    Tab("installed", I18n.T("games.downloads"), Icons.List, _g.ModCount + updates),
    _g.Def.HasCatalog ? Tab("catalog", I18n.T("games.market"), Icons.Bag, _total > 0 ? I18n.Compact(_total) : null) : new Control { IsVisible = false },
    Tab("profiles", I18n.T("games.profiles"), Icons.Layers, profiles > 0 ? profiles.ToString() : null),
    Tab("saves", I18n.T("games.saves"), Icons.Shield, saves > 0 ? saves.ToString() : null),
    Tab("tools", I18n.T("v4.tools"), Icons.Settings, Features.Tools.For(_g.Def.Id).Count is > 0 and var t ? t.ToString() : null),
    Tab("log", I18n.T("games.log"), Icons.Alert, null));
```

- [ ] **Step 4: `ModPage.Tabs()`** — заменить `var bar = new WrapPanel();`, цикл `foreach … bar.Children.Add(t);` и `return new Border { … Child = bar … };` на:

```csharp
return Ui.TabBar(
    Tab("about", I18n.T("mt.about"), Icons.Book),
    Tab("files", I18n.T("mt.files"), Icons.Layers, _versions?.Count),
    Tab("changes", I18n.T("mt.changes"), Icons.Refresh),
    Tab("reqs", I18n.T("mt.reqs"), Icons.Link, d?.Requirements.Count),
    Tab("images", I18n.T("mt.images"), Icons.Image, d?.Images.Count),
    Tab("videos", I18n.T("mt.videos"), Icons.Video, d?.Videos.Count),
    Tab("reviews", _brief.Source == "hub" ? I18n.T("mt.comments") : I18n.T("mt.reviews"), Icons.Chat, reviews),
    Tab("stats", I18n.T("mt.stats"), Icons.Chart));
```

- [ ] **Step 5: `ModsCenterPage`** — заменить `var bar = new WrapPanel { Children = { … } };` и строку `content.Children.Add(new Border { Classes = { "card" }, … Child = bar … });` на:

```csharp
content.Children.Add(Ui.TabBar(
    Tab("updates", I18n.T("mc.updates"), Icons.Refresh, installedUpdates + Tracking.Updates.Count),
    Tab("tracked", I18n.T("mc.tracked"), Icons.Bell, Tracking.All().Count),
    Tab("favorites", I18n.T("mc.favorites"), Icons.Heart, Favorites.All().Count),
    Tab("recent", I18n.T("mc.recent"), Icons.Eye, Recent.All().Count),
    Tab("history", I18n.T("mc.history"), Icons.Download, History.All().Count),
    Tab("hidden", I18n.T("mc.hidden"), Icons.EyeOff, Blocklist.All().Count)));
```

(Если между `Tab(...)` в исходнике есть ещё вкладки, перенести их все в том же порядке.)

- [ ] **Step 6: Собрать и снять экраны** (`shots-task2`). Ожидается: `Ошибок: 0`.

- [ ] **Step 7: Проверить**

`2-installed`, `4-catalog`, `9a-mod`, `12c-mods-center`:
- вкладки в одну строку, «Лог» и «Статистика» больше не на второй строке;
- у активной вкладки фиолетовая линия снизу, вокруг вкладок нет карточки;
- `7-settings` не изменился (вертикальный список, как был).

Review Focus 2: запустить `ModLaunch.exe` (демо: `--demo`, если есть; иначе обычный запуск), открыть страницу мода, сузить окно так, чтобы вкладки не влезали. Строка должна прокручиваться колесом мыши. Если колесо не крутит строку, добавить в `TabBar` у `ScrollViewer` обработчик:

```csharp
viewer.PointerWheelChanged += (_, e) =>
{
    viewer.Offset = viewer.Offset.WithX(viewer.Offset.X - e.Delta.Y * 60);
    e.Handled = true;
};
```

(предварительно вынести `new ScrollViewer { … }` в переменную `viewer`).

---

### Task 3: Правая панель — скрыта по умолчанию, без повторов и обрезаний

**Files:**
- Modify: `Views/MainWindow.cs:257`, `:375`, `:381`
- Modify: `Views/Aside.cs` (`Stat`, `ModLine`, `Mod`)

**Interfaces:**
- Consumes: ничего нового.
- Produces: `Settings.Data.Bool("asideOpen", false)` — новое значение по умолчанию.

- [ ] **Step 1: По умолчанию закрыта**

В `MainWindow.cs` во всех трёх местах заменить `Settings.Data.Bool("asideOpen", true)` на `Settings.Data.Bool("asideOpen", false)`. Для тех, кто уже переключал панель, значение сохранено в настройках, поэтому их выбор не меняется (Review Focus 4).

- [ ] **Step 2: Цифры без рамки, подпись переносится, а не обрезается**

```csharp
public static Control Stat(string value, string label) => new Border
{
    Background = Ui.Res("Surface"), CornerRadius = new CornerRadius(12), Padding = new Thickness(6, 10),
    Child = Ui.Col(2,
        new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
        new TextBlock { Text = label, FontSize = 11.5, Foreground = Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }),
};
```

- [ ] **Step 3: Длинное название мода не наезжает на кнопку**

В `ModLine` заменить строку `var row = new DockPanel { Children = { install, Ui.Row(10, Ui.Thumb(mod.Icon, mod.Name, 38, 9), info) } };` на:

```csharp
var thumb = Ui.Thumb(mod.Icon, mod.Name, 38, 9);
thumb.Margin = new Thickness(0, 0, 10, 0);
DockPanel.SetDock(thumb, Dock.Left);
install.Margin = new Thickness(8, 0, 0, 0);
var row = new DockPanel { Children = { install, thumb, info } };
```

(В горизонтальном `StackPanel` ширина бесконечная, поэтому «…» не срабатывала. `DockPanel` даёт тексту только оставшееся место.)

Полное название — в подсказке: после строки `var b = new Button { Classes = { "ghost" }, … };` добавить:

```csharp
ToolTip.SetTip(b, mod.Name);
```

- [ ] **Step 4: Убрать повторы в панели мода**

В `Aside.Mod` удалить:
- строки с `aside.downloads`, `aside.updated` и `mt.reqs` (загрузки, дата обновления и число требований уже есть в шапке мода);
- весь блок `if (mod.Url is not null) { var open = …; col.Children.Add(open); }` (кнопка «Открыть на …» уже есть в шапке, `ModPage.cs:115`).

- [ ] **Step 5: Собрать и снять экраны** (`shots-task3`). Ожидается: `Ошибок: 0`.

- [ ] **Step 6: Проверить**

- На `1-home`, `2-installed`, `9a-mod` правой панели нет, контент шире.
- Кнопка панели сверху не подсвечена. Нажатие открывает панель (проверить в живом запуске).
- В открытой панели у игры подписи «установлено / включено / проблем» видны целиком, «Configuration Manager for BepInEx» обрезан с «…» и не заходит на кнопку (Review Focus 3).
- В панели мода нет кнопки «Открыть на Nexus Mods» и повторов цифр.

---

### Task 4: Спокойная заглушка вместо букв у модов

**Files:**
- Modify: `Views/Ui.cs` (`Thumb`)
- Modify: `Views/Aside.cs:115`, `Views/FriendsDock.cs:97`, `Views/FriendsDock.cs:159`, `Views/ModPage.Tabs.cs:116` (аватары людей оставляют буквы)

**Interfaces:**
- Produces: `public static Control Ui.Thumb(string? url, string name, double size, double radius = 12, int decode = 160, bool person = false)`

- [ ] **Step 1: Новый параметр `person` и заглушка-иконка**

В `Ui.Thumb` поменять сигнатуру на `…, int decode = 160, bool person = false)` и заменить создание `fallback` на:

```csharp
Border fallback;
if (person)
{
    var initials = string.Concat(name.Split(' ', '-', '_').Where(w => w.Length > 0 && char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
    var hue = (int)(name.Aggregate(17u, (h, c) => h * 31 + c) % 360);
    fallback = new Border
    {
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(HslColor(hue, 0.55, 0.42), 0), new GradientStop(HslColor((hue + 50) % 360, 0.6, 0.3), 1) },
        },
        Child = new TextBlock { Text = initials, FontWeight = FontWeight.Bold, FontSize = size * 0.3, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };
}
else
{
    // Мод или игра без картинки: нейтральный фон и значок коробки вместо цветных букв.
    fallback = new Border { Background = Res("Surface3"), Child = Icon(Icons.Package, Math.Max(14, size * 0.36), Res("Faint")) };
}
```

(Строки `var initials = …` и `var hue = …` в начале метода удалить — они переехали внутрь `if`.)

- [ ] **Step 2: Аватарам людей оставить буквы**

Добавить `person: true` в четыре вызова:

```csharp
// Aside.cs:115
Ui.Thumb(null, profile.Name ?? "?", 44, 22, person: true)
// FriendsDock.cs:97
Ui.Thumb(null, online[i].Name, 26, 13, person: true)
// FriendsDock.cs:159
Ui.Thumb(null, name, 36, 18, person: true)
// ModPage.Tabs.cs:116
Ui.Thumb(null, r.Name, 40, 10, person: true)
```

- [ ] **Step 3: Собрать и снять экраны** (`shots-task4`). Ожидается: `Ошибок: 0`.

- [ ] **Step 4: Проверить**

- `9a-mod`, `4-catalog`, `1-home` (карусель): вместо «DM» и «M» — серый фон со значком коробки.
- `9b-friends`, `9c-account`: у людей по-прежнему цветные буквы.
- `10j-home-light`: значок виден на светлом фоне.

---

### Task 5: Короткие пути в настройках

**Files:**
- Modify: `Views/Ui.cs` (добавить `ShortPath`)
- Modify: `Views/GamePage.cs:124`, `:183-187` (удалить свой `ShortPath`, звать `Ui.ShortPath`)
- Modify: `Views/SettingsPage.cs:350`, `:356`

**Interfaces:**
- Produces: `public static string Ui.ShortPath(string path)` — `…\<предпоследняя>\<последняя>`; путь из двух частей и короче возвращается как есть.

- [ ] **Step 1: Перенести `ShortPath` в `Ui`** (после `Ago`). В `Ui.cs` имя `Path` занято фигурами, поэтому пишем `System.IO.Path`:

```csharp
/// <summary>Короткий путь для показа: «…\Games\Subnautica». Полный — в подсказке.</summary>
public static string ShortPath(string path)
{
    var sep = System.IO.Path.DirectorySeparatorChar;
    var parts = path.Split(sep, StringSplitOptions.RemoveEmptyEntries);
    return parts.Length <= 2 ? path : "…" + sep + string.Join(sep, parts[^2..]);
}
```

В `GamePage.cs` удалить метод `static string ShortPath(string path) { … }`, а в строке 124 заменить `ShortPath(_g.Path)` на `Ui.ShortPath(_g.Path)`.

- [ ] **Step 2: Настройки → Игры**

В `SettingsPage.GamesTab` заменить `Detect.Found => g.Path!,` на `Detect.Found => Ui.ShortPath(g.Path!),`. Сразу после строки `var info = Ui.Col(4, …);` добавить:

```csharp
if (g.Path is not null) ToolTip.SetTip(info, g.Path);
```

- [ ] **Step 3: Собрать и снять экраны** (`shots-task5`). Ожидается: `Ошибок: 0`.

- [ ] **Step 4: Проверить**

`7-settings`: под названием игры `…\Games\Subnautica` вместо `C:\Users\…\Temp\…`. `2-installed`: путь в шапке игры как раньше (`…\Games\Subnautica`).

---

### Task 6: Больше воздуха на страницах игры и мода

**Files:**
- Modify: `Views/GamePage.cs:81`, `Views/ModPage.cs:65`

- [ ] **Step 1: Отступы**

```csharp
// GamePage.cs:81
var content = new StackPanel { Spacing = 24, Margin = new Thickness(40, 26, 40, 40), MaxWidth = 1640 };
// ModPage.cs:65
var col = new StackPanel { Spacing = 24, Margin = new Thickness(40, 26, 40, 40), MaxWidth = 1500 };
```

- [ ] **Step 2: Собрать и снять экраны** (`shots-task6`). Ожидается: `Ошибок: 0`.

- [ ] **Step 3: Проверить**

`2-installed`, `9a-mod`: между шапкой, вкладками и содержимым заметно больше места, ничего не обрезается по краям. `14a-wide-home`, `14b-wide-library`: на широком окне всё выглядит нормально.

---

### Task 7: Итоговая проверка

- [ ] **Step 1: Полный набор снимков** (`shots-after`) и сравнение с `shots-before` по всем 54 файлам. Особое внимание: `10j-home-light`, `10k-black-green`, `13a-bigpicture`, `9g-overlay` (последние два не должны измениться, кроме заглушек иконок).

- [ ] **Step 2: Самопроверка**

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
& "$env:TEMP\mlbuild\ModLaunch.exe" --selfcheck
```

Ожидается: тот же результат, что до изменений. Сетевые проверки могут падать без интернета — это не связано с дизайном.

- [ ] **Step 3: Фокус с клавиатуры** (Review Focus 5): в живом запуске нажимать Tab на главной. Рамка фокуса должна быть видна на кнопках. Если не видна, добавить в `App.axaml`:

```xml
<Style Selector="Button:focus-visible /template/ ContentPresenter">
  <Setter Property="BorderBrush" Value="{StaticResource Brand}"/>
  <Setter Property="BorderThickness" Value="2"/>
</Style>
```

- [ ] **Step 4: Показать пользователю «было / стало»** по главным экранам: `1-home`, `2-installed`, `4-catalog`, `7-settings`, `9a-mod`, `10j-home-light`.
