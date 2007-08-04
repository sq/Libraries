function test_load()
    im = Image("..\\res\\tests\\test.jpg")
    
    checkEqual(96, im.width)
    checkEqual(96, im.height)
end

function test_create()
    im = Image(32, 32)
    
    checkEqual(32, im.width)
    checkEqual(32, im.height)
end

function test_getPixel()
    im = Image("..\\res\\tests\\test.jpg")
    
    r, g, b, a = im:getPixel(0, 0)
    checkEqual({213, 152, 219, 255}, {r, g, b, a})
    r, g, b, a = im:getPixel(16, 16)
    checkEqual({216, 157, 205, 255}, {r, g, b, a})
    r, g, b, a = im:getPixel(32, 32)
    checkEqual({16, 13, 34, 255}, {r, g, b, a})
end

function test_setPixel()
    im = Image(4, 4)
    
    r, g, b, a = im:getPixel(0, 0)
    checkEqual({0, 0, 0, 0}, {r, g, b, a})

    im:setPixel(0, 0, 1, 2, 3, 4)
    r, g, b, a = im:getPixel(0, 0)
    checkEqual({1, 2, 3, 4}, {r, g, b, a})

    im:setPixel(1, 1, {2, 4, 6, 8})
    r, g, b, a = im:getPixel(1, 1)
    checkEqual({2, 4, 6, 8}, {r, g, b, a})
end

function test_save()
    im = Image(2, 2)
    
    im:setPixel(0, 0, 255, 0, 0, 255)
    im:setPixel(1, 0, 0, 255, 0, 255)
    im:setPixel(0, 1, 0, 0, 255, 255)
    im:setPixel(1, 1, 255, 255, 0, 255)
    
    check(im:save("test.png"))
    
    im = Image("test.png")

    r, g, b, a = im:getPixel(0, 0)
    checkEqual({255, 0, 0, 255}, {r, g, b, a})
    r, g, b, a = im:getPixel(1, 0)
    checkEqual({0, 255, 0, 255}, {r, g, b, a})
    r, g, b, a = im:getPixel(0, 1)
    checkEqual({0, 0, 255, 255}, {r, g, b, a})
    r, g, b, a = im:getPixel(1, 1)
    checkEqual({255, 255, 0, 255}, {r, g, b, a})
    
    os.remove("test.png")
end
