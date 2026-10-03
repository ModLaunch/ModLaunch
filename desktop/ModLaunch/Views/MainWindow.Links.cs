using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Ссылки на моды (вставили в поиск, пришли из браузера) и поиск по мере набора.</summary>
public sealed partial class MainWindow
{
    /// <summary>Открыть страницу мода по ссылке Nexus, Thunderstore, Hub, modlaunch:// или ror2mm://.</summary>
    public async void OpenLink(string text)
    {
        if (ModLink.Parse(text) is not { } target) { Toast(I18n.T("link.unknown"), bad: true); return; }
        Toast(I18n.T("link.looking"));
        (GameState Game, Sources.ModInfo Mod)? found;
        try { found = await ModLink.Resolve(target); }
        catch (Exception e) { Toast(Jobs.Explain(e), bad: true); return; }
        if (found is not { } hit) { Toast(I18n.T("link.notFound"), bad: true); return; }
        _search.Text = "";
        Navigate(() => new ModPage(hit.Game.Def.Id, hit.Mod));
    }

    /// <summary>На странице поиска результаты обновляются прямо по мере набора в верхней строке.</summary>
    void OnSearchTyping()
    {
        // Пустую строку не передаём: при переходе на страницу поиска окно само очищает верхнюю строку,
        // и без этой проверки только что начатый поиск («map» с главной) тут же стирался.
        if (_current is SearchPage page && _search.IsFocused && _search.Text is { Length: > 0 } text) page.Typed(text);
    }
}
