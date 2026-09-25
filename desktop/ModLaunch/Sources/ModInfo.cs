namespace ModLaunch.Sources;

/// <summary>Мод из любого каталога в одном виде — так его показывает интерфейс.</summary>
public sealed class ModInfo
{
    public required string Source { get; init; } // nexus | thunderstore | modlinks
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = "";
    public string Version { get; set; } = "";
    public string Description { get; init; } = "";
    public string? Icon { get; set; }
    public string? Url { get; init; }
    public long Downloads { get; init; }
    public long Rating { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string[] Categories { get; init; } = [];
    public string? DownloadUrl { get; set; }
    public string? Sha256 { get; init; }
    public List<string> Dependencies { get; set; } = [];
    public bool Adult { get; init; }

    /// <summary>Копия с другой версией и ссылкой — для установки старой версии.</summary>
    public ModInfo WithVersion(string version, string? url) => new()
    {
        Source = Source, Id = Id, Name = Name, Author = Author, Version = version, Description = Description, Icon = Icon, Url = Url,
        Downloads = Downloads, Rating = Rating, UpdatedAt = UpdatedAt, Categories = Categories, DownloadUrl = url ?? DownloadUrl,
        Sha256 = null, Dependencies = [.. Dependencies],
    };
}

public sealed record Page(List<ModInfo> Mods, long Total, bool HasMore, int Number);

public enum SortBy { Popular, Rating, Updated, New, Name, Random }

/// <summary>Запрос к каталогу. Period — «обновлены за N дней» (0 — за всё время), Adult — показывать 18+.</summary>
public sealed record Query(string Text = "", int Page = 1, SortBy Sort = SortBy.Popular, string SectionId = "all", int Period = 0, bool Adult = false);
