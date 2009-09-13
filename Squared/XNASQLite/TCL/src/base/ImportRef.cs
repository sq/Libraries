/*
* ImportRef.java
*
*	An ImportRef is a member of the list of imported commands
*	which is part of the WrappedCommand class.
*
* Copyright (c) 1999 Mo DeJong.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: ImportRef.java,v 1.1 1999/08/05 03:42:49 mo Exp $
*/
using System;
namespace tcl.lang
{
	
	/// <summary> An imported command is created in an namespace when it imports a "real"
	/// command from another namespace. An imported command has a Command
	/// structure that points (via its ClientData value) to the "real" Command
	/// structure in the source namespace's command table. The real command
	/// records all the imported commands that refer to it in a list of ImportRef
	/// structures so that they can be deleted when the real command is deleted.
	/// </summary>
	
	class ImportRef
	{
		internal WrappedCommand importedCmd; // Points to the imported command created in
		// an importing namespace; this command
		// redirects its invocations to the "real" cmd.    
		internal ImportRef next; // Next element on the linked list of
		// imported commands that refer to the
		// "real" command. The real command deletes
		// these imported commands on this list when
		// it is deleted.
	}
}
