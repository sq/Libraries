/*
* EventDeleter.java --
*
*	Interface for deleting events in the notifier's event queue.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: EventDeleter.java,v 1.1.1.1 1998/10/14 21:09:14 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This is the interface for deleting events in the notifier's event
	* queue. It's used together with the Notifier.deleteEvents() method.
	*
	*/
	
	public interface EventDeleter
		{
			
			/*
			*----------------------------------------------------------------------
			*
			* deleteEvent --
			*
			*	This method is called once for each event in the event
			*	queue. It returns 1 for all events that should be deleted and
			*	0 for events that should remain in the queue.
			*
			*	If this method determines that an event should be removed, it
			*	should perform appropriate clean up on the event object.
			*
			* Results:
			*	1 means evt should be removed from the event queue. 0
			*	otherwise.
			*
			* Side effects:
			*	After this method returns 1, the event will be removed from the
			*	event queue and will not be processed.
			*
			*----------------------------------------------------------------------
			*/
			
			int deleteEvent(TclEvent evt); // Check whether this event should be removed.
		} // end EventDeleter
}
