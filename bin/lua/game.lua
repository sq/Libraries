game = {}

game.initialize = function()
    game.font = Font("Tahoma", 16)
    game.window = Window(640, 480)
    game.window.caption = game.name
    game.window.tickRate = 1000 / game.desiredFramerate
    game.window.onTick = game.window_onTick
end

game.window_onTick = function (absolute, elapsed)
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
    while (wm.poll(true)) do end
end