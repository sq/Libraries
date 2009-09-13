#undef DEBUG
/*
* TclInputStream.java
*
* Copyright (c) 2003 Mo DeJong
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: TclInputStream.java,v 1.1 2003/03/08 03:42:44 mdejong Exp $
*/

// A TclInputStream is a cross between a Java InputStream and
// a Reader. The class supports reading raw bytes as well as
// encoded characters. It manages buffering and supports
// line oriented reading of data. It also supports a user
// configurable EOF marker and line ending translations.
using System;
using System.Text;

namespace tcl.lang
{
	
	public class TclInputStream
	{
		 internal System.Text.Encoding Encoding
		{
			set
			{
				encoding = value;
			}
			
		}
		 internal char EofChar
		{
			set
			{
				eofChar = value;
			}
			
		}
		 internal int Translation
		{
			set
			{
				translation = value;
			}
			
		}
		 internal int Buffering
		{
			set
			{
				buffering = value;
			}
			
		}
		 internal int BufferSize
		{
			set
			{
				bufSize = value;
			}
			
		}
		 internal bool Blocking
		{
			set
			{
				blocking = value;
			}
			
		}
		 internal bool Blocked
		{
			get
			{
				return blocked;
			}
			
		}
		/// <summary> GetInput -> getInput
		/// 
		/// Reads input data from a device into a channel buffer.
		/// 
		/// The return value is the Posix error code if an error occurred while
		/// reading from the file, or 0 otherwise.  
		/// </summary>
		private int Input
		{
			
			
			get
			{
				int toRead;
				int result;
				int nread;
				
				// if (checkForDeadChannel()) return EINVAL;
				
				// Skipped pushback processing code for stacked Channels
				
				
				// See if we can fill an existing buffer. If we can, read only
				// as much as will fit in it. Otherwise allocate a new buffer,
				// add it to the input queue and attempt to fill it to the max.
				
				ChannelBuffer buf = inQueueTail;
				
				if ((buf != null) && (buf.nextAdded < buf.bufLength))
				{
					System.Diagnostics.Debug.WriteLine("smaller than buffer");
					toRead = buf.bufLength - buf.nextAdded;
				}
				else
				{
					System.Diagnostics.Debug.WriteLine("fits in existing buffer");
					
					buf = saveInBuf;
					saveInBuf = null;
					
					// Check the actual buffersize against the requested
					// buffersize. Buffers which are smaller than requested are
					// squashed. This is done to honor dynamic changes of the
					// buffersize made by the user.
					
					if ((buf != null) && ((buf.bufLength - tcl.lang.ChannelBuffer.BUFFER_PADDING) < bufSize))
					{
						buf = null;
					}
					if (buf == null)
					{
						System.Diagnostics.Debug.WriteLine("allocated ChannelBuffer of size " + bufSize);
						buf = new ChannelBuffer(bufSize);
					}
					buf.next = null;
					
					// Use the actual size of the buffer to determine
					// the number of bytes to read from the channel and not the
					// size for new buffers. They can be different if the
					// buffersize was changed between reads.
					
					toRead = buf.bufLength - buf.nextAdded;
					System.Diagnostics.Debug.WriteLine("toRead set to " + toRead);
					
					if (inQueueTail == null)
						inQueueHead = buf;
					else
						inQueueTail.next = buf;
					
					inQueueTail = buf;
				}
				
				// If EOF is set, we should avoid calling the driver because on some
				// platforms it is impossible to read from a device after EOF.
				
				if (eofCond)
				{
					System.Diagnostics.Debug.WriteLine("eofCond was true, no error return");
					return 0;
				}
				
				// FIXME: We do not handle non-blocking or this CHANNEL_TIMER_FEV flag yet
				
				if (!blocking)
				{
					return TclPosixException.EWOULDBLOCK;
				}
				else
				{
					result = 0;
					
					// Can we even use this for a brain-dead nonblocking IO check?
					int numAvailable = 0;
					
					if (!blocking && (numAvailable < toRead))
					{
						result = TclPosixException.EWOULDBLOCK;
						nread = - 1;
					}
					else
					{
						try
						{
							System.Diagnostics.Debug.WriteLine("now to read " + toRead + " bytes");
							if (input == null) input =     System.Console.OpenStandardInput();
							nread = SupportClass.ReadInput(input, ref buf.buf, buf.nextAdded, toRead);
							
							// read() returns -1 on EOF
							if (nread == - 1)
							{
								System.Diagnostics.Debug.WriteLine("got EOF from read() call");
								nread = 0;
							}
						}
						catch (System.IO.IOException ex)
						{
							// FIXME: How do we recover from IO errors here?
							// I think we need to set result to a POSIX error
							SupportClass.WriteStackTrace(ex, System.Console.Error);
							nread = - 1;
						}
					}
				}
				
				if (nread > 0)
				{
					System.Diagnostics.Debug.WriteLine("nread is " + nread);
					buf.nextAdded += nread;
					
					// should avoid calling the driver because on some platforms we
					// will block in the low level reading code even though the
					// channel is set into nonblocking mode.
					
					if (nread < toRead)
					{
						blocked = true;
					}
				}
				else if (nread == 0)
				{
					System.Diagnostics.Debug.WriteLine("nread is zero");
					eofCond = true;
					encodingEnd = true;
				}
				else if (nread < 0)
				{
					System.Diagnostics.Debug.WriteLine("nread is " + nread);
					if ((result == TclPosixException.EWOULDBLOCK) || (result == TclPosixException.EAGAIN))
					{
						blocked = true;
						result = TclPosixException.EAGAIN;
					}
					// FIXME: Called needs to raise a TclException
					//Tcl_SetErrno(result);
					return result;
				}
				System.Diagnostics.Debug.WriteLine("no error return");
				return 0;
			}
			
		}
		/// <summary> Tcl_InputBuffered -> getNumBufferedBytes
		/// 
		/// Return the number of bytes that are current buffered.
		/// </summary>
		 internal int NumBufferedBytes
		{
			
			
			get
			{
				ChannelBuffer buf;
				int IOQueued;
				for (IOQueued = 0, buf = inQueueHead; buf != null; buf = buf.next)
				{
					IOQueued += buf.nextAdded - buf.nextRemoved;
				}
				return IOQueued;
			}
			
		}
		
		/// <summary> The Java byte stream object we pull data in from.</summary>
		
		private System.IO.Stream input;
		
		/// <summary> If nonzero, use this character as EOF marker.</summary>
		
		private char eofChar;
		
		/// <summary> Flag that is set on each read. If the read encountered EOF
		/// or a custom eofChar is found, the it is set to true.
		/// </summary>
		
		private bool eofCond = false;
		private bool stickyEofCond = false;
		
		/// <summary> Translation mode for end-of-line character</summary>
		
		protected internal int translation;
		
		/// <summary> Name of Java encoding for this Channel.
		/// A null value means use no encoding (binary).
		/// </summary>
		
		protected internal System.Text.Encoding encoding;
		
		/// <summary> Current converter object. A null value means
		/// that no conversions have been done yet.
		/// </summary>
		
		protected internal Decoder btc = null;
		
		/// <summary> Buffering</summary>
		
		protected internal int buffering;
		
		/// <summary> Blocking</summary>
		
		protected internal bool blocking;
		
		/// <summary> Blocked</summary>
		
		protected internal bool blocked = false;
		
		/// <summary> Buffer size in bytes</summary>
		
		protected internal int bufSize;
		
		/// <summary> Used to track EOL state</summary>
		
		protected internal bool needNL = false;
		protected internal bool sawCR_Renamed_Field = false;
		
		protected internal bool needMoreData = false;
		
