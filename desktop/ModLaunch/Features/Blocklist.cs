using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>
/// Скрытые моды и авторы (как «Block» на Nexus): их не видно ни в каталоге,
/// ни в подборках. Список — в настройках, его можно очистить.
/// </summary>
public static class Blocklist
{
    static JsonObject Root => Settings.Data.Obj("blocklist");

    static string ModKey(string game, ModInfo mod) => $"{game}|{mod.Source}|{mod.Id}";
    static string AuthorKey(ModInfo mod) => $"{mod.Source}|{mod.Author}".ToLowerInvariant();

    public static bool Hides(string game, ModInfo mod) =>
        Root.Obj("mods").ContainsKey(ModKey(game, mod)) || (mod.Author != "" && Root.Obj("authors").ContainsKey(AuthorKey(mod)));

    public static void HideMod(string game, ModInfo mod) { Root.Obj("mods")[ModKey(game, mod)] = mod.Name; Settings.Save(); }
    public static void HideAuthor(ModInfo mod) { if (mod.Author != "") { Root.Obj("authors")[AuthorKey(mod)] = mod.Author; Settings.Save(); } }

    public static List<(string Key, string Title, bool Author)> All() =>
        Root.Obj("mods").Select(kv => (kv.Key, kv.Value?.ToString() ?? kv.Key, false))
            .Concat(Root.Obj("authors").Select(kv => (kv.Key, kv.Value?.ToString() ?? kv.Key, true))).ToList();

    public static void Unhide(string key, bool author) { Root.Obj(author ? "authors" : "mods").Remove(key); Settings.Save(); }
    public static void Clear() { Settings.Data.Remove("blocklist"); Settings.Save(); }
}
