/*
* TclEvent.java --
*
*	Abstract class for describing an event in the Tcl notifier
*	API.
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
* RCS @(#) $Id: TclEvent.java,v 1.3 2003/03/11 01:45:53 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This is an abstract class that describes an event in the Jacl
	* implementation of the notifier. It contains package protected
	* fields and methods that are accessed by the Jacl notifier. Tcl Blend
	* needs a different implementation of the TclEvent base class.
	*
	* The only public methods in this class are processEvent() and
	* sync(). These methods must appear in both the Jacl and Tcl Blend versions
	* of this class.
	*/
	
	public abstract class TclEvent
	{
		
		/*
		* The notifier in which this event is queued.
		*/
		
		internal Notifier notifier = null;
		
		/*
		* This flag is true if sync() has been called on this object.
		*/
		
		internal bool needsNotify = false;
		
		/*
		* True if this event is current being processing. This flag provents
		* an event to be processed twice when the event loop is entered
		* recursively.
		*/
		
		internal bool isProcessing = false;
		
		/*
		* True if this event has been processed.
		*/
		
		internal bool isProcessed = false;
		
		/*
		* Links to the next event in the event queue.
		*/
		
		internal TclEvent next;
		
		public abstract int processEvent(int flags); // Same as flags passed to Notifier.doOneEvent.
		
		public void  sync()
		{
			if (notifier == null)
			{
				throw new TclRuntimeError("TclEvent is not queued when sync() is called");
			}
			
			if (System.Threading.Thread.CurrentThread == notifier.primaryThread)
			{
				while (!isProcessed)
				{
					notifier.serviceEvent(0);
				}
			}
			else
			{
				lock (this)
				{
					needsNotify = true;
					while (!isProcessed)
					{
						try
						{
							System.Threading.Monitor.Wait(this, TimeSpan.FromMilliseconds(0));
						}
						catch (System.Threading.ThreadInterruptedException e)
						{
							// Another thread has sent us an "interrupt"
							// signal. We ignore it and continue waiting until
							// the event is processed.
							
							continue;
						}
					}
				}
			}
		}
	} // end TclEvent
}
