#include <core\core.hpp>
#include <script\script.hpp>
#include <audio\audio.hpp>

using namespace audio;

_CLASS_WRAP(AudioDevice, shared_ptr<AudioDevice>)
  .def(constructor<>())

  .def("openSound", &AudioDevice::openSound)

  .def("__tostring", &AudioDevice::toString)
_END_CLASS

_CLASS_WRAP(SoundEffect, shared_ptr<SoundEffect>)
  .def("play", &SoundEffect::play)

  .def("__tostring", &SoundEffect::toString)
_END_CLASS

namespace audio {

void registerNamespace(shared_ptr<script::Context> context) {
  context->registerClass<AudioDevice>();
  context->registerHolder<AudioDevice, weak_ptr<AudioDevice>>();
  context->registerClass<SoundEffect>();
  context->registerHolder<SoundEffect, weak_ptr<SoundEffect>>();
}

AudioDevice::AudioDevice() :
  m_device(0)
{
  m_device = audiere::OpenDevice();
}

AudioDevice::~AudioDevice() {
  if (m_device) {
    m_device->unref();
    m_device = 0;
  }
}

shared_ptr<SoundEffect> AudioDevice::openSound(const char * filename) {
  audiere::SoundEffect * effect = audiere::OpenSoundEffect(m_device, filename, audiere::MULTIPLE);
  if (effect)
    return shared_ptr<SoundEffect>(new SoundEffect(this, effect));
  else
    throw std::exception("Load failed");
}

std::string AudioDevice::toString() const {
  std::stringstream buf;
  if (m_device) {
    buf << "<AudioDevice:" << core::ptrToString(this) << ">";
  } else {
    buf << "<AudioDevice:none>";
  }
  return buf.str();
}

SoundEffect::SoundEffect(AudioDevice * parent, audiere::SoundEffect * handle) :
  m_handle(handle)
{
}

SoundEffect::~SoundEffect() {
  if (m_handle) {
    m_handle->unref();
    m_handle = 0;
  }
}

void SoundEffect::play() {
  m_handle->play();
}

std::string SoundEffect::toString() const {
  std::stringstream buf;
  if (m_handle) {
    buf << "<SoundEffect:" << core::ptrToString(this) << ">";
  } else {
    buf << "<SoundEffect:none>";
  }
  return buf.str();
}

}