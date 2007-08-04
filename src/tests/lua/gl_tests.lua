function gl_setUp()
    w = Window(128, 128)
    w.caption = "OpenGL Test"
    g = w.glContext
    white = {255, 255, 255, 255}
    black = {0, 0, 0, 255}
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

function test_drawRects()
    gl_setUp()
    
    g:clear()
    
    local s = w.width - 1
    local cl = {}
    local mx = (w.width / 2)
        
    for i=0,mx,1 do
		if ((i % 2) == 1) then
			cl = black
		else
			cl = white
		end
		g:drawRect(i, i, s - i, s - i, true, cl)
    end
    
    checkEqual(white, getPixel(0, 0))
    checkEqual(black, getPixel(1, 1))
    checkEqual(white, getPixel(2, 2))
    checkEqual(black, getPixel(3, 3))
    checkEqual(white, getPixel(4, 4))
    checkEqual(black, getPixel(5, 5))
    
    gl_tearDown()
end

function test_clearColor()
    gl_setUp()
    
    g:setClearColor(0, 0, 0, 255)
    g:clear()
    checkEqual(black, getPixel(0, 0))
    
    g:setClearColor(255, 0, 255, 255)
    g:clear()
    checkEqual({255, 0, 255, 255}, getPixel(0, 0))
    
    gl_tearDown()
end

function test_drawPoint()
    gl_setUp()
    
    g:clear()
    g:drawPixel(0, 0, white)
    g:drawPixel(0, 127, white)
    g:drawPixel(127, 0, white)
    g:drawPixel(127, 127, white)
    
    checkEqual(white, getPixel(0, 0))
    checkEqual(white, getPixel(0, 127))
    checkEqual(white, getPixel(127, 0))
    checkEqual(white, getPixel(127, 127))

    checkEqual(black, getPixel(1, 0))
    checkEqual(black, getPixel(0, 1))

    gl_tearDown()
end

function test_drawLine()
    gl_setUp()
    
    g:clear()
    g:drawLine(2, 2, 7, 7, white)
    
    checkEqual(white, getPixel(2, 2))
    checkEqual(white, getPixel(4, 4))
    checkEqual(white, getPixel(6, 6))

    checkEqual(black, getPixel(1, 1))
    checkEqual(black, getPixel(7, 7))

    gl_tearDown()
end

function test_drawTriangle()
    gl_setUp()
    
    g:clear()
    g:draw(
        gl.TRIANGLES, 
        {{{16, 9}, white}, {{1, 1}, white}, {{16, 1}, white}}
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
	
    checkEqual({213, 152, 219, 255}, getPixel(0, 0))
    checkEqual({223, 156, 223, 255}, getPixel(1, 1))
    checkEqual({245, 255, 246, 255}, getPixel(94, 94))
    checkEqual({170, 183, 173, 255}, getPixel(95, 95))
    checkEqual(black, getPixel(96, 96))

	gl_tearDown()
end

function test_drawImageAlpha()
	gl_setUp()
	
	i = Image("..\\res\\tests\\test.png")
	
	g:clear()
	g:drawImage(i, 0, 0)
	
    checkEqual(black, getPixel(16, 16))
    checkEqual(black, getPixel(15, 15))
    checkEqual(black, getPixel(1, 1))
    checkEqual(black, getPixel(0, 0))

    checkEqual({237, 247, 235, 255}, getPixel(7, 8))
    checkEqual({255, 255, 255, 255}, getPixel(8, 8))

	gl_tearDown()
end

function test_drawTexturedQuad()
	gl_setUp()
	
	i = Image("..\\res\\tests\\test.png")
	
	local t = i:getTexture(g)
	local v = { 
		{{0, 64}, white, {t.v0, t.u1}}, {{64, 64}, white, {t.v1, t.u1}},
		{{64, 0}, white, {t.v1, t.u0}}, {{0, 0}, white, {t.v0, t.u0}}
	}
	
	g:clear()
	g:draw(gl.QUADS, v, {t})
	
    checkEqual(black, getPixel(0, 0))
    checkEqual(black, getPixel(1, 1))
    checkEqual({81, 184, 50, 244}, getPixel(15, 15))
    checkEqual({181, 225, 174, 255}, getPixel(16, 16))
    checkEqual({112, 193, 99, 255}, getPixel(31, 31))
    checkEqual(white, getPixel(32, 32))

	gl_tearDown()
end

function test_drawText()
	gl_setUp()
    
    -- f = Font("Tahoma", 8)
    
    g:clear()
    -- g:drawText(f, "Test", 0, 0)

	gl_tearDown()
end
