using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Views;

public sealed partial class ModPage
{
    [DemoShots]
    static void ModPageShots(Shots s)
    {
        var game = GameCatalog.ById("lethal-company")!;
        var mod = Demo.Catalog(game, new Query()).Mods[0];
        s.Window.Navigate(() => new ModPage(game.Id, mod));
        s.Pump(900);
        s.Save("modpage-1-about");
    }
}
