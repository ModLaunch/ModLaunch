using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Окна-меню Big Picture, которыми управляют геймпадом: мои моды, каталог с установкой, обновления,
/// профили, друзья, загрузки и «Ещё». Все — один и тот же список поверх экрана; B возвращает на шаг назад.
/// </summary>
public sealed partial class BigPictureWindow
{
    /// <summary>Перерисовать открытое окно, когда что-то поменялось (загрузка закончилась, друг вышел в сеть).</summary>
    Action? _modalRefresh;
    int _modalToken;

    // ---------------------------------------------------------------- наблюдение за службами

    void WatchServices()
    {
        Jobs.Changed += OnJobChanged;
        Jobs.Finished += OnJobFinished;
        Social.Friends.Changed += OnFriendsChanged;
        RefreshPills();
        if (Program.Screenshot) return;
        _ = Task.Run(async () => { try { await Social.Friends.Refresh(); } catch { } }).ContinueWith(_ => Dispatcher.UIThread.Post(RefreshPills));
    }

    void UnwatchServices()
    {
        Jobs.Changed -= OnJobChanged;
        Jobs.Finished -= OnJobFinished;
        Social.Friends.Changed -= OnFriendsChanged;
    }

    void OnFriendsChanged() => Dispatcher.UIThread.Post(() => { RefreshPills(); if (_modalKind == "friends") _modalRefresh?.Invoke(); });

    DateTime _lastRefresh;

    void OnJobChanged(Job job)
    {
        RefreshPills();
        // Список загрузок обновляем не чаще раза в 0,4 с: прогресс приходит очень часто.
        if (_modalKind == "downloads" && DateTime.UtcNow - _lastRefresh > TimeSpan.FromMilliseconds(400)) { _lastRefresh = DateTime.UtcNow; _modalRefresh?.Invoke(); }
    }

    void OnJobFinished(Job job)
    {
        if (_modalKind is "downloads" or "catalog") _modalRefresh?.Invoke();
        Toast(job.Status == JobStatus.Done ? I18n.T("bp.installed", ("name", job.Title)) : I18n.T("bp.failed", ("name", job.Title), ("error", job.Error ?? "")));
        if (_zone != Zone.Modal && Selected is not null) Select(_game);
        else if (Selected is { } g) RenderChips(g);
    }

    void RefreshPills()
    {
        var running = Jobs.Running;
        if (_downPill is not null) _downPill.IsVisible = running > 0;
        _downText.Text = running.ToString();
        var view = Social.Friends.View();
        _friendsText.Text = view.SignedIn ? view.Friends.Count(f => f.State != "offline").ToString() : "–";
    }

    /// <summary>Открыть окно с новым содержимым; устаревшие ответы из сети (после закрытия) отбрасываются по жетону.</summary>
    int NewToken() => ++_modalToken;

    // ---------------------------------------------------------------- мои моды

    void OpenMods(GameState g)
    {
        NewToken();
        var registry = g.Registry;
        var updates = ModUpdates.Found.TryGetValue(g.Def.Id, out var found) ? found : [];
        var items = new List<ModalItem>();
        foreach (var m in registry?.List() ?? [])
        {
            var id = m.Str("id");
            if (id is null || m.Bool("missing")) continue;
            var on = m.Bool("enabled", true);
            var update = updates.FirstOrDefault(u => u.RecordId == id);
            var sub = m.Str("version") is { Length: > 0 } v ? "v" + v : null;
            if (update is not null) sub = $"v{update.Current} → v{update.Latest}";
            items.Add(new ModalItem(m.Str("name") ?? id, sub, () =>
            {
                try
                {
                    registry!.SetEnabled(id, !on);
                    if (m.Str("kind") == "preset") Mods.Installer.SyncPreset(registry, !on ? registry.Get(id) : null);
                }
                catch (Exception e) { Toast(Jobs.Explain(e)); }
                AppState.Notify();
                var keep = _modalIndex;
                OpenMods(g);
                _modalIndex = Math.Min(keep, _modalItems.Count - 1);
                RenderModal();
            }, on));
        }
        ShowModal(I18n.T("bp.mods.title", ("game", g.Def.Name)), items, I18n.T("bp.mods.none"), toggles: true, kind: "mods");
    }

    // ---------------------------------------------------------------- каталог

    sealed record CatSection(string Key, string Title, string? Sub, Query Query, bool Picks = false);

