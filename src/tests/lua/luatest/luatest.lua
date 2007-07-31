-- fracture::luatest (c) 2007 Kevin Gadd --

luatest = {}
tests = {}

function luatest.runtests()
    local result = 1
    local numTestsPassed = 0
    local numTestsFailed = 0
    
    for k, v in pairs(tests) do
        local success = false
        local estring = ""
        success, estring = pcall(v)
        if (not success) then
            print(estring)
            result = 0
            numTestsFailed = numTestsFailed + 1
        else
            numTestsPassed = numTestsPassed + 1
        end
    end
    
    return result
end

function check(b)
    if (not b) then
        error("Assertion failed")
    end
end

function checkEqual(expected, actual)
    if not (actual == expected) then
        error("Assertion failed: Expected '" .. tostring(expected) .. "', got '" .. tostring(actual) .. "'")
    end
end

function checkNotEqual(expected, actual)
    if (actual == expected) then
        error("Assertion failed: Expected something other than '" .. tostring(expected) .. "'")
    end
end