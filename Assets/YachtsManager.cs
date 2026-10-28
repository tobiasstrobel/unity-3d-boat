using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class YachtsManager : MonoBehaviour
{
    public BoatForces yachtPrefab;
    public GameObject windObj;
    public GameObject waterArround;
    public IYachtControls yachtControls;
    public BoatForces secondYacht;

    public BoatForces anotherYacht;
    public GameObject mainYacht;
    public GameObject[] target;

    private int waypoint = 0;

    private int minimalDistance = 5;

    Transform farWater;
    bool waveDiskReady;
    float waveRadius;

    const float FarWaterScale = 300f;
    const float TileSize = 10f;
    const float CircleBoatLengths = 5f;
    /// <summary>World height the water must stay under so it does not cut the deck.</summary>
    public static float DeckClearY = -0.5f;
    
    void Start() {
        // The demo clones a second yacht at the start. boatware drives one hull.
        if (SimBridge.Instance != null) return;
        secondYacht = cloneBoat(yachtPrefab, "SecondYacht");
    }

    void Update() {
        bool boatware = SimBridge.Instance != null;
        bool scripted = boatware && SimBridge.Instance.Mode == SimMode.Scripted;
        if (!scripted)
        {
            // boatware's interactive mode is the keyboard. The scene waypoints
            // would hold the tiller and round the boat up into the wind.
            if (!boatware) followTarget();

            if (Input.GetKey(KeyCode.A)){
                yachtControls.rotateRudder(1);
            }

            if (Input.GetKey(KeyCode.D)) {
                yachtControls.rotateRudder(-1);
            }

            if (Input.GetKey(KeyCode.Q)) {
                yachtControls.rotateHeadSail(1);
            }

            if (Input.GetKey(KeyCode.E)) {
                yachtControls.rotateHeadSail(-1);
            }

            if (Input.GetKey(KeyCode.Z)) {
                yachtControls.rotateMainSail(1);
            }

            if (Input.GetKey(KeyCode.C)) {
                yachtControls.rotateMainSail(-1);
            }
        }

        moveWaterAreaArroundShip();
    }

    BoatForces cloneBoat(BoatForces sourceBoat, string name){
        GameObject.FindGameObjectWithTag("MainCamera").GetComponent<Camera>().enabled = false;
        BoatForces newBoat = Instantiate(sourceBoat, new Vector3(0, 0, 10), Quaternion.identity);        
        GameObject.FindGameObjectWithTag("MainCamera").GetComponent<Camera>().enabled = true;
        newBoat.windObj = windObj;
        newBoat.tag = name;
        return newBoat; 
    }

    private void followTarget() {
        if (yachtPrefab == null || yachtControls == null || target == null || target.Length == 0) return;

        float angle = calcAngleToWaypoint();
        if(angle > 10){
            yachtControls.rotateRudder(1);
        } else if(angle < -10){
            yachtControls.rotateRudder(-1);
        }        
    }

    private int calcAngleToWaypoint() { 
        Vector3 targetDir = targetDirection();
        int angle = (int)Vector3.SignedAngle(targetDir, yachtPrefab.transform.forward, Vector3.up);
        //Debug.Log("angle = " + angle);
        return angle;
    }

    private Vector3 targetDirection() {
        float distance = Vector3.Distance (target[waypoint].transform.position, yachtPrefab.transform.position);
        if(distance < minimalDistance ){
            goNextWaypoint();
        }
        return target[waypoint].transform.position - yachtPrefab.transform.position;
    }

    private void goNextWaypoint(){
        if(waypoint < target.Length-1){
            waypoint++;
        } else {
            waypoint = 0;
        }
    }

    private void moveWaterAreaArroundShip() {
        if(waterArround == null || yachtPrefab == null) return;
        if (!waveDiskReady) BuildWaveDisk();
        Vector3 boat = yachtPrefab.transform.position;
        waterArround.transform.position = new Vector3(boat.x, waterArround.transform.position.y, boat.z);
        Shader.SetGlobalVector("_WaveCenter", new Vector4(boat.x, 0f, boat.z, 0f));
        Shader.SetGlobalFloat("_WaveRadius", waveRadius);
        // Floaters sit on the wave. The deck is above them; the distant plane
        // used to stay at a fixed height and sliced the stern in a trough.
        float waterline = yachtPrefab.transform.TransformPoint(0f, 0.1f, 0f).y;
        DeckClearY = waterline - 0.35f;
        // The waved tiles only cover the boat. The distant plane used to stay
        // at the origin, so after a few minutes its edge crossed the horizon.
        if (farWater == null)
        {
            var far = GameObject.Find("WaterPlane");
            if (far == null) return;
            farWater = far.transform;
            farWater.localScale = new Vector3(FarWaterScale, 1f, FarWaterScale);
        }
        farWater.position = new Vector3(boat.x, waterline - 0.55f, boat.z);
    }

    // The scene tiles are a ragged patch. Replace them with a disk of the
    // built-in 10 m planes, centered on the boat origin. The shader cuts the
    // square corners so the colored water is a circle of five boat lengths.
    void BuildWaveDisk()
    {
        Transform root = waterArround.transform;
        if (root.childCount == 0) return;
        float length = BoatLength(yachtPrefab.transform);
        waveRadius = length * CircleBoatLengths;
        GameObject template = root.GetChild(0).gameObject;
        float height = template.transform.localPosition.y;
        int steps = Mathf.CeilToInt(waveRadius / TileSize);
        float half = TileSize * 0.5f;
        var keep = new List<GameObject>();
        for (int ix = -steps; ix <= steps; ix++)
        {
            for (int iz = -steps; iz <= steps; iz++)
            {
                float x = ix * TileSize;
                float z = iz * TileSize;
                float dx = Mathf.Max(Mathf.Abs(x) - half, 0f);
                float dz = Mathf.Max(Mathf.Abs(z) - half, 0f);
                if (dx * dx + dz * dz > waveRadius * waveRadius) continue;
                var tile = Instantiate(template, root);
                tile.name = "WaterDisk";
                tile.transform.localRotation = Quaternion.identity;
                tile.transform.localScale = Vector3.one;
                tile.transform.localPosition = new Vector3(x, height, z);
                keep.Add(tile);
            }
        }
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i).gameObject;
            if (keep.Contains(child)) continue;
            child.SetActive(false);
            Destroy(child);
        }
        waveDiskReady = true;
    }

    static float BoatLength(Transform boat)
    {
        Transform skin = null;
        foreach (var t in boat.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "YachtBodySkin")
            {
                skin = t;
                break;
            }
        }
        Transform source = skin != null ? skin : boat;
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;
            Vector3 center = mesh.bounds.center;
            Vector3 ext = mesh.bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = center + Vector3.Scale(ext, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));
                Vector3 world = filter.transform.TransformPoint(corner);
                float along = Vector3.Dot(world - boat.position, boat.forward);
                if (along < min) min = along;
                if (along > max) max = along;
            }
        }
        float length = max - min;
        if (float.IsInfinity(length) || length < 1f) return 5.2f;
        return length;
    }
}
