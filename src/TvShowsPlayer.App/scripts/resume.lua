-- resume.lua — канал продолжается с ТОЙ ЖЕ СЕРИИ между перезагрузками.
--
-- Сохраняет индекс текущей серии в плейлисте и восстанавливает его при старте.
-- Сама серия начинается С НАЧАЛА — точную секунду внутри серии не
-- восстанавливаем намеренно (после холодной загрузки это предсказуемее).
-- Хочешь посекундное продолжение — см. блок RESTORE_TIME ниже.
--
-- ВАЖНО: mpv автозагружает скрипты только из <config-dir>/scripts/. Лаунчер
-- запускает mpv с --config-dir="<папка кита>", поэтому этот файл обязан лежать
-- именно в подпапке scripts\, а не в корне кита.
--
-- РOOT CAUSE «терялся прогресс» (2026-06-23, найдено): состояние раньше лежало
-- в %APPDATA%, а этот путь резолвится в РАЗНЫЕ физические папки в зависимости от
-- того, КТО запустил mpv. Если канал стартовал из песочницы упакованного
-- приложения (например, Claude: APPDATA редиректится в
-- ...\Packages\Claude_*\LocalCache\Roaming), закладка писалась в ОТДЕЛЬНУЮ копию,
-- невидимую обычному запуску (Explorer/автозапуск) → две закладки расходились,
-- и канал вставал на устаревшую позицию. ЛЕЧЕНИЕ: держим состояние (как и лог)
-- в config-dir (~~/ = папка кита, передаётся как --config-dir) — он разрешается
-- ОДИНАКОВО в любом контексте (это подтвердил общий resume-лог кита).

local utils = require 'mp.utils'

-- Состояние И лог — рядом с китом (config-dir mpv), НЕ в %APPDATA%.
local kitdir = mp.command_native({ "expand-path", "~~/" }) or "."
local state_file = kitdir .. "/localtv-channel-state.json"
local log_file = kitdir .. "/localtv-resume.log"

-- Старое расположение в %APPDATA% — только для РАЗОВОЙ миграции (если нового
-- файла ещё нет, подхватим прежнюю закладку).
local legacy_state = (os.getenv("APPDATA") or os.getenv("HOME") or ".") .. "/jetix-channel-state.json"

-- Корень библиотеки (тот же, что у channel-osd.lua) — для разбора «сериал/серия».
local root = mp.get_opt("channelosd-root") or ""

-- Прогресс по каждому сериалу: { [сериал] = rel последней проигранной серии }.
-- generate_playlist.py читает его и продолжает КАЖДЫЙ сериал с места при
-- пересборке (добавил/удалил сериал — остальные не сбиваются). Идентичность по
-- имени файла, не по индексу, поэтому премьера в середину не ломает прогресс.
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

-- Подхватываем уже накопленный прогресс (новый файл, иначе legacy), чтобы НЕ
-- затереть позиции сериалов, которые в этом сеансе ещё не игрались.
local function load_progress()
    local s = read_json_file(state_file) or read_json_file(legacy_state)
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

local function save_state()
    local pos = mp.get_property_number("playlist-pos", -1)
    if not pos or pos < 0 then return end
    local t = mp.get_property_number("time-pos", 0) or 0   -- сохраняем на будущее
    local f = io.open(state_file, "w")
    if not f then
        rlog("save FAILED: не открыть для записи " .. state_file)
        return
    end
    f:write(utils.format_json({ playlist_pos = pos, time_pos = t, shows = progress, current = current_show }))
    f:close()
    if pos ~= last_logged then
        rlog("save pos=" .. pos)
        last_logged = pos
    end
end

local function restore_state()
    if restored then return end
    restore_tries = restore_tries + 1

    local count = mp.get_property_number("playlist-count", 0) or 0

    -- читаем новый файл (config-dir); если его ещё нет — разовая миграция со старого %APPDATA%
    local src = state_file
    local f = io.open(state_file, "r")
    if not f then
        f = io.open(legacy_state, "r")
        src = legacy_state
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
rlog(string.format("loaded; kitdir=%s state=%s legacy=%s appdata=%s cwd=%s",
    tostring(kitdir), tostring(state_file), tostring(legacy_state),
    tostring(os.getenv("APPDATA")), tostring(mp.get_property("working-directory"))))
