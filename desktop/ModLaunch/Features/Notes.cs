using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Заметки к модам (как в Vortex): settings.modNotes["игра|мод"].</summary>
public static class Notes
{
    public static string Get(string gameId, string recordId) => Settings.Data.Obj("modNotes").Str($"{gameId}|{recordId}") ?? "";

    public static void Set(string gameId, string recordId, string text)
    {
        var notes = Settings.Data.Obj("modNotes");
        if (string.IsNullOrWhiteSpace(text)) notes.Remove($"{gameId}|{recordId}");
        else notes[$"{gameId}|{recordId}"] = text.Length > 2000 ? text[..2000] : text;
        Settings.Save();
    }
}
