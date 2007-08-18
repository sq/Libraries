vec = {}

vec.between = function(s, e) 
    return {e[1] - s[1], e[2] - s[2]}
end

vec.length = function(v)
    return math.sqrt((v[1] * v[1]) + (v[2] * v[2]))
end

vec.normalize = function(v)
    local l = vec.length(v)
    local r = {0, 0}
    if l ~= 0 then
        r = {v[1] / l, v[2] / l}
    end
    return r
end

vec.multiply = function(v, num)
    return {v[1] * num, v[2] * num}
end
