-- ModScript Studio для игр на RE Engine (Resident Evil, Monster Hunter, Devil May Cry, Dragon's Dogma). Скрипт REFramework.
-- Ставится ModLaunch вместе с REFramework при «Играть как разработчик». Пока включён режим разработчика ("dev" в
-- data/ModScriptStudio.json — ModLaunch выключает его, когда игра закрывается), в игре видна плашка ModScript Studio,
-- а в меню REFramework (Insert) — раздел с подсказками: свободная камера, обозреватель объектов, Lua.
-- Только цвета ImGui (без PushStyleVar): так скрипт не зависит от версии ImGui внутри REFramework.

local cfg = json.load_file("ModScriptStudio.json") or {}

local function T(key, fallback)
    local v = cfg[key]
    if type(v) == "string" and v ~= "" then return v end
    return fallback
end

local AMBER = 0xFF29B4F0
local INK = 0xFFF5F0ED
local MUTED = 0xFFB5A69E
local BG = 0xE6120E0D
local EDGE = 0xA629B4F0

-- Флаги окна-плашки: без заголовка, рамок, прокрутки, мыши и фокуса — она не мешает игре.
local BADGE_FLAGS = 1 + 2 + 4 + 8 + 64 + 256 + 4096 + 512 + 65536 + 131072

-- Шрифт Windows (ModLaunch кладёт его в reframework/fonts): во встроенном шрифте нет кириллицы и иероглифов.
local font = nil
if type(cfg.font) == "string" and imgui.load_font ~= nil then
    local ok, loaded = pcall(imgui.load_font, cfg.font, cfg.fontSize or 18, cfg.ranges)
    if ok then font = loaded end
end

local function push_font() if font ~= nil then imgui.push_font(font) end end
local function pop_font() if font ~= nil then imgui.pop_font() end end

local function badge()
    local size = imgui.get_display_size()
    if size == nil then return end
    imgui.set_next_window_pos(Vector2f.new(size.x - 380, 28), 1, Vector2f.new(0, 0))
    imgui.push_style_color(2, BG)
    imgui.push_style_color(5, EDGE)
    push_font()
    imgui.begin_window("##modscript_studio_badge", nil, BADGE_FLAGS)
    imgui.text_colored("{ }", AMBER)
    imgui.same_line()
    imgui.text_colored(T("title", "ModScript Studio"), INK)
    imgui.text_colored(T("hint", "Insert opens the developer menu"), MUTED)
    imgui.end_window()
    pop_font()
    imgui.pop_style_color(2)
end

re.on_frame(function()
    if cfg.dev ~= true then return end
    if reframework:is_drawing_ui() then return end
    badge()
end)

-- Раздел в меню REFramework: что где искать.
re.on_draw_ui(function()
    if cfg.dev ~= true then return end
    if imgui.collapsing_header(T("title", "ModScript Studio")) then
        push_font()
        imgui.text_colored("{ }  " .. T("menu", "Developer tools of this game"), AMBER)
        imgui.text(T("freecam", "Free camera: Tools → FreeCam"))
        imgui.text(T("explorer", "Game objects and their properties: DeveloperTools → ObjectExplorer"))
        imgui.text(T("lua", "Your own Lua scripts: ScriptRunner"))
        imgui.text_colored(T("credit", "REFramework by praydog — installed by ModLaunch"), MUTED)
        pop_font()
    end
end)
