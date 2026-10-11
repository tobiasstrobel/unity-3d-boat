using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public enum SimMode
{
    Interactive,
    Scripted
}

/// <summary>
/// Local NDJSON API on 127.0.0.1:8767.
/// boatware.sim sets wind, waves, helm, and pose; this component reports telemetry.
/// Keyboard sailing in YachtsManager stays available in interactive mode.
/// </summary>
public class SimBridge : MonoBehaviour
{
    public static SimBridge Instance { get; private set; }

    public SimMode Mode = SimMode.Interactive;
    public int Helm;

    const int Port = 8767;

    TcpListener listener;
    TcpClient client;
    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    readonly object sendLock = new object();
    volatile bool running;

    BoatForces yacht;
    Vector3 anchor;
    bool anchorReady;
    float runningScale = 1f;
    bool paused;
    float originLat = 54.32f;
    float originLon = 10.15f;
    GameObject buoyRoot;
    float telemetryTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindObjectOfType<SimBridge>() != null) return;
        var go = new GameObject("BoatwareSimBridge");
        go.AddComponent<SimBridge>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        buoyRoot = new GameObject("BoatwareBuoys");
        DontDestroyOnLoad(buoyRoot);
        StartServer();
    }

    void OnDestroy()
    {
        running = false;
        if (Instance == this) Instance = null;
        try { client?.Close(); } catch { /* already closed */ }
        try { listener?.Stop(); } catch { /* already stopped */ }
    }

    void StartServer()
    {
        listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Start();
        running = true;
        var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "boatware-sim-api" };
        thread.Start();
        Debug.Log("boatware SimBridge listening on 127.0.0.1:" + Port);
    }

    void AcceptLoop()
    {
        while (running)
        {
            TcpClient next;
            try
            {
                next = listener.AcceptTcpClient();
            }
            catch
            {
                if (!running) return;
                continue;
            }
            lock (sendLock)
            {
                try { client?.Close(); } catch { /* replaced */ }
                client = next;
            }
            var readerThread = new Thread(() => ReadLoop(next)) { IsBackground = true };
            readerThread.Start();
        }
    }

    void ReadLoop(TcpClient socket)
    {
        try
        {
            var stream = socket.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);
            while (running && socket.Connected)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                if (line.Length > 0) incoming.Enqueue(line);
            }
        }
        catch
        {
            /* client disconnected */
        }
    }

    void Update()
    {
        ResolveYacht();
        string line;
        while (incoming.TryDequeue(out line))
        {
            HandleCommand(line);
        }

        telemetryTimer += Time.unscaledDeltaTime;
        if (telemetryTimer >= 0.1f)
        {
            telemetryTimer = 0f;
            SendTelemetry();
        }
    }

    void FixedUpdate()
    {
        if (Mode != SimMode.Scripted || Helm == 0 || yacht == null) return;
        yacht.rotateRudder(Helm);
    }

    void ResolveYacht()
    {
        if (yacht != null) return;
        var all = FindObjectsOfType<BoatForces>();
        foreach (var candidate in all)
        {
            if (candidate.CompareTag("SecondYacht")) continue;
            yacht = candidate;
            break;
        }
        if (yacht == null && all.Length > 0) yacht = all[0];
        if (yacht != null && !anchorReady)
        {
            anchor = yacht.transform.position;
            anchorReady = true;
        }
    }

    void HandleCommand(string line)
    {
        SimCommand cmd;
        try
        {
            cmd = JsonUtility.FromJson<SimCommand>(line);
        }
        catch
        {
            return;
        }
        if (cmd == null || string.IsNullOrEmpty(cmd.cmd)) return;

        switch (cmd.cmd)
        {
            case "set_environment":
                ApplyEnvironment(cmd);
                break;
            case "set_origin":
                originLat = cmd.lat;
                originLon = cmd.lon;
                break;
            case "set_waypoints":
                PlaceBuoys(cmd.waypoints);
                break;
            case "set_helm":
                Helm = cmd.helm > 0 ? 1 : cmd.helm < 0 ? -1 : 0;
                break;
            case "set_mode":
                Mode = cmd.mode == "scripted" ? SimMode.Scripted : SimMode.Interactive;
                if (Mode == SimMode.Interactive) Helm = 0;
                break;
            case "reset":
                ResetBoat(cmd.heading_deg, cmd.speed_mps);
                break;
            case "pause":
                paused = true;
                Time.timeScale = 0f;
                break;
            case "resume":
                paused = false;
                Time.timeScale = runningScale;
                break;
            case "set_time_scale":
                runningScale = Mathf.Clamp(cmd.time_scale <= 0f ? 1f : cmd.time_scale, 0.1f, 20f);
                if (!paused) Time.timeScale = runningScale;
                break;
        }
    }

    void ApplyEnvironment(SimCommand cmd)
    {
        var boats = FindObjectsOfType<BoatForces>();
        foreach (var boat in boats)
        {
            boat.windSpeed = Mathf.Max(0f, cmd.wind_speed_mps);
            if (boat.windObj != null)
            {
                float blowToward = Mathf.Repeat(cmd.wind_from_deg + 180f, 360f);
                boat.windObj.transform.rotation = Quaternion.Euler(0f, blowToward, 0f);
            }
        }
        if (WaweManager.instance != null)
        {
            WaweManager.instance.amplitude = Mathf.Max(0f, cmd.wave_amplitude_m);
            WaweManager.instance.lenght = Mathf.Max(0.1f, cmd.wave_length_m);
            WaweManager.instance.speed = cmd.wave_speed;
        }
    }

    void PlaceBuoys(SimWaypoint[] waypoints)
    {
        for (var i = buoyRoot.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(buoyRoot.transform.GetChild(i).gameObject);
        }
        if (waypoints == null || !anchorReady) return;
        foreach (var wp in waypoints)
        {
            var buoy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var collider = buoy.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            buoy.name = string.IsNullOrEmpty(wp.name) ? "Waypoint" : wp.name;
            buoy.transform.SetParent(buoyRoot.transform, true);
            buoy.transform.localScale = Vector3.one * 8f;
            buoy.transform.position = anchor + new Vector3(wp.east_m, 2f, wp.north_m);
        }
    }

    void ResetBoat(float headingDeg, float speedMps)
    {
        ResolveYacht();
        if (yacht == null) return;
        if (!anchorReady)
        {
            anchor = yacht.transform.position;
            anchorReady = true;
        }
        var rb = yacht.yachtRigidbody != null ? yacht.yachtRigidbody : yacht.GetComponent<Rigidbody>();
        yacht.transform.position = anchor;
        yacht.transform.rotation = Quaternion.Euler(0f, headingDeg, 0f);
        if (rb != null)
        {
            float rad = headingDeg * Mathf.Deg2Rad;
            rb.velocity = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * Mathf.Max(0f, speedMps);
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
        }
        Helm = 0;
    }

    void SendTelemetry()
    {
        if (yacht == null) return;
        var rb = yacht.yachtRigidbody != null ? yacht.yachtRigidbody : yacht.GetComponent<Rigidbody>();
        Vector3 velocity = rb != null ? rb.velocity : Vector3.zero;
        float heading = yacht.transform.eulerAngles.y;
        float heel = Mathf.Asin(Mathf.Clamp(yacht.transform.right.y, -1f, 1f)) * Mathf.Rad2Deg;
        float east = yacht.transform.position.x - anchor.x;
        float north = yacht.transform.position.z - anchor.z;
        float windFrom = 0f;
        float windSpeed = yacht.windSpeed;
        if (yacht.windObj != null)
        {
            windFrom = Mathf.Repeat(yacht.windObj.transform.eulerAngles.y + 180f, 360f);
        }
        string mode = Mode == SimMode.Scripted ? "scripted" : "interactive";
        var culture = CultureInfo.InvariantCulture;
        string json = string.Format(
            culture,
            "{{\"type\":\"telemetry\",\"east_m\":{0:0.####},\"north_m\":{1:0.####},\"heading_deg\":{2:0.####},\"heel_deg\":{3:0.####},\"vel_east_mps\":{4:0.####},\"vel_north_mps\":{5:0.####},\"wind_from_deg\":{6:0.####},\"wind_speed_mps\":{7:0.####},\"helm\":{8},\"mode\":\"{9}\",\"paused\":{10},\"origin_lat\":{11:0.######},\"origin_lon\":{12:0.######}}}",
            east,
            north,
            heading,
            heel,
            velocity.x,
            velocity.z,
            windFrom,
            windSpeed,
            Helm,
            mode,
            paused ? "true" : "false",
            originLat,
            originLon);
        SendLine(json);
    }

    void SendLine(string json)
    {
        lock (sendLock)
        {
            if (client == null || !client.Connected) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json + "\n");
                client.GetStream().Write(bytes, 0, bytes.Length);
            }
            catch
            {
                try { client.Close(); } catch { /* ignore */ }
                client = null;
            }
        }
    }
}

[Serializable]
public class SimWaypoint
{
    public string name;
    public float east_m;
    public float north_m;
}

[Serializable]
public class SimCommand
{
    public string cmd;
    public float wind_speed_mps;
    public float wind_from_deg;
    public float wave_amplitude_m;
    public float wave_length_m;
    public float wave_speed;
    public float lat;
    public float lon;
    public int helm;
    public string mode;
    public float time_scale;
    public float heading_deg;
    public float speed_mps;
    public SimWaypoint[] waypoints;
}
