function test_addRemove()
    il = ImageList()
    checkEqual(0, il.count)
    
    il:add(Image(32, 32))
    checkEqual(1, il.count)
    
    il:remove(1)
    checkEqual(0, il.count)
end

function test_getImage()
    il = ImageList()
    im1 = Image(32, 32)
    im2 = Image(48, 48)
    
    il:add(im1)
    il:add(im2)
    
    checkEqual(tostring(im1), tostring(il(1)))
    checkEqual(tostring(im2), tostring(il(2)))
end
