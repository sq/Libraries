/*
* CallFrame.java
*
* Copyright (c) 1997 Cornell University.
* Copyright (c) 1997-1998 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: CallFrame.java,v 1.10 2003/01/08 02:10:17 mdejong Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	/// <summary> This class implements a frame in the call stack.
	/// 
	/// This class can be overridden to define new variable scoping rules for
	/// the Tcl interpreter.
	/// </summary>
	
	public class CallFrame
	{
		 internal ArrayList VarNames
		{
			// FIXME : need to port Tcl 8.1 implementation here
			
			
			get
			{
				ArrayList vector = new ArrayList(10);
				
				if (varTable == null)
				{
					return vector;
				}
				
				for (IEnumerator e1 = varTable.Values.GetEnumerator(); e1.MoveNext(); )
				{
					Var v = (Var) e1.Current;
					if (!v.isVarUndefined())
					{
						vector.Add(v.hashKey);
					}
				}
				return vector;
			}
			
		}
		/// <returns> an Vector the names of the (defined) local variables
		/// in this CallFrame (excluding upvar's)
		/// </returns>
		 internal ArrayList LocalVarNames
		{
			
			
			get
			{
				ArrayList vector = new ArrayList(10);
				
				if (varTable == null)
				{
					return vector;
				}
				
				for (IEnumerator e1 = varTable.Values.GetEnumerator(); e1.MoveNext(); )
				{
					Var v = (Var) e1.Current;
					if (!v.isVarUndefined() && !v.isVarLink())
					{
						vector.Add(v.hashKey);
					}
				}
				return vector;
			}
			
		}
		/// <summary> The interpreter associated with this call frame.</summary>
		
		protected internal Interp interp;
		
		
		/// <summary> The Namespace this CallFrame is executing in.
		/// Used to resolve commands and global variables.
		/// </summary>
		
		internal NamespaceCmd.Namespace ns;
		
		/// <summary> If true, the frame was pushed to execute a Tcl procedure
		/// and may have local vars. If false, the frame was pushed to execute
		/// a namespace command and var references are treated as references
		/// to namespace vars; varTable is ignored.
		/// </summary>
		
		internal bool isProcCallFrame;
		
		/// <summary> Stores the arguments of the procedure associated with this CallFrame.
		/// Is null for global level.
		/// </summary>
		
		internal TclObject[] objv;
		
		/// <summary> Value of interp.frame when this procedure was invoked
		/// (i.e. next in stack of all active procedures).
		/// </summary>
		
		protected internal CallFrame caller;
		
		/// <summary> Value of interp.varFrame when this procedure was invoked
		/// (i.e. determines variable scoping within caller; same as
		/// caller unless an "uplevel" command or something equivalent
		/// was active in the caller).
		/// </summary>
		
		protected internal CallFrame callerVar;
		
		/// <summary> Level of recursion. = 0 for the global level.</summary>
		
		protected internal int level;
		
		/// <summary> Stores the variables of this CallFrame.</summary>
		
		protected internal Hashtable varTable;
		
		
		/// <summary> Creates a CallFrame for the global variables.</summary>
		/// <param name="interp">current interpreter.
		/// </param>
		
		internal CallFrame(Interp i)
		{
			interp = i;
			ns = i.globalNs;
			varTable = new Hashtable();
			caller = null;
			callerVar = null;
			objv = null;
			level = 0;
			isProcCallFrame = true;
		}
		
		/// <summary> Creates a CallFrame. It changes the following variables:
		/// 
		/// <ul>
		/// <li> this.caller
		/// <li> this.callerVar
		/// <li> interp.frame
		/// <li> interp.varFrame
		/// </ul>
		/// </summary>
		/// <param name="i">current interpreter.
		/// </param>
		/// <param name="proc">the procedure to invoke in this call frame.
		/// </param>
		/// <param name="objv">the arguments to the procedure.
		/// </param>
		/// <exception cref=""> TclException if error occurs in parameter bindings.
		/// </exception>
		internal CallFrame(Interp i, Procedure proc, TclObject[] objv):this(i)
		{
			
			try
			{
				chain(proc, objv);
			}
			catch (TclException e)
			{
				dispose();
				throw ;
			}
		}
		
		/// <summary> Chain this frame into the call frame stack and binds the parameters
		/// values to the formal parameters of the procedure.
		/// 
		/// </summary>
		/// <param name="proc">the procedure.
		/// </param>
		/// <param name="proc">argv the parameter values.
		/// </param>
		/// <exception cref=""> TclException if wrong number of arguments.
		/// </exception>
		internal  void  chain(Procedure proc, TclObject[] objv)
		{
			// FIXME: double check this ns thing in case where proc is renamed to different ns.
			this.ns = proc.ns;
			this.objv = objv;
			// FIXME : quick level hack : fix later
			level = (interp.varFrame == null)?1:(interp.varFrame.level + 1);
			caller = interp.frame;
			callerVar = interp.varFrame;
			interp.frame = this;
			interp.varFrame = this;
			
			// parameter bindings
			
			int numArgs = proc.argList.Length;
			
			if ((!proc.isVarArgs) && (objv.Length - 1 > numArgs))
			{
				wrongNumProcArgs(objv[0], proc);
			}
			
			int i, j;
			for (i = 0, j = 1; i < numArgs; i++, j++)
			{
				// Handle the special case of the last formal being
				// "args".  When it occurs, assign it a list consisting of
				// all the remaining actual arguments.
				
				TclObject varName = proc.argList[i][0];
				TclObject value = null;
				
				if ((i == (numArgs - 1)) && proc.isVarArgs)
				{
					value = TclList.newInstance();
					value.preserve();
					for (int k = j; k < objv.Length; k++)
					{
						TclList.append(interp, value, objv[k]);
					}
					interp.setVar(varName, value, 0);
					value.release();
				}
				else
				{
					if (j < objv.Length)
					{
						value = objv[j];
					}
					else if (proc.argList[i][1] != null)
					{
						value = proc.argList[i][1];
					}
					else
					{
						wrongNumProcArgs(objv[0], proc);
					}
					interp.setVar(varName, value, 0);
				}
			}
		}
		
		private string wrongNumProcArgs(TclObject name, Procedure proc)
		{
			int i;
			System.Text.StringBuilder sbuf = new System.Text.StringBuilder(200);
			sbuf.Append("wrong # args: should be \"");
			
			sbuf.Append(name.ToString());
			for (i = 0; i < proc.argList.Length; i++)
			{
				TclObject arg = proc.argList[i][0];
				TclObject def = proc.argList[i][1];
				
				sbuf.Append(" ");
				if (def != null)
					sbuf.Append("?");
				
				sbuf.Append(arg.ToString());
				if (def != null)
					sbuf.Append("?");
			}
			sbuf.Append("\"");
			throw new TclException(interp, sbuf.ToString());
		}
		
		/// <param name="name">the name of the variable.
		/// 
		/// </param>
		/// <returns> true if a variable exists and is defined inside this
		/// CallFrame, false otherwise
		/// </returns>
		
		internal static bool exists(Interp interp, string name)
		{
			try
			{
				Var[] result = Var.lookupVar(interp, name, null, 0, "lookup", false, false);
				if (result == null)
				{
					return false;
				}
				if (result[0].isVarUndefined())
				{
					return false;
				}
				return true;
			}
			catch (TclException e)
			{
				throw new TclRuntimeError("unexpected TclException: " + e.Message,e);
			}
		}
		
		/// <returns> an Vector the names of the (defined) variables
		/// in this CallFrame.
		/// </returns>
		
		/// <summary> Tcl_GetFrame -> getFrame
		/// 
		/// Given a description of a procedure frame, such as the first
		/// argument to an "uplevel" or "upvar" command, locate the
		/// call frame for the appropriate level of procedure.
		/// 
		/// The return value is 1 if string was either a number or a number
		/// preceded by "#" and it specified a valid frame. 0 is returned
		/// if string isn't one of the two things above (in this case,
		/// the lookup acts as if string were "1"). The frameArr[0] reference
		/// will be filled by the reference of the desired frame (unless an
		/// error occurs, in which case it isn't modified).
		/// 
		/// </summary>
		/// <param name="string">a string that specifies the level.
		/// </param>
		/// <exception cref=""> TclException if s is a valid level specifier but
		/// refers to a bad level that doesn't exist.
		/// </exception>
		
		internal static int getFrame(Interp interp, string inString, CallFrame[] frameArr)
		{
			int curLevel, level, result;
			CallFrame frame;
			
			// Parse string to figure out which level number to go to.
			
			result = 1;
			curLevel = (interp.varFrame == null)?0:interp.varFrame.level;
			
			if ((inString.Length > 0) && (inString[0] == '#'))
			{
				level = Util.getInt(interp, inString.Substring(1));
				if (level < 0)
				{
					throw new TclException(interp, "bad level \"" + inString + "\"");
				}
			}
			else if ((inString.Length > 0) && System.Char.IsDigit(inString[0]))
			{
				level = Util.getInt(interp, inString);
				level = curLevel - level;
			}
			else
			{
				level = curLevel - 1;
				result = 0;
			}
			
			// FIXME: is this a bad comment from some other proc?
			// Figure out which frame to use, and modify the interpreter so
			// its variables come from that frame.
			
			if (level == 0)
			{
				frame = null;
			}
			else
			{
				for (frame = interp.varFrame; frame != null; frame = frame.callerVar)
				{
					if (frame.level == level)
					{
						break;
					}
				}
				if (frame == null)
				{
					throw new TclException(interp, "bad level \"" + inString + "\"");
				}
			}
			frameArr[0] = frame;
			return result;
		}
		
		
		/// <summary> This method is called when this CallFrame is no longer needed.
		/// Removes the reference of this object from the interpreter so
		/// that this object can be garbage collected.
		/// <p>
		/// For this procedure to work correctly, it must not be possible
		/// for any of the variable in the table to be accessed from Tcl
		/// commands (e.g. from trace procedures).
		/// </summary>
		
		protected internal  void  dispose()
		{
			// Unchain this frame from the call stack.
			
			interp.frame = caller;
			interp.varFrame = callerVar;
			caller = null;
			callerVar = null;
			
			if (varTable != null)
			{
				Var.deleteVars(interp, varTable);
				varTable.Clear();
				varTable = null;
			}
		}
	}
}
