# ModScript X

One `.modx` script, every game scripting language. ModScript X reads a game folder, shows its code,
translates your script into the engine's own language and installs it through the game's normal mod
loader. `watch` re-installs on every save, so edits show up in the running game after a reload.

| Engine | Language | Installed as | Hooks |
|---|---|---|---|
| Ren'Py | Python | `game/modscript_<id>.rpy` | ready, start, load, tick |
| RPG Maker MV/MZ | JavaScript | `js/plugins/ModScript_<id>.js` + `plugins.js` entry | ready, start, load, tick |
| Unity + BepInEx 5 | C# | source in `BepInEx/modscript/<id>`, built into `BepInEx/plugins` | ready, start, load, tick |
| Godot (Godot Mod Loader) | GDScript | `mods/<Author>-<id>.zip` | ready, start, tick |
| LÖVE / Lua-moddable games | Lua | `mods/<id>.lua` | ready, start, load, tick |
| Unreal Engine + UE4SS | Lua | `<Project>/Binaries/Win64/Mods/<id>/Scripts/main.lua` | ready, start, load, tick |
| HTML5 (NW.js / Electron) | JavaScript | `js/modscript_<id>.js` + `<script>` in `index.html` | ready, start, tick |
| RE Engine + REFramework | Lua | `reframework/autorun/modscript_<id>.lua` | ready, tick |
| **Any custom engine** | Lua, Python, JavaScript, C# or GDScript | wherever `modscript.engine.json` says | ready, start, load, tick |

## Simple syntax (for your very first mod)

No braces, no English required. Write plain words; a block ends with `конец` (or `end`) — or just
with indentation, like Python. ModScript X turns it into regular `.modx`, so it works in every engine.

```
мод "Много золота"
автор Вася

когда игра началась
    дай золото 1000
    скажи Привет! У тебя {золото} золота

каждый кадр
    если здоровье меньше 10
        здоровье = 100
```

| You write | Means |
|---|---|
| `когда игра началась` / `when game starts` | `hook start {` |
| `когда игра загружена` / `when game loads` | `hook load {` |
| `когда игра готова` / `when mod is ready` | `hook ready {` |
| `каждый кадр` / `every frame` / `всегда` | `hook tick {` |
| `дай золото 100`, `дай 100 золота` / `give gold 100` | `add gold 100` |
| `забери золото 10` / `take gold 10` | `gold -= 10` |
| `поставь здоровье в 100`, `здоровье = 100` / `set health to 100` | `set health = 100` |
| `умножь золото на 2`, `раздели золото на 2` | `gold *= 2`, `gold /= 2` |
| `скажи Привет!` / `say Hello!` | `say "Привет!"` (quotes are optional) |
| `запиши …` / `log …` | `log …` |
| `если …` · `иначе если …` · `иначе` | `if … {` · `} else if … {` · `} else {` |
| `больше`, `меньше`, `равно`, `не равно`, `больше или равно` | `>`, `<`, `==`, `!=`, `>=` |
| `и`, `или`, `не`, `да`, `нет` | `and`, `or`, `not`, `true`, `false` |
| `пока …` / `повтори 3 раза` / `для i от 1 до 10` / `для каждого x из список` | loops |
| `функция удвоить(х)` · `верни х * 2` · `вызови удвоить(5)` | functions |
| `стоп` · `дальше` | `break` · `continue` |
| `случайно(1, 6)`, `шанс(10)`, `длина(…)`, `мин`, `макс`, `округли` | `rand`, `chance`, `len`, `min`, `max`, `round` |
| `конец` / `end` | `}` |

Russian names become Latin: common game words go to their usual names (`золото` → `gold`, `здоровье` →
`health`, `очки` → `score`, `жизни` → `lives`, `опыт` → `xp`, `уровень` → `level`, `мана` → `mana`,
`монеты` → `coins`), everything else is transliterated (`кошка` → `koshka`). Errors point at the lines you wrote.
`modscript show <file.modx>` prints the regular `.modx` your simple script turns into — a good way to learn it.

## Custom engines: `modscript.engine.json`