    /// <summary>Разделы каталога: «Нужные», «Популярные», «Лучшие», «Новые», «Обновлённые» и разделы самой игры.</summary>
    static List<CatSection> CatSections(GameDef def)
    {
        var list = new List<CatSection>();
        if (def.Picks.Length > 0) list.Add(new("picks", I18n.T("bp.cat.picks"), I18n.T("bp.cat.picks.sub"), new Query(), Picks: true));
        list.Add(new("popular", I18n.T("bp.cat.popular"), null, new Query(Sort: SortBy.Popular)));
        list.Add(new("best", I18n.T("bp.cat.best"), null, new Query(SectionId: "best")));
        list.Add(new("new", I18n.T("bp.cat.new"), null, new Query(Sort: SortBy.New)));
        list.Add(new("updated", I18n.T("bp.cat.updated"), null, new Query(Sort: SortBy.Updated)));
        foreach (var s in def.Sections.Where(s => s.Id is not ("all" or "picks" or "best" or "packs")).Take(8))
            list.Add(new("sec-" + s.Id, I18n.T("sec." + s.Id), null, new Query(SectionId: s.Id)));
        return list;
    }

    void OpenCatalog(GameState g)
    {
        if (!g.Def.HasCatalog) return;
        NewToken();
        var items = CatSections(g.Def).Select(s => new ModalItem(s.Title, s.Sub, () => LoadCatalog(g, s), Badge: "›")).ToList();
        ShowModal(I18n.T("bp.cat.title", ("game", g.Def.Name)), items, "", kind: "catalog-sections");
    }

    async void LoadCatalog(GameState g, CatSection section, int page = 1, List<ModInfo>? acc = null)
    {
        var token = NewToken();
        var title = $"{section.Title} · {g.Def.Name}";
        if (acc is null) ShowModal(title, [new(I18n.T("bp.cat.loading"), null, () => { })], "", back: () => OpenCatalog(g), kind: "catalog-loading");
        try
        {
            List<ModInfo> mods;
            bool more;
            if (section.Picks) { mods = await Catalog.Many(g.Def, g.Def.Picks); more = false; }
            else
            {
                var result = Program.Screenshot ? Demo_.Catalog(g.Def, section.Query) : await Catalog.Browse(g.Def, section.Query with { Page = page });
                mods = result.Mods;
                more = result.HasMore;
            }
            if (token != _modalToken) return;
            var all = (acc ?? []).Concat(mods).GroupBy(m => m.Id).Select(x => x.First()).ToList();
            ShowCatalogList(g, section, all, more, page, focus: acc?.Count ?? 0);
        }
        catch (Exception e)
        {
            if (token != _modalToken) return;
            ShowModal(title, [new(I18n.T("bp.cat.error", ("error", Jobs.Explain(e))), null, () => LoadCatalog(g, section, page, acc)), new(I18n.T("common.back"), null, () => OpenCatalog(g))], "", back: () => OpenCatalog(g), kind: "catalog-error");
        }
    }

    void ShowCatalogList(GameState g, CatSection section, List<ModInfo> mods, bool more, int page, int focus = 0)
    {
        var items = new List<ModalItem>();
        foreach (var m in mods)
        {
            var mod = m;
            var installed = Actions.IsInstalled(g, m.Id);
            var busy = Actions.IsBusy(g, m.Id);
            var sub = string.Join(" · ", new[] { m.Author, m.Version == "" ? null : "v" + m.Version, m.Downloads > 0 ? $"{I18n.Compact(m.Downloads)} ↓" : null }.Where(x => !string.IsNullOrEmpty(x)));
            items.Add(new ModalItem(m.Name, sub, () => OpenModDetail(g, mod, section, mods, more, page), Badge: busy ? I18n.T("bp.cat.installing") : installed ? I18n.T("bp.cat.installed") : null, Thumb: m.Icon, Good: installed && !busy));
        }
        if (more) items.Add(new ModalItem(I18n.T("bp.cat.more"), null, () => LoadCatalog(g, section, page + 1, mods), Badge: "↓"));
        var title = $"{section.Title} · {g.Def.Name}";
        ShowModal(title, items, I18n.T("bp.cat.empty"), back: () => OpenCatalog(g), kind: "catalog", focus: focus);
        _modalRefresh = () => { var keep = _modalIndex; ShowCatalogList(g, section, mods, more, page, keep); };
    }

