class "HitEffect" (GameObject)

function HitEffect:__init(x, y)
    super(x, y)
    self.frames = game.loadImageSplit(game.respath .. "hit.png", 12, 14)
    self.w = 12 / 2
    self.h = 14 / 2
    self.f = 1
end

function HitEffect:onUpdate()
    GameObject.onUpdate(self)
    self.f = self.f + 1
    if self.f > #self.frames then
        return true
    end
end

function HitEffect:draw(g)
    local i = self.frames[self.f]
    g:drawImage(i, self.x - self.w, self.y - self.h)
end
