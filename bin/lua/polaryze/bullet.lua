class "Bullet" (GameObject)

function Bullet:__init(x, y, img, vel)
    super(x, y)
    self.yv = 2 * vel
    self.image = game.loadImage(game.respath .. img .. ".png")
    self.w = self.image.width / 2
    self.h = self.image.height / 2
    self.radius = 4
    self.parent = nil
end

function Bullet:onUpdate()
    GameObject.onUpdate(self)
    
    if (self.y < -32) or (self.y > 480 + 32) then
        return true
    end
    
    if rawequal(self.parent, game.player) then
        local i = 1
        while i <= game.objects.n do
            game.collChecks = game.collChecks + 1
            local o = game.objects[i]
            if rawequal(o, self.parent) then
            elseif self:touches(o) then
                o.health = o.health - 1
                game.addObject(game.effects, HitEffect(self.x, self.y))
                return true
            end
            i = i + 1
        end        
    else
        game.collChecks = game.collChecks + 1
        if self:touches(game.player) then
            game.player.health = game.player.health - 1
            game.addObject(game.effects, HitEffect(self.x, self.y))
            return true
        end
    end
    
end

function Bullet:draw(g)
    local i = self.image
    g:drawImage(i, self.x - self.w, self.y - self.h)
end
