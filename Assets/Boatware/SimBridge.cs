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
    /// <summary>Signed helm, -1..1. Negative yaws the bow to starboard.</summary>
    public float Helm;

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
        OpenWater();
        ShowSails();
        StartServer();
    }

    /// <summary>
    /// The imported sail meshes are not rigged to the mast. Draw full sheets on
    /// the yacht's mast, boom and forestay. The physics colliders only cover
    /// about half the hoist, which reads as a reef; 12 kn is full sail.
    /// 60% transparent. Parenting to the sail body lets the sheet swing.
    /// </summary>
    void ShowSails()
    {
        var material = Resources.Load<Material>("Sail");
        if (material == null)
        {
            var shader = Shader.Find("Boatware/Sail");
            if (shader == null) return;
            material = new Material(shader);
            material.color = new Color(0.96f, 0.95f, 0.90f, 0.4f);
        }
        var rig = GameObject.Find("YachtBodySkin");
        if (rig == null) return;
        // Points measured on Yacht.skp, in that model's space. The masthead is
        // the top of the mast mesh; the boom runs aft from the gooseneck.
        var masthead = rig.transform.TransformPoint(new Vector3(-5.26f, 21.20f, 8.20f));
        var gooseneck = rig.transform.TransformPoint(new Vector3(-5.26f, 2.59f, 8.20f));
        var boomEnd = rig.transform.TransformPoint(new Vector3(-10.48f, 2.59f, 8.20f));
        var bow = rig.transform.TransformPoint(new Vector3(0.77f, 1.73f, 8.19f));
        var jibClew = rig.transform.TransformPoint(new Vector3(-1.80f, 2.40f, 8.20f));
        DrawSail("MainSail", material, masthead, gooseneck, boomEnd);
        DrawSail("HeadSail", material, masthead, bow, jibClew);
    }

    static void DrawSail(string sailName, Material material, Vector3 head, Vector3 tack, Vector3 clew)
    {
        var sail = GameObject.Find(sailName);
        if (sail == null) return;
        foreach (var skin in sail.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            skin.enabled = false;
        foreach (var cloth in sail.GetComponentsInChildren<Cloth>(true))
            cloth.enabled = false;
        foreach (var existing in sail.GetComponentsInChildren<MeshRenderer>(true))
            existing.enabled = false;

        var visual = new GameObject("SailCloth");
        visual.transform.SetParent(sail.transform, false);
        var filter = visual.AddComponent<MeshFilter>();
        filter.sharedMesh = SailSheet(
            sail.transform.InverseTransformPoint(head),
            sail.transform.InverseTransformPoint(tack),
            sail.transform.InverseTransformPoint(clew));
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Mesh SailSheet(Vector3 head, Vector3 tack, Vector3 clew)
    {
        var mesh = new Mesh { name = "SailSheet" };
        mesh.vertices = new[] { head, tack, clew };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// The demo scene has a terrain beside the start. boatware sails on open water.
    /// </summary>
    void OpenWater()
    {
        foreach (var terrain in FindObjectsOfType<Terrain>())
            terrain.gameObject.SetActive(false);
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
        if (Mode != SimMode.Scripted || yacht == null || Mathf.Abs(Helm) < 0.02f) return;
        ApplyHelm(Helm);
    }

    /// <summary>
    /// BoatForces.rotateRudder adds ±1000 N per m/s of boat speed on every call.
    /// Calling that on each physics step is a hard-over tiller: the boat yaws
    /// through the course and broaches. Apply a fraction of that force, scaled
    /// by the commanded helm, and keep a little steerage when the boat is stopped.
    /// </summary>
    void ApplyHelm(float helm)
    {
        var rb = yacht.yachtRigidbody != null ? yacht.yachtRigidbody : yacht.GetComponent<Rigidbody>();
        if (rb == null || yacht.rudder == null) return;
        float speed = rb.velocity.magnitude;
        // Half of BoatForces' 1000 N per m/s, and sail force is scaled to 0.4,
        // so the tiller can still hold a course without the old hard-over broach.
        float newtons = Mathf.Clamp(helm, -1f, 1f) * (120f + 800f * speed);
        rb.AddForceAtPosition(yacht.transform.right * newtons, yacht.rudder.transform.position, ForceMode.Force);
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
                Helm = Mathf.Clamp(cmd.helm, -1f, 1f);
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
            // A length near zero makes sin(x/length) a vertical chop and the
            // float cubes throw the hull. Ignore unparsed or tiny lengths.
            WaweManager.instance.amplitude = Mathf.Clamp(cmd.wave_amplitude_m, 0f, 0.45f);
            WaweManager.instance.lenght = Mathf.Max(6f, cmd.wave_length_m);
            WaweManager.instance.speed = Mathf.Clamp(cmd.wave_speed, 0f, 2f);
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
    public float helm;
    public string mode;
    public float time_scale;
    public float heading_deg;
    public float speed_mps;
    public SimWaypoint[] waypoints;
}
