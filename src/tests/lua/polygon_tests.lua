function test_poly_create()
    p = Polygon()
    
    check(p)
    checkEqual(0, p.count)
    
    p = Polygon({{0, 0}, {1, 1}, {2, 2}})
    
    check(p)
    checkEqual(3, p.count)
end

function test_poly_getVertex()
    p = Polygon({{0, 0}, {1, 1}, {2, 2}})

    checkEqual({0, 0, 0}, p:getVertex(1))
    checkEqual({1, 1, 0}, p:getVertex(2))
    checkEqual({2, 2, 0}, p:getVertex(3))
end