		/// <summary> Flags used to track encoding states.
		/// The encodingState member of called inputEncodingState
		/// in the C ChannelState type. The encodingStart and encodingEnd
		/// members combined are called inputEncodingFlags
		/// and have the bit values TCL_ENCODING_END and TCL_ENCODING_START.
		/// </summary>
		
		internal Object encodingState = null;
		internal bool encodingStart = true;
		internal bool encodingEnd = false;
		
		/// <summary> First and last buffers in the input queue.</summary>
		
		internal ChannelBuffer inQueueHead = null;
		internal ChannelBuffer inQueueTail = null;
		internal ChannelBuffer saveInBuf = null;
		
		/// <summary> Constructor for Tcl input stream class. We require
		/// a byte stream source at init time, the stram can't
		/// be changed after the TclInputStream is created.
		/// </summary>
		
		internal TclInputStream(System.IO.Stream inInput)
		{
			input = inInput;
		}
		
				// Helper used by getsObj and filterBytes
		
		internal class GetsState
		{
			public GetsState(TclInputStream enclosingInstance)
			{
				InitBlock(enclosingInstance);
			}
			private void  InitBlock(TclInputStream enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
				rawRead = new IntPtr();
				charsWrote = new IntPtr();
			}
			private TclInputStream enclosingInstance;
			public TclInputStream Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			internal TclObject obj;
			//int dst;
			internal System.Text.Encoding encoding;
			internal ChannelBuffer buf;
			internal Object state;
						internal IntPtr rawRead;
			//IntPtr bytesWrote = new IntPtr();
						internal IntPtr charsWrote;
			internal int totalChars;
		}
		
		/// <summary> Tcl_GetsObj -> getsObj
		/// 
		/// Accumulate input from the input channel until end-of-line or
		/// end-of-file has been seen.  Bytes read from the input channel
		/// are converted to Unicode using the encoding specified by the
		/// channel.
		/// 
		/// Returns the number of characters accumulated in the object
		/// or -1 if error, blocked, or EOF. If -1, use Tcl_GetErrno()
		/// to retrieve the POSIX error code for the error or condition
		/// that occurred.
		/// 
		/// FIXME: Above setting of error code is not fully implemented.
		/// 
		/// Will consume input from the channel.
		/// On reading EOF, leave channel at EOF char.
		/// On reading EOL, leave channel after EOL, but don't
		/// return EOL in dst buffer.
		/// </summary>
		
