class "Enemy" (GameObject)

function Enemy:__init(x, y)
    super(x, y)
    if (math.random() >= 0.5) then
        self.color = "r"
    else
        self.color = "b"
    end
    self.shoot_timer = 0
    self.dirchange_timer = 0
    self.frames = game.loadImageSplit(game.respath .. "enemy_" .. self.color .. ".png", 17, 27)
    self.radius = 9
    self.health = 3
end

function Enemy:fire()
    local bullet = Bullet(self.x, self.y + 10, "bullet_" .. self.color, 1, self.color)
    bullet.parent = self
    game.addObject(game.bullets, bullet)
    self.shoot_timer = 18 + (math.random() * 24)
end

function Enemy:onUpdate()
    if self.health <= 0 then
        game.player:regen(2)
        game.loadSound(game.respath .. "explode3.wav"):play()
        return true
    end

    self.yv = 0.6
    
    if ((self.x < 0) and (self.xv < 0)) or ((self.x > 639) and (self.xv > 0)) then
        self.dirchange_timer = 0
    end
    
    if (self.dirchange_timer <= 0) then
        self.xv = (math.random() * 1.8) - 0.9
        self.dirchange_timer = 30 + (math.random() * 80)
    else
        self.dirchange_timer = self.dirchange_timer - 1
    end

    GameObject.onUpdate(self)
    
    if (self.shoot_timer <= 0) then
        self:fire()
    else
        self.shoot_timer = self.shoot_timer - 1
    end
    
    if self.y > (480 + 32) then
        return true
    end
end

function Enemy:draw(g)
    local f = 2
    if (self.xv <= -0.4) then
        f = 1
    elseif (self.xv >= 0.4) then
        f = 3
    end
    local i = self.frames[f]
    g:drawImage(i, self.x - (i.width / 2), self.y - (i.height / 2))
end
