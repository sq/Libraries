/*
* UnsetCmd.java
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
* RCS @(#) $Id: UnsetCmd.java,v 1.2 1999/07/28 03:28:52 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "unset" command in Tcl.</summary>
	
	class UnsetCmd : Command
	{
		/// <summary> Tcl_UnsetObjCmd -> UnsetCmd.cmdProc
		/// 
		/// Unsets Tcl variable (s). See Tcl user documentation * for
		/// details.
		/// </summary>
		/// <exception cref=""> TclException If tries to unset a variable that does
		/// not exist.
		/// </exception>
		
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
      switch ( objv.Length )
      {
        case 2:
            interp.unsetVar( objv[1], 0 );
            break;
          case 3:
            for ( int i =  (objv[1].ToString()!="-nocomplain")?1:2 ; i < objv.Length ; i++ )
            {
              Var.unsetVar(interp, objv[i].ToString(), 0 );
            }
            break;
        default:
          if ( objv.Length < 2 )
          {
            throw new TclNumArgsException( interp, 1, objv, "varName ?varName ...?" );
          }
          break;
      }

      return TCL.CompletionCode.RETURN;
		}
	}
}
