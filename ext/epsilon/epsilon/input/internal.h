
#ifndef EPS_EPSILON_INPUT_INTERNAL_H
#define EPS_EPSILON_INPUT_INTERNAL_H

#ifndef __cplusplus
#   error "You can't include this!  C++ only!"
#endif

struct _eps_InputServer {
    virtual ~_eps_InputServer();

    virtual eps_uint getDeviceCount() = 0;
    virtual eps_InputDeviceInfo* getDeviceInfo(eps_uint index) = 0;
    virtual void destroyDeviceInfo(eps_InputDeviceInfo* deviceInfo) = 0;
    virtual eps_InputDevice* openDevice(eps_uint deviceIndex, eps_uint* options) = 0;
    virtual void closeDevice(eps_InputDevice* device) = 0;

    // For communication with the event system:
    virtual void pollDevices() = 0;
    // others?
};

#endif
