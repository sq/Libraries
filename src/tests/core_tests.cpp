#include <test.hpp>

#include <core\core.hpp>

SUITE(Core) {
  TEST(CanUseString) {
    string a("one");
    string b("two");
    
    CHECK_EQUAL("one", a);
    CHECK_EQUAL("two", b);
  }
  
  TEST(CanUseVector) {
    vector<int> a;
    a.push_back(1);
    a.push_back(2);
    a.push_back(3);
    
    CHECK_EQUAL(1, a[0]);
    CHECK_EQUAL(2, a[1]);
    CHECK_EQUAL(3, a[2]);
  }
}
