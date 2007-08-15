function Font(name, size)
    result = {}
    result.metrics = font.getMetrics(name, size)
    result.metrics.firstChar = math.max(32, result.metrics.firstChar)
    result.metrics.lastChar = math.min(255, result.metrics.lastChar)
    result.characters = {}
    for c = result.metrics.firstChar,result.metrics.lastChar do
        local ch = font.getCharacter(c, name, size)
        result.characters[c] = ch
    end
    return result
end

function drawString(context, font, text, x, y)
    local init_x = x
    for c = 1,#text do
        local ch = text:byte(c)
        local i = font.characters[ch]
        if i then
            local cx = x + i.metrics.glyphOriginX
            local cy = y + (font.metrics.height - i.metrics.glyphOriginY)
            context:drawImage(i, cx, cy)
            x = x + i.metrics.cellIncX
        else
            if ch == 10 then
                x = init_x
                y = y + font.metrics.height
            end
        end
    end
end