		internal  int getsObj(TclObject obj)
		{
			GetsState gs;
			ChannelBuffer buf;
			bool oldEncodingStart, oldEncodingEnd;
			int oldRemoved, skip, inEofChar;
			int copiedTotal, oldLength;
			bool in_binary_encoding = false;
			int dst, dstEnd, eol, eof;
			Object oldState;
			
			buf = inQueueHead;
			//encoding = this.encoding;
			
			// Preserved so we can restore the channel's state in case we don't
			// find a newline in the available input.
			
			oldLength = 0;
			oldEncodingStart = encodingStart;
			oldEncodingEnd = encodingEnd;
			oldState = encodingState;
			oldRemoved = tcl.lang.ChannelBuffer.BUFFER_PADDING;
			if (buf != null)
			{
				oldRemoved = buf.nextRemoved;
			}
			
			// If there is no encoding, use "iso8859-1" -- readLine() doesn't
			// produce ByteArray objects.
			
			if ((System.Object) encoding == null)
			{
				in_binary_encoding = true;
				encoding = EncodingCmd.getJavaName("utf-8");
			}
			
			System.Diagnostics.Debug.WriteLine("getsObj encoding is " + encoding);
			
			// Object used by filterBytes to keep track of how much data has
			// been consumed from the channel buffers.
			
			gs = new GetsState(this);
			gs.obj = obj;
			//gs.dst = &dst;
			gs.encoding = encoding;
			gs.buf = buf;
			gs.state = oldState;
			gs.rawRead.i = 0;
			//gs.bytesWrote.i = 0;
			gs.charsWrote.i = 0;
			gs.totalChars = 0;
			
			// Ensure that tobj is an empty TclString object.
			// Cheat a bit and grab the StringBuffer out of
			// the TclString so we can query the data that
			// was just added to the buffer.
			TclString.empty(obj);
			System.Text.StringBuilder obj_sbuf = ((TclString) obj.InternalRep).sbuf;
			
			dst = 0;
			dstEnd = dst;
			
			skip = 0;
			eof = - 1;
			inEofChar = eofChar;
			
			// Used to implement goto like functionality for restore
			// and goteol loop terminaltion blocks.
			
			bool restore = false;
			bool goteol = false;
			
			// This is just here so that eol and copiedTotal are
			// definitely assigned before the try block.
			eol = - 1;
			copiedTotal = - 1;
			
			{
				while (true)
				{
					if (dst >= dstEnd)
					{
						if (filterBytes(gs) != 0)
						{
							restore = true;
														goto restore_or_goteol_brk; //goto restore
						}
						dstEnd += gs.charsWrote.i; // dstEnd = dst + gs.bytesWrote;
					}
					
					// Remember if EOF char is seen, then look for EOL anyhow, because
					// the EOL might be before the EOF char.
					
					if (inEofChar != '\x0000')
					{
						for (eol = dst; eol < dstEnd; eol++)
						{
							if (obj_sbuf[eol] == inEofChar)
							{
								dstEnd = eol;
								eof = eol;
								break;
							}
						}
					}
					
					// On EOL, leave current file position pointing after the EOL, but
					// don't store the EOL in the output string.
					
					switch (translation)
					{
						
						case TclIO.TRANS_LF:  {
								for (eol = dst; eol < dstEnd; eol++)
								{
									if (obj_sbuf[eol] == '\n')
									{
										skip = 1;
										goteol = true;
																				goto restore_or_goteol_brk; //goto goteol
									}
								}
								break;
							}
						
						case TclIO.TRANS_CR:  {
								for (eol = dst; eol < dstEnd; eol++)
								{
									if (obj_sbuf[eol] == '\r')
									{
										skip = 1;
										goteol = true;
																				goto restore_or_goteol_brk; //goto goteol
									}
								}
								break;
							}
						
						case TclIO.TRANS_CRLF:  {
								for (eol = dst; eol < dstEnd; eol++)
								{
									if (obj_sbuf[eol] == '\r')
									{
										eol++;
										
										// If a CR is at the end of the buffer,
										// then check for a LF at the begining
										// of the next buffer.
										
										if (eol >= dstEnd)
										{
											//int offset;
											
											//offset = eol - objPtr->bytes;
											dst = dstEnd;
											if (filterBytes(gs) != 0)
											{
												restore = true;
																								goto restore_or_goteol_brk; //goto restore
											}
											dstEnd += gs.charsWrote.i; // dstEnd = dst + gs.bytesWrote
											//eol = objPtr->bytes + offset;
											if (eol >= dstEnd)
											{
												skip = 0;
												goteol = true;
																								goto restore_or_goteol_brk; //goto goteol
											}
										}
										if (obj_sbuf[eol] == '\n')
										{
											eol--;
											skip = 2;
											goteol = true;
																						goto restore_or_goteol_brk; //goto goteol
										}
									}
								}
								break;
							}
						
						case TclIO.TRANS_AUTO:  {
								eol = dst;
								skip = 1;
								if (sawCR_Renamed_Field)
								{
									sawCR_Renamed_Field = false;
									if ((eol < dstEnd) && (obj_sbuf[eol] == '\n'))
									{
										// Skip the raw bytes that make up the '\n'.
										
										char[] tmp = new char[1];
										IntPtr rawRead = new IntPtr(this);
										
										buf = gs.buf;
										// FIXME: We don't actually pass gs.state here, should we?
										//if (btc != null) btc.reset();
										externalToUnicode(buf.buf, buf.nextRemoved, gs.rawRead.i, tmp, 0, 1, rawRead, null, null);
										buf.nextRemoved += rawRead.i;
										gs.rawRead.i -= rawRead.i;
										//gs.bytesWrote.i--;
										gs.charsWrote.i--;
										obj_sbuf.Remove(dst, 1);
										dstEnd--;
									}
								}
								for (eol = dst; eol < dstEnd; eol++)
								{
									if (obj_sbuf[eol] == '\r')
									{
										eol++;
										if (eol == dstEnd)
										{
											// If buffer ended on \r, peek ahead to see if a
											// \n is available.
											
											//int offset;
											//IntPtr dstEndPtr = new IntPtr();
											
											//offset = eol /* - objPtr->bytes*/;
											dst = dstEnd;
											
											// FIXME: Why does this peek in AUTO mode
											// but filter in CRLF mode?
											peekAhead(gs);
											//dstEnd = dstEndPtr.i;
											dstEnd += gs.charsWrote.i;
											//eol = /*objPtr->bytes + */ offset;
											if (eol >= dstEnd)
											{
												eol--;
												sawCR_Renamed_Field = true;
												goteol = true;
																								goto restore_or_goteol_brk; //goto goteol
											}
										}
										if (obj_sbuf[eol] == '\n')
										{
											skip++;
										}
										eol--;
										goteol = true; //goto goteol
																				goto restore_or_goteol_brk;
									}
									else if (obj_sbuf[eol] == '\n')
									{
										goteol = true;
																				goto restore_or_goteol_brk; //goto goteol
									}
								}
							}
							break;
						}
					if (eof != - 1)
					{
						// EOF character was seen.  On EOF, leave current file position
						// pointing at the EOF character, but don't store the EOF
						// character in the output string.
						
						dstEnd = eof;
						eofCond = true;
						stickyEofCond = true;
						encodingEnd = true;
					}
					if (eofCond)
					{
						skip = 0;
						eol = dstEnd;
						if (eol == oldLength)
						{
							// If we didn't append any bytes before encountering EOF,
							// caller needs to see -1.
							
							obj_sbuf.Length = oldLength;
							commonGetsCleanup();
							copiedTotal = - 1;
														goto restore_or_goteol_brk; //goto done
						}
						goteol = true;
												goto restore_or_goteol_brk; //goto goteol
					}
					dst = dstEnd;
				}
			}
			
restore_or_goteol_brk: ;
			 // end restore_or_goteol: block
			
			if (goteol)
			{
				// Found EOL or EOF, but the output buffer may now contain too many
				// characters.  We need to know how many raw bytes correspond to
				// the number of characters we want, plus how many raw bytes
				// correspond to the character(s) making up EOL (if any), so we can
				// remove the correct number of bytes from the channel buffer.
				
				int linelen = eol - dst + skip;
				char[] tmp = new char[linelen];
				
				buf = gs.buf;
				encodingState = gs.state;
				if (btc != null)
				{
					btc = this.encoding.GetDecoder();
				}
				externalToUnicode(buf.buf, buf.nextRemoved, gs.rawRead.i, tmp, 0, linelen, gs.rawRead, null, gs.charsWrote);
				buf.nextRemoved += gs.rawRead.i;
				
				// Recycle all the emptied buffers.
				
				obj_sbuf.Length = eol;
				commonGetsCleanup();
				blocked = false;
				copiedTotal = gs.totalChars + gs.charsWrote.i - skip;
			}
			if (restore)
			{
				// Couldn't get a complete line.  This only happens if we get a error
				// reading from the channel or we are non-blocking and there wasn't
				// an EOL or EOF in the data available.
				
				buf = inQueueHead;
				buf.nextRemoved = oldRemoved;
				
				for (buf = buf.next; buf != null; buf = buf.next)
				{
					buf.nextRemoved = tcl.lang.ChannelBuffer.BUFFER_PADDING;
				}
				commonGetsCleanup();
				
				encodingState = oldState;
				//if (btc != null) btc.reset(); // Not sure we want to reset encoder state here
				encodingStart = oldEncodingStart;
				encodingEnd = oldEncodingEnd;
				obj_sbuf.Length = oldLength;
				
				// We didn't get a complete line so we need to indicate to UpdateInterest
				// that the gets blocked.  It will wait for more data instead of firing
				// a timer, avoiding a busy wait.  This is where we are assuming that the
				// next operation is a gets.  No more file events will be delivered on 
				// this channel until new data arrives or some operation is performed
				// on the channel (e.g. gets, read, fconfigure) that changes the blocking
				// state.  Note that this means a file event will not be delivered even
				// though a read would be able to consume the buffered data.
				
				needMoreData = true;
				copiedTotal = - 1;
			}
			
			// Update the notifier state so we don't block while there is still
			// data in the buffers.
			
			//done:
			// Reset original encoding in case it was set to binary
			if (in_binary_encoding)
				encoding = null;
			
			updateInterest();
			
			// FIXME: copiedTotal seems to be returning incorrect values
			// for some tests, need to make caller code use the return
			// value instead of the length of the returned object before
			// these errors can be detected by the test suite.
			return copiedTotal;
		}
		
		/// <summary> FilterInputBytes -> filterBytes
		/// 
		/// Helper function for getsObj. Appends Unicode characters
		/// onto the TclObject associated with the GetsState after
		/// converting them from raw bytes encoded in the Channel.
		/// 
		/// Consumes available bytes from channel buffers.  When channel
		/// buffers are exhausted, reads more bytes from channel device into
		/// a new channel buffer.  It is the caller's responsibility to
		/// free the channel buffers that have been exhausted.
		/// 
		/// The return value is -1 if there was an error reading from the
		/// channel, 0 otherwise.
		/// 
		/// FIXME: Doc modification of object's StringBuffer
		/// 
		/// Status object keeps track of how much data from channel buffers
		/// has been consumed and where characters should be stored.
		/// </summary>
		
