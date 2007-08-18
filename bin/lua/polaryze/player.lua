class "Player" (GameObject)

function Player:__init()
    super(320, 240)
    self.shoot_timer = 0
    self.image = game.loadImage(game.respath .. "player.png")
    self.frames = self.image:split(23, 29)
end

function Player:fire()
    local bullet = Bullet(self.x, self.y - 8, "bullet", -1)
    game.addObject(bullet)
    self.shoot_timer = 2
end

function Player:onUpdate()
    GameObject.onUpdate(self)
    
    if (self.shoot_timer <= 0) then
        local mx, my, mb = game.window:getMouseState()
        if (mb == 1) then
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
end
