function test_image_load()
    im = Image("..\\res\\tests\\test.jpg")
    
    checkEqual(96, im.width)
    checkEqual(96, im.height)
end

function test_image_create()
    im = Image(32, 32)
    
    checkEqual(32, im.width)
    checkEqual(32, im.height)
end

function im_getPixel(x, y)
    r, g, b, a = im:getPixel(x, y)
    return {r, g, b, a}
end

function test_image_getPixel()
    im = Image("..\\res\\tests\\test.jpg")
    
    checkEqual({213, 152, 219, 255}, im_getPixel(0, 0))
    checkEqual({216, 157, 205, 255}, im_getPixel(16, 16))
    checkEqual({16, 13, 34, 255}, im_getPixel(32, 32))
end

function test_image_setPixel()
    im = Image(4, 4)
    
    checkEqual({0, 0, 0, 0}, im_getPixel(0, 0))

    im:setPixel(0, 0, 1, 2, 3, 4)
    checkEqual({1, 2, 3, 4}, im_getPixel(0, 0))

    im:setPixel(1, 1, {2, 4, 6, 8})
    checkEqual({2, 4, 6, 8}, im_getPixel(1, 1))
end

function test_image_save()
    im = Image(2, 2)
    
    im:setPixel(0, 0, 255, 0, 0, 255)
    im:setPixel(1, 0, 0, 255, 0, 255)
    im:setPixel(0, 1, 0, 0, 255, 255)
    im:setPixel(1, 1, 255, 255, 0, 255)
    
    check(im:save("test.png"))
    
    im = Image("test.png")

    checkEqual({255, 0, 0, 255}, im_getPixel(0, 0))
    checkEqual({0, 255, 0, 255}, im_getPixel(1, 0))
    checkEqual({0, 0, 255, 255}, im_getPixel(0, 1))
    checkEqual({255, 255, 0, 255}, im_getPixel(1, 1))
    
    os.remove("test.png")
end
