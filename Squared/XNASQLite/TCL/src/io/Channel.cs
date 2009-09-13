/*
* Channel.java
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: Channel.java,v 1.25 2003/03/08 03:42:43 mdejong Exp $
*/
using System;
using System.Text;

namespace tcl.lang
{
	
	/// <summary> The Channel class provides functionality that will
	/// be needed for any type of Tcl channel. It performs
	/// generic reads, writes, without specifying how a
	/// given channel is actually created. Each new channel
	/// type will need to extend the abstract Channel class
	/// and override any methods it needs to provide a
	/// specific implementation for.
	/// </summary>
	
	public abstract class Channel
	{
		private void  InitBlock()
		{
			buffering = TclIO.BUFF_FULL;
			inputTranslation = TclIO.TRANS_AUTO;
			outputTranslation = TclIO.TRANS_PLATFORM;
		}
		/// <summary> This method should be overridden in the subclass to provide
		/// a channel specific InputStream object.
		/// </summary>
		protected internal abstract System.IO.Stream InputStream{get;}
		/// <summary> This method should be overridden in the subclass to provide
		/// a channel specific OutputStream object.
		/// </summary>
		protected internal abstract System.IO.Stream OutputStream{get;}
				/// <summary> Gets the chanName that is the key for the chanTable hashtable.</summary>
		/// <returns> channelId
		/// </returns>
		/// <summary> Sets the chanName that is the key for the chanTable hashtable.</summary>
		/// <param name="chan">the unique channelId
		/// </param>
		 public string ChanName
		{
			
			
			get
			{
				return chanName;
			}
			
			
			
			set
			{
				chanName = value;
			}
			
		}
		/// <summary> Return a string that describes the channel type.
		/// 
		/// This is the equivilent of the Tcl_ChannelTypE.typeName field.
		/// </summary>
				public abstract string ChanType{get;}
		/// <summary> Return number of references to this Channel.</summary>
		 public int RefCount
		{
			
			
			get
			{
				return refCount;
			}
			
		}
		 public bool ReadOnly
		{
			get
			{
				return ((mode & TclIO.RDONLY) != 0);
			}
			
		}
		 public bool WriteOnly
		{
			get
			{
				return ((mode & TclIO.WRONLY) != 0);
			}
			
		}
		 public bool ReadWrite
		{
			get
			{
				return ((mode & TclIO.RDWR) != 0);
			}
			
		}
				/// <summary> Query blocking mode.</summary>
		/// <summary> Set blocking mode.
		/// 
		/// </summary>
		/// <param name="blocking">new blocking mode
		/// </param>
		 public bool Blocking
		{
			
			
			get
			{
				return blocking;
			}
			
			
			
			set
			{
				blocking = value;
				
				if (input != null)
					input.Blocking = blocking;
				if (output != null)
					output.Blocking = blocking;
			}
			
		}
				/// <summary> Query buffering mode.</summary>
		/// <summary> Set buffering mode
		/// 
		/// </summary>
		/// <param name="buffering">One of TclIO.BUFF_FULL, TclIO.BUFF_LINE,
		/// or TclIO.BUFF_NONE
		/// </param>
		 public int Buffering
		{
			
			
			get
			{
				return buffering;
			}
			
			
			
			set
			{
				if (value < TclIO.BUFF_FULL || value > TclIO.BUFF_NONE)
					throw new TclRuntimeError("invalid buffering mode in Channel.setBuffering()");
				
				buffering = value;
				if (input != null)
					input.Buffering = buffering;
				if (output != null)
					output.Buffering = buffering;
			}
			
		}
				/// <summary> Query buffer size</summary>
		/// <summary> Tcl_SetChannelBufferSize -> setBufferSize
		/// 
		/// </summary>
		/// <param name="size">new buffer size
		/// </param>
		 public int BufferSize
		{
			
			
			get
			{
				return bufferSize;
			}
			
			
			
			set
			{
				
				// If the buffer size is smaller than 10 bytes or larger than 1 Meg
				// do not accept the requested size and leave the current buffer size.
				
				if ((value < 10) || (value > (1024 * 1024)))
				{
					return ;
				}
				
				bufferSize = value;
				if (input != null)
					input.BufferSize = bufferSize;
				if (output != null)
					output.BufferSize = bufferSize;
			}
			
		}
		 public int NumBufferedInputBytes
		{
			get
			{
				if (input != null)
					return input.NumBufferedBytes;
				else
					return 0;
			}
			
		}
		 public int NumBufferedOutputBytes
		{
			get
			{
				if (output != null)
					return output.NumBufferedBytes;
				else
					return 0;
			}
			
		}
		/// <summary> Returns true if a background flush is waiting to happen.</summary>
		public bool BgFlushScheduled
		{
			
			
			get
			{
				// FIXME: Need to query output here
				return false;
			}
			
		}
				/// <summary> Query encoding
		/// 
		/// </summary>
		/// <returns> Name of Channel's Java encoding (null if no encoding)
		/// </returns>
		/// <summary> Set new Java encoding</summary>
		 internal System.Text.Encoding Encoding
		{
			get
			{
				return encoding;
			}
			set
			{
				encoding = value;
				if ((System.Object) encoding == null)
					bytesPerChar = 1;
				else
					bytesPerChar = EncodingCmd.getBytesPerChar(encoding);
				
				if (input != null)
					input.Encoding = encoding;
				if (output != null)
					output.Encoding = encoding;
				
				// FIXME: Pass bytesPerChar to input and output
			}
			
		}
		/// <summary> Query input translation
		/// Set new input translation</summary>
		public int InputTranslation
		{
			
			
			get
			{
				return inputTranslation;
			}
			
			
			
			set
			{
				inputTranslation = value;
				if (input != null)
					input.Translation = inputTranslation;
			}
			
		}
		/// <summary> Query output translation
		/// Set new output translation</summary>
		public int OutputTranslation
		{
			
			
			get
			{
				return outputTranslation;
			}
			
			
			
			set
			{
				outputTranslation = value;
				if (output != null)
					output.Translation = outputTranslation;
			}
			
		}
				/// <summary> Query input eof character</summary>
		/// <summary> Set new input eof character</summary>
		 internal char InputEofChar
		{
			
			
			get
			{
				return inputEofChar;
			}
			
			
			
			set
			{
				// Store as a byte, not a unicode character
				inputEofChar = (char) (value & 0xFF);
				if (input != null)
					input.EofChar = inputEofChar;
			}
			
		}
				/// <summary> Query output eof character</summary>
		/// <summary> Set new output eof character</summary>
		 internal char OutputEofChar
		{
			
			
			get
			{
				return outputEofChar;
			}
			
			
			
			set
			{
				// Store as a byte, not a unicode character
				outputEofChar = (char) (value & 0xFF);
				if (output != null)
					output.EofChar = outputEofChar;
			}
			
		}
		
