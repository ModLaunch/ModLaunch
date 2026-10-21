using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- маркет

    // 12.0: свои вид маркета и студия продавца здесь удалены — до них нельзя было дойти (Go ведёт
    // «market» и «studio» на страницу рынка). Остался кэш лотов для Обзора, «Кодов» и «Моделей».
    static List<Listing>? _market;
    static string? _marketError;
    static bool _marketLoading;

    async Task LoadMarket(bool force = false)
    {
        if (_marketLoading) return;
        _marketLoading = true;
        _marketError = null;
        // Как у LoadHub: из Build — без второй постройки внутри первой, сначала дать ей закончиться.
        var nested = _building;
        await Task.Yield();
        if (!nested) Build();
        try { _market = await Market.All(force); }
        catch (Exception e) { _market = []; _marketError = Market.Explain(e); }
        _marketLoading = false;
        RebuildCurrent();
    }
}
