function test_lua_unsafeFunctionsDisabled()
    checkEqual(nil, os.exit)
end

function test_lua_map()
    checkEqual(
        {"1", "2", "3", "4", "5"}, 
        table.map(tostring, {1, 2, 3, 4, 5})
    )
    checkEqual(
        {2, 4, 6, 8, 10}, 
        table.map(
            function (a, b) return a + b end, 
            {1, 2, 3, 4, 5}, {1, 2, 3, 4, 5}
        )
    )
    checkEqual(
        {"abc", "abc", "ab", "a"}, 
        table.map(
            function (a, b, c) local r = tostring(a) if (b) then r = r .. tostring(b) end if (c) then r = r .. tostring(c) end return r end,
            {"a", "a", "a", "a"}, {"b", "b", "b"}, {"c", "c"}
        )
    )
end

function test_lua_reduce()
    checkEqual(
        15, 
        table.reduce(
            function (lhs, rhs) return lhs + rhs end,
            {1, 2, 3, 4, 5}
        )
    )
end

function test_lua_filter()
    checkEqual(
        {1, 3, 5}, 
        table.filter(
            function (v) return v % 2 == 1 end,
            {1, 2, 3, 4, 5}
        )
    )
end

-- table.apply(fn, x) == fn(unpack(x))
function test_lua_apply()
    checkEqual(
        {3, 6, 9, 12, 15},
        table.apply(
            table.map,
            {
                function (a, b, c) return a + b + c end,
                {1, 2, 3, 4, 5},
                {1, 2, 3, 4, 5},
                {1, 2, 3, 4, 5}
            }
        )
    )
end
