class "Bullet" (GameObject)

function Bullet:__init(x, y, img, vel)
    super(x, y)
    self.yv = 2 * vel
    self.image = game.loadImage(game.respath .. img .. ".png")
end

function Bullet:onUpdate()
    GameObject.onUpdate(self)
    
    if (self.y < -32) or (self.y > 480 + 32) then
        return true
    end
end

function Bullet:draw(g)
    local i = self.image
    g:drawImage(i, self.x - (i.width / 2), self.y - (i.height / 2))
end