		/// <summary> The read, write, append and create flags are set here.  The 
		/// variables used to set the flags are found in the class TclIO.
		/// </summary>
		
		protected internal int mode;
		
		/// <summary> This is a unique name that sub-classes need to set.  It is used
		/// as the key in the hashtable of registered channels (in interp).
		/// </summary>
		
		private string chanName;
		
		/// <summary> How many interpreters hold references to this IO channel?</summary>
		
		protected internal int refCount = 0;
		
		/// <summary> Tcl input and output objecs. These are like a mix between
		/// a Java Stream and a Reader.
		/// </summary>
		
		protected internal TclInputStream input = null;
		protected internal TclOutputStream output = null;
		
		/// <summary> Set to false when channel is in non-blocking mode.</summary>
		
		protected internal bool blocking = true;
		
		/// <summary> Buffering (full,line, or none)</summary>
		
				protected internal int buffering;
		
		/// <summary> Buffer size, in bytes, allocated for channel to store input or output</summary>
		
		protected internal int bufferSize = 4096;
		
		/// <summary> Name of Java encoding for this Channel.
		/// A null value means use no encoding (binary).
		/// </summary>
		
		// FIXME: Check to see if this field is updated after a call
		// to "encoding system $enc" for new Channel objects!
		
		protected internal System.Text.Encoding encoding;
		protected internal int bytesPerChar;
		
