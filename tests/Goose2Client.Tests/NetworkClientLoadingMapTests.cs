using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Goose2Client.Network;
using Xunit;

namespace Goose2Client.Network.Tests;

public class NetworkClientLoadingMapTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly NetworkClient _client = new();
    private readonly Socket _server;

    public NetworkClientLoadingMapTests()
    {
        _listener.Start();
        _client.Connect("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port);
        _server = _listener.AcceptSocket();
        _server.ReceiveTimeout = 2000;
    }

    public void Dispose()
    {
        _client.Disconnect();
        _server.Dispose();
        _listener.Stop();
    }

    private string ReceiveUntil(string terminator)
    {
        var received = new StringBuilder();
        var buffer = new byte[1024];
        while (!received.ToString().EndsWith(terminator, StringComparison.Ordinal))
        {
            int n = _server.Receive(buffer);
            if (n == 0) break;
            received.Append(Encoding.ASCII.GetString(buffer, 0, n));
        }
        return received.ToString();
    }

    [Fact]
    public void Send_WhileLoadingMap_OnlyDoneLoadingMapReachesServer()
    {
        _client.LoadingMap = true;

        _client.Move(Direction.Up);
        _client.CastSpell(1, 2);
        _client.ChatMessage("hi");
        _client.DoneLoadingMap();

        Assert.Equal("DLM\u0001", ReceiveUntil("DLM\u0001"));
    }

    [Fact]
    public void Send_AfterLoadingMapCleared_SendsGameplayPackets()
    {
        _client.LoadingMap = true;
        _client.Move(Direction.Up);
        _client.LoadingMap = false;

        _client.Move(Direction.Up);
        _client.DoneLoadingMap();

        Assert.Equal("M1\u0001DLM\u0001", ReceiveUntil("DLM\u0001"));
    }

    [Fact]
    public void Disconnect_ClearsLoadingMap()
    {
        _client.LoadingMap = true;

        _client.Disconnect();

        Assert.False(_client.LoadingMap);
    }
}
