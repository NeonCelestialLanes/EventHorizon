using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Persistence;

namespace EventHorizon;

internal sealed class PlannerSync : IDisposable
{
    public const int DiscoveryPort = 47820;

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly JsonSerializerOptions Line = new(JsonIo.Options) { WriteIndented = false };

    private readonly HorizonStore store;
    private readonly string origin = Guid.NewGuid().ToString("N");
    private readonly object gate = new();
    private readonly ConcurrentQueue<string> inbox = new();
    private readonly List<Peer> peers = [];

    private CancellationTokenSource? life;
    private TcpListener? listener;
    private long revision;
    private bool applying;
    private bool pending;
    private DateTime nextSendUtc;
    private string? sharedEventId;

    public PlannerSync(HorizonStore store)
    {
        this.store = store;
        this.store.Changed += this.OnChanged;
    }

    public string JoinCode { get; set; } = "";

    public bool IsHost { get; private set; }

    public bool Sharing { get; private set; }

    public string? Code { get; private set; }

    public string? Direct { get; private set; }

    public string Status { get; private set; } = "Not sharing.";

    public string? Error { get; private set; }

    public int Others { get; private set; }

    public void StartHost()
    {
        var ev = this.store.SelectedEvent();
        if (ev is null)
        {
            this.Error = "Select an event before you create a sync code.";
            return;
        }

        this.Stop();
        var code = NewCode();
        if (!TryListen(out var listen, out var port, out var problem))
        {
            this.Error = problem;
            return;
        }

        var tokenSource = new CancellationTokenSource();
        this.life = tokenSource;
        this.listener = listen;
        this.IsHost = true;
        this.Sharing = true;
        this.Code = code;
        this.sharedEventId = ev.EventId;
        this.revision = 0;
        this.Error = null;
        var address = LocalAddress();
        this.Direct = address is null ? null : code + "@" + address + ":" + port;
        this.Status = "Code " + Pretty(code) + ". Waiting for another planner.";
        var token = tokenSource.Token;
        _ = Task.Run(() => this.AcceptLoop(listen, token));
        _ = Task.Run(() => this.BeaconLoop(code, port, token));
        this.pending = true;
        this.nextSendUtc = DateTime.UtcNow;
    }

    public void Join(string raw)
    {
        if (!TryReadCode(raw, out var code, out var host, out var port, out var problem))
        {
            this.Error = problem;
            return;
        }

        this.Stop();
        var tokenSource = new CancellationTokenSource();
        this.life = tokenSource;
        this.IsHost = false;
        this.Sharing = true;
        this.Code = code;
        this.Error = null;
        this.Status = host is null
            ? "Looking for " + Pretty(code) + " on your network."
            : "Connecting to " + host + ".";
        var token = tokenSource.Token;
        _ = Task.Run(() => this.SeekLoop(code, host, port, token));
    }

    public void Stop()
    {
        this.Sharing = false;
        this.IsHost = false;
        this.Code = null;
        this.Direct = null;
        this.Others = 0;
        this.sharedEventId = null;
        this.pending = false;
        this.Status = "Not sharing.";
        try
        {
            this.life?.Cancel();
        }
        catch (Exception)
        {
            // Already stopped.
        }

        try
        {
            this.listener?.Stop();
        }
        catch (Exception)
        {
            // The listener was already closed.
        }

        this.listener = null;
        List<Peer> copy;
        lock (this.gate)
        {
            copy = this.peers.ToList();
            this.peers.Clear();
        }

        foreach (var peer in copy)
            peer.Close();
        this.life?.Dispose();
        this.life = null;
    }

    public void Pump()
    {
        while (this.inbox.TryDequeue(out var line))
            this.ApplyLine(line);

        if (!this.pending || !this.Sharing || DateTime.UtcNow < this.nextSendUtc)
            return;

        this.pending = false;
        this.nextSendUtc = DateTime.UtcNow.AddMilliseconds(200);
        this.revision++;
        this.Broadcast(this.revision);
    }

    public void Dispose()
    {
        this.store.Changed -= this.OnChanged;
        this.Stop();
    }

    public static bool TryReadCode(string? raw, out string code, out string? host, out int port, out string error)
    {
        code = "";
        host = null;
        port = 0;
        error = "";
        var text = (raw ?? "").Trim().Replace(" ", "").Replace("-", "");
        if (text.Length == 0)
        {
            error = "Enter the sync code.";
            return false;
        }

        var at = text.IndexOf('@');
        var body = at < 0 ? text : text[..at];
        code = body.ToUpperInvariant();
        if (code.Length != 6 || code.Any(character => !Alphabet.Contains(character)))
        {
            error = "That sync code does not look right. It is six letters and numbers, like AB3-K7M.";
            return false;
        }

        if (at < 0)
            return true;

        var tail = text[(at + 1)..];
        var colon = tail.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(tail[(colon + 1)..], out port) || port is < 1 or > 65535)
        {
            error = "That address needs a host and a port, like AB3K7M@192.168.1.20:47821.";
            return false;
        }