		internal  int filterBytes(GetsState gs)
		{
			ChannelBuffer buf;
			byte[] raw;
			int rawStart, rawEnd;
			char[] dst;
			int offset, toRead, spaceLeft, result, rawLen, length;
			TclObject obj;
						int ENCODING_LINESIZE = 20; // Lower bound on how many bytes
			// to convert at a time. Since we
			// don't know a priori how many
			// bytes of storage this many
			// source bytes will use, we
			// actually need at least
			// ENCODING_LINESIZE bytes of room.
			
			bool goto_read = false; // Set to true when jumping to the read
			// label, used to simulate a goto.
			
			obj = gs.obj;
			
			// Subtract the number of bytes that were removed from channel buffer
			// during last call.
			
			buf = gs.buf;
			if (buf != null)
			{
				buf.nextRemoved += gs.rawRead.i;
				if (buf.nextRemoved >= buf.nextAdded)
				{
					buf = buf.next;
				}
			}
			gs.totalChars += gs.charsWrote.i;
			
						while (true)
			{
				if (goto_read || (buf == null) || (buf.nextAdded == tcl.lang.ChannelBuffer.BUFFER_PADDING))
				{
					// All channel buffers were exhausted and the caller still hasn't
					// seen EOL.  Need to read more bytes from the channel device.
					// Side effect is to allocate another channel buffer.
					
					//read:
					if (blocked)
					{
						if (!blocking)
						{
							gs.charsWrote.i = 0;
							gs.rawRead.i = 0;
							return - 1;
						}
						blocked = false;
					}
					if (Input != 0)
					{
						gs.charsWrote.i = 0;
						gs.rawRead.i = 0;
						return - 1;
					}
					buf = inQueueTail;
					gs.buf = buf;
				}
				
				// Convert some of the bytes from the channel buffer to characters.
				// Space in obj's string rep is used to hold the characters.
				
				rawStart = buf.nextRemoved;
				raw = buf.buf;
				rawEnd = buf.nextAdded;
				rawLen = rawEnd - rawStart;
				
				//dst = *gsPtr->dstPtr;
				//offset = dst - objPtr->bytes;
				toRead = ENCODING_LINESIZE;
				if (toRead > rawLen)
				{
					toRead = rawLen;
				}
				//dstNeeded = toRead * TCL_UTF_MAX + 1;
				//spaceLeft = objPtr->length - offset - TCL_UTF_MAX - 1;
				//if (dstNeeded > spaceLeft) {
				//    length = offset * 2;
				//    if (offset < dstNeeded) {
				//        length = offset + dstNeeded;
				//    }
				//    length += TCL_UTF_MAX + 1;
				//    Tcl_SetObjLength(objPtr, length);
				//    spaceLeft = length - offset;
				//    dst = objPtr->bytes + offset;
				//    *gsPtr->dstPtr = dst;
				//}
				dst = new char[toRead];
				gs.state = encodingState;
				result = externalToUnicode(raw, rawStart, rawLen, dst, 0, toRead, gs.rawRead, null, gs.charsWrote);
				TclString.append(gs.obj, dst, 0, gs.charsWrote.i);
				
				// Make sure that if we go through 'gets', that we reset the
				// TCL_ENCODING_START flag still.
				
				encodingStart = false;
				
				if (result == TCL_CONVERT_MULTIBYTE)
				{
					// The last few bytes in this channel buffer were the start of a
					// multibyte sequence.  If this buffer was full, then move them to
					// the next buffer so the bytes will be contiguous.  
					
					ChannelBuffer next;
					int extra;
					
					next = buf.next;
					if (buf.nextAdded < buf.bufLength)
					{
						if (gs.rawRead.i > 0)
						{
							// Some raw bytes were converted to UTF-8.  Fall through,
							// returning those UTF-8 characters because a EOL might be
							// present in them.
						}
						else if (eofCond)
						{
							// There was a partial character followed by EOF on the
							// device.  Fall through, returning that nothing was found.
							
							buf.nextRemoved = buf.nextAdded;
						}
						else
						{
							// There are no more cached raw bytes left.  See if we can
							// get some more.
							
							goto_read = true;
														goto read; //goto read;
						}
					}
					else
					{
						if (next == null)
						{
							next = new ChannelBuffer(bufSize);
							buf.next = next;
							inQueueTail = next;
						}
						extra = rawLen - gs.rawRead.i;
						Array.Copy(raw, gs.rawRead.i, next.buf, tcl.lang.ChannelBuffer.BUFFER_PADDING - extra, extra);
						next.nextRemoved -= extra;
						buf.nextAdded -= extra;
					}
				}
				
								goto read_brk; // End loop in the normal case
				
read: ;
			}
			
read_brk: ;
			
			
			gs.buf = buf;
			return 0;
		}
		
		/// <summary> PeekAhead -> peekAhead
		/// 
		/// Helper function used by getsObj.  Called when we've seen a
		/// \r at the end of the string and want to look ahead one
		/// character to see if it is a \n.
		/// 
		/// Characters read from the channel are appended to gs.obj
		/// via the filterBytes method.
		/// </summary>
		
		internal  void  peekAhead(GetsState gs)
		{
			ChannelBuffer buf;
			//Tcl_DriverBlockModeProc *blockModeProc;
			int bytesLeft;
			bool goto_cleanup = false; // Set to true when jumping to the
			// cleanup label, used to simulate a goto.
			
			buf = gs.buf;
			
			// If there's any more raw input that's still buffered, we'll peek into
			// that.  Otherwise, only get more data from the channel driver if it
			// looks like there might actually be more data.  The assumption is that
			// if the channel buffer is filled right up to the end, then there
			// might be more data to read.
			
			{
				//blockModeProc = NULL;
				if (buf.next == null)
				{
					bytesLeft = buf.nextAdded - (buf.nextRemoved + gs.rawRead.i);
					if (bytesLeft == 0)
					{
						if (buf.nextAdded < buf.bufLength)
						{
							// Don't peek ahead if last read was short read.
							goto_cleanup = true;
														goto cleanup_brk;
						}
						// FIXME: This non-blocking check is currently disabled, non-blocking
						// is not currently supported and it is not clean why we would
						// need to depend on non-blocking IO when peeking anyway.
						if (blocking)
						{
							//blockModeProc = Tcl_ChannelBlockModeProc(chanPtr->typePtr);
              //if (false)
              //{
              //  // Don't peek ahead if cannot set non-blocking mode.
              //  goto_cleanup = true;
              //  goto cleanup_brk;
              //}
							//StackSetBlockMode(chanPtr, TCL_MODE_NONBLOCKING);
						}
					}
				}
				//if (filterBytes(gs) == 0) {
				//    dstEndPtr.i = gs.charsWrote.i; *gsPtr->dstPtr + gs.bytesWrote.i
				//}
				filterBytes(gs);
				//if (blockModeProc != NULL) {
				//    StackSetBlockMode(chanPtr, TCL_MODE_BLOCKING);
				//}
			}
			
cleanup_brk: ;
			
			
			if (goto_cleanup)
			{
				buf.nextRemoved += gs.rawRead.i;
				gs.rawRead.i = 0;
				gs.totalChars += gs.charsWrote.i;
				//gs.bytesWrote.i = 0;
				gs.charsWrote.i = 0;
			}
		}
		
		/// <summary> CommonGetsCleanup -> commonGetsCleanup
		/// 
		/// Helper function used by getsObj to restore the channel after
		/// a "gets" operation.
		/// 
		/// </summary>
		
		internal  void  commonGetsCleanup()
		{
			ChannelBuffer buf, next;
			
			buf = inQueueHead;
			for (; buf != null; buf = next)
			{
				next = buf.next;
				if (buf.nextRemoved < buf.nextAdded)
				{
					break;
				}
				recycleBuffer(buf, false);
			}
			inQueueHead = buf;
			if (buf == null)
			{
				inQueueTail = null;
			}
			else
			{
				// If any multi-byte characters were split across channel buffer
				// boundaries, the split-up bytes were moved to the next channel
				// buffer by filterBytes().  Move the bytes back to their
				// original buffer because the caller could change the channel's
				// encoding which could change the interpretation of whether those
				// bytes really made up multi-byte characters after all.
				
				next = buf.next;
				for (; next != null; next = buf.next)
				{
					int extra;
					
					extra = buf.bufLength - buf.nextAdded;
					if (extra > 0)
					{
						Array.Copy(next.buf, tcl.lang.ChannelBuffer.BUFFER_PADDING - extra, buf.buf, buf.nextAdded, extra);
						buf.nextAdded += extra;
						next.nextRemoved = tcl.lang.ChannelBuffer.BUFFER_PADDING;
					}
					buf = next;
				}
			}
			if ((System.Object) encoding != null)
			{
				//Tcl_FreeEncoding(encoding);
			}
		}
		
		// CloseChannel -> close
		
		internal  void  close()
		{
			discardQueued(true);
			// FIXME: More close logic in CloseChannel
		}
		
		internal  bool eof()
		{
			return eofCond;
		}
		
		internal  bool sawCR()
		{
			return sawCR_Renamed_Field;
		}
		
