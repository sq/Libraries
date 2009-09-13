/*
* Included in SQLite3 port to C# for use in testharness only;  2008 Noah B Hart
* $Header$
* SocketChannel.java
*
* Implements a socket channel.
*/
using System;
namespace tcl.lang
{
	
	/// <summary> The SocketChannel class implements a channel object for Socket
	/// connections, created using the socket command.
	/// 
	/// </summary>
	
	public class SocketChannel:Channel
	{
		public override string ChanType
		{
			get
			{
				return "tcp";
			}
			
		}
		override protected internal System.IO.Stream InputStream
		{
			get
			{
				return (System.IO.Stream) sock.GetStream();
			}
			
		}
		override protected internal System.IO.Stream OutputStream
		{
			get
			{
				return (System.IO.Stream) sock.GetStream();
			}
			
		}
		
		/// <summary> The java Socket object associated with this Channel
		/// 
		/// </summary>
		
		private System.Net.Sockets.TcpClient sock;
		
		/// <summary> Constructor - creates a new SocketChannel object with the given
		/// options. Also creates an underlying Socket object, and Input and
		/// Output Streams.
		/// 
		/// </summary>
		
		public SocketChannel(Interp interp, int mode, string localAddr, int localPort, bool async, string address, int port)
		{
			System.Net.IPAddress localAddress = null;
			System.Net.IPAddress addr = null;
			
			if (async)
				throw new TclException(interp, "Asynchronous socket connection not " + "currently implemented");
			
			// Resolve addresses
			if (!localAddr.Equals(""))
			{
				try
				{
					localAddress = System.Net.Dns.GetHostByName(localAddr).AddressList[0];
				}
				catch (System.Exception e)
				{
					throw new TclException(interp, "host unknown: " + localAddr);
				}
			}
			
			try
			{
				addr = System.Net.Dns.GetHostByName(address).AddressList[0];
			}
			catch (System.Exception e)
			{
				throw new TclException(interp, "host unknown: " + address);
			}
			
			
			// Set the mode of this socket.
			this.mode = mode;
			
			// Create the Socket object
			
//			if ((localAddress != null) && (localPort != 0))
//			{
//				
//				sock = new Socket(addr, port, localAddress, localPort);
//			}
//			else
				sock = new System.Net.Sockets.TcpClient(addr.ToString(), port);
			
			// If we got this far, then the socket has been created.
			// Create the channel name
			ChanName = TclIO.getNextDescriptor(interp, "sock");
		}
		
		/// <summary> Constructor for making SocketChannel objects from connections
		/// made to a ServerSocket.
		/// 
		/// </summary>
		
		public SocketChannel(Interp interp, System.Net.Sockets.TcpClient s)
		{
			this.mode = TclIO.RDWR;
			this.sock = s;
			
			ChanName = TclIO.getNextDescriptor(interp, "sock");
		}
		
		/// <summary> Close the SocketChannel.</summary>
		
				internal override void  close()
		{
			// Invoke super.close() first since it might write an eof char
			try
			{
				base.close();
			}
			finally
			{
				sock.Close();
			}
		}
	}
}
