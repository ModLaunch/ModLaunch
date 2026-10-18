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
