#undef DEBUG
/*
* SetCmd.java --
*
*	Implements the built-in "set" Tcl command.
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: SetCmd.java,v 1.2 1999/05/09 01:23:19 dejong Exp $
*
*/
using System;
namespace tcl.lang
{

  /*
  * This class implements the built-in "set" command in Tcl.
  */

  class SetCmd : Command
  {
    public TCL.CompletionCode cmdProc( Interp interp, TclObject[] argv )
    {
      bool debug;

      if ( argv.Length == 2 )
      {
        System.Diagnostics.Debug.WriteLine( "getting value of \"" + argv[1].ToString() + "\"" );

        interp.setResult( interp.getVar( argv[1], 0 ) );
      }
      else if ( argv.Length == 3 )
      {
        System.Diagnostics.Debug.WriteLine( "setting value of \"" + argv[1].ToString() + "\" to \"" + argv[2].ToString() + "\"" );
        interp.setResult( interp.setVar( argv[1], argv[2], 0 ) );
      }
      else
      {
        throw new TclNumArgsException( interp, 1, argv, "varName ?newValue?" );
      }
      return TCL.CompletionCode.RETURN;
    }
  } // end SetCmd
}
