function test_lua_unsafeFunctionsDisabled()
    checkEqual(nil, os.exit)
end