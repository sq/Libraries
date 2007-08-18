require("font")
require("vector")
require("gameobject")

game = {}
game.imageCache = {}

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
    game.font = Font("Tahoma", 14)
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
        for i=1,elapsed do
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
