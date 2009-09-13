#undef DEBUG
/*
* StdChannel.java --
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: StdChannel.java,v 1.19 2003/03/08 03:42:44 mdejong Exp $
*
*/
using System;
namespace tcl.lang
{
	
	/// <summary> Subclass of the abstract class Channel.  It implements all of the 
	/// methods to perform read, write, open, close, etc on system stdio channels.
	/// </summary>
	
	public class StdChannel:Channel
	{
		public override string ChanType
		{
			get
			{
				return "tty";
			}
			
		}
		override protected internal System.IO.Stream InputStream
		{
			get
			{
				return null;
				// return System.Console.In;
			}
			
		}
		override protected internal System.IO.Stream OutputStream
		{
			get
			{
				throw new System.SystemException("should never be called");
			}
			
		}
		
		/// <summary> stdType store which type, of the three below, this StdChannel is.</summary>
		
		private int stdType = - 1;
		
		/// <summary> Flags indicating the type of this StdChannel.</summary>

    public const int STDIN = 0;
    public const int STDOUT = 1;
    public const int STDERR = 2;
		
		/// <summary> Constructor that does nothing.  Open() must be called before
		/// any of the subsequent read, write, etc calls can be made.
		/// </summary>
		
		internal StdChannel()
		{
		}
		
		/// <summary> Constructor that will automatically call open.
		/// 
		/// </summary>
		/// <param name="stdName">name of the stdio channel; stdin, stderr or stdout.
		/// </param>
		
		internal StdChannel(string stdName)
		{
			if (stdName.Equals("stdin"))
			{
				open(STDIN);
			}
			else if (stdName.Equals("stdout"))
			{
				open(STDOUT);
			}
			else if (stdName.Equals("stderr"))
			{
				open(STDERR);
			}
			else
			{
				throw new TclRuntimeError("Error: unexpected type for StdChannel");
			}
		}
		
		
		internal StdChannel(int type)
		{
			open(type);
		}
		
		
		/// <summary> Set the channel type to one of the three stdio types.  Throw a 
		/// tclRuntimeEerror if the stdName is not one of the three types.  If
		/// it is a stdin channel, initialize the "in" data member.  Since "in"
		/// is static it may have already be initialized, test for this case 
		/// first.  Set the names to fileX, this will be the key in the chanTable 
		/// hashtable to access this object.  Note: it is not put into the hash 
		/// table in this function.  The calling function is responsible for that.
		/// 
		/// </summary>
		/// <param name="stdName">String that equals stdin, stdout, stderr
		/// </param>
		/// <returns> The name of the channelId
		/// </returns>
		
		internal  string open(int type)
		{
			
			switch (type)
			{
				
				case STDIN: 
					mode = TclIO.RDONLY;
					Buffering = TclIO.BUFF_LINE;
					ChanName = "stdin";
					break;
				
				case STDOUT: 
					mode = TclIO.WRONLY;
					Buffering = TclIO.BUFF_LINE;
					ChanName = "stdout";
					break;
				
				case STDERR: 
					mode = TclIO.WRONLY;
					Buffering = TclIO.BUFF_NONE;
					ChanName = "stderr";
					break;
				
				default: 
					throw new System.SystemException("type does not match one of STDIN, STDOUT, or STDERR");
				
			}
			
			stdType = type;
			
			return ChanName;
		}
		
		/// <summary> Write to stdout or stderr.  If the stdType is not set to 
		/// STDOUT or STDERR this is an error; either the stdType wasnt
		/// correctly initialized, or this was called on a STDIN channel.
		/// 
		/// </summary>
		/// <param name="interp">the current interpreter.
		/// </param>
		/// <param name="s">the string to write 
		/// </param>
		
				public override void write(Interp interp, TclObject outData)
		{
			
			checkWrite(interp);
			
			if (stdType == STDERR)
			{
				
				System.Console.Error.Write(outData.ToString());
			}
			else
			{
				
				string s = outData.ToString();
				System.Console.Out.Write(s);
				if (buffering == TclIO.BUFF_NONE || (buffering == TclIO.BUFF_LINE && s.EndsWith("\n")))
				{
					System.Console.Out.Flush();
				}
			}
		}
		
		/// <summary> Check for any output that might still need to be flushed
		/// when the channel is closed.
		/// </summary>
		
				internal override void  close()
		{
			if (stdType == STDOUT)
				System.Console.Out.Flush();
			base.close();
		}
	}
}
