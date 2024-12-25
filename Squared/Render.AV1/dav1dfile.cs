/* dav1dfile# - C# Wrapper for dav1dfile
 *
 * Copyright (c) 2023 Evan Hemsley
 *
 * This software is provided 'as-is', without any express or implied warranty.
 * In no event will the authors be held liable for any damages arising from
 * the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 * claim that you wrote the original software. If you use this software in a
 * product, an acknowledgment in the product documentation would be
 * appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not be
 * misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source distribution.
 *
 * Evan "cosmonaut" Hemsley <evan@moonside.games>
 *
 */

using System;
using System.Reflection;
using System.Runtime.InteropServices;

public static class Dav1dfile
{
    static Dav1dfile () {
        try {
            var loader = new Squared.Util.EmbeddedDLLLoader(Assembly.GetExecutingAssembly());
            loader.Load(nativeLibName + ".dll");
        } catch (Exception exc) {
            Console.Error.WriteLine("Failed to load {0}: {1}", nativeLibName, exc.Message);
        }
    }

	const string nativeLibName = "dav1dfile";

	public const uint DAV1DFILE_MAJOR_VERSION = 1;
	public const uint DAV1DFILE_MINOR_VERSION = 0;
	public const uint DAV1DFILE_PATCH_VERSION = 0;

	public const uint DAV1DFILE_COMPILED_VERSION = (
		(DAV1DFILE_MAJOR_VERSION * 100 * 100) +
		(DAV1DFILE_MINOR_VERSION * 100) +
		(DAV1DFILE_PATCH_VERSION)
	);

	[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
	public static extern uint df_linked_version();

	public enum PixelLayout
	{
		I400,
		I420,
		I422,
		I444
	}

	[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
	public extern static int df_fopen(
		[MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
		out IntPtr context
	);

	[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
	public static extern int df_open_from_memory(IntPtr bytes, uint size, out IntPtr context);

	[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
	public extern static void df_close(IntPtr context);

	[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
	public extern static void df_videoinfo(
		IntPtr context,
		out int width,
		out int height,
		out PixelLayout pixelLayout
	);

	[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
	public extern static void df_videoinfo2(
		IntPtr context,
		out int width,
		out int height,
		out PixelLayout pixelLayout,
		out byte hbd
	);

	[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
	public extern static int df_eos(IntPtr context);

	[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
	public extern static void df_reset(IntPtr context);

	[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
	public extern static int df_readvideo(
		IntPtr context,
		int numFrames,
		out IntPtr yDataPtr,
		out IntPtr uDataPtr,
		out IntPtr vDataPtr,
		out uint yDataLength,
		out uint uvDataLength,
		out uint yStride,
		out uint uvStride
	);
}