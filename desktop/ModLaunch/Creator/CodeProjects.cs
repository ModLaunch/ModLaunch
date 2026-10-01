using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Creator;

/// <summary>Какой это мод на C#: под какой загрузчик и среду.</summary>
public enum CodeKind { BepInEx5, BepInEx6, Smapi, HkApi }

public sealed record CodeProject(string Id, string Dir, string Name, string Game, CodeKind Kind, DateTime Updated)
{
    public string Csproj => Directory.EnumerateFiles(Dir, "*.csproj").FirstOrDefault() ?? Path.Combine(Dir, Id + ".csproj");
}

/// <summary>Одна строка проверки перед сборкой: что в порядке, а что нет.</summary>
public sealed record Check(bool Ok, string Key, string Detail = "");

/// <summary>
/// Проекты на C# (%APPDATA%\ModHub\creator-code\&lt;проект&gt;): мастер создаёт готовый проект с
/// настоящими ссылками на папку игры, а сборка кладёт мод прямо в игру. Для игр без загрузчика
/// (и без .NET SDK у человека) мастер подсказывает, чего не хватает.
/// </summary>
public static class CodeProjects
{
    public static string Root => Directory.CreateDirectory(Path.Combine(Paths.DataDir, "creator-code")).FullName;
    const string MetaFile = "modlaunch-project.json";

    public static string Label(CodeKind kind) => kind switch
    {
        CodeKind.BepInEx5 => "BepInEx 5 (Unity Mono)",
        CodeKind.BepInEx6 => "BepInEx 6 (Unity IL2CPP)",
        CodeKind.Smapi => "SMAPI (Stardew Valley)",
        _ => "Hollow Knight Modding API",
    };

    static bool Il2Cpp(GameDef def, string? path) =>
        def.Id == "gtfo" || (path is not null && File.Exists(Path.Combine(path, "GameAssembly.dll")));

    /// <summary>Вид проекта для игры; null — у игры нет загрузчика, под который можно писать код.</summary>
    public static CodeKind? KindFor(GameDef def, string? gamePath) => def.Loader switch
    {
        LoaderKind.Smapi => CodeKind.Smapi,
        LoaderKind.HkApi => CodeKind.HkApi,
        LoaderKind.Bepinex => Il2Cpp(def, gamePath) ? CodeKind.BepInEx6 : CodeKind.BepInEx5,
        _ => null,
    };

