/*
* TimerHandler.java --
*
*	The API for defining timer event handler.
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
* RCS @(#) $Id: TimerHandler.java,v 1.1.1.1 1998/10/14 21:09:21 cvsadmin Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/*
	* This abstract class is used to define timer handlers.
	*/
	
	abstract public class TimerHandler
	{
		
		/*
		* Back pointer to the notifier that will fire this timer.
		*/
		
		internal Notifier notifier;
		
		/*
		* System time at (of after) which the timer should be fired.
		*/
		
		internal long atTime;
		
		/*
		* True if the cancel() method has been called.
		*/
		
		internal bool isCancelled;
		
		/*
		* Used to distinguish older idle handlers from recently-created ones.
		*/
		
		internal int generation;
		
		public TimerHandler(Notifier n, int milliseconds)
		{
			int i;
			
			atTime = (System.DateTime.Now.Ticks - 621355968000000000) / 10000 + milliseconds;
			notifier = (Notifier) n;
			isCancelled = false;
			
			/*
			* Add the event to the queue in the correct position (ordered by
			* event firing time).
			*
			* NOTE: it's very important that if two timer handlers have the
			* same atTime, the newer timer handler always goes after the
			* older handler in the list. See comments in
			* Notifier.TimerEvent.processEvent() for details.
			*/
			
			lock (notifier)
			{
				generation = notifier.timerGeneration;
				
				for (i = 0; i < notifier.timerList.Count; i++)
				{
					TimerHandler q = (TimerHandler) notifier.timerList[i];
					if (atTime < q.atTime)
					{
						break;
					}
				}
				notifier.timerList.Insert(i, this);
				
				if (System.Threading.Thread.CurrentThread != notifier.primaryThread)
				{
					System.Threading.Monitor.PulseAll(notifier);
				}
			}
		}
				public  void  cancel()
		{
			lock (this)
			{
				if (isCancelled)
				{
					return ;
				}
				
				isCancelled = true;
				
				lock (notifier)
				{
					for (int i = 0; i < notifier.timerList.Count; i++)
					{
						if (notifier.timerList[i] == this)
						{
							notifier.timerList.RemoveAt(i);
							
							/*
							* We can return now because the same timer can be
							* registered only once in the list of timers.
							*/
							
							return ;
						}
					}
				}
			}
		}
				internal int invoke()
		{
			lock (this)
			{
				/*
				* The timer may be cancelled after it was put on the
				* event queue. Check its isCancelled field to make sure it's
				* not cancelled.
				*/
				
				if (!isCancelled)
				{
					processTimerEvent();
					return 1;
				}
				else
				{
					return 0;
				}
			}
		}
		abstract public void  processTimerEvent();
	} // end TimerHandler
}
