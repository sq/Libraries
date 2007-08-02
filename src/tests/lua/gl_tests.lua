function gl_setUp()
    w = Window(64, 64)
    w.caption = "OpenGL Test"
    g = w.glContext
end

function gl_tearDown()
    w = nil
    g = nil
end

function getPixel(x, y)
    local r, g, b, a = g:getPixel(x, y)
    return {r, g, b, a}
end

function wait()
    g:flip()
    while wm.poll(true) do end
end

function test_clearColor()
    gl_setUp()
    
    g:setClearColor(0, 0, 0, 0)
    g:clear()
    checkEqual({0, 0, 0, 0}, getPixel(0, 0))
    
    g:setClearColor(1, 0, 1, 0)
    g:clear()
    checkEqual({1, 0, 1, 0}, getPixel(0, 0))
    
    gl_tearDown()
end

function test_drawPoint()
    gl_setUp()
    
    g:clear()
    g:draw(
        gl.POINTS, 
        {{{2, 2}, {1}},{{7, 7}, {1}}}
    )
    
    checkEqual({1, 1, 1, 1}, getPixel(2, 2))
    checkEqual({1, 1, 1, 1}, getPixel(7, 7))

    checkEqual({0, 0, 0, 0}, getPixel(0, 0))
    checkEqual({0, 0, 0, 0}, getPixel(4, 4))
    checkEqual({0, 0, 0, 0}, getPixel(6, 6))

    gl_tearDown()
end

function test_drawLine()
    gl_setUp()
    
    g:clear()
    g:draw(
        gl.LINES, 
        {{{2, 2}, {1}},{{7, 7}, {1}}}
    )
    
    checkEqual({1, 1, 1, 1}, getPixel(2, 2))
    checkEqual({1, 1, 1, 1}, getPixel(4, 4))
    checkEqual({1, 1, 1, 1}, getPixel(6, 6))

    checkEqual({0, 0, 0, 0}, getPixel(0, 0))
    checkEqual({0, 0, 0, 0}, getPixel(7, 7))

    gl_tearDown()
end

function test_drawTriangle()
    gl_setUp()
    
    g:clear()
    g:draw(
        gl.TRIANGLES, 
        {{{16, 9}, {1}}, {{1, 1}, {1}},{{16, 1}, {1}}}
    )
    
    checkEqual({1, 1, 1, 1}, getPixel(2, 1))
    checkEqual({1, 1, 1, 1}, getPixel(15, 1))
    checkEqual({1, 1, 1, 1}, getPixel(15, 8))

    checkEqual({0, 0, 0, 0}, getPixel(0, 0))
    checkEqual({0, 0, 0, 0}, getPixel(2, 2))
    checkEqual({0, 0, 0, 0}, getPixel(16, 1))
    checkEqual({0, 0, 0, 0}, getPixel(15, 9))
    
    wait()

    gl_tearDown()
end