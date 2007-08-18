class "GameObject"

numGameObjs = 0

function GameObject:__init(x, y)
    self.x = x
    self.y = y
    self.xv = 0
    self.yv = 0
    numGameObjs = numGameObjs + 1
end

function GameObject:__finalize()
    numGameObjs = numGameObjs - 1
end

function GameObject:onUpdate()
    self.x = self.x + self.xv
    self.y = self.y + self.yv
end

function GameObject:draw(g)
end