        host = tail[..colon];
        return host.Length > 0;
    }

    private void OnChanged(string eventId)
    {
        if (this.applying || !this.Sharing || eventId != this.sharedEventId)
            return;

        this.pending = true;
    }

    private void ApplyLine(string line)
    {
        SyncMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<SyncMessage>(line, Line);
        }
        catch (JsonException)
        {
            return;
        }

        if (message is null || message.Kind != "snapshot" || message.Origin == this.origin)
            return;
        if (!string.Equals(message.Code, this.Code, StringComparison.OrdinalIgnoreCase))
            return;
        if (string.IsNullOrWhiteSpace(message.EventJson) || string.IsNullOrWhiteSpace(message.StateJson))
            return;

        EventDocument? ev;
        LiveEventState? state;
        try
        {
            ev = JsonIo.ReadText<EventDocument>(message.EventJson);
            state = JsonIo.ReadText<LiveEventState>(message.StateJson);
        }
        catch (JsonException)
        {
            this.Error = "A sync update could not be read.";
            return;
        }

        if (ev is null || state is null)
            return;

        var remoteNewer = message.Revision >= this.revision;
        var chosenEvent = ev;
        var chosenState = state;
        var sendBack = false;
        if (this.store.Events.ContainsKey(ev.EventId))
        {
            var localEvent = this.store.Events[ev.EventId];
            var localState = this.store.StateFor(ev.EventId);
            chosenEvent = remoteNewer ? ev : localEvent;
            chosenState = LiveMerge.Apply(localState, state, remoteNewer);
            sendBack = LiveMerge.Differs(chosenState, state);
        }

        this.applying = true;
        try
        {
            this.store.ApplySync(chosenEvent, chosenState);
            this.sharedEventId = chosenEvent.EventId;
            if (message.Revision > this.revision)
                this.revision = message.Revision;
            if (sendBack)
                this.pending = true;
            this.Error = null;
            this.Status = "Live with the other planners. " + chosenEvent.EventName + " is updating as they edit.";
        }
        catch (Exception)
        {
            this.Error = "A sync update could not be saved.";
        }
        finally
        {
            this.applying = false;
        }

        if (this.IsHost)
            this.SendRaw(line, message.Origin);
    }

    private void Broadcast(long revision)
    {
        var line = this.BuildSnapshot(revision);
        if (line is null)
            return;

        this.SendRaw(line, exceptOrigin: null);
        this.RefreshHostStatus();
    }

    private string? BuildSnapshot(long revision)
    {
        if (this.sharedEventId is null || !this.store.Events.TryGetValue(this.sharedEventId, out var ev))
            return null;

        var state = this.store.StateFor(ev.EventId);
        var message = new SyncMessage
        {
            Kind = "snapshot",
            Code = this.Code ?? "",
            Origin = this.origin,
            Revision = revision,
            EventJson = JsonSerializer.Serialize(ev, Line),
            StateJson = JsonSerializer.Serialize(state, Line),
        };
        return JsonSerializer.Serialize(message, Line);
    }

    private void SendRaw(string line, string? exceptOrigin)
    {
        List<Peer> copy;
        lock (this.gate)
            copy = this.peers.ToList();

        foreach (var peer in copy)
        {
            if (exceptOrigin is not null && peer.Origin == exceptOrigin)
                continue;
            peer.Send(line);
        }
    }

    private async Task AcceptLoop(TcpListener listen, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listen.AcceptTcpClientAsync(token);
                _ = Task.Run(() => this.Welcome(client), token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
        catch (ObjectDisposedException)
        {
            // Stopping.
        }
        catch (Exception)
        {
            this.Error = "Sharing stopped because the connection closed.";
        }
    }

    private void Welcome(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            var helloLine = reader.ReadLine();
            var hello = helloLine is null ? null : JsonSerializer.Deserialize<SyncMessage>(helloLine, Line);
            if (hello is null || hello.Kind != "hello" || !string.Equals(hello.Code, this.Code, StringComparison.OrdinalIgnoreCase))
            {
                client.Close();
                return;
            }

            var peer = new Peer(stream, hello.Origin);
            lock (this.gate)
            {
                this.peers.Add(peer);
                this.Others = this.peers.Count;
            }

            this.pending = true;
            this.nextSendUtc = DateTime.UtcNow;
            this.RefreshHostStatus();
            string? line;
            while ((line = reader.ReadLine()) is not null)
                this.inbox.Enqueue(line);

            lock (this.gate)
            {
                this.peers.Remove(peer);
                this.Others = this.peers.Count;
            }

            this.RefreshHostStatus();
        }
        catch (Exception)
        {
            // The other planner disconnected.
        }
        finally
        {
            client.Close();
        }
    }

    private async Task SeekLoop(string code, string? host, int port, CancellationToken token)
    {
        try
        {
            if (host is null)
            {
                this.Status = "Looking for " + Pretty(code) + " on your network.";
                var found = await ListenForBeacon(code, token);
                if (found is null || token.IsCancellationRequested)
                {
                    if (this.Sharing)
                        this.Error = "No planner with that code is on this network. If they are somewhere else, paste the longer address they copied.";
                    return;
                }

                host = found.Address.ToString();
                port = found.Port;
            }

            if (token.IsCancellationRequested)
                return;

            this.Status = "Connecting.";
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, token);
            client.NoDelay = true;
            await using var stream = client.GetStream();
            var peer = new Peer(stream, "");
            var hello = JsonSerializer.Serialize(new SyncMessage { Kind = "hello", Code = code, Origin = this.origin }, Line);
            peer.Send(hello);
            lock (this.gate)
            {
                this.peers.Add(peer);
                this.Others = 1;
            }

            this.Status = "Connected. Waiting for the event.";
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            string? line;
            while (!token.IsCancellationRequested && (line = reader.ReadLine()) is not null)
                this.inbox.Enqueue(line);
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        catch (Exception)
        {
            if (this.Sharing)
                this.Error = "Could not connect. Check the code, and allow Event Horizon on private networks if Windows asks.";
        }
        finally
        {
            lock (this.gate)
            {
                this.peers.Clear();
                this.Others = 0;
            }

            if (this.Sharing && !this.IsHost)
                this.Status = "Disconnected.";
        }
    }

    private static async Task<IPEndPoint?> ListenForBeacon(string code, CancellationToken token)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!token.IsCancellationRequested && DateTime.UtcNow < until)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                var result = await udp.ReceiveAsync(wait.Token);
                var text = Encoding.ASCII.GetString(result.Buffer);
                var parts = text.Split(' ');
                if (parts.Length == 3 && parts[0] == "EH1" && string.Equals(parts[1], code, StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[2], out var port))
                    return new IPEndPoint(((IPEndPoint)result.RemoteEndPoint).Address, port);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Keep listening until the search window ends.
            }
        }

        return null;
    }

    private async Task BeaconLoop(string code, int port, CancellationToken token)
    {
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            var payload = Encoding.ASCII.GetBytes("EH1 " + code + " " + port);
            while (!token.IsCancellationRequested)
            {
                await udp.SendAsync(payload, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort), token);
                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
        catch (Exception)
        {
            this.Error = "This computer could not announce the sync code on the network.";
        }
    }

    private void RefreshHostStatus()
    {
        if (!this.IsHost || this.Code is null)
            return;

        var count = this.Others;
        this.Status = count == 0
            ? "Code " + Pretty(this.Code) + ". Waiting for another planner."
            : "Code " + Pretty(this.Code) + ". Live with " + count + (count == 1 ? " planner." : " planners.");
    }

    private static bool TryListen(out TcpListener listener, out int port, out string error)
    {
        listener = null!;
        port = 0;
        error = "";
        for (var candidate = 47821; candidate < 47841; candidate++)
        {
            try
            {
                var listen = new TcpListener(IPAddress.Any, candidate);
                listen.Start();
                listener = listen;
                port = candidate;
                return true;
            }
            catch (SocketException)
            {
                // Try the next port.
            }
        }

        error = "Could not open a port for sharing. Another program may be using it.";
        return false;
    }

    private static string NewCode()
    {
        var bytes = new byte[6];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var characters = new char[6];
        for (var index = 0; index < characters.Length; index++)
            characters[index] = Alphabet[bytes[index] % Alphabet.Length];
        return new string(characters);
    }

    private static string Pretty(string code) => code.Length == 6 ? code[..3] + "-" + code[3..] : code;

    private static string? LocalAddress()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                    return address.Address.ToString();
            }
        }

        return null;
    }

    private sealed class Peer(Stream stream, string origin)
    {
        private readonly object gate = new();

        public string Origin { get; } = origin;

        public void Send(string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            lock (this.gate)
            {
                try
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                catch (Exception)
                {
                    // The other planner is gone.
                }
            }
        }

        public void Close()
        {
            try
            {
                stream.Close();
            }
            catch (Exception)
            {
                // Already closed.
            }
        }
    }

    private sealed class SyncMessage
    {
        public string Kind { get; set; } = "";

        public string Code { get; set; } = "";

        public string Origin { get; set; } = "";

        public long Revision { get; set; }

        public string? EventJson { get; set; }

        public string? StateJson { get; set; }
    }
}
