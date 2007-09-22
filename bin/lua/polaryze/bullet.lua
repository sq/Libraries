class "Bullet" (GameObject)

function Bullet:__init(x, y, img, vel, color)
    super(x, y)
    self.yv = 2 * vel
    self.image = game.loadImage(game.respath .. img .. ".png")
    self.w = self.image.width / 2
    self.h = self.image.height / 2
    self.radius = 4
    self.parent = nil
    self.color = color
end

function Bullet:onUpdate()
    GameObject.onUpdate(self)
    
    if (self.y < -32) or (self.y > 480 + 32) then
        return true
    end
    
    if rawequal(self.parent, game.player) then
        local i = 1
        while i <= game.objects.n do
            local o = game.objects[i]
            if rawequal(o, self.parent) then
            elseif self:touches(o) then
                if o.color ~= self.color then
                    o.health = o.health - 1
                    game.addObject(game.effects, HitEffect(self.x, self.y))
                    game.loadSound(game.respath .. "explode3.wav"):play()
                else
                    o.health = o.health - 0.2
                    game.loadSound(game.respath .. "explode2.wav"):play()
                end
                return true
            end
            i = i + 1
        end        
    else
        if self:touches(game.player) then
            game.player.shieldAlpha = 1.0
            if game.player.color ~= self.color then
                game.player:damage(3.5)
                game.addObject(game.effects, HitEffect(self.x, self.y))
                game.loadSound(game.respath .. "explode3.wav"):play()
            else
                game.player:damage(0.3)
                game.loadSound(game.respath .. "explode2.wav"):play()
            end
            return true
        end
    end
    
end

function Bullet:draw(g)
    local i = self.image
    g:drawImage(i, self.x - self.w, self.y - self.h)
end
