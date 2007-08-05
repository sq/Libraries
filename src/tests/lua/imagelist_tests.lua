function test_il_addRemove()
    il = ImageList()
    checkEqual(0, il.count)
    
    il:add(Image(32, 32))
    checkEqual(1, il.count)
    
    il:remove(1)
    checkEqual(0, il.count)
end

function test_il_getImage()
    il = ImageList()
    im1 = Image(32, 32)
    im2 = Image(48, 48)
    
    il:add(im1)
    il:add(im2)
    
    checkEqual(tostring(im1), tostring(il(1)))
    checkEqual(tostring(im2), tostring(il(2)))
end

function test_il_insert()
    il = ImageList()
    im1 = Image(32, 32)
    im2 = Image(48, 48)
    
    il:add(im1)
    il:insert(1, im2)
    
    checkEqual(tostring(im2), tostring(il(1)))
    checkEqual(tostring(im1), tostring(il(2)))
end

function test_il_clear()
    il = ImageList()
    il:add(Image(32, 32))
    il:add(Image(32, 32))
    il:add(Image(32, 32))
    
    checkEqual(3, il.count)
    
    il:clear()
    
    checkEqual(0, il.count)
end

function test_il_constructFromImages()
    images = {Image(32, 32), Image(32, 32), Image(32, 32)}
    il = ImageList(images)
    
    checkEqual(tostring(images[1]), tostring(il(1)))
    checkEqual(tostring(images[2]), tostring(il(2)))
    checkEqual(tostring(images[3]), tostring(il(3)))
end

function test_il_constructFromFilenames()
    filenames = {"..\\res\\tests\\test.jpg", "..\\res\\tests\\test.png", "..\\res\\tests\\test.gif"}
    il = ImageList(filenames)
    
    checkEqual("..\\res\\tests\\test.jpg", il(1).filename)
    checkEqual("..\\res\\tests\\test.png", il(2).filename)
    checkEqual("..\\res\\tests\\test.gif", il(3).filename)
end