				// Helper class to implement integer pass by reference
		// for methods like doReadChars, readBytes and so on.
		
		internal class IntPtr
		{
			private void  InitBlock(TclInputStream enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TclInputStream enclosingInstance;
			public TclInputStream Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			internal int i;
			
			internal IntPtr()
			{
			}
			internal IntPtr(TclInputStream enclosingInstance)
			{
				InitBlock(enclosingInstance);
			}
			
			internal IntPtr(TclInputStream enclosingInstance, int value)
			{
				InitBlock(enclosingInstance);
				i = value;
			}
		}
		
		/// <summary> DoReadChars -> doReadChars
		/// 
		/// Reads from the channel until the requested number of characters
		/// have been seen, EOF is seen, or the channel would block.  EOL
		/// and EOF translation is done.  If reading binary data, the raw
		/// bytes are wrapped in a Tcl byte array object.  Otherwise, the raw
		/// bytes are converted to characters using the channel's current
		/// encoding and stored in a Tcl string object.
		/// 
		/// </summary>
		/// <param name="obj">Input data is stored in this object.
		/// </param>
		/// <param name="toRead">Maximum number of characters to store,
		/// or -1 to read all available data (up to EOF
		/// or when channel blocks).
		/// </param>
		
		internal  int doReadChars(TclObject obj, int toRead)
		{
			ChannelBuffer buf;
			int copied, copiedNow, result;
			IntPtr offset = new IntPtr(this);
			
			if ((System.Object) encoding == null)
			{
				TclByteArray.setLength(null, obj, 0);
			}
			else
			{
				TclString.empty(obj);
			}
			offset.i = 0;
			
			// if toRead is negative, read until EOF
			if (toRead < 0)
			{
				toRead = System.Int32.MaxValue;
			}
			
			{
				for (copied = 0; toRead > 0; )
				{
					copiedNow = - 1;
					if (inQueueHead != null)
					{
						if ((System.Object) encoding == null)
						{
							System.Diagnostics.Debug.WriteLine("calling readBytes " + toRead);
							copiedNow = readBytes(obj, toRead, offset);
						}
						else
						{
							System.Diagnostics.Debug.WriteLine("calling readChars " + toRead);
							copiedNow = readChars(obj, toRead);
						}
						
						// If the current buffer is empty recycle it.
						
						buf = inQueueHead;
						System.Diagnostics.Debug.WriteLine("after read* buf.nextRemoved is " + buf.nextRemoved);
						System.Diagnostics.Debug.WriteLine("after read* buf.nextAdded is " + buf.nextAdded);

						if (buf.nextRemoved == buf.nextAdded)
						{
							System.Diagnostics.Debug.WriteLine("recycling empty buffer");
							ChannelBuffer next;
							
							next = buf.next;
							recycleBuffer(buf, false);
							inQueueHead = next;
							if (next == null)
							{
								System.Diagnostics.Debug.WriteLine("inQueueTail set to null");
								inQueueTail = null;
							}
							else
							{
								System.Diagnostics.Debug.WriteLine("inQueueTail is not null");
							}
						}
					}
					if (copiedNow < 0)
					{
						System.Diagnostics.Debug.WriteLine("copiedNow < 0");
						if (eofCond)
						{
							System.Diagnostics.Debug.WriteLine("eofCond");
							break;
						}
						if (blocked)
						{
							System.Diagnostics.Debug.WriteLine("blocked");
							if (!blocking)
							{
								break;
							}
							blocked = false;
						}
						result = Input;
						if (result != 0)
						{
							System.Diagnostics.Debug.WriteLine("non-zero result");
							if (result == TclPosixException.EAGAIN)
							{
								break;
							}
							copied = - 1;
														goto done_brk; //goto done
						}
					}
					else
					{
						copied += copiedNow;
						System.Diagnostics.Debug.WriteLine("copied incremented to " + copied);
						toRead -= copiedNow;
						System.Diagnostics.Debug.WriteLine("toRead decremented to " + toRead);
					}
				}
				
				blocked = false;
				
				if ((System.Object) encoding == null)
				{
					TclByteArray.setLength(null, obj, offset.i);
					System.Diagnostics.Debug.WriteLine("set byte array length to " + offset.i);
				}
			}
			
done_brk: ;
			 // end done: block
			
			//done:
			updateInterest();
	
#if DEBUG
				System.Diagnostics.Debug.WriteLine("returning copied = " + copied);
				
				System.Diagnostics.Debug.WriteLine("returning string \"" + obj + "\"");
				obj.invalidateStringRep();
				
				System.Diagnostics.Debug.WriteLine("returning string \"" + obj + "\"");
#endif
			
			return copied;
		}
		
		/// <summary> ReadBytes -> readBytes
		/// 
		/// Reads from the channel until the requested number of
		/// bytes have been seen, EOF is seen, or the channel would
		/// block. Bytes from the channel are stored in obj as a
		/// ByteArray object.  EOL and EOF translation are done.
		/// 
		/// 'bytesToRead' can safely be a very large number because
		/// space is only allocated to hold data read from the channel
		/// as needed.
		/// 
		/// The return value is the number of bytes appended to
		/// the object.
		/// 
		/// </summary>
		/// <param name="obj,">the TclByteArrayObject we are operating on
		/// </param>
		/// <param name="bytesToRead,">Maximum number of bytes to store.
		/// Bytes are obtained from the first
		/// buffer in the queue -- even if this number
		/// is larger than the number of bytes only
		/// the bytes from the first buffer are returned.
		/// </param>
		/// <param name="offsetPtr">   On input, contains how many bytes of
		/// obj have been used to hold data. On
		/// output, how many bytes are now being used.
		/// </param>
		
		internal  int readBytes(TclObject obj, int bytesToRead, IntPtr offsetPtr)
		{
			int toRead, srcOff, srcLen, offset, length;
			ChannelBuffer buf;
			IntPtr srcRead, dstWrote;
			byte[] src, dst;
			
			offset = offsetPtr.i;
			
			buf = inQueueHead;
			src = buf.buf;
			srcOff = buf.nextRemoved;
			srcLen = buf.nextAdded - buf.nextRemoved;

			System.Diagnostics.Debug.WriteLine("readBytes() : src buffer len is " + buf.buf.Length);
			System.Diagnostics.Debug.WriteLine("readBytes() : buf.nextRemoved is " + buf.nextRemoved);
			System.Diagnostics.Debug.WriteLine("readBytes() : buf.nextAdded is " + buf.nextAdded);
			
			toRead = bytesToRead;
			if (toRead > srcLen)
			{
				toRead = srcLen;
				System.Diagnostics.Debug.WriteLine("readBytes() : toRead set to " + toRead);
			}
			
			length = TclByteArray.getLength(null, obj);
			dst = TclByteArray.getBytes(null, obj);
			System.Diagnostics.Debug.WriteLine("readBytes() : toRead is " + toRead);
			System.Diagnostics.Debug.WriteLine("readBytes() : length is " + length);
			System.Diagnostics.Debug.WriteLine("readBytes() : array length is " + dst.Length);
			
			if (toRead > length - offset - 1)
			{
				System.Diagnostics.Debug.WriteLine("readBytes() : TclObject too small");
				
				// Double the existing size of the object or make enough room to
				// hold all the characters we may get from the source buffer,
				// whichever is larger.
				
				length = offset * 2;
				if (offset < toRead)
				{
					length = offset + toRead + 1;
				}
				dst = TclByteArray.setLength(null, obj, length);
			}
			
			if (needNL)
			{
				needNL = false;
				if ((srcLen == 0) || (src[srcOff] != '\n'))
				{
					dst[offset] = (byte) SupportClass.Identity('\r');
					offsetPtr.i += 1;
					return 1;
				}
				dst[offset++] = (byte) SupportClass.Identity('\n');
				srcOff++;
				srcLen--;
				toRead--;
			}
			
			srcRead = new IntPtr(this, srcLen);
			dstWrote = new IntPtr(this, toRead);
			
			if (translateEOL(dst, offset, src, srcOff, dstWrote, srcRead) != 0)
			{
				if (dstWrote.i == 0)
				{
					return - 1;
				}
			}
			
			buf.nextRemoved += srcRead.i;
			offsetPtr.i += dstWrote.i;
			return dstWrote.i;
		}
		
