using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Games;

/// <summary>
/// 30.2: полки библиотеки «Все игры», как в Steam, но для игр из всех лаунчеров: избранное, скрытые игры,
/// свои коллекции («Кооператив», «Пройти»…) и порядок списка. Хранится в library.json рядом с настройками;
/// ключ игры — LibGame.Key, поэтому игры из разных магазинов могут лежать в одной коллекции.
/// </summary>
public sealed class Shelves
{
    public const int MaxCollections = 40, MaxName = 32;
    public static readonly string[] Sorts = ["name", "recent", "played", "size"];

    static readonly Lazy<(JsonFile File, Shelves Shelves)> Store = new(() =>
    {
        var file = new JsonFile(Path.Combine(Paths.DataDir, "library.json"), () => new JsonObject());
        return (file, new Shelves(file.Data));
    });

    /// <summary>Полки этого ПК (library.json).</summary>
    public static Shelves Current => Store.Value.Shelves;

    public static event Action? Changed;

    readonly JsonObject _data;

    public Shelves(JsonObject data) => _data = data;

    void Save()
    {
        if (Store.IsValueCreated && ReferenceEquals(this, Store.Value.Shelves)) Store.Value.File.Save();
        Changed?.Invoke();
    }

    JsonArray List(string name)
    {
        if (_data[name] is JsonArray a) return a;
        var created = new JsonArray();
        _data[name] = created;
        return created;
    }

    static bool Has(JsonArray a, string key) => a.Any(n => n?.GetValue<string>() == key);

    static bool Toggle(JsonArray a, string key)
    {
        var at = a.Select((n, i) => (n, i)).FirstOrDefault(x => x.n?.GetValue<string>() == key);
        if (at.n is not null) { a.RemoveAt(at.i); return false; }
        a.Add(JsonValue.Create(key));
        return true;
    }

    // ---------------------------------------------------------------- избранное и скрытые

    public bool IsFavorite(string key) => Has(List("fav"), key);
    public bool IsHidden(string key) => Has(List("hidden"), key);
    public int FavoriteCount => List("fav").Count;
    public int HiddenCount => List("hidden").Count;

    /// <summary>Добавить в избранное или убрать; true — теперь в избранном.</summary>
    public bool ToggleFavorite(string key) { var on = Toggle(List("fav"), key); Save(); return on; }

    /// <summary>Скрыть из списка или вернуть; true — теперь скрыта. Скрытая игра уходит и из избранного.</summary>
    public bool ToggleHidden(string key)
    {
        var on = Toggle(List("hidden"), key);
        if (on && IsFavorite(key)) Toggle(List("fav"), key);
        Save();
        return on;
    }

    // ---------------------------------------------------------------- коллекции

    JsonObject Groups()
    {
        if (_data["collections"] is JsonObject o) return o;
        var created = new JsonObject();
        _data["collections"] = created;
        return created;
    }

    public IReadOnlyList<string> Collections => Groups().Select(kv => kv.Key).ToList();

    public IReadOnlyList<string> In(string collection) =>
        (Groups()[collection] as JsonArray ?? new JsonArray()).Select(n => n?.GetValue<string>() ?? "").Where(k => k != "").ToList();

    public bool Contains(string collection, string key) => Groups()[collection] is JsonArray a && Has(a, key);

    public static string CleanName(string? name)
    {
        var s = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length > MaxName ? s[..MaxName] : s;
    }

