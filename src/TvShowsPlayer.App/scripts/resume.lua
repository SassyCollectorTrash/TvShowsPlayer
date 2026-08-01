-- resume.lua — канал продолжается с ТОЙ ЖЕ СЕРИИ между перезагрузками.
--
-- Сохраняет индекс текущей серии в плейлисте и восстанавливает его при старте.
-- Сама серия начинается С НАЧАЛА — точную секунду внутри серии не
-- восстанавливаем намеренно (после холодной загрузки это предсказуемее).
-- Хочешь посекундное продолжение — см. блок RESTORE_TIME ниже.
--
-- ВАЖНО: mpv автозагружает скрипты только из <config-dir>/scripts/, поэтому файл
-- обязан лежать именно в подпапке scripts\, а не в корне рабочей папки.
--
-- Состояние храним в рабочей папке (~~/ = то, что передано в --config-dir), а НЕ в
-- %APPDATA%: этот путь резолвится в РАЗНЫЕ физические папки в зависимости от того,
-- кто запустил проигрыватель (например, из песочницы упакованного приложения он
-- редиректится). Так появлялись две несогласованные закладки, и канал вставал на
-- устаревшую позицию. Рабочая папка разрешается одинаково в любом контексте.

local utils = require 'mp.utils'

-- Состояние И журнал — в рабочей папке канала.
local workdir = mp.command_native({ "expand-path", "~~/" }) or "."
local state_file = workdir .. "/localtv-channel-state.json"
local log_file = workdir .. "/localtv-resume.log"

-- Корень библиотеки (тот же, что у channel-osd.lua) — для разбора «сериал/серия».
local root = mp.get_opt("channelosd-root") or ""

-- Прогресс по каждому сериалу: { [сериал] = путь последней проигранной серии }.
-- Программа читает его и продолжает КАЖДЫЙ сериал с места при пересборке эфира
-- (добавил или убрал сериал — остальные не сбиваются). Узнаём серию по имени
-- файла, а не по номеру, поэтому вставка серии в середину не ломает прогресс.
local progress = {}
local current_show = nil   -- какой сериал идёт сейчас (возобновить ровно его после пересборки)

local restored = false
local restore_tries = 0
local RESTORE_MAX_TRIES = 12   -- ~столько серий-стартов даём, чтобы плейлист/диск «прогрелись»
local last_logged = -1

-- Лог закладки (диагностика). Пишет PID — два разных PID одновременно = два mpv.
local function rlog(s)
    local pid = (utils.getpid and utils.getpid()) or "?"
    local f = io.open(log_file, "a")
    if f then f:write(os.date("%Y-%m-%d %H:%M:%S ") .. "pid=" .. pid .. " " .. s .. "\n"); f:close() end
end

local function read_json_file(path)
    local f = io.open(path, "r")
    if not f then return nil end
    local data = f:read("*a")
    f:close()
    return data and utils.parse_json(data) or nil
end

-- Подхватываем уже накопленный прогресс (каноничный файл, иначе со старым именем
-- рядом), чтобы НЕ затереть позиции сериалов, которые в этом сеансе ещё не игрались.
local function load_progress()
    -- основной файл → запасная копия (если основной обрезан обрывом записи)
    local s = read_json_file(state_file)
        or read_json_file(state_file .. ".bak")
    if s and type(s.shows) == "table" then
        progress = s.shows
    end
end