		/// <summary> ReadChars -> readChars
		/// 
		/// Reads from the channel until the requested number of
		/// characters have been seen, EOF is seen, or the channel would
		/// block.  Raw bytes from the channel are converted to characters
		/// and stored in obj.  EOL and EOF translation is done.
		/// 
		/// 'charsToRead' can safely be a very large number because
		/// space is only allocated to hold data read from the channel
		/// as needed.
		/// 
		/// The return value is the number of characters appended to
		/// the object.
		/// 
		/// </summary>
		/// <param name="obj,">the TclByteArrayObject we are operating on
		/// </param>
		/// <param name="charsToRead,">Maximum number of chars to store.
		/// Chars are obtained from the first
		/// buffer in the queue -- even if this number
		/// is larger than the number of chars only
		/// the chars from the first buffer are returned.
		/// </param>
		
		internal  int readChars(TclObject obj, int charsToRead)
		{
			int toRead, factor, spaceLeft, length, srcLen, dstNeeded;
			int srcOff, dstOff;
			IntPtr srcRead, numChars, dstRead, dstWrote;
			ChannelBuffer buf;
			byte[] src;
			char[] dst;
			
			Object oldState;
			
			srcRead = new IntPtr(this);
			numChars = new IntPtr(this);
			dstRead = new IntPtr(this);
			dstWrote = new IntPtr(this);
			
			buf = inQueueHead;
			src = buf.buf;
			srcOff = buf.nextRemoved;
			srcLen = buf.nextAdded - buf.nextRemoved;
			
			/* FIXME: Include final Tcl patch for srcLen == 0 case */
			
			if (srcLen == 0)
			{
				if (needNL)
				{
					TclString.append(obj, "\r");
					return 1;
				}
				return - 1;
			}
			
			toRead = charsToRead;
			if (toRead > srcLen)
			{
				toRead = srcLen;
			}
			
			// FIXME : Do something to cache conversion buffer, or it might also
			// to pass the TclObject directly into the externalToUnicode method
			// so as to avoid the need for this extra buffer.
			dstNeeded = toRead;
			dst = new char[dstNeeded];
			dstOff = 0;
			
			oldState = encodingState;
			if (needNL)
			{
				// We want a '\n' because the last character we saw was '\r'.
				needNL = false;
				
				externalToUnicode(src, srcOff, srcLen, dst, dstOff, 1, srcRead, dstWrote, numChars);
				if ((numChars.i > 0) && (dst[dstOff] == '\n'))
				{
					// The next char was a '\n'.  Consume it and produce a '\n'.
					buf.nextRemoved += srcRead.i;
				}
				else
				{
					// The next char was not a '\n'.  Produce a '\r'.
					dst[dstOff] = '\r';
				}
				encodingStart = false;
				TclString.append(obj, dst, dstOff, 1);
				return 1;
			}
			
			externalToUnicode(src, srcOff, srcLen, dst, dstOff, dstNeeded, srcRead, dstWrote, numChars);
			
			// This block is disabled since the char converter does
			// not inform us about partial chars, instead it silently
			// stores the partial character internally.
			
      //if (false && srcRead.i == 0)
      //{
      //  // Not enough bytes in src buffer to make a complete char.  Copy
      //  // the bytes to the next buffer to make a new contiguous string,
      //  // then tell the caller to fill the buffer with more bytes.
				
      //  ChannelBuffer next;
				
      //  next = buf.next;
      //  if (next == null)
      //  {
      //    if (srcLen > 0)
      //    {
      //      // There isn't enough data in the buffers to complete the next
      //      // character, so we need to wait for more data before the next
      //      // file event can be delivered.
      //      //
      //      // The exception to this is if the input buffer was
      //      // completely empty before we tried to convert its
      //      // contents. Nothing in, nothing out, and no incomplete
      //      // character data. The conversion before the current one
      //      // was complete.
						
      //      needMoreData = true;
      //    }
      //    return - 1;
      //  }
      //  next.nextRemoved -= srcLen;
      //  Array.Copy(src, srcOff, next.buf, next.nextRemoved, srcLen);
      //  recycleBuffer(buf, false);
      //  inQueueHead = next;
      //  return readChars(obj, charsToRead);
      //}
			
			dstRead.i = dstWrote.i;
			if (translateEOL(dst, dstOff, dst, dstOff, dstWrote, dstRead) != 0)
			{
				// Hit EOF char.  How many bytes of src correspond to where the
				// EOF was located in dst? Run the conversion again with an
				// output buffer just big enough to hold the data so we can
				// get the correct value for srcRead.
				
				if (dstWrote.i == 0)
				{
					return - 1;
				}
				encodingState = oldState;
				if (btc != null)
				{
					btc = this.encoding.GetDecoder();
				}
				externalToUnicode(src, srcOff, srcLen, dst, dstOff, dstRead.i, srcRead, dstWrote, numChars);
				translateEOL(dst, dstOff, dst, dstOff, dstWrote, dstRead);
			}
			
			// The number of characters that we got may be less than the number
			// that we started with because "\r\n" sequences may have been
			// turned into just '\n' in dst.
			
			numChars.i -= (dstRead.i - dstWrote.i);
			
			if (numChars.i > toRead)
			{
				// Got too many chars.
				
				int eof;
				eof = toRead;
				encodingState = oldState;
				if (btc != null)
				{
					btc =  this.encoding.GetDecoder();
				}
				externalToUnicode(src, srcOff, srcLen, dst, dstOff, (eof - dstOff), srcRead, dstWrote, numChars);
				dstRead.i = dstWrote.i;
				translateEOL(dst, dstOff, dst, dstOff, dstWrote, dstRead);
				numChars.i -= (dstRead.i - dstWrote.i);
			}
			encodingStart = false;
			
			buf.nextRemoved += srcRead.i;
			
			TclString.append(obj, dst, dstOff, numChars.i);
			
			return numChars.i;
		}
		
		// FIXME: Only define the ones that we actually need/use.
		
		// The following definitions are the error codes returned by externalToUnicode
		//
		// TCL_OK:			All characters were converted.
		//
		// TCL_CONVERT_NOSPACE:	The output buffer would not have been large
		//				enough for all of the converted data; as many
		//				characters as could fit were converted though.
		//
		// TCL_CONVERT_MULTIBYTE:	The last few bytes in the source string were
		//				the beginning of a multibyte sequence, but
		//				more bytes were needed to complete this
		//				sequence.  A subsequent call to the conversion
		//				routine should pass the beginning of this
		//				unconverted sequence plus additional bytes
		//				from the source stream to properly convert
		//				the formerly split-up multibyte sequence.
		//
		// TCL_CONVERT_SYNTAX:		The source stream contained an invalid
		//				character sequence.  This may occur if the
		//				input stream has been damaged or if the input
		//				encoding method was misidentified.  This error
		//				is reported only if TCL_ENCODING_STOPONERROR
		//				was specified.
		// 
		// TCL_CONVERT_UNKNOWN:		The source string contained a character
		//				that could not be represented in the target
		//				encoding.  This error is reported only if
		//				TCL_ENCODING_STOPONERROR was specified.
		
