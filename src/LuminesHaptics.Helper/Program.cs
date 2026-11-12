using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using LuminesHaptics.Helper;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
var s = new Settings();
for (int i = 0; i < args.Length - 1; i++)
{
    string v = args[i + 1];
    switch (args[i])
    {
        case "--port": s.Port = int.Parse(v); i++; break;
        case "--parent": s.ParentPid = int.Parse(v); i++; break;
        case "--curve": s.Curve = float.Parse(v, CultureInfo.InvariantCulture); i++; break;
        case "--low-scale": s.LowScale = float.Parse(v, CultureInfo.InvariantCulture); i++; break;
        case "--high-scale": s.HighScale = float.Parse(v, CultureInfo.InvariantCulture); i++; break;
        case "--device": s.Device = v; i++; break;
        case "--play-file": s.PlayFile = v; i++; break;
    }
}

Log.Init(Path.Combine(AppContext.BaseDirectory, "LuminesHapticsHelper.log"));
Log.Write($"Helper start: port={s.Port} parent={s.ParentPid} curve={s.Curve} device='{s.Device}'");

var cts = new CancellationTokenSource();
var mixer = new Mixer(s);
var backend = new DualSenseBackend(s, mixer);
var outThread = new Thread(() => backend.Run(cts.Token)) { IsBackground = true, Name = "output", Priority = ThreadPriority.AboveNormal };
outThread.Start();

// Test mode: play one .bnvib (looped twice-ish) and exit. No game needed.
if (s.PlayFile != null)
{
    var b = Bnvib.Parse(File.ReadAllBytes(s.PlayFile));
    mixer.Load("test", b);
    Thread.Sleep(500); // let the device open
    mixer.Play("test", 1, false, 1f, 1f);
    Thread.Sleep((int)(b.Length * 1000L / b.Rate) + 300);
    cts.Cancel(); outThread.Join(2000);
    return;
}

// Exit with the game.
if (s.ParentPid != 0)
{
    try
    {
        var parent = Process.GetProcessById(s.ParentPid);
        new Thread(() => { parent.WaitForExit(); Log.Write("Game exited."); cts.Cancel(); Environment.Exit(0); }) { IsBackground = true }.Start();
    }
    catch (ArgumentException) { Log.Write("Parent not running; exiting."); return; }
}

UdpClient udp;
try { udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, s.Port)); }
catch (SocketException) { Log.Write($"Port {s.Port} busy (helper already running?); exiting."); return; }
udp.Client.ReceiveBufferSize = 1 << 20;

var remote = new IPEndPoint(IPAddress.Any, 0);
while (!cts.IsCancellationRequested)
{
    try
    {
        var msg = udp.Receive(ref remote);
        Net.Handle(msg, mixer);
        // Answer the plugin's heartbeat with the device status (see protocol in Core.cs).
        if (msg.Length > 0 && msg[0] == (byte)'G')
            udp.Send([(byte)'D', (byte)(backend.Open ? 1 : 0)], 2, remote);
    }
    catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset) { }
    catch (Exception e) { Log.Write("msg error: " + e.Message); }
}
