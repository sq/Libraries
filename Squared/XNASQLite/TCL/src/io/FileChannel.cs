#undef DEBUG
/*
* FileChannel.java --
*
* Copyright (c) 1997 Sun Microsystems, Inc.
*
* See the file "license.terms" for information on usage and
* redistribution of this file, and for a DISCLAIMER OF ALL
* WARRANTIES.
* 
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* RCS @(#) $Id: FileChannel.java,v 1.20 2003/03/08 03:42:44 mdejong Exp $
*
*/
using System;
using System.IO;

namespace tcl.lang
{
	
	/// <summary> Subclass of the abstract class Channel.  It implements all of the 
	/// methods to perform read, write, open, close, etc on a file.
	/// </summary>
	
	class FileChannel:Channel
	{
		public override string ChanType
		{
			get
			{
				return "file";
			}
			
		}
		override protected internal System.IO.Stream InputStream
		{
			get
			{
				
				
				// return new FileInputStream(file.getFD());
				return file;
			}
			
		}
		override protected internal System.IO.Stream OutputStream
		{
			get
			{
				
				
				// return new FileOutputStream(file.getFD());
				return file;
			}
			
		}
		
		/// <summary> The file needs to have a file pointer that can be moved randomly
		/// within the file.  The RandomAccessFile is the only java.io class
		/// that allows this behavior.
		/// </summary>
		
		private System.IO.FileStream file = null;
		
		/// <summary> Open a file with the read/write permissions determined by modeFlags.
		/// This method must be called before any other methods will function
		/// properly.
		/// 
		/// </summary>
		/// <param name="interp">currrent interpreter.
		/// </param>
		/// <param name="fileName">the absolute path or name of file in the current 
		/// directory to open
		/// </param>
		/// <param name="modeFlags">modes used to open a file for reading, writing, etc
		/// </param>
		/// <returns> the channelId of the file.
		/// </returns>
		/// <exception cref=""> TclException is thrown when the modeFlags try to open
		/// a file it does not have permission for or if the
		/// file dosent exist and CREAT wasnt specified.
		/// </exception>
		/// <exception cref=""> IOException is thrown when an IO error occurs that was not
		/// correctly tested for.  Most cases should be caught.
		/// </exception>
		
		internal  string open(Interp interp, string fileName, int modeFlags)
		{
			
			mode = modeFlags;
			System.IO.FileInfo fileObj = FileUtil.getNewFileObj(interp, fileName);
			FileMode fileMode = 0;
			FileAccess fileAccess = 0;
			
			if (((modeFlags & TclIO.CREAT) != 0) && ((modeFlags & TclIO.EXCL) != 0))
			{
				fileMode = FileMode.CreateNew;
			} else if ((modeFlags & TclIO.CREAT) != 0) {
				fileMode = FileMode.Create;
			} else {
				fileMode = FileMode.Open;
			}
			if ((modeFlags & TclIO.TRUNC) != 0) {
				fileMode = fileMode & FileMode.Truncate;
			}
			if ((modeFlags & TclIO.APPEND) != 0) {
				fileMode = fileMode & FileMode.Append;
			}
			
			if ((modeFlags & TclIO.RDWR) != 0)
			{
				fileAccess = FileAccess.ReadWrite;
			}
			else if ((modeFlags & TclIO.RDONLY) != 0)
			{
				fileAccess = FileAccess.Read;
			}
			else if ((modeFlags & TclIO.WRONLY) != 0)
			{
				fileAccess = FileAccess.Write;
			}
			else
			{
				throw new TclRuntimeError("FileChannel.java: invalid mode value");
			}
			
			file = new FileStream(fileObj.FullName,fileMode,fileAccess,FileShare.ReadWrite);
				
			string fName = TclIO.getNextDescriptor(interp, "file");
			ChanName = fName;
			//Console.Out.WriteLine("",file.Name);
			return fName;
		}
		
		/// <summary> Close the file.  The file MUST be open or a TclRuntimeError
		/// is thrown.
		/// </summary>
		
				internal override void close()
		{
			if (file == null)
			{
				throw new TclRuntimeError("FileChannel.close(): null file object");
			}
			
			// Invoke super.close() first since it might write an eof char
			try
			{
				base.close();
			}
			finally
			{
				// Console.Out.WriteLine("Debugg Closing {0}",file.Name);
				file.Close();
			}
		}
		
