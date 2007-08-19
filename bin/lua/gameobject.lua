class "GameObject"

function GameObject:__init(x, y)
    self.x = x
    self.y = y
    self.xv = 0
    self.yv = 0
    self.radius = 0
end

function GameObject:touches(o)
    local d = vec.distance({self.x, self.y}, {o.x, o.y})
    return (d <= (self.radius + o.radius))
end

function GameObject:onUpdate()
    self.x = self.x + self.xv
    self.y = self.y + self.yv
end

function GameObject:draw(g)
end