		/// <summary> Translation mode for end-of-line character</summary>
		
				protected internal int inputTranslation;
				protected internal int outputTranslation;
		
		/// <summary> If nonzero, use this as a signal of EOF on input.</summary>
		
		protected internal char inputEofChar = (char) (0);
		
		/// <summary> If nonzero, append this to a writeable channel on close.</summary>
		
		protected internal char outputEofChar = (char) (0);
		
		internal Channel()
		{
			InitBlock();
			Encoding = EncodingCmd.systemJavaEncoding;
		}
		
		/// <summary> Tcl_ReadChars -> read
		/// 
		/// Read data from the Channel into the given TclObject.
		/// 
		/// </summary>
		/// <param name="interp">          is used for TclExceptions.  
		/// </param>
		/// <param name="tobj">            the object data will be added to.
		/// </param>
		/// <param name="readType">        specifies if the read should read the entire
		/// buffer (TclIO.READ_ALL), the next line
		/// (TclIO.READ_LINE), of a specified number
		/// of bytes (TclIO.READ_N_BYTES).
		/// </param>
		/// <param name="numBytes">        the number of bytes/chars to read. Used only
		/// when the readType is TclIO.READ_N_BYTES.
		/// </param>
		/// <returns>                 the number of bytes read.
		/// Returns -1 on EOF or on error.
		/// </returns>
		/// <exception cref=""> TclException is thrown if read occurs on WRONLY channel.
		/// </exception>
		/// <exception cref=""> IOException  is thrown when an IO error occurs that was not
		/// correctly tested for.  Most cases should be caught.
		/// </exception>
		
		internal  int read(Interp interp, TclObject tobj, int readType, int numBytes)
		{
			TclObject dataObj;
			
			checkRead(interp);
			initInput();
			
			switch (readType)
			{
				
				case TclIO.READ_ALL:  {
						return input.doReadChars(tobj, - 1);
					}
				
				case TclIO.READ_LINE:  {
						return input.getsObj(tobj);
					}
				
				case TclIO.READ_N_BYTES:  {
						return input.doReadChars(tobj, numBytes);
					}
				
				default:  {
						throw new TclRuntimeError("Channel.read: Invalid read mode.");
					}
				
			}
		}
		
		/// <summary> Tcl_WriteObj -> write
		/// 
		/// Write data to the Channel
		/// 
		/// </summary>
		/// <param name="interp">is used for TclExceptions.  
		/// </param>
		/// <param name="outData">the TclObject that holds the data to write.
		/// </param>
		
		public virtual void  write(Interp interp, TclObject outData)
		{
			
			checkWrite(interp);
			initOutput();
			
			// FIXME: Is it possible for a write to happen with a null output?
			if (output != null)
			{
				output.writeObj(outData);
			}
		}
		
		/// <summary> Tcl_WriteChars -> write
		/// 
		/// Write string data to the Channel.
		/// 
		/// </summary>
		/// <param name="interp">is used for TclExceptions.  
		/// </param>
		/// <param name="outStr">the String object to write.
		/// </param>
		
		public  void  write(Interp interp, string outStr)
		{
			write(interp, TclString.newInstance(outStr));
		}
		
		/// <summary> Close the Channel.  The channel is only closed, it is 
		/// the responsibility of the "closer" to remove the channel from 
		/// the channel table.
		/// </summary>
		
