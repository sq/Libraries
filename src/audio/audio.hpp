#ifndef _FR_AUDIO
#define _FR_AUDIO

#include "libs.hpp"

namespace audio {

  class AudioDevice : public enable_shared_from_this<AudioDevice> {
  
    audiere::AudioDevice * m_device;
  
  public:
    AudioDevice();
    ~AudioDevice();
    
    shared_ptr<SoundEffect> openSound(const char * filename);
    
    std::string toString() const;
  };
  
  class SoundEffect : public enable_shared_from_this<SoundEffect> {
  
    audiere::SoundEffect * m_handle;
  
  public:
    SoundEffect(AudioDevice * parent, audiere::SoundEffect * handle);
    ~SoundEffect();
    
    void play();
    
    std::string toString() const;
  };

  void registerNamespace(shared_ptr<script::Context> context);

}

#endif