				private int TCL_CONVERT_MULTIBYTE = - 1;
				private int TCL_CONVERT_SYNTAX = - 2;
				private int TCL_CONVERT_UNKNOWN = - 3;
				private int TCL_CONVERT_NOSPACE = - 4;
		
		/// <summary> Tcl_ExternalToUtf -> externalToUnicode
		/// 
		/// Convert a source buffer from the specified encoding into Unicode.
		/// 
		/// FIXME: Add doc for return values
		/// 
		/// </summary>
		/// <param name="src,">        Source bytes in specified encoding.
		/// </param>
		/// <param name="srcOff,">     First index in src input array.
		/// </param>
		/// <param name="srcLen,">     Number of bytes in src buffer.
		/// </param>
		/// <param name="dst,">        Array to store unicode characters in.
		/// </param>
		/// <param name="dstOff,">     First available index in dst array.
		/// </param>
		/// <param name="dstLen,">     Length of dst array.
		/// </param>
		/// <param name="srcReadPtr,"> Filled with the number of bytes from
		/// the source string that were converted.
		/// This may be less than the original source
		/// length if there was a problem converting
		/// some source characters.
		/// </param>
		/// <param name="dstWrotePtr,">Filled with the number of chars that were
		/// stored in the output buffer as a result of
		/// the conversion
		/// </param>
		/// <param name="dstCharsPtr,">Filled with the number of characters that
		/// correspond to the bytes stored in the
		/// output buffer.
		/// </param>
		
		internal  int externalToUnicode(byte[] src, int srcOff, int srcLen, char[] dst, int dstOff, int dstLen, IntPtr srcReadPtr, IntPtr dstWrotePtr, IntPtr dstCharsPtr)
		{
			System.Text.Encoding encoding = this.encoding;
			int result;
			//Object state;
			//String encoded_string;
			
			if ((System.Object) encoding == null)
			{
				// This should never happen
				//encoding = Encoding.getJavaName("identity");
				throw new TclRuntimeError("externalToUnicode called with null encoding");
			}
			
			// FIXME: This may no longer be needed after Tcl srcLen == 0 patch
			
			if (srcLen == 0)
			{
				srcReadPtr.i = 0;
				if (dstWrotePtr != null)
					dstWrotePtr.i = 0;
				if (dstCharsPtr != null)
					dstCharsPtr.i = 0;
				return 0;
			}
			
			// Convert bytes from src into unicode chars and store them in dst.
			
			// FIXME: This allocated a buffer for the String and then copies the
			// encoded data into a second buffer. Need to decode the data directly
			// into the dst array since this is performance critical.

			#if DEBUG
				System.Diagnostics.Debug.WriteLine("now to decode byte array of length " + srcLen);
				System.Diagnostics.Debug.WriteLine("srcOff is " + srcOff);
				for (int i = srcOff; i < (srcOff + srcLen); i++)
				{
					System.Diagnostics.Debug.WriteLine("(byte) '" + ((char) src[i]) + "'");
				}
				System.Diagnostics.Debug.WriteLine("encoded as " + encoding);
			#endif
				
			// FIXME: In the cases where we know that we don't actually want
			// to copy the data, we could pass a flag so that we could
			// take advantage of encodings that had a one to one mapping
			// from bytes to chars (now need to copy then to find bytes used).
			
			if (btc == null)
			{
				try
				{
					btc = this.encoding.GetDecoder();
				}
				catch (System.IO.IOException ex)
				{
					// Valid encodings should be checked already
					throw new TclRuntimeError("unsupported encoding \"" + encoding + "\"");
				}
			}
			
			int bytes_read, chars_written;
			
			int required_chars = btc.GetCharCount(src,srcOff,srcLen);
			if (required_chars>dstLen) 
			{
				srcLen = dstLen;
			}
			chars_written = btc.GetChars(src,srcOff,srcLen,dst,dstOff);
			bytes_read = srcLen;
			
			srcReadPtr.i = bytes_read;
			if (dstWrotePtr != null)
				dstWrotePtr.i = chars_written;
			if (dstCharsPtr != null)
				dstCharsPtr.i = chars_written;
			
			// FIXME: When do we return error codes?
			result = 0;
			
			return result;
		}
		
		/// <summary> RecycleBuffer -> recycleBuffer
		/// 
		/// Helper function to recycle input buffers. Ensures that
		/// two input buffers are saved (one in the input queue and
		/// another in the saveInBuf field). Only if these conditions
		/// are met is the buffer released so that it can be
		/// garbage collected.
		/// </summary>
		
		private void  recycleBuffer(ChannelBuffer buf, bool mustDiscard)
		{
			
			if (mustDiscard)
				return ;
			
			// Only save buffers which are at least as big as the requested
			// buffersize for the channel. This is to honor dynamic changes
			// of the buffersize made by the user.
			
			if ((buf.bufLength - tcl.lang.ChannelBuffer.BUFFER_PADDING) < bufSize)
			{
				return ;
			}
			
			if (inQueueHead == null)
			{
				inQueueHead = buf;
				inQueueTail = buf;
				
				buf.nextRemoved = tcl.lang.ChannelBuffer.BUFFER_PADDING;
				buf.nextAdded = tcl.lang.ChannelBuffer.BUFFER_PADDING;
				buf.next = null;
				return ;
			}
			if (saveInBuf == null)
			{
				saveInBuf = buf;
				
				buf.nextRemoved = tcl.lang.ChannelBuffer.BUFFER_PADDING;
				buf.nextAdded = tcl.lang.ChannelBuffer.BUFFER_PADDING;
				buf.next = null;
				return ;
			}
		}
		
		/// <summary> DiscardInputQueued -> discardQueued
		/// 
		/// Discards any input read from the channel but not yet consumed
		/// by Tcl reading commands.
		/// </summary>
		
		private void  discardQueued(bool discardSavedBuffers)
		{
			ChannelBuffer buf, nxt;
			
			buf = inQueueHead;
			inQueueHead = null;
			inQueueTail = null;
			for (; buf != null; buf = nxt)
			{
				nxt = buf.next;
				recycleBuffer(buf, discardSavedBuffers);
			}
			
			// If discardSavedBuffers is true, must also discard any previously
			// saved buffer in the saveInBuf field.
			
			if (discardSavedBuffers)
			{
				if (saveInBuf != null)
				{
					saveInBuf = null;
				}
			}
		}
		
		/// <summary> TranslateInputEOL -> translateEOL
		/// 
		/// Perform input EOL and EOF translation on the source buffer,
		/// leaving the translated result in the destination buffer.
		/// 
		/// Results:
		/// The return value is 1 if the EOF character was found when 
		/// copying bytes to the destination buffer, 0 otherwise.  
		/// 
		/// </summary>
		/// <param name="dstArray,">Output buffer to fill with translated bytes or chars.
		/// </param>
		/// <param name="dstStart,">First unused index in the dst output array.
		/// </param>
		/// <param name="srcArray,">Input buffer that holds the bytes or chars to translate
		/// </param>
		/// <param name="srcStart,">Index of first available byte in src array.
		/// </param>
		/// <param name="dstLenPtr,">On entry, the maximum length of output
		/// buffer in bytes or chars; must be <= srcLenPtr.i.  On
		/// exit, the number of bytes or chars actually used in
		/// output buffer.
		/// </param>
		/// <param name="srcLenPtr,">On entry, the length of source buffer.
		/// On exit, the number of bytes or chars read from
		/// the source buffer.
		/// </param>
		
