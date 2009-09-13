/*
* TclIO.java --
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclIO.java,v 1.9 2003/03/06 22:53:07 mdejong Exp $
*
*/
using System;
using System.Collections;

namespace tcl.lang
{
	
	public class TclIO
	{

    public const int READ_ALL = 1;
    public const int READ_LINE = 2;
    public const int READ_N_BYTES = 3;

    public const int SEEK_SET = 1;
    public const int SEEK_CUR = 2;
    public const int SEEK_END = 3;

    public const int RDONLY = 1;
    public const int WRONLY = 2;
    public const int RDWR = 4;
    public const int APPEND = 8;
    public const int CREAT = 16;
    public const int EXCL = 32;
    public const int TRUNC = 64;

    public const int BUFF_FULL = 0;
    public const int BUFF_LINE = 1;
    public const int BUFF_NONE = 2;

    public const int TRANS_AUTO = 0;
    public const int TRANS_BINARY = 1;
    public const int TRANS_LF = 2;
    public const int TRANS_CR = 3;
    public const int TRANS_CRLF = 4;
		
		public static int TRANS_PLATFORM;
		
		/// <summary> Table of channels currently registered for all interps.  The 
		/// interpChanTable has "" references into this table that
		/// stores the registered channels for the individual interp.
		/// </summary>
		
		private static StdChannel stdinChan = null;
		private static StdChannel stdoutChan = null;
		private static StdChannel stderrChan = null;
		
		public static Channel getChannel(Interp interp, string chanName)
		{
			return ((Channel) getInterpChanTable(interp)[chanName]);
		}
		
		
		internal static void  registerChannel(Interp interp, Channel chan)
		{
			
			if (interp != null)
			{
				Hashtable chanTable = getInterpChanTable(interp);
				SupportClass.PutElement(chanTable, chan.ChanName, chan);
				chan.refCount++;
			}
		}
		
		
		internal static void  unregisterChannel(Interp interp, Channel chan)
		{
			
			Hashtable chanTable = getInterpChanTable(interp);
			SupportClass.HashtableRemove(chanTable, chan.ChanName);
			
			if (--chan.refCount <= 0)
			{
				try
				{
					chan.close();
				}
				catch (System.IO.IOException e)
				{
					throw new TclRuntimeError("TclIO.unregisterChannel() Error: IOException when closing " + chan.ChanName + ": " + e.Message,e);
				}
			}
		}
		
		
		public static Hashtable getInterpChanTable(Interp interp)
		{
			Channel chan;
			
			if (interp.interpChanTable == null)
			{
				
				interp.interpChanTable = new Hashtable();
				
				chan = getStdChannel(StdChannel.STDIN);
				registerChannel(interp, chan);
				
				chan = getStdChannel(StdChannel.STDOUT);
				registerChannel(interp, chan);
				
				chan = getStdChannel(StdChannel.STDERR);
				registerChannel(interp, chan);
			}
			
			return interp.interpChanTable;
		}
		
		
		public static Channel getStdChannel(int type)
		{
			Channel chan = null;
			
			switch (type)
			{
				
				case StdChannel.STDIN: 
					if (stdinChan == null)
					{
						stdinChan = new StdChannel(StdChannel.STDIN);
					}
					chan = stdinChan;
					break;
				
				case StdChannel.STDOUT: 
					if (stdoutChan == null)
					{
						stdoutChan = new StdChannel(StdChannel.STDOUT);
					}
					chan = stdoutChan;
					break;
				
				case StdChannel.STDERR: 
					if (stderrChan == null)
					{
						stderrChan = new StdChannel(StdChannel.STDERR);
					}
					chan = stderrChan;
					break;
				
				default: 
					throw new TclRuntimeError("Invalid type for StdChannel");
				
			}
			
			return (chan);
		}
		
		/// <summary> Really ugly function that attempts to get the next available
		/// channelId name.  In C the FD returned in the native open call
		/// returns this value, but we don't have that so we need to do
		/// this funky iteration over the Hashtable.
		/// 
		/// </summary>
		/// <param name="interp">currrent interpreter.
		/// </param>
		/// <returns> the next integer to use in the channelId name.
		/// </returns>
		
		internal static string getNextDescriptor(Interp interp, string prefix)
		{
			int i;
			Hashtable htbl = getInterpChanTable(interp);
			
			// The first available file identifier in Tcl is "file3"
			if (prefix.Equals("file"))
				i = 3;
			else
				i = 0;
			
			for (; (htbl[prefix + i]) != null; i++)
			{
				// Do nothing...
			}
			return prefix + i;
		}
		
		/*
		* Return a string description for a translation id defined above.
		*/
		
		internal static string getTranslationString(int translation)
		{
			switch (translation)
			{
				
				case TRANS_AUTO: 
					return "auto";
				
				case TRANS_CR: 
					return "cr";
				
				case TRANS_CRLF: 
					return "crlf";
				
				case TRANS_LF: 
					return "lf";
				
				case TRANS_BINARY: 
					return "lf";
				
				default: 
					throw new TclRuntimeError("bad translation id");
				
			}
		}
		
		/*
		* Return a numerical identifier for the given -translation string.
		*/
		
		internal static int getTranslationID(string translation)
		{
			if (translation.Equals("auto"))
				return TRANS_AUTO;
			else if (translation.Equals("cr"))
				return TRANS_CR;
			else if (translation.Equals("crlf"))
				return TRANS_CRLF;
			else if (translation.Equals("lf"))
				return TRANS_LF;
			else if (translation.Equals("binary"))
				return TRANS_LF;
			else if (translation.Equals("platform"))
				return TRANS_PLATFORM;
			else
				return - 1;
		}
		
		/*
		* Return a string description for a -buffering id defined above.
		*/
		
		internal static string getBufferingString(int buffering)
		{
			switch (buffering)
			{
				
				case BUFF_FULL: 
					return "full";
				
				case BUFF_LINE: 
					return "line";
				
				case BUFF_NONE: 
					return "none";
				
				default: 
					throw new TclRuntimeError("bad buffering id");
				
			}
		}
		
		/*
		* Return a numerical identifier for the given -buffering string.
		*/
		
		internal static int getBufferingID(string buffering)
		{
			if (buffering.Equals("full"))
				return BUFF_FULL;
			else if (buffering.Equals("line"))
				return BUFF_LINE;
			else if (buffering.Equals("none"))
				return BUFF_NONE;
			else
				return - 1;
		}
		static TclIO()
		{
			{
				if (Util.Windows)
					TRANS_PLATFORM = TRANS_CRLF;
				else if (Util.Mac)
					TRANS_PLATFORM = TRANS_CR;
				else
					TRANS_PLATFORM = TRANS_LF;
			}
		}
	}
}
