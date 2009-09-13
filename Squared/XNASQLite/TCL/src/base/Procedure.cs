#undef DEBUG
/*
* Procedure.java --
*
*	This class implements the body of a Tcl procedure.
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
* RCS @(#) $Id: Procedure.java,v 1.4 1999/08/05 03:40:31 mo Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> This class implements the body of a Tcl procedure.</summary>
	
	public class Procedure : Command, CommandWithDispose
	{
		
		// The formal parameters of the procedure and their default values.
		//     argList[0][0] = name of the 1st formal param
		//     argList[0][1] = if non-null, default value of the 1st formal param
		
		
		internal TclObject[][] argList;
		
		// True if this proc takes an variable number of arguments. False
		// otherwise.
		
		internal bool isVarArgs;
		
		// The body of the procedure.
		
		internal CharPointer body;
		internal int body_length;
		
		// The namespace that the Command is defined in
		internal NamespaceCmd.Namespace ns;
		
		// Name of the source file that contains this procedure. May be null, which
		// indicates that the source file is unknown.
		
		internal string srcFileName;
		
		// Position where the body of the procedure starts in the source file.
		// 1 means the first line in the source file.
		
		internal int srcLineNumber;
		
		internal Procedure(Interp interp, NamespaceCmd.Namespace ns, string name, TclObject args, TclObject b, string sFileName, int sLineNumber)
		{
			this.ns = ns;
			srcFileName = sFileName;
			srcLineNumber = sLineNumber;
			
			// Break up the argument list into argument specifiers, then process
			// each argument specifier.
			
			int numArgs = TclList.getLength(interp, args);
			argList = new TclObject[numArgs][];
			for (int i = 0; i < numArgs; i++)
			{
				argList[i] = new TclObject[2];
			}
			
			for (int i = 0; i < numArgs; i++)
			{
				// Now divide the specifier up into name and default.
				
				TclObject argSpec = TclList.index(interp, args, i);
				int specLen = TclList.getLength(interp, argSpec);
				
				if (specLen == 0)
				{
					throw new TclException(interp, "procedure \"" + name + "\" has argument with no name");
				}
				if (specLen > 2)
				{
					
					throw new TclException(interp, "too many fields in argument " + "specifier \"" + argSpec + "\"");
				}
				
				argList[i][0] = TclList.index(interp, argSpec, 0);
				argList[i][0].preserve();
				if (specLen == 2)
				{
					argList[i][1] = TclList.index(interp, argSpec, 1);
					argList[i][1].preserve();
				}
				else
				{
					argList[i][1] = null;
				}
			}
			
			
			if (numArgs > 0 && (argList[numArgs - 1][0].ToString().Equals("args")))
			{
				isVarArgs = true;
			}
			else
			{
				isVarArgs = false;
			}
			
			
			body = new CharPointer(b.ToString());
			body_length = body.length();
		}
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			// Create the call frame and parameter bindings
			
			CallFrame frame = interp.newCallFrame(this, argv);
			
			// Execute the body
			
			interp.pushDebugStack(srcFileName, srcLineNumber);
			try
			{
				Parser.eval2(interp, body.array, body.index, body_length, 0);
			}
			catch (TclException e)
			{
				TCL.CompletionCode code = e.getCompletionCode();
				if (code == TCL.CompletionCode.RETURN)
				{
					TCL.CompletionCode realCode = interp.updateReturnInfo();
					if (realCode != TCL.CompletionCode.OK)
					{
						e.setCompletionCode(realCode);
						throw ;
					}
				}
				else if (code == TCL.CompletionCode.ERROR)
				{
					
					interp.addErrorInfo("\n    (procedure \"" + argv[0] + "\" line " + interp.errorLine + ")");
					throw ;
				}
				else if (code == TCL.CompletionCode.BREAK)
				{
					throw new TclException(interp, "invoked \"break\" outside of a loop");
				}
				else if (code == TCL.CompletionCode.CONTINUE)
				{
					throw new TclException(interp, "invoked \"continue\" outside of a loop");
				}
				else
				{
					throw ;
				}
			}
			finally
			{
				interp.popDebugStack();
				
				// The check below is a hack.  The problem is that there
				// could be unset traces on the variables, which cause
				// scripts to be evaluated.  This will clear the
				// errInProgress flag, losing stack trace information if
				// the procedure was exiting with an error.  The code
				// below preserves the flag.  Unfortunately, that isn't
				// really enough: we really should preserve the errorInfo
				// variable too (otherwise a nested error in the trace
				// script will trash errorInfo).  What's really needed is
				// a general-purpose mechanism for saving and restoring
				// interpreter state.
				
				if (interp.errInProgress)
				{
					frame.dispose();
					interp.errInProgress = true;
				}
				else
				{
					frame.dispose();
				}
			}
      return TCL.CompletionCode.RETURN;
    }
		public  void  disposeCmd()
		{
			//body.release();
			body = null;
			for (int i = 0; i < argList.Length; i++)
			{
				argList[i][0].release();
				argList[i][0] = null;
				
				if (argList[i][1] != null)
				{
					argList[i][1].release();
					argList[i][1] = null;
				}
			}
			argList = null;
		}
		
		internal static bool isProc(WrappedCommand cmd)
		{
			return (cmd.cmd is Procedure);
			
			/*
			// FIXME: do we really want to get the original command
			// and test that? Methods like InfoCmd.InfoProcsCmd seem
			// to do this already.
			
			WrappedCommand origCmd;
			
			origCmd = NamespaceCmd.getOriginalCommand(cmd);
			if (origCmd != null) {
			cmd = origCmd;
			}
			return (cmd.cmd instanceof Procedure);
			*/
		}
		
		internal static Procedure findProc(Interp interp, string procName)
		{
			WrappedCommand cmd;
			WrappedCommand origCmd;
			
			try
			{
				cmd = NamespaceCmd.findCommand(interp, procName, null, 0);
			}
			catch (TclException e)
			{
				// This should never happen
				throw new TclRuntimeError("unexpected TclException: " + e.Message);
			}
			
			if (cmd == null)
			{
				return null;
			}
			
			origCmd = NamespaceCmd.getOriginalCommand(cmd);
			if (origCmd != null)
			{
				cmd = origCmd;
			}
			if (!(cmd.cmd is Procedure))
			{
				return null;
			}
			return (Procedure) cmd.cmd;
		}
	} // end Procedure
}