    /// <summary>Новая коллекция (пустая). Имя чистится; такое уже есть — вернёт существующее.</summary>
    public string Create(string name)
    {
        name = CleanName(name);
        if (name == "") throw new ServiceError("NO_NAME");
        var existing = Collections.FirstOrDefault(c => string.Equals(c, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null) return existing;
        if (Groups().Count >= MaxCollections) throw new ServiceError("TOO_MANY");
        Groups()[name] = new JsonArray();
        Save();
        return name;
    }

    /// <summary>Положить в коллекцию или вынуть; true — теперь в коллекции.</summary>
    public bool Toggle(string collection, string key)
    {
        if (Groups()[collection] is not JsonArray a) return false;
        var on = Toggle(a, key);
        Save();
        return on;
    }

    public void Rename(string from, string to)
    {
        to = CleanName(to);
        if (to == "" || Groups()[from] is not JsonArray a) return;
        if (to == from) return;
        if (Collections.Any(c => string.Equals(c, to, StringComparison.CurrentCultureIgnoreCase))) throw new ServiceError("EXISTS");
        Groups().Remove(from);
        Groups()[to] = a;
        Save();
    }

    public void Delete(string collection)
    {
        if (Groups().Remove(collection)) Save();
    }

    // ---------------------------------------------------------------- порядок

    public string Sort
    {
        get => _data["sort"]?.GetValue<string>() is { } s && Sorts.Contains(s) ? s : "name";
        set { if (Sorts.Contains(value)) { _data["sort"] = value; Save(); } }
    }

    /// <summary>
    /// Список в нужном порядке: по имени, недавно запущенные, больше всего наиграно, самые большие.
    /// При равенстве — по имени, чтобы порядок не прыгал.
    /// </summary>
    public static IEnumerable<LibGame> Order(IEnumerable<LibGame> games, string sort, Func<LibGame, (long Last, long Minutes)> played)
    {
        static string NameKey(LibGame g) => g.Name == "" ? "￿" : g.Name;
        var byName = StringComparer.CurrentCultureIgnoreCase;
        return sort switch
        {
            "recent" => games.OrderByDescending(g => played(g).Last).ThenBy(NameKey, byName),
            "played" => games.OrderByDescending(g => played(g).Minutes).ThenBy(NameKey, byName),
            "size" => games.OrderByDescending(g => g.Size).ThenBy(NameKey, byName),
            _ => games.OrderBy(NameKey, byName),
        };
    }

    [SelfTest]
    static string ShelvesKeepFavoritesHiddenAndCollections()
    {
        var s = new Shelves(new JsonObject());
        if (!s.ToggleFavorite("steam:1") || !s.IsFavorite("steam:1") || s.FavoriteCount != 1) throw new Exception("favorite");
        if (!s.ToggleHidden("steam:1") || !s.IsHidden("steam:1") || s.IsFavorite("steam:1")) throw new Exception("hiding drops favorite");
        if (s.ToggleHidden("steam:1") || s.HiddenCount != 0) throw new Exception("unhide");
        var coop = s.Create("  Кооператив   с друзьями ");
        if (coop != "Кооператив с друзьями" || s.Create("кооператив С ДРУЗЬЯМИ") != coop) throw new Exception("name clean / duplicate");
        if (!s.Toggle(coop, "epic:Hades") || !s.Toggle(coop, "steam:2") || s.In(coop).Count != 2) throw new Exception("add");
        if (s.Toggle(coop, "epic:Hades") || s.Contains(coop, "epic:Hades")) throw new Exception("remove");
        s.Rename(coop, "Coop");
        if (s.Collections.Single() != "Coop" || !s.Contains("Coop", "steam:2")) throw new Exception("rename keeps games");
        // Сохранение в файл: в собранной программе JsonArray.Add<T> без рефлексии не пишется — проверяем запись.
        s.ToggleFavorite("epic:Hades");
        if (!s._data.ToJsonString().Contains("epic:Hades")) throw new Exception("serialize");
        s.Delete("Coop");
        if (s.Collections.Count != 0) throw new Exception("delete");
        try { s.Create("   "); throw new Exception("empty name accepted"); } catch (ServiceError) { }

        var a = new LibGame("steam:1", "steam", "Alpha", null, 1, true, 10);
        var b = new LibGame("steam:2", "steam", "beta", null, 2, true, 30);
        var c = new LibGame("epic:c", "epic", "Gamma", null, 0, true, 20);
        var stats = new Dictionary<string, (long, long)> { ["steam:1"] = (300, 5), ["steam:2"] = (100, 50), ["epic:c"] = (200, 50) };
        string Names(string sort) => string.Join(",", Order([c, b, a], sort, g => stats[g.Key]).Select(g => g.Name));
        if (Names("name") != "Alpha,beta,Gamma") throw new Exception("name " + Names("name"));
        if (Names("recent") != "Alpha,Gamma,beta") throw new Exception("recent " + Names("recent"));
        if (Names("played") != "beta,Gamma,Alpha") throw new Exception("played ties by name " + Names("played"));
        if (Names("size") != "beta,Gamma,Alpha") throw new Exception("size " + Names("size"));
        return "favorites, hidden, collections (clean names, rename, delete) and four sort orders";
    }
}
