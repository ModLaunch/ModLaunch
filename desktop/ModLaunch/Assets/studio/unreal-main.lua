-- ModScript Studio для игр на Unreal Engine (мод UE4SS). Ставится ModLaunch вместе с UE4SS при «Играть как разработчик».
-- Работает, только пока рядом лежит dev.flag (ModLaunch убирает его, когда игра закрывается): тогда в игре
--   F8  — свободная камера Unreal (ToggleDebugCamera): полёт сквозь стены, объект под прицелом и его свойства;
--   F7  — пауза мира (slomo 0 / 1): всё замирает, камера летает;
--   F6  — полёт сквозь стены для персонажа (ghost / walk);
--   ~ или F10 — консоль разработчика Unreal (summon, teleport, set, god…), Ctrl+O — окно Live View UE4SS.

local hint = {{hint}}

local function Dir()
    local src = debug.getinfo(1, "S").source or ""
    src = src:gsub("^@", "")
    return src:match("^(.*)[/\\]Scripts[/\\][^/\\]*$") or "."
end

local function DevMode()
    local f = io.open(Dir() .. "\\dev.flag", "r")
    if f then f:close() return true end
    return false
end

if not DevMode() then
    print("[ModScript Studio] developer mode is off\n")
    return
end

print("[ModScript Studio] " .. hint .. "\n")

local paused, ghost = false, false

local function Exec(command)
    ExecuteInGameThread(function()
        local pc = FindFirstOf("PlayerController")
        if not pc or not pc:IsValid() then return end
        local ksl = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
        if ksl and ksl:IsValid() then
            ksl:ExecuteConsoleCommand(pc, command, pc)
            print("[ModScript Studio] " .. command .. "\n")
        end
    end)
end

RegisterKeyBind(Key.F8, function() Exec("ToggleDebugCamera") end)

RegisterKeyBind(Key.F7, function()
    paused = not paused
    Exec(paused and "slomo 0.0001" or "slomo 1")
end)

RegisterKeyBind(Key.F6, function()
    ghost = not ghost
    Exec(ghost and "ghost" or "walk")
end)
