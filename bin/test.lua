function runTest()
    frameRate = 50
    w = Window(320, 240)
    gr = w.glContext
    i = Image("..\\res\\tests\\test.png")
    w.onClose = function ()
        quit()
    end
    w.onTick = function (absolute, elapsed)
        local g = math.abs(((absolute % (2 * frameRate)) - frameRate) / frameRate) * 255
        gr:setClearColor(g, g, g, 1)
        gr:clear()
        for y=0,240,16 do
            for x=0,320,16 do
                gr:drawImage(i, x, y)
            end
        end
        gr:flip()
    end
    w.tickRate = 1000 / frameRate
    w.vsync = false
    while (wm.poll(true)) do end
end

runTest()