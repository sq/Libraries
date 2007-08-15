require("font")
require("game")

objs = {}

function newobj (x, y)
    local r = {}
    r.x = x
    r.y = y
    r.xv = 0
    r.yv = 0
    return r
end

game.name = "Polaryze"
game.desiredFramerate = 60

nextConstruct = 0

game.onUpdate = function ()
    local mx, my = game.window:getMouseState()
    
    if nextConstruct == 0 then
        objs[#objs + 1] = newobj(mx, my)
        nextConstruct = 2
    end
    nextConstruct = nextConstruct - 1
    
    local nx, ny = mx, my
    
    for k, v in pairs(objs) do
        v.x = v.x + v.xv
        v.y = v.y + v.yv
        v.xv = (nx - v.x) / 30
        v.yv = (ny - v.y) / 30
        nx = v.x
        ny = v.y
    end
end

game.onTick = function (elapsed)
    g = game.window.glContext
    g:clear()
    local text = ":)"
    for k, v in pairs(objs) do
        drawString(g, game.font, text, v.x, v.y)
    end
    local numobjs = #objs
    drawString(g, game.font, numobjs .. " objects", 0, 0)
    g:flip()
end

game.initialize()
game.run()

quit()