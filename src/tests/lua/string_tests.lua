function test_str_toBase64()
    local s = "test"
    local b64 = s:toBase64()
    checkEqual("dGVzdA==", b64)
end

function test_str_fromBase64()
    local b64 = "dGVzdA=="
    local s = b64:fromBase64()
    checkEqual("test", s)
end

function test_str_split()
    local s = "one,two,three"
    local r = s:split(",")
    checkEqual({"one", "two", "three"}, r)
    s = "one,,two,three,,,four,"
    r = s:split(",")
    checkEqual({"one", "", "two", "three", "", "", "four", ""}, r)
    r = s:split(",", false)
    checkEqual({"one", "two", "three", "four"}, r)
end

function test_str_startsWith()
    local s = "hello, world"
    checkEqual(false, s:startsWith("world"))
    checkEqual(true, s:startsWith("hello"))
    s = "test"
    checkEqual(false, s:startsWith("testing"))
    checkEqual(true, s:startsWith("test"))
end

function test_str_endsWith()
    local s = "hello, world"
    checkEqual(true, s:endsWith("world"))
    checkEqual(false, s:endsWith("hello"))
    s = "test"
    checkEqual(false, s:endsWith("testing"))
    checkEqual(true, s:endsWith("test"))
end

function test_str_compare()
    local a = "apple"
    local b = "banana"
    local c = "cat"
    checkEqual(-1, a:compare(b))
    checkEqual(-1, b:compare(c))
    checkEqual(1, b:compare(a))
    checkEqual(1, c:compare(b))
    checkEqual(0, a:compare(a))
end

function test_str_trim()
    local s = "  hello, world!  "
    local r = s:trim()
    checkEqual("hello, world!", r)
end
