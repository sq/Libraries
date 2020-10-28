The Squared C# Library Collection

License: MIT/X11

Squared.Util
============

Assorted .NET utility functions and types. Originally authored for .NET 2.0, so some of this stuff is now redundant.

Squared.Task
============

Lock-free-ish Future/Promise type for thread safe data exchange, thread safe coroutine scheduler (based on IEnumerable), and other concurrency-oriented utilities. Somewhat redundant now that C# has async support built in.

Squared.Render
==============

Multithreaded pipelined rendering stack for XNA that replaces components like SpriteBatch and sits atop things like SpriteFont. Delivers a dramatic performance improvement over stock XNA and scales better on modern multicore configurations. Offers some extra rendering primitives XNA lacks, like rendering for basic geometric shapes.

Also has Linux/Mac support via the FNA MonoGame fork.

Squared.Game
============

Generic utility classes/APIs for XNA game development, including a robust SAT-based collision detection/resolution API and a straightforward spatial partitioning container with efficient updates and iteration (grid-based, not tree-based)
