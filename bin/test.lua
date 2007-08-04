function runTest()
    frameRate = 100
    w = Window(320, 240)
    gr = w.glContext
    last_second = 0
    fps = 0
    next_fps = 0
    i = Image("..\\res\\tests\\test.png")
    w.onClose = function ()
        quit()
    end
    w.onTick = function (absolute, elapsed)
        local g = math.abs(((absolute % (2 * frameRate)) - frameRate) / frameRate) * 255
        gr:setClearColor(g, g, g, 1)
        gr:clear()
        for y=0,240,2 do
            for x=0,320,2 do
                gr:drawImage(i, x, y)
            end
        end
        gr:flip()
        next_fps = next_fps + 1
        if (os.clock() - last_second > 1.0) then
            last_second = os.clock()
            fps = next_fps
            next_fps = 0
            w.caption = tostring(fps)
        end
    end
    w.tickRate = 1000 / frameRate
    w.vsync = false
    while (wm.poll(true)) do end
end

runTest()