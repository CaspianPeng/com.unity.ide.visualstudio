﻿using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.VisualStudio.Editor.Messaging
{
	internal class UdpSocket : Socket
	{
		public const int BufferSize = 1024 * 8;

		internal UdpSocket()
			: base(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
		{
			SetIOControl();
		}

		public void Bind(IPAddress address, int port = 0)
		{
			Bind(new IPEndPoint(address ?? IPAddress.Any, port));
		}

		private void SetIOControl()
		{
			if (!VisualStudioEditor.IsWindows)
				return;

			try
			{
				const int SIO_UDP_CONNRESET = -1744830452;

				IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, new byte[0]);
			}
			catch
			{
			}
		}

		public static byte[] BufferFor(IAsyncResult result)
		{
			return (byte[])result.AsyncState;
		}

		public static EndPoint Any()
		{
			return new IPEndPoint(IPAddress.Any, 0);
		}
	}
}
