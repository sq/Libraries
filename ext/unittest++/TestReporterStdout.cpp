#include "TestReporterStdout.h"
#include <cstring>
#include <cstdio>

#include "TestDetails.h"

namespace UnitTest {

void TestReporterStdout::ReportFailure(TestDetails const& details, char const* failure)
{
    char const* const errorFormat = "%s(%d): %s.%s: %s\n";
    std::printf(errorFormat, strrchr(details.filename, '\\'), details.lineNumber, details.suiteName, details.testName, failure);
}

void TestReporterStdout::ReportTestStart(TestDetails const& /*test*/)
{
}

void TestReporterStdout::ReportTestFinish(TestDetails const& /*test*/, float)
{
}

void TestReporterStdout::ReportSummary(int const totalTestCount, int const failedTestCount,
                                       int const failureCount, float secondsElapsed)
{
    if (failureCount > 0)
        std::printf("FAILURE: %d out of %d tests failed (%d failures).\n", failedTestCount, totalTestCount, failureCount);
    else
        std::printf("Success: %d tests passed.\n", totalTestCount);
    std::printf("Test time: %.2f seconds.\n", secondsElapsed);
}

}
