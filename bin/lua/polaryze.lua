require("game")
require("polaryze/bullet")
require("polaryze/player")
require("polaryze/enemy")

collectgarbage("setpause", 150)
collectgarbage("setstepmul", 150)

game.name = "Polaryze"
game.desiredFramerate = 100
game.objects = {}
game.numObjects = 0
game.spawnTimer = 0
game.ticked = false

game.addObject = function(obj)
    local n = game.numObjects + 1
    game.objects[n] = obj
    obj.index = n
    game.numObjects = n
    return n
end

game.removeObject = function(index)
    local n = game.numObjects
    local o = game.objects[n]
    local j = game.objects[index]
    j.index = nil
    o.index = index
    game.objects[index] = o
    game.objects[n] = nil
    game.numObjects = n - 1
end

game.onInitialize = function()
    game.player = Player()
    game.addObject(game.player)
end

game.onUpdate = function ()
    local gcneeded = false
    local m = {game.window:getMouseState()}
    local p = {game.player.x, game.player.y}
    local v = vec.between(p, m)
    local d = vec.length(v)
    d = math.min(4, d)
    v = vec.normalize(v)
    game.player.xv, game.player.yv = unpack(vec.multiply(v, d))
    
    if game.spawnTimer <= 0 then
        game.addObject(Enemy((math.random() * 440) + 100, -16))
        game.spawnTimer = 10 + (math.random() * 20)
    else
        game.spawnTimer = game.spawnTimer - 1
    end
    
    local i = 1
    while i <= game.numObjects do
        local o = game.objects[i]
        if o:onUpdate() then
            game.removeObject(i)
            gcneeded = true
        else
            i = i + 1
        end
    end
end

game.onTick = function (elapsed)
    g = game.window.glContext
    g:clear()
    for i=1,game.numObjects do
        local o = game.objects[i]
        o:draw(g)
    end
    drawString(g, game.font, game.fps .. " fps", 0, 0)
    g:flip()

    -- collectgarbage()
end
