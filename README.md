The Squared C# Library Collection

License: MIT/X11

Squared.Util
============

Assorted .NET utility functions and types. Originally authored for .NET 2.0, so some of this stuff is now redundant. Notable inclusions:
* Generic arithmetic API with support for user-defined-types
* Generic interpolator system
* Generic curve type with custom control points
* Abstract clock types for generalized timing/animation
* Tween struct for straightforward fading and interpolation of constant values
* List<T> alternative that provides fast non-order-preserving operations and mutable enumerators
* Generic sort implementation with superior performance for large values
* Zero-allocation generic list struct that heap-allocates space on demand for larger numbers of values
* EventBus class that distributes event notifications to listeners with configurable filtering and suppression.
* DeclarativeSorter constructs specialized comparer functions based on complex sort criteria for use with sorting algorithms.
* BoundMember abstraction over reflected fields/properties/events for efficient data-binding.
* Utility APIs for working with unicode text.

Squared.Threading
=================
* Minimal-locking Future type that acts as a thread-safe, strongly-typed write-once value container and allows registering callbacks to be notified upon success or failure.
* ThreadGroup API for creating a dedicated thread pool that runs strongly-typed work items from zero-allocation queues.
* Adapter APIs for async/await support.
* CancellationScope allows externally cancelling async functions that are suspended.

Squared.Task
============

* Thread-safe coroutine scheduler (based on IEnumerable)
* Thread-safe, single-thread-capable in-process HTTP server written using coroutines
* Non-blocking IO adapters for various file/network stream operations
* Adapter APIs and types for async/await support.

Squared.Render
==============

Multithreaded pipelined rendering stack for XNA that vastly outperforms XNA's built-in types and provides an expanded feature set. Full Linux/Mac compatibility via FNA.

Notable features:
* High-quality text layout engine with configurable wrapping, alignment, font fallback, hit testing, and more with pluggable support for alternative font formats
* Efficient text rendering using pre-computed text layouts
* Automated generation of packed atlases and their mip chains at runtime
* Material system with state management, cloning, and automatic propagation of common uniform values
* High-performance SpriteBatch alternative with configurable sorting, z-buffer support, and mixed material support.
* High-performance convenience APIs for rendering simple geometry
* High-quality dithered, linear-space rasterization of vector shapes, with configurable gradients, outlines, textures, and shadows

Squared.Render.Freetype
=======================
FreeType adapter for Squared.Render, with full unicode and Hi-DPI support.

Squared.Render.STB
=======================
STB_image + STB_image_write adapter for Squared.Render, with support for automatic mip-chain generation, premultiplication, and floating-point formats.

PRGUI
=====
(very incomplete, under development)
Partially Retained-mode UI library for games that attempts to merge the best of IMGUI libraries with the robustness of retained-mode desktop APIs, with support for keyboard, mouse, gamepad and touch input. Provides a flexible layout engine with comprehensive support for unicode text and scalable sizes/resolutions (based on https://github.com/randrew/layout). Delivers high-performance layout and rasterization with pluggable themes based on Squared.Render.

Squared.Game
============
Generic utility classes/APIs for XNA game development, including a robust SAT-based collision detection/resolution API and a straightforward spatial partitioning container with efficient updates and iteration (grid-based, not tree-based). This is basically Squared.Util Depends-On-XNA-Edition, so there isn't much here.