		internal  int translateEOL(System.Object dstArray, int dstStart, Object srcArray, int srcStart, IntPtr dstLenPtr, IntPtr srcLenPtr)
		{
			
			// Figure out if the srcArray and dstArray buffers
			// are byte or char arrays.
			bool isCharType;
			char[] srcArrayChar, dstArrayChar;
			byte[] srcArrayByte, dstArrayByte;
			
			if ((srcArray is char[]) && (dstArray is char[]))
			{
				isCharType = true;
				srcArrayChar = (char[]) srcArray;
				dstArrayChar = (char[]) dstArray;
				srcArrayByte = null;
				dstArrayByte = null;
			}
			else if ((srcArray is byte[]) && (dstArray is byte[]))
			{
				isCharType = false;
				srcArrayChar = null;
				dstArrayChar = null;
				srcArrayByte = (byte[]) srcArray;
				dstArrayByte = (byte[]) dstArray;
			}
			else
			{
				throw new TclRuntimeError("unknown array argument types");
			}
			
			int dstLen, srcLen, inEofChar, index;
			int eof;
			
			dstLen = dstLenPtr.i;
			
			eof = - 1;
			inEofChar = eofChar;
			if (inEofChar != '\x0000')
			{
				// Find EOF in translated buffer then compress out the EOL.  The
				// source buffer may be much longer than the destination buffer --
				// we only want to return EOF if the EOF has been copied to the
				// destination buffer.
				
				int src, srcMax;
				
				srcMax = srcStart + srcLenPtr.i;
				for (src = srcStart; src < srcMax; src++)
				{
					if (isCharType)
					{
						index = srcArrayChar[src];
					}
					else
					{
						index = srcArrayByte[src];
					}
					if (index == inEofChar)
					{
						eof = src;
						srcLen = src - srcStart;
						if (srcLen < dstLen)
						{
							dstLen = srcLen;
						}
						srcLenPtr.i = srcLen;
						break;
					}
				}
			}
			switch (translation)
			{
				
				case TclIO.TRANS_LF:  {
						if ((dstArray != srcArray) || ((dstArray == srcArray) && (dstStart != srcStart)))
						{
							Array.Copy((System.Array) srcArray, srcStart, (System.Array) dstArray, dstStart, dstLen);
						}
						srcLen = dstLen;
						break;
					}
				
				case TclIO.TRANS_CR:  {
						int dst, dstEnd;
						
						if ((dstArray != srcArray) || ((dstArray == srcArray) && (dstStart != srcStart)))
						{
							Array.Copy((System.Array) srcArray, srcStart, (System.Array) dstArray, dstStart, dstLen);
						}
						dstEnd = dstStart + dstLen;
						if (isCharType)
						{
							for (dst = dstStart; dst < dstEnd; dst++)
							{
								if (dstArrayChar[dst] == '\r')
								{
									dstArrayChar[dst] = '\n';
								}
							}
						}
						else
						{
							for (dst = dstStart; dst < dstEnd; dst++)
							{
								if (dstArrayByte[dst] == '\r')
								{
									dstArrayByte[dst] = (byte) SupportClass.Identity('\n');
								}
							}
						}
						srcLen = dstLen;
						break;
					}
				
				case TclIO.TRANS_CRLF:  {
						int dst;
						int src, srcEnd, srcMax;
						
						dst = dstStart;
						src = srcStart;
						srcEnd = srcStart + dstLen;
						srcMax = srcStart + srcLenPtr.i;
						
						if (isCharType)
						{
							for (; src < srcEnd; )
							{
								if (srcArrayChar[src] == '\r')
								{
									src++;
									if (src >= srcMax)
									{
										needNL = true;
									}
									else if (srcArrayChar[src] == '\n')
									{
										dstArrayChar[dst++] = srcArrayChar[src++];
									}
									else
									{
										dstArrayChar[dst++] = '\r';
									}
								}
								else
								{
									dstArrayChar[dst++] = srcArrayChar[src++];
								}
							}
						}
						else
						{
							for (; src < srcEnd; )
							{
								if (srcArrayByte[src] == '\r')
								{
									src++;
									if (src >= srcMax)
									{
										needNL = true;
									}
									else if (srcArrayByte[src] == '\n')
									{
										dstArrayByte[dst++] = srcArrayByte[src++];
									}
									else
									{
										dstArrayByte[dst++] = (byte) SupportClass.Identity('\r');
									}
								}
								else
								{
									dstArrayByte[dst++] = srcArrayByte[src++];
								}
							}
						}
						
						srcLen = src - srcStart;
						dstLen = dst - dstStart;
						break;
					}
				
				case TclIO.TRANS_AUTO:  {
						int dst;
						int src, srcEnd, srcMax;
						
						dst = dstStart;
						src = srcStart;
						srcEnd = srcStart + dstLen;
						srcMax = srcStart + srcLenPtr.i;
						
						if (sawCR_Renamed_Field && (src < srcMax))
						{
							if (isCharType)
							{
								index = srcArrayChar[src];
							}
							else
							{
								index = srcArrayByte[src];
							}
							if (index == '\n')
							{
								src++;
							}
							sawCR_Renamed_Field = false;
						}
						if (isCharType)
						{
							for (; src < srcEnd; )
							{
								if (srcArrayChar[src] == '\r')
								{
									src++;
									if (src >= srcMax)
									{
										sawCR_Renamed_Field = true;
									}
									else if (srcArrayChar[src] == '\n')
									{
										if (srcEnd < srcMax)
										{
											srcEnd++;
										}
										src++;
									}
									dstArrayChar[dst++] = '\n';
								}
								else
								{
									dstArrayChar[dst++] = srcArrayChar[src++];
								}
							}
						}
						else
						{
							for (; src < srcEnd; )
							{
								if (srcArrayByte[src] == '\r')
								{
									src++;
									if (src >= srcMax)
									{
										sawCR_Renamed_Field = true;
									}
									else if (srcArrayByte[src] == '\n')
									{
										if (srcEnd < srcMax)
										{
											srcEnd++;
										}
										src++;
									}
									dstArrayByte[dst++] = (byte) SupportClass.Identity('\n');
								}
								else
								{
									dstArrayByte[dst++] = srcArrayByte[src++];
								}
							}
						}
						srcLen = src - srcStart;
						dstLen = dst - dstStart;
						break;
					}
				
				default:  {
						throw new TclRuntimeError("invalid translation");
					}
				
			}
			dstLenPtr.i = dstLen;
			
			if ((eof != - 1) && (srcStart + srcLen >= eof))
			{
				// EOF character was seen in EOL translated range.  Leave current
				// file position pointing at the EOF character, but don't store the
				// EOF character in the output string.
				
				eofCond = true;
				stickyEofCond = true;
				encodingEnd = true;
				sawCR_Renamed_Field = false;
				needNL = false;
				return 1;
			}
			
			srcLenPtr.i = srcLen;
			return 0;
		}
		
		/// <summary> UpdateInterest -> updateInterest
		/// 
		/// Arrange for the notifier to call us back at appropriate times
		/// based on the current state of the channel.
		/// </summary>
		
		internal  void  updateInterest()
		{
			// FIXME: Currently unimplemented
		}
		
		/// <summary> seekReset
		/// 
		/// Helper method used to reset state info when doing a seek.
		/// </summary>
		
		internal  void  seekReset()
		{
			discardQueued(false);
			eofCond = false;
			stickyEofCond = false;
			blocked = false;
			sawCR_Renamed_Field = false;
			// FIXME: Change needed in Tcl
			//needNL = false;
		}
	}
}
