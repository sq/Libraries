#include "Checks.h"
#include <cstring>

namespace UnitTest {

namespace {

void CheckStringsEqual(TestResults& results, char const* expected, char const* actual, const char * actual_str, 
                       TestDetails const& details)
{
    if (std::strcmp(expected, actual))
    {
        UnitTest::MemoryOutStream stream;
        stream << "Expected " << actual_str << " to be '" << expected << "' but was '" << actual << "'";

        results.OnTestFailure(details, stream.GetText());
    }
}

}


void CheckEqual(TestResults& results, char const* expected, char const* actual, const char * actual_str,
                TestDetails const& details)
{
    CheckStringsEqual(results, expected, actual, actual_str, details);
}

void CheckEqual(TestResults& results, char* expected, char* actual, const char * actual_str,
                TestDetails const& details)
{
    CheckStringsEqual(results, expected, actual, actual_str, details);
}

void CheckEqual(TestResults& results, char* expected, char const* actual, const char * actual_str,
                TestDetails const& details)
{
    CheckStringsEqual(results, expected, actual, actual_str, details);
}

void CheckEqual(TestResults& results, char const* expected, char* actual, const char * actual_str,
                TestDetails const& details)
{
    CheckStringsEqual(results, expected, actual, actual_str, details);
}


}
