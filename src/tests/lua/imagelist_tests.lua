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
    
    checkEqual(im1, il:getImage(1))
    checkEqual(im2, il(2))
end

function test_il_insert()
    il = ImageList()
    im1 = Image(32, 32)
    im2 = Image(48, 48)
    
    il:add(im1)
    il:insert(1, im2)
    
    checkEqual(im2, il(1))
    checkEqual(im1, il(2))
end

function test_il_insert_append()
    il = ImageList()
    im1 = Image(32, 32)
    im2 = Image(48, 48)
    
    il:insert(3, im1)
    il:insert(6, im2)
    
    checkEqual(nil, il(1))
    checkEqual(im1, il(3))
    checkEqual(nil, il(5))
    checkEqual(im2, il(6))
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
    
    checkEqual(images[1], il(1))
    checkEqual(images[2], il(2))
    checkEqual(images[3], il(3))
end

function test_il_constructFromFilenames()
    filenames = {"..\\res\\tests\\test.jpg", "..\\res\\tests\\test.png", "..\\res\\tests\\test.gif"}
    il = ImageList(filenames)
    
    checkEqual("..\\res\\tests\\test.jpg", il(1).filename)
    checkEqual("..\\res\\tests\\test.png", il(2).filename)
    checkEqual("..\\res\\tests\\test.gif", il(3).filename)
end

function test_il_storeAttributes()
    il = ImageList()
    il:add(Image(32, 32))
    il:add(Image(24, 24))
    il(1).attr = 32
    il(2).attr = 24
    checkEqual(32, il(1).attr)
    checkEqual(24, il(2).attr)
end

function test_il_typeSafety()
    il = ImageList()
    il:add(Image(32, 32))
    il:add(nil)
    checkError("ImageLists can only contain Images", function() il:add("test") end)
    checkError("ImageLists can only contain Images", function() il:add(5) end)
    checkError("ImageLists can only contain Images", function() il:add({}) end)
end
