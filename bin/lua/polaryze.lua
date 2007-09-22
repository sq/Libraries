require("game")
require("polaryze/hiteffect")
require("polaryze/bullet")
require("polaryze/player")
require("polaryze/enemy")

game.name = "Polaryze"
game.desiredFramerate = 60
game.objects = {n=0}
game.bullets = {n=0}
game.effects = {n=0}
game.spawnTimer = 0
game.collChecks = 0
game.ticked = false

colors = {}
colors['r'] = {220, 60, 0, 255}
colors['b'] = {0, 171, 220, 255}

game.addObject = function(d, obj)
    local n = d.n + 1
    d[n] = obj
    obj.index = n
    d.n = n
    return n
end

game.removeObject = function(d, index)
    local n = d.n
    local o = d[n]
    local j = d[index]
    j.index = nil
    o.index = index
    d[index] = o
    d[n] = nil
    d.n = n - 1
end

game.onInitialize = function()
    game.player = Player()
    game.addObject(game.objects, game.player)
end

game.onUpdate = function ()
    local m = {game.window:getMouseState()}
    local p = {game.player.x, game.player.y}
    local v = vec.between(p, m)
    local d = vec.length(v)
    d = math.min(4, d)
    v = vec.normalize(v)
    game.player.xv, game.player.yv = unpack(vec.multiply(v, d))
    
    if game.spawnTimer <= 0 then
        game.addObject(game.objects, Enemy((math.random() * 440) + 100, -16))
        game.spawnTimer = 30 + (math.random() * 25)
    else
        game.spawnTimer = game.spawnTimer - 1
    end
    
    for _,od in pairs({game.bullets, game.objects, game.effects}) do
        local i = 1
        while i <= od.n do
            local o = od[i]
            if o:onUpdate() then
                game.removeObject(od, i)
            else
                i = i + 1
            end
        end
    end
end

game.onTick = function (elapsed)
    g = game.window.glContext
    g:clear()
    for _,od in pairs({game.bullets, game.objects, game.effects}) do
        for i=1,od.n do
            (od[i]):draw(g)
        end
    end
    local back = game.loadImage(game.respath .. "hud/energy_back.png")
    local mask = game.loadImage(game.respath .. "hud/energy_mask.png")
    local mtex = mask:getTexture(g)
    g:drawImage(back, 0, 0)
    local c = colors[game.player.color]
    local w = game.player.health / game.player.maxHealth
    local v = {
        {{0, 0}, c, {0, 0}}, {{mask.width * w, 0}, c, {mtex.u1 * w, 0}}, 
        {{mask.width * w, mask.height}, c, {mtex.u1 * w, mtex.v1}}, {{0, mask.height}, c, {0, mtex.v1}}
    }
    g:draw(gl.QUADS, v, {mtex})
    g:flip()
    collectgarbage()
end
