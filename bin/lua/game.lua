require("font")
require("vector")
require("gameobject")

game = {}
game.imageCache = {}
game.splitCache = {}
game.soundCache = {}

game.loadSound = function(fn)
    if game.soundCache[fn] then
        return game.soundCache[fn]
    else
        local s = game.soundEngine:openSound(fn)
        game.soundCache[fn] = s
        return s
    end
end

game.loadImageSplit = function(fn, w, h)
    if game.splitCache[fn] then
        return game.splitCache[fn]
    else
        local i = game.loadImage(fn)
        local f = i:split(w, h)
        game.splitCache[fn] = f
        return f
    end
end

game.loadImage = function(fn)
    if game.imageCache[fn] then
        return game.imageCache[fn]
    else
        local i = Image(fn)
        game.imageCache[fn] = i
        return i
    end
end

game.initialize = function()
    game.respath = "../res/" .. game.name .. "/"
    game.soundEngine = AudioDevice()
    game.font = Font("Consolas", 10)
    game.window = Window(640, 480)
    game.window.caption = game.name
    game.window.tickRate = 1000 / game.desiredFramerate
    game.window.onTick = game.window_onTick
    game.fps = 0
    game.nextfps = 0
    if game.onInitialize then
        game.onInitialize()
    end
end

game.window_onTick = function (absolute, elapsed)
    local now = os.clock()
    game.nextfps = game.nextfps + 1
    if (now - game.lastsecond) >= 1 then
        game.lastsecond = now
        game.fps = game.nextfps
        game.nextfps = 0
    end
    if game.onUpdate then
        local _absolute = absolute - elapsed
        for i=1,math.min(elapsed, 4) do
            game.currentTick = _absolute + i
            game.onUpdate()
        end
    end
    game.currentTick = absolute
    if game.onTick then
        game.onTick(elapsed)
    end
end

game.run = function()
    if game.onStart then
        game.onStart()
    end
    game.lastsecond = os.clock()

    while (wm.poll(true)) do end

    if game.onEnd then
        game.onEnd()
    end
end
