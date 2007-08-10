game = {}
game.window = Window(640, 480)
game.window.caption = "Polarium"
game.window.tickRate = 1000 / 60
game.window.onTick = function (absolute, elapsed)
    game.tick = absolute
    if game.onTick then
        game.onTick(elapsed)
    end
end

-- while (wm.poll(true)) do end

-- quit()