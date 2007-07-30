
#ifndef EPS_INPUT_INPUT_H
#define EPS_INPUT_INPUT_H

#include "epsilon.h"

// Platform specific
#if defined(EPS_WIN32)
#   include "epsilon/input/win32/input.h"
#elif defined(EPS_X11)
#   include "epsilon/input/x11/input.h"
#elif defined(EPS_MACOSX)
#   include "epsilon/input/macosx/input.h"
#else
#   error "Unsupported platform!!"
#endif

/** @brief Controller class for input devices. 
 * Input servers listen on an eps_Window for events, and send them to that 
 * window.
 *
 * An input server can also listen to all input events, no matter what the
 * active focus is, but devices created with them will not raise events.
 * Client code will have to poll them explicitly.
 */
typedef struct _eps_InputServer eps_InputServer;

/** Represents a single input device. */
typedef struct _eps_InputDevice eps_InputDevice;

typedef enum _eps_InputDeviceType {
    EPS_INPUT_DEVICE_UNKNOWN,
    EPS_INPUT_DEVICE_KEYBOARD,
    EPS_INPUT_DEVICE_MOUSE,
    EPS_INPUT_DEVICE_JOYSTICK,
} eps_InputDeviceType;

typedef struct _eps_InputDeviceInfo {
    eps_char*           name;
    eps_InputDeviceType type;
    eps_uint            handle;
} eps_InputDeviceInfo;

typedef enum _eps_InputOptions {
    EPS_INPUT_OPT_END,

} eps_InputOptions;

/** Create a new eps_InputServer.
 * @param parent eps_Window to which the server belongs.  This parameter can be null, in which case
 *               the server will attempt to work in a scary global way as opposed to listening to
 *               a single window's events.  Parentless servers cannot post events.
 * @param options Typical zero-terminated option list.
 * @return A new input server, or 0 if an error occurred.
 */
EPS_EXPORT(eps_InputServer*) eps_input_createServer(eps_Window* parent, eps_uint* options);

/** Destroy an input server.
 * @note It is best to close all open input devices before doing this.  Some implementations
 *       may not be able to automatically deallocate open devices.
 * @param server The server to destroy.
 */
EPS_EXPORT(void) eps_input_destroyServer(eps_InputServer* server);

/** Returns the number of connected devices. 
 * @param server The server to query through.
 * @return The number of connected devices. (omg)
 */
EPS_EXPORT(eps_uint) eps_input_getDeviceCount(eps_InputServer* server);

/** Retrieves information about all connected devices.
 * @note I do not like passing pointers to structs around.  Is there a better way?
 * @note Use eps_input_destroyDeviceInfo when done with the struct returned by this function.
 * @param server The input server to query through.
 * @param deviceIndex Index of the device to query.
 * @return Pointer to an eps_InputDeviceInfo structure if deviceIndex is a valid index, else 0.
 */
EPS_EXPORT(eps_InputDeviceInfo*) eps_input_getDeviceInfo(eps_InputServer* server, eps_uint deviceIndex);

/** Deletes an eps_DeviceInfo instance.  Use this when done with the struct.
 */
EPS_EXPORT(void) eps_input_destroyDeviceInfo(eps_InputDeviceInfo* info);

/** Opens a connection to an input device.
 * @param server The server to open the connection from.
 * @param deviceIndex Index of the desired device.  Use eps_input_getDeviceInfo to retrieve this.
 * @param options zero-terminated list of options.
 */
EPS_EXPORT(eps_InputDevice*) eps_input_openDevice(eps_InputServer* server, eps_uint deviceIndex, eps_uint* options);

/** Close a connection to a device.
 * @param device The device to be disposed.
 */
EPS_EXPORT(void) eps_input_closeDevice(eps_InputDevice* device);

/** Gets the number of axes a given device has.
 * @param device The device.
 * @return The number of axes the device has.
 */
EPS_EXPORT(eps_uint) eps_input_getAxisCount(eps_InputDevice* device);

/** Returns the range of a given axis.
 * @param device The device
 * @param axisIndex Index of the axis in question.
 * @param minimum Pointer to an int that recieves the axis's minimum value.
 * @param maximum Pointer to an int that recieves the axis's maximum value.
 * @return True if the operation succeeded. (basically, if the axis exists)
 */
EPS_EXPORT(eps_bool) eps_input_getAxisRange(eps_InputDevice* device, eps_uint axisIndex, eps_int* minimum, eps_int* maximum);

/** Returns the position of an axis.
 * @param device The device
 * @param axisIndex The axis
 * @return The current position of the axis.  Use eps_input_getAxisRange to determine the range of the axis.
 */
EPS_EXPORT(eps_int)  eps_input_getAxisPosition(eps_InputDevice* device, eps_uint axisIndex);

/** Gets the X and Y axes of the device at once.
 * Usually, the X and Y axes are interpreted to be 0 and 1, respectively.
 * @param device The device.
 * @param x Pointer to an int to recieve the X position.
 * @param y You get the idea.
 * @return True if the operation succeeded. (ie if the operation makes sense)
 */
EPS_EXPORT(eps_bool) eps_input_getAxes(eps_InputDevice* device, eps_int* x, eps_int* y);

/** Returns the number of buttons the device has. */
EPS_EXPORT(eps_uint) eps_input_getButtonCount(eps_InputDevice*);

/** Returns the position of the button. */
EPS_EXPORT(eps_bool) eps_input_getButtonPosition(eps_InputDevice* device, eps_uint buttonIndex);

#endif
