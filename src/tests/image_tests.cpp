#include <test.hpp>

#include <core\core.hpp>
#include <image\image.hpp>

using namespace image;

SUITE(ImageTests) {
  TEST(CanCreate) {
    shared_ptr<Image> im = new Image(48, 32);
    
    CHECK_EQUAL(48, im.getWidth());
    CHECK_EQUAL(32, im.getHeight());
  }

  TEST(CanLoad) {
    shared_ptr<Image> im = new Image("test.png");
    
    CHECK_EQUAL(32, im.getWidth());
    CHECK_EQUAL(32, im.getHeight());
  }
}