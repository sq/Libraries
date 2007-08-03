-- fracture::luatest (c) 2007 Kevin Gadd --

luatest = {}

function luatest.tostring(value)
    local result = tostring(value)
    if (type(value) == "table") then
        result = "{"
        local i = 1
        for k, v in pairs(value) do
            if (k == i) then
                if (i > 1) then
                    result = result .. ", "
                end
                result = result .. luatest.tostring(v)
            else
                if (i > 1) then
                    result = result .. ", "
                end
                result = result .. tostring(k) .. ": " .. luatest.tostring(v)
            end
            i = i + 1
        end
        result = result .. "}"
    elseif (type(value) == "string") then
        result = "\"" .. value .. "\""
    end
    return result
end

function luatest.equality(lhs, rhs)
    local tlhs = type(lhs)
    local result = true
    if (tlhs == type(rhs)) and (tlhs == "table") then
        for k, v in pairs(lhs) do
            result = result and luatest.equality(lhs[k], rhs[k])
        end
        for k, v in pairs(rhs) do
            result = result and luatest.equality(lhs[k], rhs[k])
        end
    else
        result = (lhs == rhs)
    end
    return result
end

function check(b)
    if (not b) then
        failure("Assertion failed", 2)
    end
end

function checkEqual(expected, actual)
    if not luatest.equality(expected, actual) then
        failure("Expected " .. luatest.tostring(expected) .. ", got " .. luatest.tostring(actual) .. "", 2)
    end
end

function checkNotEqual(expected, actual)
    if luatest.equality(expected, actual) then
        failure("Expected something other than " .. luatest.tostring(expected) .. "", 2)
    end
end
