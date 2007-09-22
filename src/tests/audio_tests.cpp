#include <test.hpp>

#include <core\core.hpp>
#include <audio\audio.hpp>

using namespace audio;

SUITE(DeviceTests) {
  TEST(CanCreate) {
    shared_ptr<AudioDevice> dev(new AudioDevice());
    
    CHECK(dev);
  }
  
  TEST(CanLoadSound) {
    shared_ptr<AudioDevice> dev(new AudioDevice());
    
    {
      shared_ptr<SoundEffect> snd(dev->openSound("..\\res\\tests\\test.wav"));
    
      CHECK(snd);
    }
  }
  
  TEST(CanPlaySound) {
    shared_ptr<AudioDevice> dev(new AudioDevice());
    
    {
      shared_ptr<SoundEffect> snd(dev->openSound("..\\res\\tests\\test.wav"));
      
      // snd->play();
    }
  }
}