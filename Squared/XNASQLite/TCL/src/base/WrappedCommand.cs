/*
* WrappedCommand.java
*
*	Wrapper for commands located inside a Jacl interp.
*
* Copyright (c) 1999 Mo DeJong.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: WrappedCommand.java,v 1.2 1999/08/05 03:42:05 mo Exp $
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	/// <summary> A Wrapped Command is like the Command struct defined in the C version
	/// in the file generic/tclInt.h. It is "wrapped" around a TclJava Command
	/// interface reference. We need to wrap Command references so that we
	/// can keep track of sticky issues like what namespace the command is
	/// defined in without requiring that every implementation of a Command
	/// interface provide method to do this. This class is only used in
	/// the internal implementation of Jacl.
	/// </summary>
	
	public class WrappedCommand
	{
		internal Hashtable table; // Reference to the table that this command is
		// defined inside. The hashKey member can be
		// used to lookup this CommandWrapper instance
		// in the table of CommandWrappers. The table
		// member combined with the hashKey member are
		// are equivilent to the C version's Command->hPtr.
		internal string hashKey; // A string that stores the name of the command.
		// This name is NOT fully qualified.
		
		
		internal NamespaceCmd.Namespace ns; // The namespace where the command is located
		
		internal Command cmd; // The actual Command interface that we are wrapping.
		
		internal bool deleted; // Means that the command is in the process
		// of being deleted. Other attempts to
		// delete the command should be ignored.
		
		internal ImportRef importRef; // List of each imported Command created in
		// another namespace when this command is
		// imported. These imported commands
		// redirect invocations back to this
		// command. The list is used to remove all
		// those imported commands when deleting
		// this "real" command.

    internal Interp.dxObjCmdProc objProc;//cmdPtr->objProc = proc;
    public  object objClientData;//cmdPtr->objClientData = clientData;
    //internal TclInvokeObjectCommand proc; //cmdPtr.proc = TclInvokeObjectCommand;
    internal object clientData;//cmdPtr->clientData = (ClientData)cmdPtr;
    internal Interp.dxCmdDeleteProc deleteProc;//cmdPtr->deleteProc = deleteProc;
    internal object deleteData;//cmdPtr->deleteData = clientData;
    internal int flags;//cmdPtr->flags = 0;
		
		public override string ToString()
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			
			sb.Append("Wrapper for ");
			if (ns != null)
			{
				sb.Append(ns.fullName);
				if (ns.fullName != "::")
				{
					sb.Append("::");
				}
			}
			if (table != null)
			{
				sb.Append(hashKey);
			}
			
			sb.Append(" -> ");
			sb.Append(cmd.GetType().FullName);
			
			return sb.ToString();
		}
	}
}
