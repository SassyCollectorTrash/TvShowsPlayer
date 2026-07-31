-- channel-osd.lua — экранная графика «телеканала» поверх видео.
--   • часы в верхнем-правом углу (всегда);
--   • «прогрев» — заставка канала на старте на несколько секунд;
--   • бампер «ДАЛЕЕ: ‹сериал›» при смене сериала (на стыке блоков карусели);
--   • плашка «‹сериал› · S01E05» при переходе к следующей серии того же сериала.
-- Всё рисуется через ASS-оверлеи mpv, поэтому работает даже при osd-level=0.
-- Имя сериала берётся из пути: первый каталог под корнем (--script-opts=channelosd-root=...).

local mp = require 'mp'
local msg = require 'mp.msg'

local CHANNEL = mp.get_opt("channelosd-name") or "LocalTV"   -- имя канала из настроек
local SPLASH_SEC = 4.0      -- длительность заставки-прогрева
local BUMPER_SEC = 3.0      -- длительность бампера «ДАЛЕЕ»
local PLASHKA_SEC = 5.0     -- длительность плашки серии
local NOW_SEC = 6.0         -- длительность плашки «сейчас идёт» (по хоткею/трею)
local STARTUP_GRACE = 2.0   -- окно после старта, где показываем только заставку

local root = mp.get_opt("channelosd-root") or ""

local clock_ov = mp.create_osd_overlay("ass-events")
local msg_ov = mp.create_osd_overlay("ass-events")
local msg_timer = nil
-- Отдельный оверлей под плашку «сейчас идёт»: её вызывают вручную в любой
-- момент, поэтому держим НЕЗАВИСИМО от splash/bumper/plashka — иначе смена серии
-- или авто-resync затёрли бы её (те делят общий msg_ov и общий msg_timer).
local now_ov = mp.create_osd_overlay("ass-events")
local now_timer = nil

local startup = true
local splash_done = false
local last_show = nil

