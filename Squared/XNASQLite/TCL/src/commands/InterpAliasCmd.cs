/*
* InterpAliasCmd.java --
*
*	Implements the built-in "interp" Tcl command.
*
* Copyright (c) 2000 Christian Krone.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: InterpAliasCmd.java,v 1.1 2000/08/20 06:08:42 mo Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	/// <summary> This class implements the alias commands, which are created
	/// in response to the built-in "interp alias" command in Tcl.
	/// 
	/// </summary>
	
	class InterpAliasCmd : CommandWithDispose
	{
		
		// Name of alias command in slave interp.
		
		internal TclObject name;
		
		// Interp in which target command will be invoked.
		
		private Interp targetInterp;
		
		// Tcl list making up the prefix of the target command to be invoked in
		// the target interpreter. Additional arguments specified when calling
		// the alias in the slave interp will be appended to the prefix before
		// the command is invoked.
		
		private TclObject prefix;
		
		// Source command in slave interpreter, bound to command that invokes
		// the target command in the target interpreter.
		
		private WrappedCommand slaveCmd;
		
		// Entry for the alias hash table in slave.
		// This is used by alias deletion to remove the alias from the slave
		// interpreter alias table.
		
		private string aliasEntry;
		
		// Interp in which the command is defined.
		// This is the interpreter with the aliasTable in Slave.
		
		private Interp slaveInterp;
		public TCL.CompletionCode cmdProc(Interp interp, TclObject[] argv)
		{
			targetInterp.preserve();
			targetInterp.nestLevel++;
			
			targetInterp.resetResult();
			targetInterp.allowExceptions();
			
			// Append the arguments to the command prefix and invoke the command
			// in the target interp's global namespace.
			
			TclObject[] prefv = TclList.getElements(interp, prefix);
			TclObject cmd = TclList.newInstance();
			cmd.preserve();
			TclList.replace(interp, cmd, 0, 0, prefv, 0, prefv.Length - 1);
			TclList.replace(interp, cmd, prefv.Length, 0, argv, 1, argv.Length - 1);
			TclObject[] cmdv = TclList.getElements(interp, cmd);
			
			TCL.CompletionCode result = targetInterp.invoke(cmdv, Interp.INVOKE_NO_TRACEBACK);
			
			cmd.release();
			targetInterp.nestLevel--;
			
			// Check if we are at the bottom of the stack for the target interpreter.
			// If so, check for special return codes.
			
			if (targetInterp.nestLevel == 0)
			{
				if (result == TCL.CompletionCode.RETURN)
				{
					result = targetInterp.updateReturnInfo();
				}
				if (result != TCL.CompletionCode.OK && result != TCL.CompletionCode.ERROR)
				{
					try
					{
						targetInterp.processUnexpectedResult(result);
					}
					catch (TclException e)
					{
						result = e.getCompletionCode();
					}
				}
			}
			
			targetInterp.release();
			interp.transferResult(targetInterp, result);
      return TCL.CompletionCode.RETURN;
    }
		public  void  disposeCmd()
		{
			if ((System.Object) aliasEntry != null)
			{
				SupportClass.HashtableRemove(slaveInterp.aliasTable, aliasEntry);
			}
			
			if (slaveCmd != null)
			{
				SupportClass.HashtableRemove(targetInterp.targetTable, slaveCmd);
			}
			
			name.release();
			prefix.release();
		}
		internal static void  create(Interp interp, Interp slaveInterp, Interp masterInterp, TclObject name, TclObject targetName, int objIx, TclObject[] objv)
		{
			
			string inString = name.ToString();
			
			InterpAliasCmd alias = new InterpAliasCmd();
			
			alias.name = name;
			name.preserve();
			
			alias.slaveInterp = slaveInterp;
			alias.targetInterp = masterInterp;
			
			alias.prefix = TclList.newInstance();
			alias.prefix.preserve();
			TclList.append(interp, alias.prefix, targetName);
			TclList.insert(interp, alias.prefix, 1, objv, objIx, objv.Length - 1);
			
			slaveInterp.createCommand(inString, alias);
			alias.slaveCmd = NamespaceCmd.findCommand(slaveInterp, inString, null, 0);
			
			try
			{
				interp.preventAliasLoop(slaveInterp, alias.slaveCmd);
			}
			catch (TclException e)
			{
				// Found an alias loop!  The last call to Tcl_CreateObjCommand made
				// the alias point to itself.  Delete the command and its alias
				// record.  Be careful to wipe out its client data first, so the
				// command doesn't try to delete itself.
				
				slaveInterp.deleteCommandFromToken(alias.slaveCmd);
				throw ;
			}
			
			// Make an entry in the alias table. If it already exists delete
			// the alias command. Then retry.
			
			if (slaveInterp.aliasTable.ContainsKey(inString))
			{
				InterpAliasCmd oldAlias = (InterpAliasCmd) slaveInterp.aliasTable[inString];
				slaveInterp.deleteCommandFromToken(oldAlias.slaveCmd);
			}
			
			alias.aliasEntry = inString;
			SupportClass.PutElement(slaveInterp.aliasTable, inString, alias);
			
			// Create the new command. We must do it after deleting any old command,
			// because the alias may be pointing at a renamed alias, as in:
			//
			// interp alias {} foo {} bar		# Create an alias "foo"
			// rename foo zop				# Now rename the alias
			// interp alias {} foo {} zop		# Now recreate "foo"...
			
			SupportClass.PutElement(masterInterp.targetTable, alias.slaveCmd, slaveInterp);
			
			interp.setResult(name);
		}
		internal static void  delete(Interp interp, Interp slaveInterp, TclObject name)
		{
			// If the alias has been renamed in the slave, the master can still use
			// the original name (with which it was created) to find the alias to
			// delete it.
			
			
			string inString = name.ToString();
			if (!slaveInterp.aliasTable.ContainsKey(inString))
			{
				throw new TclException(interp, "alias \"" + inString + "\" not found");
			}
			
			InterpAliasCmd alias = (InterpAliasCmd) slaveInterp.aliasTable[inString];
			slaveInterp.deleteCommandFromToken(alias.slaveCmd);
		}
		internal static void  describe(Interp interp, Interp slaveInterp, TclObject name)
		{
			// If the alias has been renamed in the slave, the master can still use
			// the original name (with which it was created) to find the alias to
			// describe it.
			
			
			string inString = name.ToString();
			if (slaveInterp.aliasTable.ContainsKey(inString))
			{
				InterpAliasCmd alias = (InterpAliasCmd) slaveInterp.aliasTable[inString];
				interp.setResult(alias.prefix);
			}
		}
		internal static void  list(Interp interp, Interp slaveInterp)
		{
			TclObject result = TclList.newInstance();
			interp.setResult(result);
			
			IEnumerator aliases = slaveInterp.aliasTable.Values.GetEnumerator();
			while (aliases.MoveNext())
			{
				InterpAliasCmd alias = (InterpAliasCmd) aliases.Current;
				TclList.append(interp, result, alias.name);
			}
		}
		internal  WrappedCommand getTargetCmd(Interp interp)
		{
			TclObject[] objv = TclList.getElements(interp, prefix);
			
			string targetName = objv[0].ToString();
			return NamespaceCmd.findCommand(targetInterp, targetName, null, 0);
		}
		internal static Interp getTargetInterp(Interp slaveInterp, string aliasName)
		{
			if (!slaveInterp.aliasTable.ContainsKey(aliasName))
			{
				return null;
			}
			
			InterpAliasCmd alias = (InterpAliasCmd) slaveInterp.aliasTable[aliasName];
			
			return alias.targetInterp;
		}
	} // end InterpAliasCmd
}
