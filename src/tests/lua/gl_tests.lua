function gl_setUp()
    w = Window(128, 128)
    w.caption = "OpenGL Test"
    g = w.glContext
    white = {255, 255, 255, 255}
    black = {0, 0, 0, 0}
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
    checkEqual(black, getPixel(0, 0))
    
    g:setClearColor(255, 0, 255, 0)
    g:clear()
    checkEqual({255, 0, 255, 0}, getPixel(0, 0))
    
    gl_tearDown()
end

function test_drawPoint()
    gl_setUp()
    
    g:clear()
    g:draw(
        gl.POINTS, 
        {{{2, 2}, white},{{7, 7}, white}}
    )
    
    checkEqual(white, getPixel(2, 2))
    checkEqual(white, getPixel(7, 7))

    checkEqual(black, getPixel(0, 0))
    checkEqual(black, getPixel(4, 4))
    checkEqual(black, getPixel(6, 6))

    gl_tearDown()
end

function test_drawLine()
    gl_setUp()
    
    g:clear()
    g:draw(
        gl.LINES, 
        {{{2, 2}, white},{{7, 7}, white}}
    )
    
    checkEqual(white, getPixel(2, 2))
    checkEqual(white, getPixel(4, 4))
    checkEqual(white, getPixel(6, 6))

    checkEqual(black, getPixel(0, 0))
    checkEqual(black, getPixel(7, 7))

    gl_tearDown()
end

function test_drawTriangle()
    gl_setUp()
    
    g:clear()
    g:draw(
        gl.TRIANGLES, 
        {{{16, 9}, white}, {{1, 1}, white},{{16, 1}, white}}
    )
    
    checkEqual(white, getPixel(2, 1))
    checkEqual(white, getPixel(15, 1))
    checkEqual(white, getPixel(15, 8))

    checkEqual(black, getPixel(0, 0))
    checkEqual(black, getPixel(2, 2))
    checkEqual(black, getPixel(16, 1))
    checkEqual(black, getPixel(15, 9))

    gl_tearDown()
end

function test_drawQuad()
    gl_setUp()
    
    g:clear()
    g:draw(
        gl.QUADS, 
        {{{1, 16}, white}, {{16, 16}, white},{{16, 1}, white}, {{1, 1}, white}}
    )
    
    checkEqual(white, getPixel(1, 1))
    checkEqual(white, getPixel(15, 15))
    checkEqual(white, getPixel(15, 1))
    checkEqual(white, getPixel(1, 15))

    checkEqual(black, getPixel(0, 0))
    checkEqual(black, getPixel(16, 16))
    checkEqual(black, getPixel(16, 1))
    checkEqual(black, getPixel(1, 16))

    gl_tearDown()
end

function test_drawImage()
	gl_setUp()
	
	i = Image("..\\res\\tests\\test.jpg")
	
	g:clear()
	g:drawImage(i, 0, 0)
	
    checkEqual(black, getPixel(96, 96))
    checkEqual({252, 253, 248, 255}, getPixel(95, 95))
    checkEqual({53, 60, 114, 255}, getPixel(1, 1))
    checkEqual({48, 55, 110, 255}, getPixel(0, 0))

	gl_tearDown()
end

