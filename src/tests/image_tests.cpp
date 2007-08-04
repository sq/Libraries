#include <test.hpp>

#include <core\core.hpp>
#include <image\image.hpp>

using namespace image;

SUITE(ImageTests) {
  TEST(CanCreate) {
    shared_ptr<Image> im(new Image(48, 32));
    
    CHECK_EQUAL(48, im->getWidth());
    CHECK_EQUAL(32, im->getHeight());
  }

  TEST(CanLoad) {
    {
      shared_ptr<Image> im(new Image("..\\res\\tests\\test.png"));
      
      CHECK_EQUAL(16, im->getWidth());
      CHECK_EQUAL(16, im->getHeight());
    }

    {
      shared_ptr<Image> im(new Image("..\\res\\tests\\test.jpg"));
      
      CHECK_EQUAL(96, im->getWidth());
      CHECK_EQUAL(96, im->getHeight());
    }

    {
      shared_ptr<Image> im(new Image("..\\res\\tests\\test.gif"));
      
      CHECK_EQUAL(80, im->getWidth());
      CHECK_EQUAL(40, im->getHeight());
    }
  }
}

SUITE(ImageListTests) {
  TEST(CanCreate) {
    shared_ptr<ImageList> il(new ImageList());
  }
}