Any game whose engine runs scripts in Lua, Python, JavaScript, C# or GDScript can take ModScript X mods,
even a homemade engine nobody has heard of. Describe it once in `modscript.engine.json` in the game folder
(`modscript profile <gameDir>` writes one and guesses the language from the game's scripts):

```json
{
  "name": "My Game",
  "language": "lua",
  "file": "mods/{id}.lua",
  "say": "Game.toast({text})",
  "log": "print({text})",
  "events": {
    "ready": "{fn}()",
    "start": "Game.on(\"start\", {fn})",
    "tick":  "Game.on(\"update\", {fn})"
  },
  "header": [], "footer": []
}
```

`{id}` is the mod id, `{fn}` the generated hook function, `{text}` the text to show. Without `events` the hooks
are exported: Lua returns a table `M` (`M.start`, `M.tick(dt)`…), Python defines `modx_hooks`, JavaScript sets
`globalThis.modx_<id>` and `module.exports`, C# makes a class with `Ready/Start/Load/Tick(dt)`, GDScript maps
events to `_init/_ready/_process` (override with plain names in `events`). The profile wins over engine
fingerprints, so detection, `inject`, `watch` and `eject` work as usual. Without a game folder:
`modscript build mod.modx --profile engine.json`.

### What "every game" honestly means

* **Code mods** work wherever the game can run scripts: the 8 built-in engines plus anything described by a profile.
* **File mods** (configs, ini, replaced assets) work for every game, including GameMaker, Source, Bethesda,
  CryEngine, Frostbite, id Tech, Java, XNA/FNA and RPG Maker XP/VX.
* A game that has no scripting at all (code compiled into the exe, no mod loader) cannot run new code without
  patching its binary or memory — ModScript X does not do that, and never touches games with anti-cheat.

## Why `.modx`

Short, readable in any language as "mod + exchange", and not used by any common format.
The old ModLaunch builder format `.mls` keeps working in ModLaunch.

## The format

```
mod "Gold Boost"            # ModScript header: mod, version, author, about
engine renpy                # optional: otherwise detected from the game folder
let bonus = 250             # shared variables, computed by ModScript, exported to every language

hook start {                # ready | start | load | tick
    set gold = bonus * 2    # set, add, say, log, call, if … { } else { }
    say "You have {gold} gold"
}

native python {             # raw engine code, used only when the target speaks this language
    renpy.notify("hi")
}
```

Expressions use `and / or / not / true / false / null`; strings interpolate `{expression}`.
Each is translated to the target language (`~=` in Lua, `===` in JavaScript, `.format()` in Ren'Py, …).

## Commands

```
modscript inspect <gameDir>
modscript inject  mod.modx --game <gameDir> [--target renpy|rpgmaker|unity|godot|lua]
modscript watch   mod.modx --game <gameDir>
modscript build   mod.modx --target lua --out build
modscript eject   <modId> --game <gameDir>
modscript selftest
```

## Install on Windows

Run `ModScriptX-Setup-1.0.0.exe`. No admin rights needed: it installs to
`%LOCALAPPDATA%\Programs\ModScript X`, adds `modscript` to your PATH, registers `.modx` files
(open in Notepad, right-click → "Check with ModScript X") and adds a Start menu entry.
Uninstall from Windows Settings → Apps.

Build it yourself: `dotnet publish -c Release -r win-x64 -o Setup/out`, then `makensis Setup/modscript.nsi` (NSIS 3).

## Limits

ModScript X is for single-player games. It only writes files into the game folder through the
engine's own mod mechanisms; it does not touch process memory. Games with anti-cheat
(EasyAntiCheat, BattlEye and others) are refused.

---

**По-русски:** ModScript X — отдельный инжектор-переходник. Один файл `.modx` переводится в Python,
JavaScript, C#, GDScript или Lua и ставится в папку модов игры; `inspect` показывает код игры
(метки и переменные Ren'Py, плагины и переменные RPG Maker, функции Lua), `watch` ставит мод заново
при каждом сохранении. Только одиночные игры, без обхода античитов.

**Простой ModScript (3.0):** мод можно писать обычными русскими словами, без скобок — `когда игра началась`,
`дай золото 100`, `скажи Привет!`, `если здоровье меньше 10`, `конец` (или просто отступ). Пример —
`Examples/first-mod.modx`, а `modscript show` показывает, во что превращается такой код.
**Свой движок:** опиши его в `modscript.engine.json` (команда `modscript profile <папка игры>`), и моды ставятся
в любой движок со скриптами на Lua, Python, JavaScript, C# или GDScript.