-- Из полного пути → (сериал, rel) той же идентичности, что у генератора:
-- сериал = первый каталог под root; rel = путь относительно папки сериала.
local function show_and_rel(path)
    if not path or root == "" then return nil, nil end
    local p = path:gsub("/", "\\")
    local prefix = root:gsub("/", "\\")
    if prefix:sub(-1) ~= "\\" then prefix = prefix .. "\\" end
    if p:sub(1, #prefix):lower() ~= prefix:lower() then return nil, nil end
    local tail = p:sub(#prefix + 1)              -- "<сериал>\<...>"
    local show = tail:match("^([^\\]+)")
    if not show then return nil, nil end
    local rel = tail:sub(#show + 2)              -- после "<сериал>\"
    if rel == "" then return nil, nil end
    return show, rel
end

-- Запоминаем последнюю проигранную серию каждого сериала (для засева генератора).
local function record_progress()
    local show, rel = show_and_rel(mp.get_property("path"))
    if show then
        progress[show] = rel
        current_show = show
    end
end

-- Запись АТОМАРНАЯ: сначала полностью во временный файл, затем подменяем основной,
-- сохраняя предыдущую версию в .bak. Прямая запись усекала файл в самом начале —
-- обрыв (выключение питания, чтение приложением в этот момент) означал потерю
-- всего прогресса просмотра.
local function save_state()
    local pos = mp.get_property_number("playlist-pos", -1)
    if not pos or pos < 0 then return end
    local t = mp.get_property_number("time-pos", 0) or 0   -- сохраняем на будущее

    local tmp_file = state_file .. ".tmp"
    local f = io.open(tmp_file, "w")
    if not f then
        rlog("save FAILED: не открыть для записи " .. tmp_file)
        return
    end
    f:write(utils.format_json({ playlist_pos = pos, time_pos = t, shows = progress, current = current_show }))
    f:close()

    os.remove(state_file .. ".bak")
    os.rename(state_file, state_file .. ".bak")   -- прежняя версия остаётся страховкой
    local ok, err = os.rename(tmp_file, state_file)
    if not ok then
        rlog("save FAILED: не переименовать " .. tmp_file .. " -> " .. tostring(err))
        return
    end
    if pos ~= last_logged then
        rlog("save pos=" .. pos)
        last_logged = pos
    end
end

local function restore_state()
    if restored then return end
    restore_tries = restore_tries + 1

    local count = mp.get_property_number("playlist-count", 0) or 0

    -- основной файл → запасная копия (страховка от обрыва записи)
    local src = state_file
    local f = io.open(state_file, "r")
    if not f then
        f = io.open(state_file .. ".bak", "r")
        src = state_file .. ".bak"
    end
    if not f then
        if restore_tries >= RESTORE_MAX_TRIES then
            restored = true
            rlog("restore: state не найден (" .. state_file .. ") после " .. restore_tries .. " попыток -> старт с начала")
        end
        return   -- РЕТРАЙ на следующем file-loaded (флаг restored НЕ ставим)
    end
    local data = f:read("*a")
    f:close()

    local s = data and utils.parse_json(data)
    if not s or not s.playlist_pos or count <= 0 then
        if restore_tries >= RESTORE_MAX_TRIES then
            restored = true
            rlog("restore: не готово (count=" .. count .. ") после " .. restore_tries .. " попыток -> сдаюсь")
        end
        return
    end

    if s.playlist_pos < count then
        if (mp.get_property_number("playlist-pos", 0) or 0) ~= s.playlist_pos then
            mp.set_property_number("playlist-pos", s.playlist_pos)
        end
        -- RESTORE_TIME: чтобы продолжать с той же секунды, раскомментируй:
        -- if s.time_pos and s.time_pos > 1 then
        --     local tp = s.time_pos
        --     mp.add_timeout(0.7, function()
        --         mp.commandv("seek", tp, "absolute", "exact")
        --     end)
        -- end
    end

    restored = true   -- успех — фиксируем, повторно не восстанавливаем
    rlog("restore -> " .. s.playlist_pos .. " (count=" .. count .. ", try=" .. restore_tries .. ", src=" .. src .. ")")
end

load_progress()
mp.register_event("file-loaded", restore_state)
mp.register_event("file-loaded", record_progress)
mp.register_event("shutdown", save_state)
mp.add_periodic_timer(15, save_state)

-- «Маяк» загрузки: нет этой строки в логе после старта = скрипт не загрузился.
rlog(string.format("loaded; workdir=%s state=%s cwd=%s",
    tostring(workdir), tostring(state_file),
    tostring(mp.get_property("working-directory"))))