    void OpenModDetail(GameState g, ModInfo mod, CatSection section, List<ModInfo> mods, bool more, int page)
    {
        var installed = Actions.IsInstalled(g, mod.Id);
        var busy = Actions.IsBusy(g, mod.Id);
        void Back() => ShowCatalogList(g, section, mods, more, page, focus: Math.Max(0, mods.FindIndex(m => m.Id == mod.Id)));
        var sub = string.Join(" · ", new[] { mod.Author, mod.Version == "" ? null : "v" + mod.Version, mod.Downloads > 0 ? I18n.T("bp.mod.downloads", ("n", I18n.Compact(mod.Downloads))) : null }.Where(x => !string.IsNullOrEmpty(x)));
        var items = new List<ModalItem>
        {
            busy ? new(I18n.T("bp.mod.installing"), sub, () => { }, Thumb: mod.Icon)
            : installed ? new(I18n.T("bp.mod.installed"), sub, Back, Badge: "✓", Good: true, Thumb: mod.Icon)
            : new(I18n.T("bp.mod.install"), sub, () =>
            {
                _ = Actions.Install(g, mod);
                Toast(I18n.T("bp.install.started", ("name", mod.Name)));
                Back();
            }, Thumb: mod.Icon),
            new(I18n.T("common.back"), null, Back),
        };
        var hint = new List<string>();
        var text = System.Text.RegularExpressions.Regex.Replace(mod.Description ?? "", "<.*?>", " ").Trim();
        if (text != "") hint.Add(text.Length > 320 ? text[..320] + "…" : text);
        if (mod.Dependencies.Count > 0) hint.Add(I18n.T("bp.mod.deps", ("list", string.Join(", ", mod.Dependencies.Take(5)))));
        if (mod.Source == "nexus" && !Settings.NexusPremium) hint.Add(I18n.T("bp.mod.nexus"));
        NewToken();
        ShowModal(mod.Name, items, "", back: Back, kind: "mod", hint: hint.Count == 0 ? null : string.Join("\n\n", hint));
    }

    // ---------------------------------------------------------------- профили

    void OpenProfiles(GameState g)
    {
        NewToken();
        var registry = g.Registry;
        var items = new List<ModalItem>();
        foreach (var p in Profiles.List(g.Def.Id))
        {
            var name = p.Name;
            items.Add(new ModalItem(name, I18n.T("bp.prof.count", ("n", p.Count)), () =>
            {
                try
                {
                    var (on, off, missing) = Profiles.Apply(g.Def.Id, name, registry!);
                    AppState.Notify();
                    Toast(I18n.T("bp.prof.applied", ("name", name), ("on", on), ("off", off)) + (missing > 0 ? " " + I18n.T("bp.prof.missing", ("n", missing)) : ""));
                }
                catch (Exception e) { Toast(Jobs.Explain(e)); }
                OpenProfiles(g);
                Select(_game);
                _zone = Zone.Modal;
            }, Badge: p.Active ? I18n.T("bp.prof.active") : null, Good: p.Active));
        }
        if (registry is not null)
            items.Add(new ModalItem(I18n.T("bp.prof.save"), I18n.T("bp.prof.save.sub"), () =>
            {
                var name = I18n.T("bp.prof.autoName", ("when", DateTime.Now.ToString("dd.MM HH:mm")));
                try { Profiles.Save(g.Def.Id, name, registry); Toast(I18n.T("bp.prof.saved", ("name", name))); }
                catch (Exception e) { Toast(Jobs.Explain(e)); }
                OpenProfiles(g);
                Select(_game);
                _zone = Zone.Modal;
            }, Badge: "+"));
        ShowModal(I18n.T("bp.prof.title", ("game", g.Def.Name)), items, I18n.T("bp.prof.none"), kind: "profiles");
    }

    // ---------------------------------------------------------------- «Ещё» и обновления

    void OpenMore(GameState g)
    {
        NewToken();
        var n = ModUpdates.Found.TryGetValue(g.Def.Id, out var ups) ? ups.Count : 0;
        ShowModal(I18n.T("bp.more"),
        [
            new(I18n.T("bp.upd.title"), n > 0 ? I18n.T("bp.upd.available", ("n", n)) : I18n.T("bp.upd.check.sub"), () => OpenUpdates(g), Badge: n > 0 ? n.ToString() : "›", Good: n > 0),
            new(I18n.T("bp.openInApp"), null, () => { var id = g.Def.Id; Close(); MainWindow.Current?.Navigate(() => new GamePage(id)); }),
            new(I18n.T("bp.pc"), I18n.T("bp.pc.sub"), () => { CloseModal(); TogglePc(); }),
        ], "", kind: "more");
    }

