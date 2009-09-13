/*
* TraceCmd.java --
*
*	This file implements the Tcl "trace" command.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TraceCmd.java,v 1.6 1999/08/15 19:38:36 mo Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	/// <summary> The TraceCmd class implements the Command interface for specifying
	/// a new Tcl command. The method cmdProc implements the built-in Tcl
	/// command "trace" which is used to manupilate variable traces.  See
	/// user documentation for more details.
	/// </summary>
	
	class TraceCmd : Command
	{
		
		// Valid sub-commands for the trace command.
		
		private static readonly string[] validCmds = new string[]{"variable", "vdelete", "vinfo"};
		
		private const int OPT_VARIABLE = 0;
		private const int OPT_VDELETE = 1;
		private const int OPT_VINFO = 2;
		
		// An array for quickly generating the Tcl strings corresponding to
		// the TCL.VarFlag.TRACE_READS, TCL.VarFlag.TRACE_WRITES and TCL.VarFlag.TRACE_UNSETS flags.
		
				private static TclObject[] opStr;
		
		/*
		*----------------------------------------------------------------------
		*
		* initOptStr --
		*
		*	This static method is called when the TraceCmd class is loaded
		*	into the VM. It initializes the opStr array.
		*
		* Results:
		*	Initial value for opStr.
		*
		* Side effects:
		*	The TclObjects stored in opStr are preserve()'ed.
		*
		*----------------------------------------------------------------------
		*/
		
		private static TclObject[] initOptStr()
		{
			TclObject[] strings = new TclObject[8];
			strings[0] = TclString.newInstance("error");
			strings[1] = TclString.newInstance("r");
			strings[2] = TclString.newInstance("w");
			strings[3] = TclString.newInstance("rw");
			strings[4] = TclString.newInstance("u");
			strings[5] = TclString.newInstance("ru");
			strings[6] = TclString.newInstance("wu");
			strings[7] = TclString.newInstance("rwu");
			
			for (int i = 0; i < 8; i++)
			{
				strings[i].preserve();
			}
			
			return strings;
		}
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			int len;
			
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "option [arg arg ...]");
			}
			int opt = TclIndex.get(interp, objv[1], validCmds, "option", 0);
			
			switch (opt)
			{
				
				case OPT_VARIABLE: 
				case OPT_VDELETE: 
					if (objv.Length != 5)
					{
						if (opt == OPT_VARIABLE)
						{
							throw new TclNumArgsException(interp, 1, objv, "variable name ops command");
						}
						else
						{
							throw new TclNumArgsException(interp, 1, objv, "vdelete name ops command");
						}
					}
					
					TCL.VarFlag flags = 0;
					
					string ops = objv[3].ToString();
					len = ops.Length;
					{
						for (int i = 0; i < len; i++)
						{
							switch (ops[i])
							{
								
								case 'r': 
									flags |= TCL.VarFlag.TRACE_READS;
									break;
								
								case 'w': 
									flags |= TCL.VarFlag.TRACE_WRITES;
									break;
								
								case 'u': 
									flags |= TCL.VarFlag.TRACE_UNSETS;
									break;
								
								default: 
									flags = 0;
																		goto check_ops_brk;
								
							}
						}
					}
					
check_ops_brk: ;
					
					
					if (flags == 0)
					{
						
						throw new TclException(interp, "bad operations \"" + objv[3] + "\": should be one or more of rwu");
					}
					
					if (opt == OPT_VARIABLE)
					{
						
						CmdTraceProc trace = new CmdTraceProc(objv[4].ToString(), flags);
						Var.traceVar(interp, objv[2], flags, trace);
					}
					else
					{
						// Search through all of our traces on this variable to
						// see if there's one with the given command.  If so, then
						// delete the first one that matches.
						
						
						ArrayList traces = Var.getTraces(interp, objv[2].ToString(), 0);
						if (traces != null)
						{
							len = traces.Count;
							for (int i = 0; i < len; i++)
							{
								TraceRecord rec = (TraceRecord) traces[i];
								
								if (rec.trace is CmdTraceProc)
								{
									CmdTraceProc proc = (CmdTraceProc) rec.trace;
									
									if (proc.flags == flags && proc.command.ToString().Equals(objv[4].ToString()))
									{
										Var.untraceVar(interp, objv[2], flags, proc);
										break;
									}
								}
							}
						}
					}
					break;
				
				
				case OPT_VINFO: 
					if (objv.Length != 3)
					{
						throw new TclNumArgsException(interp, 2, objv, "name");
					}
					
					ArrayList traces2 = Var.getTraces(interp, objv[2].ToString(), 0);
					if (traces2 != null)
					{
						len = traces2.Count;
						TclObject list = TclList.newInstance();
						TclObject cmd = null;
						list.preserve();
						
						try
						{
							for (int i = 0; i < len; i++)
							{
								TraceRecord rec = (TraceRecord) traces2[i];
								
								if (rec.trace is CmdTraceProc)
								{
									CmdTraceProc proc = (CmdTraceProc) rec.trace;
									TCL.VarFlag mode = proc.flags;
									mode &= (TCL.VarFlag.TRACE_READS | TCL.VarFlag.TRACE_WRITES | TCL.VarFlag.TRACE_UNSETS);
									int modeInt = (int)mode;
									modeInt /= ((int)TCL.VarFlag.TRACE_READS);
									
									cmd = TclList.newInstance();
									TclList.append(interp, cmd, opStr[modeInt]);
									TclList.append(interp, cmd, TclString.newInstance(proc.command));
									TclList.append(interp, list, cmd);
								}
							}
							interp.setResult(list);
						}
						finally
						{
							list.release();
						}
					}
					break;
				}
        return TCL.CompletionCode.RETURN;
      }
		static TraceCmd()
		{
			opStr = initOptStr();
		}
	} // TraceCmd
	class CmdTraceProc : VarTrace
	{
		
		// The command holds the Tcl script that will execute. The flags
		// hold the mode flags that define what conditions to fire under.
		
		internal string command;
		internal TCL.VarFlag flags;
		
		internal CmdTraceProc(string cmd, TCL.VarFlag newFlags)
		{
			flags = newFlags;
			command = cmd;
		}
		public  void  traceProc(Interp interp, string part1, string part2, TCL.VarFlag flags)
		{
			if (((this.flags & flags) != 0) && ((flags & TCL.VarFlag.INTERP_DESTROYED) == 0))
			{
				System.Text.StringBuilder sbuf = new System.Text.StringBuilder(command);
				
				try
				{
					Util.appendElement(interp, sbuf, part1);
					if ((System.Object) part2 != null)
					{
						Util.appendElement(interp, sbuf, part2);
					}
					else
					{
						Util.appendElement(interp, sbuf, "");
					}
					
					if ((flags & TCL.VarFlag.TRACE_READS) != 0)
					{
						Util.appendElement(interp, sbuf, "r");
					}
					else if ((flags & TCL.VarFlag.TRACE_WRITES) != 0)
					{
						Util.appendElement(interp, sbuf, "w");
					}
					else if ((flags & TCL.VarFlag.TRACE_UNSETS) != 0)
					{
						Util.appendElement(interp, sbuf, "u");
					}
				}
				catch (TclException e)
				{
					throw new TclRuntimeError("unexpected TclException: " + e.Message,e);
				}
				
				// Execute the command.
				
				interp.eval(sbuf.ToString(), 0);
			}
		}
	} // CmdTraceProc
}
