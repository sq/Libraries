/*
* Notifier.java --
*
*	Implements the Jacl version of the Notifier class.
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: Notifier.java,v 1.8 2003/03/11 02:21:14 mdejong Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	// Implements the Jacl version of the Notifier class. The Notifier is
	// the lowest-level part of the event system. It is used by
	// higher-level event sources such as file, JavaBean and timer
	// events. The Notifier manages an event queue that holds TclEvent
	// objects.
	//
	// The Jacl notifier is designed to run in a multi-threaded
	// environment. Each notifier instance is associated with a primary
	// thread. Any thread can queue (or dequeue) events using the
	// queueEvent (or deleteEvents) call. However, only the primary thread
	// may process events in the queue using the doOneEvent()
	// call. Attepmts to call doOneEvent from a non-primary thread will
	// cause a TclRuntimeError.
	//
	// This class does not have a public constructor and thus cannot be
	// instantiated. The only way to for a Tcl extension to get an
	// Notifier is to call Interp.getNotifier() (or
	// Notifier.getNotifierForThread() ), which returns the Notifier for that
	// interpreter (thread).
	
	public class Notifier : EventDeleter
	{
		
		// First pending event, or null if none.
		
		private TclEvent firstEvent;
		
		// Last pending event, or null if none.
		
		private TclEvent lastEvent;
		
		// Last high-priority event in queue, or null if none.
		
		private TclEvent markerEvent;
		
		// Event that was just processed by serviceEvent
		
		private TclEvent servicedEvent = null;
		
		// The primary thread of this notifier. Only this thread should process
		// events from the event queue.
		
		internal System.Threading.Thread primaryThread;
		
		// Stores the Notifier for each thread.
		
				private static Hashtable notifierTable;
		
		// List of registered timer handlers.
		
		internal ArrayList timerList;
		
		// Used to distinguish older timer handlers from recently-created ones.
		
		internal int timerGeneration;
		
		// True if there is a pending timer event in the event queue, false
		// otherwise.
		
		internal bool timerPending;
		
		// List of registered idle handlers.
		
		internal ArrayList idleList;
		
		// Used to distinguish older idle handlers from recently-created ones.
		
		internal int idleGeneration;
		
		// Reference count of the notifier. It's used to tell when a notifier
		// is no longer needed.
		
		internal int refCount;
		
		private Notifier(System.Threading.Thread primaryTh)
		{
			primaryThread = primaryTh;
			firstEvent = null;
			lastEvent = null;
			markerEvent = null;
			
			timerList = new ArrayList(10);
			timerGeneration = 0;
			idleList = new ArrayList(10);
			idleGeneration = 0;
			timerPending = false;
			refCount = 0;
		}
				public static Notifier getNotifierForThread(System.Threading.Thread thread)
		// The thread that owns this Notifier.
		{
			lock (typeof(tcl.lang.Notifier))
			{
				Notifier notifier = (Notifier) notifierTable[thread];
				if (notifier == null)
				{
					notifier = new Notifier(thread);
					SupportClass.PutElement(notifierTable, thread, notifier);
				}
				
				return notifier;
			}
		}
				public  void  preserve()
		{
			lock (this)
			{
				if (refCount < 0)
				{
					throw new TclRuntimeError("Attempting to preserve a freed Notifier");
				}
				++refCount;
			}
		}
				public  void  release()
		{
			lock (this)
			{
				if ((refCount == 0) && (primaryThread != null))
				{
					throw new TclRuntimeError("Attempting to release a Notifier before it's preserved");
				}
				if (refCount <= 0)
				{
					throw new TclRuntimeError("Attempting to release a freed Notifier");
				}
				--refCount;
				if (refCount == 0)
				{
					SupportClass.HashtableRemove(notifierTable, primaryThread);
					primaryThread = null;
				}
			}
		}
				public  void  queueEvent(TclEvent evt, int position)
		// One of TCL.QUEUE_TAIL,
		// TCL.QUEUE_HEAD or TCL.QUEUE_MARK.
		{
			lock (this)
			{
				evt.notifier = this;
				
				if (position == TCL.QUEUE_TAIL)
				{
					// Append the event on the end of the queue.
					
					evt.next = null;
					
					if (firstEvent == null)
					{
						firstEvent = evt;
					}
					else
					{
						lastEvent.next = evt;
					}
					lastEvent = evt;
				}
				else if (position == TCL.QUEUE_HEAD)
				{
					// Push the event on the head of the queue.
					
					evt.next = firstEvent;
					if (firstEvent == null)
					{
						lastEvent = evt;
					}
					firstEvent = evt;
				}
				else if (position == TCL.QUEUE_MARK)
				{
					// Insert the event after the current marker event and advance
					// the marker to the new event.
					
					if (markerEvent == null)
					{
						evt.next = firstEvent;
						firstEvent = evt;
					}
					else
					{
						evt.next = markerEvent.next;
						markerEvent.next = evt;
					}
					markerEvent = evt;
					if (evt.next == null)
					{
						lastEvent = evt;
					}
				}
				else
				{
					// Wrong flag.
					
					throw new TclRuntimeError("wrong position \"" + position + "\", must be TCL.QUEUE_HEAD, TCL.QUEUE_TAIL or TCL.QUEUE_MARK");
				}
				
				if (System.Threading.Thread.CurrentThread != primaryThread)
				{
					System.Threading.Monitor.PulseAll(this);
				}
			}
		}
				public  void  deleteEvents(EventDeleter deleter)
		// The deleter that checks whether an event
		// should be removed.
		{
			lock (this)
			{
				TclEvent evt, prev;
				TclEvent servicedEvent = null;
				
				// Handle the special case of deletion of a single event that was just
				// processed by the serviceEvent() method.
				
				if (deleter == this)
				{
					servicedEvent = this.servicedEvent;
					if (servicedEvent == null)
						throw new TclRuntimeError("servicedEvent was not set by serviceEvent()");
					this.servicedEvent = null;
				}
				
				for (prev = null, evt = firstEvent; evt != null; evt = evt.next)
				{
					if (((servicedEvent == null) && (deleter.deleteEvent(evt) == 1)) || (evt == servicedEvent))
					{
						if (evt == firstEvent)
						{
							firstEvent = evt.next;
						}
						else
						{
							prev.next = evt.next;
						}
						if (evt.next == null)
						{
							lastEvent = prev;
						}
						if (evt == markerEvent)
						{
							markerEvent = prev;
						}
						if (evt == servicedEvent)
						{
							servicedEvent = null;
							break; // Just service this one event in the special case
						}
					}
					else
					{
						prev = evt;
					}
				}
				if (servicedEvent != null)
				{
					throw new TclRuntimeError("servicedEvent was not removed from the queue");
				}
			}
		}
		public  int deleteEvent(TclEvent evt)
		{
			throw new TclRuntimeError("The Notifier.deleteEvent() method should not be called");
		}
		internal  int serviceEvent(int flags)
		// Indicates what events should be processed.
		// May be any combination of TCL.WINDOW_EVENTS
		// TCL.FILE_EVENTS, TCL.TIMER_EVENTS, or other
		// flags defined elsewhere.  Events not
		// matching this will be skipped for processing
		// later.
		{
			TclEvent evt;
			
			// No event flags is equivalent to TCL_ALL_EVENTS.
			
			if ((flags & TCL.ALL_EVENTS) == 0)
			{
				flags |= TCL.ALL_EVENTS;
			}
			
			// Loop through all the events in the queue until we find one
			// that can actually be handled.
			
			evt = null;
			while ((evt = getAvailableEvent(evt)) != null)
			{
				// Call the handler for the event.  If it actually handles the
				// event then free the storage for the event.  There are two
				// tricky things here, both stemming from the fact that the event
				// code may be re-entered while servicing the event:
				//
				// 1. Set the "isProcessing" field to true. This is a signal to
				//    ourselves that we shouldn't reexecute the handler if the
				//    event loop is re-entered.
				// 2. When freeing the event, must search the queue again from the
				//    front to find it.  This is because the event queue could
				//    change almost arbitrarily while handling the event, so we
				//    can't depend on pointers found now still being valid when
				//    the handler returns.
				
				evt.isProcessing = true;
				
				if (evt.processEvent(flags) != 0)
				{
					evt.isProcessed = true;
					// Don't allocate/grab the monitor for the event unless sync()
					// has been called in another thread. This is thread safe
					// since sync() checks the isProcessed flag before calling wait.
					if (evt.needsNotify)
					{
						lock (evt)
						{
							System.Threading.Monitor.PulseAll(evt);
						}
					}
					// Remove this specific event from the queue
					servicedEvent = evt;
					deleteEvents(this);
					return 1;
				}
				else
				{
					// The event wasn't actually handled, so we have to
					// restore the isProcessing field to allow the event to be
					// attempted again.
					
					evt.isProcessing = false;
				}
				
				// The handler for this event asked to defer it.  Just go on to
				// the next event.
				
				continue;
			}
			return 0;
		}
				private TclEvent getAvailableEvent(TclEvent skipEvent)
		// Indicates that the given event should not
		// be returned.  This argument can be null.
		{
			lock (this)
			{
				TclEvent evt;
				
				for (evt = firstEvent; evt != null; evt = evt.next)
				{
					if ((evt.isProcessing == false) && (evt.isProcessed == false) && (evt != skipEvent))
					{
						return evt;
					}
				}
				return null;
			}
		}
		public  int doOneEvent(int flags)
		// Miscellaneous flag values: may be any
		// combination of TCL.DONT_WAIT,
		// TCL.WINDOW_EVENTS, TCL.FILE_EVENTS,
		// TCL.TIMER_EVENTS, TCL.IDLE_EVENTS,
		// or others defined by event sources.
		{
			int result = 0;
			
			// No event flags is equivalent to TCL_ALL_EVENTS.
			
			if ((flags & TCL.ALL_EVENTS) == 0)
			{
				flags |= TCL.ALL_EVENTS;
			}
			
			// The core of this procedure is an infinite loop, even though
			// we only service one event.  The reason for this is that we
			// may be processing events that don't do anything inside of Tcl.
			
			while (true)
			{
				// If idle events are the only things to service, skip the
				// main part of the loop and go directly to handle idle
				// events (i.e. don't wait even if TCL_DONT_WAIT isn't set).
				
				if ((flags & TCL.ALL_EVENTS) == TCL.IDLE_EVENTS)
				{
					return serviceIdle();
				}
				
				long sysTime = (System.DateTime.Now.Ticks - 621355968000000000) / 10000;
				
				// If some timers have been expired, queue them into the
				// event queue. We can't process expired times right away,
				// because there may already be other events on the queue.
				
				if (!timerPending && (timerList.Count > 0))
				{
					TimerHandler h = (TimerHandler) timerList[0];
					
					if (h.atTime <= sysTime)
					{
						TimerEvent Tevent = new TimerEvent();
						Tevent.notifier = this;
            queueEvent( Tevent, TCL.QUEUE_TAIL );
						timerPending = true;
					}
				}
				
				// Service a queued event, if there are any.
				
				if (serviceEvent(flags) != 0)
				{
					result = 1;
					break;
				}
				
				// There is no event on the queue. Check for idle events.
				
				if ((flags & TCL.IDLE_EVENTS) != 0)
				{
					if (serviceIdle() != 0)
					{
						result = 1;
						break;
					}
				}
				
				if ((flags & TCL.DONT_WAIT) != 0)
				{
					break;
				}
				
				// We don't have any event to service. We'll wait if
				// TCL.DONT_WAIT. When the following wait() call returns,
				// one of the following things may happen:
				//
				// (1) waitTime milliseconds has elasped (if waitTime != 0);
				//
				// (2) The primary notifier has been notify()'ed by other threads:
				//     (a) an event is queued by queueEvent().
				//     (b) a timer handler was created by new TimerHandler();
				//     (c) an idle handler was created by new IdleHandler();
				// (3) We receive an InterruptedException.
				//
				
				try
				{
					// Don't acquire the monitor until we are about to wait
					// for notification from another thread. It is critical
					// that this entire method not be synchronized since
					// a call to processEvent via serviceEvent could take
					// a very long time. We don't want the monitor held
					// during that time since that would force calls to
					// queueEvent in other threads to wait.
					
					lock (this)
					{
						if (timerList.Count > 0)
						{
							TimerHandler h = (TimerHandler) timerList[0];
							long waitTime = h.atTime - sysTime;
							if (waitTime > 0)
							{
								System.Threading.Monitor.Wait(this, TimeSpan.FromMilliseconds(waitTime));
							}
						}
						else
						{
							System.Threading.Monitor.Wait(this);
						}
					} // synchronized (this)
				}
				catch (System.Threading.ThreadInterruptedException e)
				{
					// We ignore any InterruptedException and loop continuously
					// until we receive an event.
				}
			}
			
			return result;
		}
		private int serviceIdle()
		{
			int result = 0;
			int gen = idleGeneration;
			idleGeneration++;
			
			// The code below is trickier than it may look, for the following
			// reasons:
			//
			// 1. New handlers can get added to the list while the current
			//    one is being processed.  If new ones get added, we don't
			//    want to process them during this pass through the list (want
			//    to check for other work to do first).  This is implemented
			//    using the generation number in the handler:  new handlers
			//    will have a different generation than any of the ones currently
			//    on the list.
			// 2. The handler can call doOneEvent, so we have to remove
			//    the handler from the list before calling it. Otherwise an
			//    infinite loop could result.
			
			while (idleList.Count > 0)
			{
				IdleHandler h = (IdleHandler) idleList[0];
				if (h.generation > gen)
				{
					break;
				}
				idleList.RemoveAt(0);
				if (h.invoke() != 0)
				{
					result = 1;
				}
			}
			
			return result;
		}
		static Notifier()
		{
			notifierTable = new Hashtable();
		}
	} // end Notifier
	
	class TimerEvent:TclEvent
	{
		
		// The notifier what owns this TimerEvent.
		
		new internal Notifier notifier;
		
		public override int processEvent(int flags)
		// Same as flags passed to Notifier.doOneEvent.
		{
			if ((flags & TCL.TIMER_EVENTS) == 0)
			{
				return 0;
			}
			
			long sysTime = (System.DateTime.Now.Ticks - 621355968000000000) / 10000;
			int gen = notifier.timerGeneration;
			notifier.timerGeneration++;
			
			// The code below is trickier than it may look, for the following
			// reasons:
			//
			// 1. New handlers can get added to the list while the current
			//    one is being processed.  If new ones get added, we don't
			//    want to process them during this pass through the list to
			//    avoid starving other event sources. This is implemented
			//    using the timer generation number: new handlers will have
			//    a newer generation number than any of the ones currently on
			//    the list.
			// 2. The handler can call doOneEvent, so we have to remove
			//    the handler from the list before calling it. Otherwise an
			//    infinite loop could result.
			// 3. Because we only fetch the current time before entering the loop,
			//    the only way a new timer will even be considered runnable is if
			//	  its expiration time is within the same millisecond as the
			//	  current time.  This is fairly likely on Windows, since it has
			//	  a course granularity clock. Since timers are placed
			//	  on the queue in time order with the most recently created
			//    handler appearing after earlier ones with the same expiration
			//	  time, we don't have to worry about newer generation timers
			//	  appearing before later ones.
			
			while (notifier.timerList.Count > 0)
			{
				TimerHandler h = (TimerHandler) notifier.timerList[0];
				if (h.generation > gen)
				{
					break;
				}
				if (h.atTime > sysTime)
				{
					break;
				}
				notifier.timerList.RemoveAt(0);
				h.invoke();
			}
			
			notifier.timerPending = false;
			return 1;
		}
	} // end TimerEvent
}