    void OpenUpdates(GameState g, bool checking = false)
    {
        var token = NewToken();
        if (checking)
        {
            ShowModal(I18n.T("bp.upd.title"), [new(I18n.T("bp.upd.checking"), null, () => { })], "", back: () => OpenMore(g), kind: "updates");
            return;
        }
        var list = ModUpdates.Found.TryGetValue(g.Def.Id, out var found) ? found : [];
        var items = new List<ModalItem>();
        var auto = list.Where(u => !u.Manual).ToList();
        if (auto.Count > 1)
            items.Add(new ModalItem(I18n.T("bp.upd.all", ("n", auto.Count)), null, () => { foreach (var u in auto) StartUpdate(g, u); Toast(I18n.T("bp.upd.started", ("n", auto.Count))); CloseModal(); }, Badge: "↑", Good: true));
        foreach (var u in list)
        {
            var update = u;
            items.Add(new ModalItem(u.Name, $"v{u.Current} → v{u.Latest}", () =>
            {
                if (update.Manual) { Toast(I18n.T("bp.upd.manualHint")); return; }
                StartUpdate(g, update);
                Toast(I18n.T("bp.upd.started", ("n", 1)));
                CloseModal();
            }, Badge: u.Manual ? I18n.T("bp.upd.manual") : I18n.T("bp.upd.update"), Thumb: u.Icon));
        }
        items.Add(new ModalItem(I18n.T("bp.upd.check"), null, async () =>
        {
            OpenUpdates(g, checking: true);
            var mine = _modalToken;
            try { await ModUpdates.Check(g); } catch (Exception e) { Toast(Jobs.Explain(e)); }
            if (mine == _modalToken) OpenUpdates(g);
        }, Badge: "⟳"));
        ShowModal(I18n.T("bp.upd.title"), items, I18n.T("bp.upd.none"), back: () => OpenMore(g), kind: "updates");
    }

    static void StartUpdate(GameState g, ModUpdate u) =>
        Jobs.Run(u.Name, g.Def.Name, async (_, progress, ct) =>
        {
            try { await ModUpdates.Update(g, u, progress, ct); }
            finally { AppState.Notify(); }
        });

    // ---------------------------------------------------------------- друзья и загрузки

    void OpenFriends()
    {
        NewToken();
        void Render()
        {
            var view = Social.Friends.View();
            var items = new List<ModalItem>();
            if (!view.SignedIn || !view.Configured)
                items.Add(new ModalItem(I18n.T("bp.friends.signin"), null, () => { }));
            else
            {
                foreach (var f in view.Friends.OrderBy(f => f.State == "playing" ? 0 : f.State == "offline" ? 2 : 1).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var playing = f.State == "playing";
                    items.Add(new ModalItem(f.Name, playing && f.GameName != "" ? f.GameName : null, () => { },
                        Badge: playing ? I18n.T("dock.inGame") : f.State == "offline" ? I18n.T("friends.offline") : I18n.T("friends.online"), Good: f.State != "offline"));
                }
            }
            var keep = _modalIndex;
            ShowModal(I18n.T("bp.friends.title"), items, I18n.T("bp.friends.none"), kind: "friends", focus: keep, hint: I18n.T("bp.friends.hint"));
            _modalRefresh = Render;
        }
        Render();
        if (Program.Screenshot) return;
        _ = Task.Run(async () => { try { await Social.Friends.Refresh(); } catch { } });
    }

    void OpenDownloads()
    {
        NewToken();
        void Render()
        {
            var items = Jobs.All.Take(12).Select(j => new ModalItem(j.Title, j.Status == JobStatus.Failed ? j.Error ?? j.Step : $"{j.GameName} · {j.Step}", () => { },
                Badge: j.Status == JobStatus.Done ? "✓" : j.Status == JobStatus.Failed ? "!" : j.Ratio >= 0 ? $"{j.Ratio * 100:0}%" : "…", Good: j.Status == JobStatus.Done)).ToList();
            var keep = _modalIndex;
            ShowModal(I18n.T("bp.downloads.title"), items, I18n.T("bp.downloads.none"), kind: "downloads", focus: keep, hint: I18n.T("bp.downloads.hint"));
            _modalRefresh = Render;
        }
        Render();
    }

    // ---------------------------------------------------------------- для снимков экрана

    static List<ModInfo> DemoCatalog(GameState g) => Demo_.Many(g.Def, ["2800", "1311", "2210", "4056", "913", "1777"]);

    /// <summary>Поддельные данные только для режима снимков.</summary>
    static class Demo_
    {
        public static Sources.Page Catalog(GameDef def, Query q) => Core.Demo.Catalog(def, q);
        public static List<ModInfo> Many(GameDef def, IEnumerable<string> ids) => Core.Demo.Many(def, ids);
    }
}
