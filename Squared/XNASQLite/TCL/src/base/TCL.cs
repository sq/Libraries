/*
* TCL.java --
*
*	This class stores all the public constants for the tcl.lang.
*	The exact values should match those in tcl.h.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TCL.java,v 1.4 2000/05/14 23:10:20 mo Exp $
*
*/
using System;
namespace tcl.lang
{

  // This class holds all the publicly defined constants contained by the
  // tcl.lang package.

  public partial class TCL
  {

    // Flag values passed to variable-related procedures.  THESE VALUES
    // MUST BE CONSISTANT WITH THE C IMPLEMENTATION OF TCL.

    [Flags()]
    public enum VarFlag
    {
      GLOBAL_ONLY = 1,
      NAMESPACE_ONLY = 2,
      APPEND_VALUE = 4,
      LIST_ELEMENT = 8,
      TRACE_READS = 0x10,
      TRACE_WRITES = 0x20,
      TRACE_UNSETS = 0x40,
      TRACE_DESTROYED = 0x80,
      INTERP_DESTROYED = 0x100,
      LEAVE_ERR_MSG = 0x200,
      TRACE_ARRAY = 0x800,
      FIND_ONLY_NS = 0x1000,
      CREATE_NS_IF_UNKNOWN = 0x800,
    };

    // When an TclException is thrown, its compCode may contain any
    // of the following values:
    //
    // TCL.CompletionCode.ERROR		The command couldn't be completed successfully;
    //			the interpreter's result describes what went wrong.
    // TCL.CompletionCode.RETURN		The command requests that the current procedure
    //			return; the interpreter's result contains the
    //			procedure's return value.
    // TCL.CompletionCode.BREAK		The command requests that the innermost loop
    //			be exited; the interpreter's result is meaningless.
    // TCL.CompletionCode.CONTINUE		Go on to the next iteration of the current loop;
    //			the interpreter's result is meaningless.
    // TCL.CompletionCode.OK is only used internally.  TclExceptions should never be thrown with
    // the completion code TCL.CompletionCode.OK.  If the desired completion code is TCL.CompletionCode.OK, no
    // exception should be thrown.

    public enum CompletionCode
    {
      OK = 0,
      ERROR = 1,
      RETURN = 2,
      BREAK = 3,
      CONTINUE = 4
    };


    // The following value is used by the Interp::commandComplete(). It's used
    // to report that a script is not complete.

    protected internal const int INCOMPLETE = 10;

    // Flag values to pass to TCL.Tcl_DoOneEvent to disable searches
    // for some kinds of events:

    public const int DONT_WAIT = ( 1 << 1 );
    public const int WINDOW_EVENTS = ( 1 << 2 );
    public const int FILE_EVENTS = ( 1 << 3 );
    public const int TIMER_EVENTS = ( 1 << 4 );
    public const int IDLE_EVENTS = ( 1 << 5 );
    public const int ALL_EVENTS = ( ~DONT_WAIT );

    // The largest positive and negative integer values that can be
    // represented in Tcl.

    internal const long INT_MAX = 2147483647;
    internal const long INT_MIN = - 2147483648;

    // These values are used by Util.strtoul and Util.strtod to
    // report conversion errors.

    internal const int INVALID_INTEGER = -1;
    internal const int INTEGER_RANGE = -2;
    internal const int INVALID_DOUBLE = -3;
    internal const int DOUBLE_RANGE = -4;

    // Positions to pass to TCL.Tcl_QueueEvent. THESE VALUES
    // MUST BE CONSISTANT WITH THE C IMPLEMENTATION OF TCL.

    public const int QUEUE_TAIL = 0;
    public const int QUEUE_HEAD = 1;
    public const int QUEUE_MARK = 2;

    // Flags used to control the TclIndex.get method.

    public const int EXACT = 1; // Matches must be exact.

    // Flag values passed to recordAndEval and/or evalObj.
    // These values must match those defined in tcl.h !!!

    // Note: EVAL_DIRECT is not currently used in Jacl.

    public const int NO_EVAL = 0x10000;
    public const int EVAL_GLOBAL = 0x20000;
    public const int EVAL_DIRECT = 0x40000;
  } // end TCL
}
