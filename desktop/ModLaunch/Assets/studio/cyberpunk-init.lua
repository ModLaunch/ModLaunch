-- ModScript Studio для Cyberpunk 2077 (ModLaunch). Мод Cyber Engine Tweaks.
-- В режиме разработчика (файл dev.flag рядом) рисует в игре плашку «ModScript Studio»; когда открыта консоль CET —
-- окно мастерской: время суток, погода и подсказка про конструктор мира Entity Spawner (keanuWheeze).
-- Строки — в labels.lua на языке ModLaunch.

local L = {}
local loaded, labels = pcall(require, "labels")
if loaded and type(labels) == "table" then L = labels end

local function T(key, fallback)
    return L[key] or fallback
end

local studio = {
    dev = false,
    overlay = false,
    hour = 12,
}

local weathers = {
    { id = "24h_weather_sunny", key = "sunny", fallback = "Sunny" },
    { id = "24h_weather_rain", key = "rain", fallback = "Rain" },
    { id = "24h_weather_fog", key = "fog", fallback = "Fog" },
    { id = "24h_weather_heavy_clouds", key = "clouds", fallback = "Clouds" },
    { id = "24h_weather_sandstorm", key = "storm", fallback = "Sandstorm" },
}

registerForEvent("onInit", function()
    local flag = io.open("dev.flag", "r")
    if flag then
        studio.dev = true
        flag:close()
    end
end)

registerForEvent("onOverlayOpen", function()
    studio.overlay = true
    pcall(function()
        studio.hour = Game.GetTimeSystem():GetGameTime():Hours()
    end)
end)

registerForEvent("onOverlayClose", function()
    studio.overlay = false
end)

local amber = { 0.94, 0.71, 0.16, 1.0 }

-- Плашка в правом верхнем углу: знак { }, название и подсказка, как открыть мастерскую.
local function badge()
    local width = GetDisplayResolution()
    ImGui.SetNextWindowPos(width - 360, 28, ImGuiCond.Always)
    ImGui.SetNextWindowBgAlpha(0.86)
    ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 16)
    ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.5)
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, 16, 12)
    ImGui.PushStyleColor(ImGuiCol.Border, 0.94, 0.71, 0.16, 0.65)
    ImGui.PushStyleColor(ImGuiCol.WindowBg, 0.05, 0.055, 0.07, 0.9)
    local flags = ImGuiWindowFlags.NoDecoration + ImGuiWindowFlags.AlwaysAutoResize + ImGuiWindowFlags.NoSavedSettings
        + ImGuiWindowFlags.NoFocusOnAppearing + ImGuiWindowFlags.NoNav + ImGuiWindowFlags.NoMove
    if ImGui.Begin("##modscript_badge", flags) then
        ImGui.TextColored(amber[1], amber[2], amber[3], amber[4], "{ }")
        ImGui.SameLine()
        ImGui.Text(T("title", "ModScript Studio"))
        if studio.overlay then
            ImGui.TextDisabled(T("open", "The workshop is open"))
        else
            ImGui.TextDisabled(T("hint", "Open the CET console for the world builder"))
        end
    end
    ImGui.End()
    ImGui.PopStyleColor(2)
    ImGui.PopStyleVar(3)
end

-- Окно мастерской (видно вместе с консолью CET): время, погода, конструктор мира.
local function workshop()
    ImGui.SetNextWindowSize(440, 0, ImGuiCond.FirstUseEver)
    ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12)
    ImGui.PushStyleColor(ImGuiCol.TitleBgActive, 0.30, 0.24, 0.08, 1.0)
    if ImGui.Begin(T("title", "ModScript Studio")) then
        ImGui.TextWrapped(T("builder", "World builder: open the Entity Spawner window in the CET console."))
        ImGui.Separator()
        local hour, used = ImGui.SliderInt(T("time", "Time of day"), studio.hour, 0, 23)
        if used then
            studio.hour = hour
            pcall(function()
                Game.GetTimeSystem():SetGameTimeByHMS(hour, 0, 0)
            end)
        end
        ImGui.Text(T("weather", "Weather"))
        for i, w in ipairs(weathers) do
            if i > 1 then
                ImGui.SameLine()
            end
            if ImGui.Button(T(w.key, w.fallback)) then
                pcall(function()
                    Game.GetWeatherSystem():SetWeather(w.id, 10.0, 5)
                end)
            end
        end
        if ImGui.Button(T("weather.reset", "Normal weather")) then
            pcall(function()
                Game.GetWeatherSystem():ResetWeather(true)
            end)
        end
        ImGui.Separator()
        ImGui.TextDisabled(T("credit", "World builder: Entity Spawner by keanuWheeze"))
    end
    ImGui.End()
    ImGui.PopStyleColor(1)
    ImGui.PopStyleVar(1)
end

registerForEvent("onDraw", function()
    if not studio.dev then
        return
    end
    badge()
    if studio.overlay then
        workshop()
    end
end)

return studio
