#ifndef UNITTEST_CHECKMACROS_H 
#define UNITTEST_CHECKMACROS_H

#include "Checks.h"
#include "AssertException.h"
#include "MemoryOutStream.h"
#include "TestDetails.h"

#ifdef CHECK
    #error UnitTest++ redefines CHECK
#endif


#define CHECK(value) \
    do \
    { \
        try { \
            if (!UnitTest::Check(value)) \
                testResults_.OnTestFailure( UnitTest::TestDetails(m_details, __LINE__), "Assertion failed: " #value); \
        } \
        catch (...) { \
            testResults_.OnTestFailure(UnitTest::TestDetails(m_details, __LINE__), \
                    "Unhandled exception in CHECK(" #value ")"); \
        } \
    } while (0)

#define CHECK_EQUAL(expected, actual) \
    do \
    { \
        try { \
            UnitTest::CheckEqual(testResults_, expected, actual, #actual, UnitTest::TestDetails(m_details, __LINE__)); \
        } \
        catch (...) { \
            testResults_.OnTestFailure(UnitTest::TestDetails(m_details, __LINE__), \
                    "Unhandled exception in CHECK_EQUAL(" #expected ", " #actual ")"); \
        } \
    } while (0)

#define CHECK_CLOSE(expected, actual, tolerance) \
    do \
    { \
        try { \
            UnitTest::CheckClose(testResults_, expected, actual, #actual, tolerance, UnitTest::TestDetails(m_details, __LINE__)); \
        } \
        catch (...) { \
            testResults_.OnTestFailure(UnitTest::TestDetails(m_details, __LINE__), \
                    "Unhandled exception in CHECK_CLOSE(" #expected ", " #actual ")"); \
        } \
    } while (0)

#define CHECK_ARRAY_EQUAL(expected, actual, count) \
    do \
    { \
        try { \
            UnitTest::CheckArrayEqual(testResults_, expected, actual, #actual, count, UnitTest::TestDetails(m_details, __LINE__)); \
        } \
        catch (...) { \
            testResults_.OnTestFailure(UnitTest::TestDetails(m_details, __LINE__), \
                    "Unhandled exception in CHECK_ARRAY_EQUAL(" #expected ", " #actual ")"); \
        } \
    } while (0)

#define CHECK_ARRAY_CLOSE(expected, actual, count, tolerance) \
    do \
    { \
        try { \
            UnitTest::CheckArrayClose(testResults_, expected, actual, #actual, count, tolerance, UnitTest::TestDetails(m_details, __LINE__)); \
        } \
        catch (...) { \
            testResults_.OnTestFailure(UnitTest::TestDetails(m_details, __LINE__), \
                    "Unhandled exception in CHECK_ARRAY_CLOSE(" #expected ", " #actual ")"); \
        } \
    } while (0)

#define CHECK_ARRAY2D_CLOSE(expected, actual, rows, columns, tolerance) \
    do \
    { \
        try { \
            UnitTest::CheckArray2DClose(testResults_, expected, actual, #actual, rows, columns, tolerance, UnitTest::TestDetails(m_details, __LINE__)); \
        } \
        catch (...) { \
            testResults_.OnTestFailure(UnitTest::TestDetails(m_details, __LINE__), \
                    "Unhandled exception in CHECK_ARRAY_CLOSE(" #expected ", " #actual ")"); \
        } \
    } while (0)


#define CHECK_THROW(expression, ExpectedExceptionType) \
    do \
    { \
        bool caught_ = false; \
        try { expression; } \
        catch (ExpectedExceptionType const&) { caught_ = true; } \
        catch (...) {} \
        if (!caught_) \
            testResults_.OnTestFailure(UnitTest::TestDetails(m_details, __LINE__), "Expected exception: \"" #ExpectedExceptionType "\" not thrown"); \
    } while(0)
    
#define CHECK_THROW_STRING(expression, ExpectedExceptionMessage) \
    do \
    { \
        bool caught_ = false; \
        try { expression; } \
        catch (std::exception exception) { \
          caught_ = std::strcmp(ExpectedExceptionMessage, exception.what()) == 0; \
        } catch (...) {} \
        if (!caught_) \
          testResults_.OnTestFailure(UnitTest::TestDetails(m_details, __LINE__), "Expected exception: std::exception(\"" ExpectedExceptionMessage "\") not thrown"); \
    } while(0)

#define CHECK_ASSERT(expression) \
    CHECK_THROW(expression, UnitTest::AssertException);

#endif