		/// <summary> Move the file pointer internal to the RandomAccessFile object. 
		/// The file MUST be open or a TclRuntimeError is thrown.
		/// 
		/// </summary>
		/// <param name="offset">The number of bytes to move the file pointer.
		/// </param>
		/// <param name="inmode">to begin incrementing the file pointer; beginning,
		/// current, or end of the file.
		/// </param>
		public override void  seek(Interp interp, long offset, int inmode)
		{
			
			if (file == null)
			{
				throw new TclRuntimeError("FileChannel.seek(): null file object");
			}
			
			//FIXME: Disallow seek on dead channels (raise TclPosixException ??)
			//if (CheckForDeadChannel(NULL, statePtr)) {
			//    return Tcl_LongAsWide(-1);
			//}
			
			// Compute how much input and output is buffered. If both input and
			// output is buffered, cannot compute the current position.
			
			int inputBuffered = NumBufferedInputBytes;
			int outputBuffered = NumBufferedOutputBytes;
			
			if ((inputBuffered != 0) && (outputBuffered != 0))
			{
				throw new TclPosixException(interp, TclPosixException.EFAULT, true, "error during seek on \"" + ChanName + "\"");
			}
			
			// If we are seeking relative to the current position, compute the
			// corrected offset taking into account the amount of unread input.
			
			if (inmode == TclIO.SEEK_CUR)
			{
				offset -= inputBuffered;
			}
			
			// The seekReset method will discard queued input and
			// reset flags like EOF and BLOCKED.
			
			if (input != null)
			{
				input.seekReset();
			}
			
			// FIXME: Next block is disabled since non-blocking is not implemented.
			// If the channel is in asynchronous output mode, switch it back
			// to synchronous mode and cancel any async flush that may be
			// scheduled. After the flush, the channel will be put back into
			// asynchronous output mode.
			
			bool wasAsync = false;
      //if (false && !Blocking)
      //{
      //  wasAsync = true;
      //  Blocking = true;
      //  if (BgFlushScheduled)
      //  {
      //    //scheduleBgFlush();
      //  }
      //}
			
			// If there is data buffered in curOut then mark the
			// channel as ready to flush before invoking flushChannel.
			
			if (output != null)
			{
				output.seekCheckBuferReady();
			}
			
			// If the flush fails we cannot recover the original position. In
			// that case the seek is not attempted because we do not know where
			// the access position is - instead we return the error. FlushChannel
			// has already called Tcl_SetErrno() to report the error upwards.
			// If the flush succeeds we do the seek also.
			
			if (output != null && output.flushChannel(null, false) != 0)
			{
				// FIXME: IS this the proper action to take on error?
				throw new System.IO.IOException("flush error while seeking");
			}
			else
			{
				// Now seek to the new position in the channel as requested by the
				// caller.
				
				long actual_offset;
				
				switch (inmode)
				{
					
					case TclIO.SEEK_SET:  {
							actual_offset = offset;
							break;
						}
					
					case TclIO.SEEK_CUR:  {
							actual_offset = file.Position + offset;
							break;
						}
					
					case TclIO.SEEK_END:  {
							actual_offset = file.Length + offset;
							break;
						}
					
					default:  {
							throw new TclRuntimeError("invalid seek mode");
						}
					
				}
				
				// A negative offset to seek() would raise an IOException, but
				// we want to raise an invalid argument error instead
				
				if (actual_offset < 0)
				{
					throw new TclPosixException(interp, TclPosixException.EINVAL, true, "error during seek on \"" + ChanName + "\"");
				}
				
				file.Seek(actual_offset, System.IO.SeekOrigin.Begin);
			}
			
			// Restore to nonblocking mode if that was the previous behavior.
			//
			// NOTE: Even if there was an async flush active we do not restore
			// it now because we already flushed all the queued output, above.
			
			if (wasAsync)
			{
				Blocking = false;
			}
		}
		
		/// <summary> Tcl_Tell -> tell
		/// 
		/// Return the current offset of the file pointer in number of bytes from 
		/// the beginning of the file.  The file MUST be open or a TclRuntimeError
		/// is thrown.
		/// 
		/// </summary>
		/// <returns> The current value of the file pointer.
		/// </returns>
		public override long tell()
		{
			if (file == null)
			{
				throw new TclRuntimeError("FileChannel.tell(): null file object");
			}
			int inputBuffered = NumBufferedInputBytes;
			int outputBuffered = NumBufferedOutputBytes;
			
			if ((inputBuffered != 0) && (outputBuffered != 0))
			{
				// FIXME: Posix error EFAULT ?
				return - 1;
			}
			long curPos = file.Position;
			if (curPos == - 1)
			{
				// FIXME: Set errno here?
				return - 1;
			}
			if (inputBuffered != 0)
			{
				return curPos - inputBuffered;
			}
			return curPos + outputBuffered;
		}
		
		/// <summary> If the file dosent exist then a TclExcpetion is thrown. 
		/// 
		/// </summary>
		/// <param name="interp">currrent interpreter.
		/// </param>
		/// <param name="fileObj">a java.io.File object of the file for this channel.
		/// </param>
		
		private void  checkFileExists(Interp interp, System.IO.FileInfo fileObj)
		{
			bool tmpBool;
			if (System.IO.File.Exists(fileObj.FullName))
				tmpBool = true;
			else
				tmpBool = System.IO.Directory.Exists(fileObj.FullName);
			if (!tmpBool)
			{
				throw new TclPosixException(interp, TclPosixException.ENOENT, true, "couldn't open \"" + fileObj.Name + "\"");
			}
		}
		
		
		/// <summary> Checks the read/write permissions on the File object.  If inmode is less
		/// than 0 it checks for read permissions, if mode greater than 0 it checks
		/// for write permissions, and if it equals 0 then it checks both.
		/// 
		/// </summary>
		/// <param name="interp">currrent interpreter.
		/// </param>
		/// <param name="fileObj">a java.io.File object of the file for this channel.
		/// </param>
		/// <param name="inmode">what permissions to check for.
		/// </param>
		
		private void  checkReadWritePerm(Interp interp, System.IO.FileInfo fileObj, int inmode)
		{
			bool error = false;
			
			if (inmode <= 0)
			{
				
				// HACK
//				if (!fileObj.canRead())
//				{
//					error = true;
//				}
			}
			if (inmode >= 0)
			{
				if (!SupportClass.FileCanWrite(fileObj))
				{
					error = true;
				}
			}
			if (error)
			{
				throw new TclPosixException(interp, TclPosixException.EACCES, true, "couldn't open \"" + fileObj.Name + "\"");
			}
		}
	}
}