-- ---------- разбор пути ----------
local function get_show(path)
    if not path then return "" end
    local p = path:gsub("/", "\\")
    if root ~= "" then
        local prefix = root
        if prefix:sub(-1) ~= "\\" then prefix = prefix .. "\\" end
        if p:sub(1, #prefix) == prefix then
            local seg = p:sub(#prefix + 1):match("^([^\\]+)")
            if seg then return seg end
        end
    end
    return (p:match("([^\\]+)\\[^\\]+$")) or ""
end

local function get_ep(path)
    local name = (path or ""):gsub("/", "\\"):match("([^\\]+)$") or ""
    local s, e = name:match("[Ss](%d+)[ ._-]*[Ee](%d+)")
    if s and e then
        return string.format("S%02dE%02d", tonumber(s), tonumber(e))
    end
    local lead = name:match("^%s*(%d+)")     -- «001 Pokemon», «01. ...», «001_s01_...»
    if lead then
        return "№" .. tostring(tonumber(lead))
    end
    return nil
end

-- ---------- размеры экрана ----------
-- Возвращает размеры экрана И фиксирует их как систему координат оверлея,
-- иначе ASS-координаты считаются в чужом масштабе (по умолчанию ~720 по высоте).
local function prep(ov)
    local w, h = mp.get_osd_size()
    if not w or w == 0 then w = 640 end
    if not h or h == 0 then h = 480 end
    ov.res_x = w
    ov.res_y = h
    return w, h
end

local function fs(h, k) return math.max(10, math.floor(h * k)) end

local function dim_rect(w, h, alpha)
    return string.format(
        "{\\an7\\pos(0,0)\\bord0\\shad0\\1c&H000000&\\alpha&H%02X&\\p1}m 0 0 l %d 0 %d %d 0 %d{\\p0}",
        alpha, w, w, h, h)
end

-- ---------- часы ----------
local function update_clock()
    local w, h = prep(clock_ov)
    clock_ov.data = string.format(
        "{\\an9\\pos(%d,%d)\\fs%d\\b1\\bord2\\3c&H000000&\\1c&HFFFFFF&\\alpha&H20&}%s",
        w - 12, 12, fs(h, 0.05), os.date("%H:%M"))
    clock_ov:update()
end

-- ---------- транзиентные сообщения ----------
local function clear_msg()
    if msg_timer then msg_timer:kill(); msg_timer = nil end
    msg_ov.data = ""
    msg_ov:update()
end

local function show_for(seconds)
    if msg_timer then msg_timer:kill() end
    msg_timer = mp.add_timeout(seconds, clear_msg)
end

local function show_splash()
    local w, h = prep(msg_ov)
    local cx = math.floor(w / 2)
    local lines = {
        dim_rect(w, h, 0x55),
        string.format("{\\an5\\pos(%d,%d)\\fs%d\\b1\\bord3\\3c&H000000&\\1c&HFFFFFF&}%s",
            cx, math.floor(h * 0.42), fs(h, 0.17), CHANNEL),
        string.format("{\\an5\\pos(%d,%d)\\fs%d\\bord2\\3c&H000000&\\1c&HCCCCCC&}%s",
            cx, math.floor(h * 0.60), fs(h, 0.07), os.date("%H:%M")),
    }
    msg_ov.data = table.concat(lines, "\n")
    msg_ov:update()
    show_for(SPLASH_SEC)
end

local function show_bumper(show)
    local w, h = prep(msg_ov)
    local cx = math.floor(w / 2)
    local lines = {
        dim_rect(w, h, 0x88),
        string.format("{\\an5\\pos(%d,%d)\\fs%d\\b1\\bord2\\3c&H000000&\\1c&H00CCFF&}%s",
            cx, math.floor(h * 0.40), fs(h, 0.055), "ДАЛЕЕ"),
        string.format("{\\an5\\pos(%d,%d)\\fs%d\\b1\\bord3\\3c&H000000&\\1c&HFFFFFF&}%s",
            cx, math.floor(h * 0.52), fs(h, 0.10), show),
    }
    msg_ov.data = table.concat(lines, "\n")
    msg_ov:update()
    show_for(BUMPER_SEC)
end

local function show_plashka(show, ep)
    local w, h = prep(msg_ov)
    local text = ep and (show .. "  ·  " .. ep) or show
    msg_ov.data = string.format(
        "{\\an1\\pos(%d,%d)\\fs%d\\b1\\bord2\\3c&H000000&\\1c&HFFFFFF&}%s",
        14, h - 14, fs(h, 0.05), text)
    msg_ov:update()
    show_for(PLASHKA_SEC)
end

-- ---------- плашка «сейчас идёт» (по хоткею Ctrl+Alt+N или из трея) ----------
local function clear_now()
    if now_timer then now_timer:kill(); now_timer = nil end
    now_ov.data = ""
    now_ov:update()
end

local function show_now()
    local path = mp.get_property("path")
    local show = get_show(path)
    if show == "" then return end
    local ep = get_ep(path)
    local text = ep and (show .. "  ·  " .. ep) or show
    local w, h = prep(now_ov)
    local cx = math.floor(w / 2)
    local lines = {
        string.format("{\\an2\\pos(%d,%d)\\fs%d\\b1\\bord2\\3c&H000000&\\1c&H00CCFF&}%s",
            cx, math.floor(h * 0.88), fs(h, 0.035), "СЕЙЧАС В ЭФИРЕ"),
        string.format("{\\an2\\pos(%d,%d)\\fs%d\\b1\\bord3\\3c&H000000&\\1c&HFFFFFF&}%s",
            cx, math.floor(h * 0.97), fs(h, 0.06), text),
    }
    now_ov.data = table.concat(lines, "\n")
    now_ov:update()
    if now_timer then now_timer:kill() end
    now_timer = mp.add_timeout(NOW_SEC, clear_now)
end

-- ---------- события ----------
local function on_file_loaded()
    local path = mp.get_property("path")
    local show = get_show(path)
    local ep = get_ep(path)

    if startup then
        if not splash_done then
            show_splash()
            splash_done = true
            mp.add_timeout(STARTUP_GRACE, function() startup = false end)
        end
        last_show = show
        msg.info(string.format("channel-osd: [splash] show=%s ep=%s", show, tostring(ep)))
        return
    end

    if show ~= last_show then
        show_bumper(show)
        msg.info(string.format("channel-osd: [bumper] show=%s ep=%s", show, tostring(ep)))
    else
        show_plashka(show, ep)
        msg.info(string.format("channel-osd: [plashka] show=%s ep=%s", show, tostring(ep)))
    end
    last_show = show
end

-- Перемотать к началу блока СЛЕДУЮЩЕГО сериала (вызывается из лаунчера по IPC).
local function next_show()
    local count = mp.get_property_number("playlist-count", 0)
    local cur = mp.get_property_number("playlist-pos", 0)
    if count == 0 then return end
    local cur_show = get_show(mp.get_property("playlist/" .. cur .. "/filename"))
    for i = cur + 1, count - 1 do
        local s = get_show(mp.get_property("playlist/" .. i .. "/filename"))
        if s ~= cur_show then
            mp.set_property_number("playlist-pos", i)
            return
        end
    end
    mp.commandv("playlist-next")   -- не нашли (конец списка) — просто дальше
end

-- Пересинхронизация звука: пересоздать аудио-цепочку (aid off->on), не трогая
-- видео. Помогает, если после долгого эфира звук «уплыл» от картинки
-- (аудио-выход не пересоздаётся при смене серии, поэтому переключение не лечит).
local function resync_audio()
    local aid = mp.get_property_number("aid")
    if not aid then return end
    mp.set_property("aid", "no")
    mp.add_timeout(0.15, function() mp.set_property_number("aid", aid) end)
end

-- Сторож рассинхрона: следим за avsync; если звук заметно «уплыл» от картинки —
-- пишем короткую строку в лог И сами пересинхронизируем (самолечение 24/7-канала).
-- Лог маленький: пишется только при реальном уплытии, не постоянно.
local DESYNC_LOG = (os.getenv("APPDATA") or ".") .. "/localtv-desync.log"
local DESYNC_THRESH = 0.5    -- секунд: порог «звук уплыл»
local resync_after = 0       -- авто-пересинхрон не чаще раза в минуту

local function dlog(s)
    local f = io.open(DESYNC_LOG, "a")
    if f then f:write(os.date("%Y-%m-%d %H:%M:%S ") .. s .. "\n"); f:close() end
end

local function watchdog()
    local av = mp.get_property_number("avsync")
    if not av or math.abs(av) <= DESYNC_THRESH then return end
    dlog(string.format("desync avsync=%.3f file=%s", av, mp.get_property("filename") or "?"))
    local now = os.time()
    if now >= resync_after then
        resync_audio()
        resync_after = now + 60
        dlog("-> auto-resync")
    end
end

mp.register_event("file-loaded", on_file_loaded)
mp.register_script_message("localtv-next-show", next_show)
mp.register_script_message("localtv-resync", resync_audio)
mp.register_script_message("localtv-now", show_now)
update_clock()
mp.add_periodic_timer(15, update_clock)
mp.add_periodic_timer(12, watchdog)
