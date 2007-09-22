class "Player" (GameObject)

function Player:__init()
    super(320, 240)
    self.shoot_timer = 0
    self.frames = game.loadImageSplit(game.respath .. "player.png", 23, 29)
    self.shields = {}
    self.shields['r'] = game.loadImage(game.respath .. "shield_r.png")
    self.shields['b'] = game.loadImage(game.respath .. "shield_b.png")
    self.color = 'r'
    self.radius = 8
    self.maxHealth = 100
    self.healthDelta = 0
    self.health = self.maxHealth * 0.75
    self.shieldAlpha = 0
end

function Player:fire()
    local bullet = Bullet(self.x, self.y - 8, "pbullet_" .. self.color, -2, self.color)
    bullet.parent = self
    game.addObject(game.bullets, bullet)
    self.shoot_timer = 10
    self:damage(0.1)
    game.loadSound(game.respath .. "explode1.wav"):play()
end

function Player:regen(amount)
    self.healthDelta = self.healthDelta + amount
end

function Player:damage(amount)
    self.healthDelta = self.healthDelta - amount
end

function Player:onUpdate()
    GameObject.onUpdate(self)

    local i = 1
    while i <= game.objects.n do
        local o = game.objects[i]
        if rawequal(o, self) then
        elseif self:touches(o) then
            game.loadSound(game.respath .. "explode2.wav"):play()
            o.health = o.health - 0.1
            self:damage(0.35)
            self.shieldAlpha = 1.0
        end
        i = i + 1
    end

    self.healthDelta = self.healthDelta + ((10 - self.shoot_timer) / 200)
    
    if self.healthDelta < -0.25 then
        self.healthDelta = self.healthDelta + 0.25
        self.health = self.health - 0.25
    elseif self.healthDelta > 0.25 then
        self.healthDelta = self.healthDelta - 0.25
        self.health = self.health + 0.25
    else
        self.health = self.health + self.healthDelta
        self.healthDelta = 0
    end
    
    if (self.health >= self.maxHealth) then
        self.health = self.maxHealth
    elseif (self.health <= 0) then
        self.health = 0
    end
    
    self.shieldAlpha = math.max(0.0, self.shieldAlpha - 0.05)
    
    local mx, my, mb = game.window:getMouseState()

    if ((mb % 4) - (mb % 2)) == 2 then
        if self.changed then
        elseif (self.color == 'r') then
            self:damage(2)
            self.color = 'b'
        else
            self:damage(2)
            self.color = 'r'
        end
        self.changed = true
    else
        self.changed = false
    end
    
    if (self.shoot_timer <= 0) then
        if (mb % 2) == 1 then
            self:fire()
        end
    else
        self.shoot_timer = self.shoot_timer - 1
    end
end

function Player:draw(g)
    local f = 3
    if (self.xv <= -3) then
        f = 1
    elseif (self.xv <= -1) then
        f = 2
    elseif (self.xv >= 3) then
        f = 5
    elseif (self.xv >= 1) then
        f = 4
    end
    local i = self.frames[f]
    g:drawImage(i, self.x - (i.width / 2), self.y - (i.height / 2))
    local s = self.shields[self.color]
    g:drawImage(s, self.x - (s.width / 2), self.y - (s.height / 2), self.shieldAlpha)
end
