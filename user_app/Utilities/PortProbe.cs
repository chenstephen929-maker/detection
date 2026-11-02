using System.Net;
using System.Net.Sockets;

namespace AirGroundControlApp.Utilities;

public static class PortProbe
{
    public static bool IsTcpPortAvailable(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