				internal virtual void  close()
		{
			
			System.IO.IOException ex = null;
			
			if (input != null)
			{
				try
				{
					input.close();
				}
				catch (System.IO.IOException e)
				{
					ex = e;
				}
				input = null;
			}
			
			if (output != null)
			{
				try
				{
					output.close();
				}
				catch (System.IO.IOException e)
				{
					ex = e;
				}
				output = null;
			}
			
			if (ex != null)
				throw ex;
		}
		
		/// <summary> Flush the Channel.
		/// 
		/// </summary>
		/// <exception cref=""> TclException is thrown when attempting to flush a 
		/// read only channel.
		/// </exception>
		/// <exception cref=""> IOEcception is thrown for all other flush errors.
		/// </exception>
		
		public  void  flush(Interp interp)
		{
			
			checkWrite(interp);
			
			if (output != null)
			{
				output.flush();
			}
		}
		
		/// <summary> Move the current file pointer. If seek is not supported on the
		/// given channel then -1 will be returned. A subclass should
		/// override this method if it supports the seek operation.
		/// 
		/// </summary>
		/// <param name="interp">currrent interpreter.
		/// </param>
		/// <param name="offset">The number of bytes to move the file pointer.
		/// </param>
		/// <param name="mode">where to begin incrementing the file pointer; beginning,
		/// current, end.
		/// </param>
		
		public virtual void  seek(Interp interp, long offset, int mode)
		{
			throw new TclPosixException(interp, TclPosixException.EINVAL, true, "error during seek on \"" + ChanName + "\"");
		}
		
		/// <summary> Return the current file pointer. If tell is not supported on the
		/// given channel then -1 will be returned. A subclass should override
		/// this method if it supports the tell operation.
		/// </summary>
		
		public virtual long tell()
		{
			return (long) (- 1);
		}
		
		/// <summary> Setup the TclInputStream on the first call to read</summary>
		
		protected internal  void  initInput()
		{
			if (input != null)
				return ;
			
			input = new TclInputStream(InputStream);
			input.Encoding = encoding;
			input.Translation = inputTranslation;
			input.EofChar = inputEofChar;
			input.Buffering = buffering;
			input.BufferSize = bufferSize;
			input.Blocking = blocking;
		}
		
		/// <summary> Setup the TclOutputStream on the first call to write</summary>
		
		protected internal  void  initOutput()
		{
			if (output != null)
				return ;
			
			output = new TclOutputStream(OutputStream);
			output.Encoding = encoding;
			output.Translation = outputTranslation;
			output.EofChar = outputEofChar;
			output.Buffering = buffering;
			output.BufferSize = bufferSize;
			output.Blocking = blocking;
		}
		
		/// <summary> Returns true if the last read reached the EOF.</summary>
		
		public bool eof()
		{
			if (input != null)
				return input.eof();
			else
				return false;
		}
		
		// Helper methods to check read/write permission and raise a
		// TclException if reading is not allowed.
		
		protected internal  void  checkRead(Interp interp)
		{
			if (!ReadOnly && !ReadWrite)
			{
				throw new TclException(interp, "channel \"" + ChanName + "\" wasn't opened for reading");
			}
		}
		
		protected internal  void  checkWrite(Interp interp)
		{
			if (!WriteOnly && !ReadWrite)
			{
				throw new TclException(interp, "channel \"" + ChanName + "\" wasn't opened for writing");
			}
		}
		
		/// <summary> Tcl_InputBlocked -> isBlocked
		/// 
		/// Returns true if input is blocked on this channel, false otherwise.
		/// 
		/// </summary>
		
		public bool isBlocked(Interp interp)
		{
			checkRead(interp);
			
			if (input != null)
				return input.Blocked;
			else
				return false;
		}
		
		/// <summary> Channel is in CRLF eol input translation mode and the last
		/// byte seen was a CR.
		/// </summary>
		
		public bool inputSawCR()
		{
			if (input != null)
				return input.sawCR();
			return false;
		}
	}
}