    public static List<CodeProject> List()
    {
        var list = new List<CodeProject>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var meta = Path.Combine(dir, MetaFile);
            if (!File.Exists(meta)) continue;
            try
            {
                var json = JsonNode.Parse(File.ReadAllText(meta));
                if (!Enum.TryParse<CodeKind>(json.Str("kind"), out var kind)) continue;
                list.Add(new CodeProject(Path.GetFileName(dir), dir, json.Str("name") ?? Path.GetFileName(dir), json.Str("game") ?? "", kind, Directory.GetLastWriteTimeUtc(dir)));
            }
            catch { }
        }
        return list.OrderByDescending(p => p.Updated).ToList();
    }

    public static void Delete(CodeProject p)
    {
        try { Directory.Delete(p.Dir, true); } catch { }
    }

    // ---------------------------------------------------------------- создание

    /// <summary>Имя, пригодное для C#: латиница, цифры и «_», не начинается с цифры.</summary>
    public static string Identifier(string name)
    {
        var s = Projects.PackageName(name).Replace("_", "");
        if (s == "") s = "MyMod";
        return char.IsDigit(s[0]) ? "Mod" + s : s;
    }

    public static CodeProject Create(string name, GameDef def, string? gamePath, string author)
    {
        var kind = KindFor(def, gamePath) ?? throw new InvalidOperationException(I18n.T("cr.code.noLoader", ("game", def.Name)));
        var ns = Identifier(name);
        var slug = CustomGames.Slug(name);
        if (slug == "") slug = "mod";
        var id = slug;
        for (var i = 2; Directory.Exists(Path.Combine(Root, id)); i++) id = $"{slug}-{i}";
        var dir = Directory.CreateDirectory(Path.Combine(Root, id)).FullName;
        author = string.IsNullOrWhiteSpace(author) ? "Me" : author.Trim();
        var t = new Template(name.Trim(), ns, author, def, gamePath);

        void Write(string file, string text)
        {
            var full = Path.Combine(dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text, new UTF8Encoding(false));
        }

        Write(MetaFile, new JsonObject { ["name"] = name.Trim(), ["game"] = def.Id, ["kind"] = kind.ToString(), ["author"] = author, ["ns"] = ns, ["created"] = DateTime.UtcNow.ToString("o") }.ToJsonString());
        Write(ns + ".csproj", t.Csproj(kind));
        Write(kind switch { CodeKind.Smapi => "ModEntry.cs", CodeKind.HkApi => "MainMod.cs", _ => "Plugin.cs" }, t.Source(kind));
        if (kind == CodeKind.Smapi) Write("manifest.json", t.Manifest());
        Write("README.md", t.Readme(kind));
        Write(".gitignore", "bin/\nobj/\n*.user\n.vs/\n");
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        Write(Path.Combine("assets", "README.txt"), I18n.T("cr.code.assetsReadme"));
        return List().First(p => p.Id == id);
    }

    sealed record Template(string Name, string Ns, string Author, GameDef Def, string? GamePath)
    {
        public string Guid => $"{Regex.Replace(Author.ToLowerInvariant(), "[^a-z0-9]", "")}.{Ns.ToLowerInvariant()}";

        string GameDir => GamePath ?? $@"C:\Program Files (x86)\Steam\steamapps\common\{Def.Name}";

        string ManagedDir => GamePath is null ? $@"{GameDir}\{Def.Name}_Data\Managed"
            : Def.Loader == LoaderKind.HkApi ? Loaders.HkApi.ManagedDir(GamePath)
            : Directory.EnumerateDirectories(GamePath, "*_Data").Select(d => Path.Combine(d, "Managed")).FirstOrDefault() ?? Path.Combine(GamePath, Def.Name + "_Data", "Managed");

        /// <summary>Ссылки на библиотеки игры: то, что реально лежит в Managed, а не угаданные имена.</summary>
        string GameReferences(bool unity = true)
        {
            var sb = new StringBuilder();
            var names = new List<string>();
            if (Directory.Exists(ManagedDir))
            {
                names.AddRange(Directory.EnumerateFiles(ManagedDir, "*.dll").Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(n => n.StartsWith("Assembly-CSharp") || (unity && (n.StartsWith("UnityEngine") || n.StartsWith("Unity.TextMeshPro") || n.StartsWith("Unity.InputSystem")))));
                names = names.Where(n => !n.EndsWith(".vanilla")).OrderBy(n => n).ToList();
            }
            if (names.Count == 0) names = ["Assembly-CSharp", "UnityEngine", "UnityEngine.CoreModule", "UnityEngine.IMGUIModule", "UnityEngine.InputLegacyModule"];
            foreach (var n in names)
                sb.Append($"    <Reference Include=\"{n}\">\n      <HintPath>{ManagedDir}\\{n}.dll</HintPath>\n      <Private>false</Private>\n    </Reference>\n");
            return sb.ToString();
        }

        const string Assets = "  <ItemGroup>\n    <ModAssets Include=\"assets\\**\\*\" Exclude=\"assets\\README.txt\" />\n  </ItemGroup>\n";

        string Deploy(string folder) => $"""
              <!-- После сборки мод сам копируется в игру: собрал, запустил игру — и можно смотреть. -->
              <Target Name="CopyToGame" AfterTargets="Build" Condition="Exists('$(GameDir)')">
                <MakeDir Directories="{folder}" />
                <Copy SourceFiles="$(TargetPath)" DestinationFolder="{folder}" />
                <Copy SourceFiles="@(ModAssets)" DestinationFiles="@(ModAssets->'{folder}\assets\%(RecursiveDir)%(Filename)%(Extension)')" />
              </Target>
            """;

        public string Csproj(CodeKind kind) => kind switch
        {
            CodeKind.Smapi => $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net6.0</TargetFramework>
                    <AssemblyName>{Ns}</AssemblyName>
                    <RootNamespace>{Ns}</RootNamespace>
                    <Version>1.0.0</Version>
                    <LangVersion>latest</LangVersion>
                    <Nullable>enable</Nullable>
                    <EnableHarmony>true</EnableHarmony>
                    <!-- Папка игры. ModBuildConfig сам копирует собранный мод в Mods и умеет собирать zip. -->
                    <GamePath>{GameDir}</GamePath>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Pathoschild.Stardew.ModBuildConfig" Version="4.*" />
                  </ItemGroup>
                </Project>
                """,
            CodeKind.HkApi => $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net472</TargetFramework>
                    <AssemblyName>{Ns}</AssemblyName>
                    <RootNamespace>{Ns}</RootNamespace>
                    <Version>1.0.0</Version>
                    <LangVersion>latest</LangVersion>
                    <GameDir>{GameDir}</GameDir>
                    <ModDir>{ManagedDir}\Mods\{Ns}</ModDir>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
                  </ItemGroup>
                  <ItemGroup>
                {GameReferences().TrimEnd()}
                  </ItemGroup>
                {Assets}
                {Deploy("$(ModDir)")}
                </Project>
                """,
            CodeKind.BepInEx6 => $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net6.0</TargetFramework>
                    <AssemblyName>{Ns}</AssemblyName>
                    <RootNamespace>{Ns}</RootNamespace>
                    <Version>1.0.0</Version>
                    <LangVersion>latest</LangVersion>
                    <GameDir>{GameDir}</GameDir>
                    <PluginDir>$(GameDir)\BepInEx\plugins\{Ns}</PluginDir>
                    <RestoreAdditionalProjectSources>https://nuget.bepinex.dev/v3/index.json</RestoreAdditionalProjectSources>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="BepInEx.Unity.IL2CPP" Version="6.0.0-be.*" IncludeAssets="compile" />
                  </ItemGroup>
                  <ItemGroup>
                    <!-- Библиотеки игры для IL2CPP BepInEx создаёт при первом запуске игры (папка BepInEx\interop). -->
                    <Reference Include="$(GameDir)\BepInEx\interop\*.dll" Private="false" />
                  </ItemGroup>
                {Assets}
                {Deploy("$(PluginDir)")}
                </Project>
                """,
            _ => $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <AssemblyName>{Ns}</AssemblyName>
                    <RootNamespace>{Ns}</RootNamespace>
                    <Version>1.0.0</Version>
                    <LangVersion>latest</LangVersion>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                    <GameDir>{GameDir}</GameDir>
                    <PluginDir>$(GameDir)\BepInEx\plugins\{Ns}</PluginDir>
                    <RestoreAdditionalProjectSources>https://nuget.bepinex.dev/v3/index.json</RestoreAdditionalProjectSources>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="BepInEx.Core" Version="5.*" PrivateAssets="all" />
                  </ItemGroup>
                  <ItemGroup>
                {GameReferences().TrimEnd()}
                  </ItemGroup>
                {Assets}
                {Deploy("$(PluginDir)")}
                </Project>
                """,
        };

        public string Source(CodeKind kind) => (kind switch
        {
            CodeKind.Smapi => """
                using StardewModdingAPI;
                using StardewModdingAPI.Events;
                using StardewValley;

                namespace __NS__;

                /// <summary>Точка входа мода: SMAPI вызывает Entry один раз при загрузке.</summary>
                internal sealed class ModEntry : Mod
                {
                    private ModConfig _config = new();

                    public override void Entry(IModHelper helper)
                    {
                        _config = helper.ReadConfig<ModConfig>();
                        helper.Events.GameLoop.DayStarted += OnDayStarted;
                        helper.Events.Input.ButtonPressed += OnButtonPressed;
                        Monitor.Log("__NAME__ загружен", LogLevel.Info);
                    }

                    private void OnDayStarted(object? sender, DayStartedEventArgs e)
                    {
                        if (!_config.Enabled) return;
                        Monitor.Log($"Начался день {Game1.dayOfMonth}", LogLevel.Debug);
                    }

                    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
                    {
                        if (!Context.IsWorldReady || e.Button != _config.Hotkey) return;
                        Game1.showGlobalMessage("__NAME__: горячая клавиша нажата");
                    }
                }

                /// <summary>Настройки лежат в config.json рядом с модом; игрок может их менять.</summary>
                internal sealed class ModConfig
                {
                    public bool Enabled { get; set; } = true;
                    public SButton Hotkey { get; set; } = SButton.F6;
                }
                """,
            CodeKind.HkApi => """
                using Modding;
                using UnityEngine;

                namespace __NS__
                {
                    /// <summary>Мод для Hollow Knight Modding API: Initialize вызывается при запуске игры.</summary>
                    public class MainMod : Mod
                    {
                        public MainMod() : base("__NAME__") { }

                        public override string GetVersion() => "1.0.0.0";

                        public override void Initialize()
                        {
                            Log("Инициализация");
                            ModHooks.HeroUpdateHook += OnHeroUpdate;
                        }

                        private void OnHeroUpdate()
                        {
                            if (Input.GetKeyDown(KeyCode.F6)) Log("F6 нажата");
                        }
                    }
                }
                """,
            CodeKind.BepInEx6 => """
                using BepInEx;
                using BepInEx.Logging;
                using BepInEx.Unity.IL2CPP;
                using HarmonyLib;

                namespace __NS__;

                [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
                public class Plugin : BasePlugin
                {
                    public const string PluginGuid = "__GUID__";
                    public const string PluginName = "__NAME__";
                    public const string PluginVersion = "1.0.0";

                    internal static ManualLogSource Logger = null!;

                    public override void Load()
                    {
                        Logger = Log;
                        var enabled = Config.Bind("General", "Enabled", true, "Включить мод");
                        if (!enabled.Value) return;

                        new Harmony(PluginGuid).PatchAll();
                        Log.LogInfo($"{PluginName} {PluginVersion} загружен");
                    }
                }

                // Пример патча (классы игры берутся из BepInEx\interop):
                // [HarmonyPatch(typeof(Player), nameof(Player.Update))]
                // static class PlayerUpdatePatch { static void Postfix(Player __instance) { } }
                """,
            _ => """
                using BepInEx;
                using BepInEx.Configuration;
                using BepInEx.Logging;
                using HarmonyLib;
                using UnityEngine;

                namespace __NS__
                {
                    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
                    public class Plugin : BaseUnityPlugin
                    {
                        public const string PluginGuid = "__GUID__";
                        public const string PluginName = "__NAME__";
                        public const string PluginVersion = "1.0.0";

                        internal static ManualLogSource Log;

                        private ConfigEntry<bool> _enabled;
                        private ConfigEntry<KeyCode> _hotkey;

                        private void Awake()
                        {
                            Log = Logger;
                            _enabled = Config.Bind("General", "Enabled", true, "Включить мод");
                            _hotkey = Config.Bind("Keys", "Hotkey", KeyCode.F6, "Горячая клавиша");

                            new Harmony(PluginGuid).PatchAll();
                            Log.LogInfo($"{PluginName} {PluginVersion} загружен");
                        }

                        private void Update()
                        {
                            if (!_enabled.Value) return;
                            if (Input.GetKeyDown(_hotkey.Value)) Log.LogInfo("Горячая клавиша нажата");
                        }
                    }

                    // Пример патча: меняем поведение метода игры (имена классов смотрите в dnSpy / ILSpy).
                    // [HarmonyPatch(typeof(Player), "TakeDamage")]
                    // static class TakeDamagePatch { static bool Prefix(ref float damage) { damage *= 0.5f; return true; } }
                }
                """,
        }).Replace("__NS__", Ns).Replace("__NAME__", Name.Replace("\"", "'")).Replace("__GUID__", Guid);

        public string Manifest() => new JsonObject
        {
            ["Name"] = Name,
            ["Author"] = Author,
            ["Version"] = "1.0.0",
            ["Description"] = Name,
            ["UniqueID"] = $"{Regex.Replace(Author, "[^A-Za-z0-9]", "")}.{Ns}",
            ["EntryDll"] = Ns + ".dll",
            ["MinimumApiVersion"] = "4.0.0",
            ["UpdateKeys"] = new JsonArray(),
        }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        public string Readme(CodeKind kind) => $"""
            # {Name}

            Мод для {Def.Name} на C# ({CodeProjects.Label(kind)}). Проект создан в ModLaunch Creator Hub.

            ## Как собрать
            1. Поставьте [.NET SDK](https://dotnet.microsoft.com/download).
            2. `dotnet build -c Release` в этой папке (или кнопка «Собрать» в Creator Hub).
            3. Мод сам копируется в папку игры — запустите игру из ModLaunch и посмотрите журнал.

            ## Файлы
            - `{Ns}.csproj` — проект. В нём прописана папка игры (`GameDir`): если игра переехала, поправьте путь.
            - `assets/` — сюда кладите модели, текстуры, звук. Всё из этой папки копируется рядом с модом.

            ## Выпуск
            В Creator Hub → «Упаковка» соберите zip для {(Def.Loader == LoaderKind.Bepinex ? "Thunderstore" : Def.Loader == LoaderKind.Smapi ? "Nexus Mods" : "сообщества")}.
            """;
    }

    // ---------------------------------------------------------------- проверка и сборка

    public static string? FindDotnet()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            if (!p.WaitForExit(8000)) return null;
            return p.ExitCode == 0 && output != "" ? output : null;
        }
        catch { return null; }
    }

    /// <summary>Что мешает собрать: нет SDK, не найдена игра, нет загрузчика, IL2CPP ещё не запускали.</summary>
    public static List<Check> Preflight(CodeProject p, string? dotnet)
    {
        var def = GameCatalog.ById(p.Game);
        var state = def is null ? null : AppState.Game(def.Id);
        var checks = new List<Check> { new(dotnet is not null, "cr.code.chk.dotnet", dotnet ?? "") };
        checks.Add(new(state?.Path is not null, "cr.code.chk.game", state?.Path ?? ""));
        if (state?.Path is not null)
        {
            state.Refresh();
            checks.Add(new(state.LoaderInstalled, "cr.code.chk.loader", def!.LoaderName));
            if (p.Kind == CodeKind.BepInEx6) checks.Add(new(Directory.Exists(Path.Combine(state.Path, "BepInEx", "interop")), "cr.code.chk.interop"));
        }
        return checks;
    }

    /// <summary>dotnet build -c Release: строки вывода — по мере появления; возвращает код выхода.</summary>
    public static async Task<int> Build(CodeProject p, Action<string> log, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("dotnet", "build -c Release -nologo -consoleloggerparameters:NoSummary")
        {
            WorkingDirectory = p.Dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("dotnet");
        Task Pipe(StreamReader reader) => Task.Run(async () => { while (await reader.ReadLineAsync(ct) is { } line) log(line); }, ct);
        var pipes = Task.WhenAll(Pipe(proc.StandardOutput), Pipe(proc.StandardError));
        await proc.WaitForExitAsync(ct);
        await pipes;
        return proc.ExitCode;
    }

    /// <summary>Где лежит собранный мод: bin\Release\&lt;framework&gt;.</summary>
    public static string? OutputDir(CodeProject p) =>
        Directory.Exists(Path.Combine(p.Dir, "bin", "Release")) ? Directory.EnumerateDirectories(Path.Combine(p.Dir, "bin", "Release")).FirstOrDefault(d => Directory.EnumerateFiles(d, "*.dll").Any()) : null;
}
