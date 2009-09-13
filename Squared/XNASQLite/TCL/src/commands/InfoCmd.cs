/*
* InfoCmd.java
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
* RCS @(#) $Id: InfoCmd.java,v 1.8 2001/11/16 23:57:13 mdejong Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	/// <summary> This class implements the built-in "info" command in Tcl.</summary>
	
	class InfoCmd : Command
	{
		private static readonly string[] validCmds = new string[]{"args", "body", "cmdcount", "commands", "complete", "default", "exists", "globals", "hostname", "level", "library", "loaded", "locals", "nameofexecutable", "patchlevel", "procs", "script", "sharedlibextension", "tclversion", "vars"};
		
		internal const int OPT_ARGS = 0;
		internal const int OPT_BODY = 1;
		internal const int OPT_CMDCOUNT = 2;
		internal const int OPT_COMMANDS = 3;
		internal const int OPT_COMPLETE = 4;
		internal const int OPT_DEFAULT = 5;
		internal const int OPT_EXISTS = 6;
		internal const int OPT_GLOBALS = 7;
		internal const int OPT_HOSTNAME = 8;
		internal const int OPT_LEVEL = 9;
		internal const int OPT_LIBRARY = 10;
		internal const int OPT_LOADED = 11;
		internal const int OPT_LOCALS = 12;
		internal const int OPT_NAMEOFEXECUTABLE = 13;
		internal const int OPT_PATCHLEVEL = 14;
		internal const int OPT_PROCS = 15;
		internal const int OPT_SCRIPT = 16;
		internal const int OPT_SHAREDLIBEXTENSION = 17;
		internal const int OPT_TCLVERSION = 18;
		internal const int OPT_VARS = 19;
		
		/// <summary> Tcl_InfoObjCmd -> InfoCmd.cmdProc
		/// 
		/// This procedure is invoked to process the "info" Tcl command.
		/// See the user documentation for details on what it does.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="argv">command arguments.
		/// </param>
		/// <exception cref=""> TclException if wrong # of args or invalid argument(s).
		/// </exception>
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] objv)
		{
			int index;
			
			if (objv.Length < 2)
			{
				throw new TclNumArgsException(interp, 1, objv, "option ?arg arg ...?");
			}
			index = TclIndex.get(interp, objv[1], validCmds, "option", 0);
			
			switch (index)
			{
				
				case OPT_ARGS: 
					InfoArgsCmd(interp, objv);
					break;
				
				case OPT_BODY: 
					InfoBodyCmd(interp, objv);
					break;
				
				case OPT_CMDCOUNT: 
					InfoCmdCountCmd(interp, objv);
					break;
				
				case OPT_COMMANDS: 
					InfoCommandsCmd(interp, objv);
					break;
				
				case OPT_COMPLETE: 
					InfoCompleteCmd(interp, objv);
					break;
				
				case OPT_DEFAULT: 
					InfoDefaultCmd(interp, objv);
					break;
				
				case OPT_EXISTS: 
					InfoExistsCmd(interp, objv);
					break;
				
				case OPT_GLOBALS: 
					InfoGlobalsCmd(interp, objv);
					break;
				
				case OPT_HOSTNAME: 
					InfoHostnameCmd(interp, objv);
					break;
				
				case OPT_LEVEL: 
					InfoLevelCmd(interp, objv);
					break;
				
				case OPT_LIBRARY: 
					InfoLibraryCmd(interp, objv);
					break;
				
				case OPT_LOADED: 
					InfoLoadedCmd(interp, objv);
					break;
				
				case OPT_LOCALS: 
					InfoLocalsCmd(interp, objv);
					break;
				
				case OPT_NAMEOFEXECUTABLE: 
					InfoNameOfExecutableCmd(interp, objv);
					break;
				
				case OPT_PATCHLEVEL: 
					InfoPatchLevelCmd(interp, objv);
					break;
				
				case OPT_PROCS: 
					InfoProcsCmd(interp, objv);
					break;
				
				case OPT_SCRIPT: 
					InfoScriptCmd(interp, objv);
					break;
				
				case OPT_SHAREDLIBEXTENSION: 
					InfoSharedlibCmd(interp, objv);
					break;
				
				case OPT_TCLVERSION: 
					InfoTclVersionCmd(interp, objv);
					break;
				
				case OPT_VARS: 
					InfoVarsCmd(interp, objv);
					break;
				}
        return TCL.CompletionCode.RETURN;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoArgsCmd --
		*
		*      Called to implement the "info args" command that returns the
		*      argument list for a procedure. Handles the following syntax:
		*
		*          info args procName
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoArgsCmd(Interp interp, TclObject[] objv)
		{
			string name;
			Procedure proc;
			TclObject listObj;
			
			if (objv.Length != 3)
			{
				throw new TclNumArgsException(interp, 2, objv, "procname");
			}
			
			name = objv[2].ToString();
			proc = Procedure.findProc(interp, name);
			if (proc == null)
			{
				throw new TclException(interp, "\"" + name + "\" isn't a procedure");
			}
			
			// Build a return list containing the arguments.
			
			listObj = TclList.newInstance();
			for (int i = 0; i < proc.argList.Length; i++)
			{
				TclObject s = TclString.newInstance(proc.argList[i][0]);
				TclList.append(interp, listObj, s);
			}
			interp.setResult(listObj);
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoBodyCmd --
		*
		*      Called to implement the "info body" command that returns the body
		*      for a procedure. Handles the following syntax:
		*
		*          info body procName
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoBodyCmd(Interp interp, TclObject[] objv)
		{
			string name;
			Procedure proc;
			//TclObject body, result;
			
			if (objv.Length != 3)
			{
				throw new TclNumArgsException(interp, 2, objv, "procname");
			}
			
			name = objv[2].ToString();
			proc = Procedure.findProc(interp, name);
			if (proc == null)
			{
				throw new TclException(interp, "\"" + name + "\" isn't a procedure");
			}
			
			
			interp.setResult(proc.body.ToString());
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoCmdCountCmd --
		*
		*      Called to implement the "info cmdcount" command that returns the
		*      number of commands that have been executed. Handles the following
		*      syntax:
		*
		*          info cmdcount
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoCmdCountCmd(Interp interp, TclObject[] objv)
		{
			if (objv.Length != 2)
			{
				throw new TclNumArgsException(interp, 2, objv, null);
			}
			interp.setResult(interp.cmdCount);
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoCommandsCmd --
		*
		*	Called to implement the "info commands" command that returns the
		*	list of commands in the interpreter that match an optional pattern.
		*	The pattern, if any, consists of an optional sequence of namespace
		*	names separated by "::" qualifiers, which is followed by a
		*	glob-style pattern that restricts which commands are returned.
		*	Handles the following syntax:
		*
		*          info commands ?pattern?
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoCommandsCmd(Interp interp, TclObject[] objv)
		{
			string cmdName, pattern, simplePattern;
			IDictionaryEnumerator search;
			NamespaceCmd.Namespace ns;
			NamespaceCmd.Namespace globalNs = NamespaceCmd.getGlobalNamespace(interp);
			NamespaceCmd.Namespace currNs = NamespaceCmd.getCurrentNamespace(interp);
			TclObject list, elemObj;
			bool specificNsInPattern = false; // Init. to avoid compiler warning.
			WrappedCommand cmd;
			
			// Get the pattern and find the "effective namespace" in which to
			// list commands.
			
			if (objv.Length == 2)
			{
				simplePattern = null;
				ns = currNs;
				specificNsInPattern = false;
			}
			else if (objv.Length == 3)
			{
				// From the pattern, get the effective namespace and the simple
				// pattern (no namespace qualifiers or ::'s) at the end. If an
				// error was found while parsing the pattern, return it. Otherwise,
				// if the namespace wasn't found, just leave ns NULL: we will
				// return an empty list since no commands there can be found.
				
				
				pattern = objv[2].ToString();
				
				// Java does not support passing an address so we pass
				// an array of size 1 and then assign arr[0] to the value
				NamespaceCmd.Namespace[] nsArr = new NamespaceCmd.Namespace[1];
				NamespaceCmd.Namespace[] dummy1Arr = new NamespaceCmd.Namespace[1];
				NamespaceCmd.Namespace[] dummy2Arr = new NamespaceCmd.Namespace[1];
				string[] simplePatternArr = new string[1];
				
				NamespaceCmd.getNamespaceForQualName(interp, pattern, null, 0, nsArr, dummy1Arr, dummy2Arr, simplePatternArr);
				
				// Get the values out of the arrays!
				ns = nsArr[0];
				simplePattern = simplePatternArr[0];
				
				if (ns != null)
				{
					// we successfully found the pattern's ns
					specificNsInPattern = (simplePattern.CompareTo(pattern) != 0);
				}
			}
			else
			{
				throw new TclNumArgsException(interp, 2, objv, "?pattern?");
			}
			
			// Scan through the effective namespace's command table and create a
			// list with all commands that match the pattern. If a specific
			// namespace was requested in the pattern, qualify the command names
			// with the namespace name.
			
			list = TclList.newInstance();
			
			if (ns != null)
			{
				search = ns.cmdTable.GetEnumerator();
				while (search.MoveNext())
				{
					cmdName = ((string) search.Key);
					if (((System.Object) simplePattern == null) || Util.stringMatch(cmdName, simplePattern))
					{
						if (specificNsInPattern)
						{
							cmd = (WrappedCommand)search.Value;
							elemObj = TclString.newInstance(interp.getCommandFullName(cmd));
						}
						else
						{
							elemObj = TclString.newInstance(cmdName);
						}
						TclList.append(interp, list, elemObj);
					}
				}
				
				// If the effective namespace isn't the global :: namespace, and a
				// specific namespace wasn't requested in the pattern, then add in
				// all global :: commands that match the simple pattern. Of course,
				// we add in only those commands that aren't hidden by a command in
				// the effective namespace.
				
				if ((ns != globalNs) && !specificNsInPattern)
				{
					search = globalNs.cmdTable.GetEnumerator();
					while (search.MoveNext())
					{
						cmdName = ((string)search.Key);
						if (((System.Object) simplePattern == null) || Util.stringMatch(cmdName, simplePattern))
						{
							if (ns.cmdTable[cmdName] == null)
							{
								TclList.append(interp, list, TclString.newInstance(cmdName));
							}
						}
					}
				}
			}
			
			interp.setResult(list);
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoCompleteCmd --
		*
		*      Called to implement the "info complete" command that determines
		*      whether a string is a complete Tcl command. Handles the following
		*      syntax:
		*
		*          info complete command
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoCompleteCmd(Interp interp, TclObject[] objv)
		{
			if (objv.Length != 3)
			{
				throw new TclNumArgsException(interp, 2, objv, "command");
			}
			
			
			interp.setResult(tcl.lang.Interp.commandComplete(objv[2].ToString()));
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoDefaultCmd --
		*
		*      Called to implement the "info default" command that returns the
		*      default value for a procedure argument. Handles the following
		*      syntax:
		*
		*          info default procName arg varName
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoDefaultCmd(Interp interp, TclObject[] objv)
		{
			string procName, argName, varName;
			Procedure proc;
			//TclObject valueObj;
			
			if (objv.Length != 5)
			{
				throw new TclNumArgsException(interp, 2, objv, "procname arg varname");
			}
			
			
			procName = objv[2].ToString();
			
			argName = objv[3].ToString();
			proc = Procedure.findProc(interp, procName);
			if (proc == null)
			{
				throw new TclException(interp, "\"" + procName + "\" isn't a procedure");
			}
			
			for (int i = 0; i < proc.argList.Length; i++)
			{
				
				if (argName.Equals(proc.argList[i][0].ToString()))
				{
					
					varName = objv[4].ToString();
					try
					{
						if (proc.argList[i][1] != null)
						{
							interp.setVar(varName, proc.argList[i][1], 0);
							interp.setResult(1);
						}
						else
						{
							interp.setVar(varName, "", 0);
							interp.setResult(0);
						}
					}
					catch (TclException excp)
					{
						throw new TclException(interp, "couldn't store default value in variable \"" + varName + "\"");
					}
					return ;
				}
			}
			throw new TclException(interp, "procedure \"" + procName + "\" doesn't have an argument \"" + argName + "\"");
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoExistsCmd --
		*
		*      Called to implement the "info exists" command that determines
		*      whether a variable exists. Handles the following syntax:
		*
		*          info exists varName
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoExistsCmd(Interp interp, TclObject[] objv)
		{
			string varName;
			Var var = null;
			
			if (objv.Length != 3)
			{
				throw new TclNumArgsException(interp, 2, objv, "varName");
			}
			
			
			varName = objv[2].ToString();
			Var[] result = Var.lookupVar(interp, varName, null, 0, "access", false, false);
			if (result != null)
			{
				var = result[0];
			}
			
			if ((var != null) && !var.isVarUndefined())
			{
				interp.setResult(true);
			}
			else
			{
				interp.setResult(false);
			}
			
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		*  InfoGlobalsCmd --
		*
		*      Called to implement the "info globals" command that returns the list
		*      of global variables matching an optional pattern. Handles the
		*      following syntax:
		*
		*          info globals ?pattern?*
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoGlobalsCmd(Interp interp, TclObject[] objv)
		{
			string varName, pattern;
			NamespaceCmd.Namespace globalNs = NamespaceCmd.getGlobalNamespace(interp);
			IDictionaryEnumerator search;
			Var var;
			TclObject list;
			
			if (objv.Length == 2)
			{
				pattern = null;
			}
			else if (objv.Length == 3)
			{
				
				pattern = objv[2].ToString();
			}
			else
			{
				throw new TclNumArgsException(interp, 2, objv, "?pattern?");
			}
			
			// Scan through the global :: namespace's variable table and create a
			// list of all global variables that match the pattern.
			
			list = TclList.newInstance();
			
			for (search = globalNs.varTable.GetEnumerator(); search.MoveNext(); )
			{
				varName = ((string) search.Key);
				var = (Var)search.Value;
				if (var.isVarUndefined())
				{
					continue;
				}
				if (((System.Object) pattern == null) || Util.stringMatch(varName, pattern))
				{
					TclList.append(interp, list, TclString.newInstance(varName));
				}
			}
			
			interp.setResult(list);
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoHostnameCmd --
		*
		*      Called to implement the "info hostname" command that returns the
		*      host name. Handles the following syntax:
		*
		*          info hostname
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoHostnameCmd(Interp interp, TclObject[] objv)
		{
			string name;
			
			if (objv.Length != 2)
			{
				throw new TclNumArgsException(interp, 2, objv, null);
			}
			
			// FIXME : how can we find the hostname
			
			name = null;
			
			if ((System.Object) name != null)
			{
				interp.setResult(name);
				return ;
			}
			else
			{
				interp.setResult("unable to determine name of host");
				return ;
			}
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoLevelCmd --
		*
		*      Called to implement the "info level" command that returns
		*      information about the call stack. Handles the following syntax:
		*
		*          info level ?number?
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoLevelCmd(Interp interp, TclObject[] objv)
		{
			int level;
			CallFrame frame;
			TclObject list;
			
			if (objv.Length == 2)
			{
				// just "info level"
				if (interp.varFrame == null)
				{
					interp.setResult(0);
				}
				else
				{
					interp.setResult(interp.varFrame.level);
				}
				return ;
			}
			else if (objv.Length == 3)
			{
				level = TclInteger.get(interp, objv[2]);
				
				if (level <= 0)
				{
					if (interp.varFrame == null)
					{
						
						throw new TclException(interp, "bad level \"" + objv[2].ToString() + "\"");
					}
					
					level += interp.varFrame.level;
				}
				
				for (frame = interp.varFrame; frame != null; frame = frame.callerVar)
				{
					if (frame.level == level)
					{
						break;
					}
				}
				if ((frame == null) || frame.objv == null)
				{
					
					throw new TclException(interp, "bad level \"" + objv[2].ToString() + "\"");
				}
				
				list = TclList.newInstance();
				for (int i = 0; i < frame.objv.Length; i++)
				{
					TclList.append(interp, list, TclString.newInstance(frame.objv[i]));
				}
				interp.setResult(list);
				return ;
			}
			
			throw new TclNumArgsException(interp, 2, objv, "?number?");
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoLibraryCmd --
		*
		*      Called to implement the "info library" command that returns the
		*      library directory for the Tcl installation. Handles the following
		*      syntax:
		*
		*          info library
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoLibraryCmd(Interp interp, TclObject[] objv)
		{
			if (objv.Length != 2)
			{
				throw new TclNumArgsException(interp, 2, objv, null);
			}
			try
			{
				interp.setResult(interp.getVar("tcl_library", TCL.VarFlag.GLOBAL_ONLY));
				return ;
			}
			catch (TclException e)
			{
				// If the variable has not been defined
				throw new TclException(interp, "no library has been specified for Tcl");
			}
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoLoadedCmd --
		*
		*      Called to implement the "info loaded" command that returns the
		*      packages that have been loaded into an interpreter. Handles the
		*      following syntax:
		*
		*          info loaded ?interp?
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoLoadedCmd(Interp interp, TclObject[] objv)
		{
			if (objv.Length != 2 && objv.Length != 3)
			{
				throw new TclNumArgsException(interp, 2, objv, "?interp?");
			}
			// FIXME : what should "info loaded" return?
			throw new TclException(interp, "info loaded not implemented");
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoLocalsCmd --
		*
		*      Called to implement the "info locals" command to return a list of
		*      local variables that match an optional pattern. Handles the
		*      following syntax:
		*
		*          info locals ?pattern?
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoLocalsCmd(Interp interp, TclObject[] objv)
		{
			string pattern;
			TclObject list;
			
			if (objv.Length == 2)
			{
				pattern = null;
			}
			else if (objv.Length == 3)
			{
				
				pattern = objv[2].ToString();
			}
			else
			{
				throw new TclNumArgsException(interp, 2, objv, "?pattern?");
			}
			
			if (interp.varFrame == null || !interp.varFrame.isProcCallFrame)
			{
				return ;
			}
			
			// Return a list containing names of first the compiled locals (i.e. the
			// ones stored in the call frame), then the variables in the local hash
			// table (if one exists).
			
			list = TclList.newInstance();
			AppendLocals(interp, list, pattern, false);
			interp.setResult(list);
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* AppendLocals --
		*
		*	Append the local variables for the current frame to the
		*	specified list object.
		*
		* Results:
		*	None.
		*
		* Side effects:
		*	None.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  AppendLocals(Interp interp, TclObject list, string pattern, bool includeLinks)
		{
			Var var;
			string varName;
			Hashtable localVarTable;
			IDictionaryEnumerator search;
			
			localVarTable = interp.varFrame.varTable;
			
			// Compiled locals do not exist in Jacl
			
			if (localVarTable != null)
			{
				for (search = localVarTable.GetEnumerator(); search.MoveNext(); )
				{
					var = (Var)search.Value;
					varName = (string)search.Key;
					if (!var.isVarUndefined() && (includeLinks || !var.isVarLink()))
					{
						if (((System.Object) pattern == null) || Util.stringMatch(varName, pattern))
						{
							TclList.append(interp, list, TclString.newInstance(varName));
						}
					}
				}
			}
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoNameOfExecutableCmd --
		*
		*      Called to implement the "info nameofexecutable" command that returns
		*      the name of the binary file running this application. Handles the
		*      following syntax:
		*
		*          info nameofexecutable
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoNameOfExecutableCmd(Interp interp, TclObject[] objv)
		{
			
			if (objv.Length != 2)
			{
				throw new TclNumArgsException(interp, 2, objv, null);
			}
			
			// We depend on a user defined property named "JAVA" since
			// the JDK provides no means to learn the name of the executable
			// that launched the application.
			
			string nameOfExecutable = "nacl";
			
			if ((System.Object) nameOfExecutable != null)
			{
				TclObject result = TclList.newInstance();
				TclList.append(interp, result, TclString.newInstance(nameOfExecutable));
				TclList.append(interp, result, TclString.newInstance("tcl.lang.Shell"));
				interp.setResult(result);
			}
			
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoPatchLevelCmd --
		*
		*      Called to implement the "info patchlevel" command that returns the
		*      default value for an argument to a procedure. Handles the following
		*      syntax:
		*
		*          info patchlevel
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoPatchLevelCmd(Interp interp, TclObject[] objv)
		{
			if (objv.Length != 2)
			{
				throw new TclNumArgsException(interp, 2, objv, null);
			}
			
			interp.setResult(interp.getVar("tcl_patchLevel", TCL.VarFlag.GLOBAL_ONLY));
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoProcsCmd --
		*
		*      Called to implement the "info procs" command that returns the
		*      procedures in the current namespace that match an optional pattern.
		*      Handles the following syntax:
		*
		*          info procs ?pattern?
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoProcsCmd(Interp interp, TclObject[] objv)
		{
			string cmdName, pattern;
			NamespaceCmd.Namespace currNs = NamespaceCmd.getCurrentNamespace(interp);
			IDictionaryEnumerator search;
			WrappedCommand cmd, realCmd;
			TclObject list;
			
			if (objv.Length == 2)
			{
				pattern = null;
			}
			else if (objv.Length == 3)
			{
				
				pattern = objv[2].ToString();
			}
			else
			{
				throw new TclNumArgsException(interp, 2, objv, "?pattern?");
			}
			
			// Scan through the current namespace's command table and return a list
			// of all procs that match the pattern.
			
			list = TclList.newInstance();
			for (search = currNs.cmdTable.GetEnumerator(); search.MoveNext(); )
			{
				cmdName = ((string)search.Key);
				cmd = (WrappedCommand)search.Value;
				
				// If the command isn't itself a proc, it still might be an
				// imported command that points to a "real" proc in a different
				// namespace.
				
				realCmd = NamespaceCmd.getOriginalCommand(cmd);
				
				if (Procedure.isProc(cmd) || ((realCmd != null) && Procedure.isProc(realCmd)))
				{
					if (((System.Object) pattern == null) || Util.stringMatch(cmdName, pattern))
					{
						TclList.append(interp, list, TclString.newInstance(cmdName));
					}
				}
			}
			
			interp.setResult(list);
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoScriptCmd --
		*
		*      Called to implement the "info script" command that returns the
		*      script file that is currently being evaluated. Handles the
		*      following syntax:
		*
		*          info script
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoScriptCmd(Interp interp, TclObject[] objv)
		{
			if (objv.Length != 2)
			{
				throw new TclNumArgsException(interp, 2, objv, null);
			}
			
			interp.setResult(interp.scriptFile);
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoSharedlibCmd --
		*
		*      Called to implement the "info sharedlibextension" command that
		*      returns the file extension used for shared libraries. Handles the
		*      following syntax:
		*
		*          info sharedlibextension
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoSharedlibCmd(Interp interp, TclObject[] objv)
		{
			if (objv.Length != 2)
			{
				throw new TclNumArgsException(interp, 2, objv, null);
			}
			interp.setResult(".jar");
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoTclVersionCmd --
		*
		*      Called to implement the "info tclversion" command that returns the
		*      version number for this Tcl library. Handles the following syntax:
		*
		*          info tclversion
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoTclVersionCmd(Interp interp, TclObject[] objv)
		{
			if (objv.Length != 2)
			{
				throw new TclNumArgsException(interp, 2, objv, null);
			}
			
			interp.setResult(interp.getVar("tcl_version", TCL.VarFlag.GLOBAL_ONLY));
			return ;
		}
		
		/*
		*----------------------------------------------------------------------
		*
		* InfoVarsCmd --
		*
		*	Called to implement the "info vars" command that returns the
		*	list of variables in the interpreter that match an optional pattern.
		*	The pattern, if any, consists of an optional sequence of namespace
		*	names separated by "::" qualifiers, which is followed by a
		*	glob-style pattern that restricts which variables are returned.
		*	Handles the following syntax:
		*
		*          info vars ?pattern?
		*
		* Results:
		*      Returns if successful, raises TclException otherwise.
		*
		* Side effects:
		*      Returns a result in the interpreter's result object.
		*
		*----------------------------------------------------------------------
		*/
		
		private static void  InfoVarsCmd(Interp interp, TclObject[] objv)
		{
			string varName, pattern, simplePattern;
			IDictionaryEnumerator search;
			Var var;
			NamespaceCmd.Namespace ns;
			NamespaceCmd.Namespace globalNs = NamespaceCmd.getGlobalNamespace(interp);
			NamespaceCmd.Namespace currNs = NamespaceCmd.getCurrentNamespace(interp);
			TclObject list, elemObj;
			bool specificNsInPattern = false; // Init. to avoid compiler warning.
			
			// Get the pattern and find the "effective namespace" in which to
			// list variables. We only use this effective namespace if there's
			// no active Tcl procedure frame.
			
			if (objv.Length == 2)
			{
				simplePattern = null;
				ns = currNs;
				specificNsInPattern = false;
			}
			else if (objv.Length == 3)
			{
				// From the pattern, get the effective namespace and the simple
				// pattern (no namespace qualifiers or ::'s) at the end. If an
				// error was found while parsing the pattern, return it. Otherwise,
				// if the namespace wasn't found, just leave ns = null: we will
				// return an empty list since no variables there can be found.
				
				
				pattern = objv[2].ToString();
				
				// Java does not support passing an address so we pass
				// an array of size 1 and then assign arr[0] to the value
				NamespaceCmd.Namespace[] nsArr = new NamespaceCmd.Namespace[1];
				NamespaceCmd.Namespace[] dummy1Arr = new NamespaceCmd.Namespace[1];
				NamespaceCmd.Namespace[] dummy2Arr = new NamespaceCmd.Namespace[1];
				string[] simplePatternArr = new string[1];
				
				NamespaceCmd.getNamespaceForQualName(interp, pattern, null, 0, nsArr, dummy1Arr, dummy2Arr, simplePatternArr);
				
				// Get the values out of the arrays!
				ns = nsArr[0];
				simplePattern = simplePatternArr[0];
				
				if (ns != null)
				{
					// we successfully found the pattern's ns
					specificNsInPattern = (simplePattern.CompareTo(pattern) != 0);
				}
			}
			else
			{
				throw new TclNumArgsException(interp, 2, objv, "?pattern?");
			}
			
			// If the namespace specified in the pattern wasn't found, just return.
			
			if (ns == null)
			{
				return ;
			}
			
			list = TclList.newInstance();
			
			if ((interp.varFrame == null) || !interp.varFrame.isProcCallFrame || specificNsInPattern)
			{
				// There is no frame pointer, the frame pointer was pushed only
				// to activate a namespace, or we are in a procedure call frame
				// but a specific namespace was specified. Create a list containing
				// only the variables in the effective namespace's variable table.
				
				search = ns.varTable.GetEnumerator();
				while (search.MoveNext())
				{
					varName = ((string) search.Key);
					var = (Var)search.Value;
					if (!var.isVarUndefined() || ((var.flags & VarFlags.NAMESPACE_VAR) != 0))
					{
						if (((System.Object) simplePattern == null) || Util.stringMatch(varName, simplePattern))
						{
							if (specificNsInPattern)
							{
								elemObj = TclString.newInstance(Var.getVariableFullName(interp, var));
							}
							else
							{
								elemObj = TclString.newInstance(varName);
							}
							TclList.append(interp, list, elemObj);
						}
					}
				}
				
				// If the effective namespace isn't the global :: namespace, and a
				// specific namespace wasn't requested in the pattern (i.e., the
				// pattern only specifies variable names), then add in all global ::
				// variables that match the simple pattern. Of course, add in only
				// those variables that aren't hidden by a variable in the effective
				// namespace.
				
				if ((ns != globalNs) && !specificNsInPattern)
				{
					search = globalNs.varTable.GetEnumerator();
					while (search.MoveNext())
					{
						varName = ((string) search.Key);
						var = (Var)search.Value;
						if (!var.isVarUndefined() || ((var.flags & VarFlags.NAMESPACE_VAR) != 0))
						{
							if (((System.Object) simplePattern == null) || Util.stringMatch(varName, simplePattern))
							{
								
								// Skip vars defined in current namespace
								if (ns.varTable[varName] == null)
								{
									TclList.append(interp, list, TclString.newInstance(varName));
								}
							}
						}
					}
				}
			}
			else
			{
				AppendLocals(interp, list, simplePattern, true);
			}
			
			interp.setResult(list);
			return ;
		}
	}
}
