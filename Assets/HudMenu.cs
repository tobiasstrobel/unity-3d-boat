using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HudMenu : MonoBehaviour
{
    public GameObject boat;
    public GameObject wind;
    public Text status;
    public RectTransform twaIndicator;
    public RectTransform awaIndicator;
    public RectTransform dirIndicator;
    public RectTransform speedIndicator;

    protected Vector3 awaVector;
    protected Vector3 headSailVector;
    protected Vector3 mainSailVector;
    

    // Update is called once per frame
    void Update()
    {
        if (boat == null) return;
        Rigidbody bootRb = boat.GetComponent<Rigidbody>();
        if (bootRb == null) return;

        float speed = bootRb.velocity.magnitude;
        // The dial and the HMI both read speed over ground in knots.
        const float MpsToKnots = 1f / 0.514444f;
        float knots = speed * MpsToKnots;
        if (status != null) status.text = knots.ToString("0.0");

        Vector3 ahead = boat.transform.forward;
        ahead.y = 0f;

        // Wind-object forward is the direction the air moves. The needles show
        // where the wind comes from, on the same scale. The old apparent-wind
        // needle used an unsigned angle and the true-wind side, so the two
        // marks diverged even when the boat was stopped.
        float twa = WindFromAngle(ahead, wind != null ? -wind.transform.forward : ahead);
        float awa = awaVector.sqrMagnitude > 0.05f ? WindFromAngle(ahead, -awaVector) : twa;

        if (twaIndicator != null)
            twaIndicator.localEulerAngles = new Vector3(0f, 0f, -twa - 180f);
        if (awaIndicator != null)
            awaIndicator.localEulerAngles = new Vector3(0f, 0f, -awa - 180f);

        Vector3 vel = bootRb.velocity;
        vel.y = 0f;
        float drift = vel.sqrMagnitude > 0.25f ? Vector3.SignedAngle(ahead, vel, Vector3.up) : 0f;
        if (dirIndicator != null)
            dirIndicator.localEulerAngles = new Vector3(0f, 0f, -drift);
        if (speedIndicator != null)
            speedIndicator.localEulerAngles = new Vector3(0f, 0f, 120f - knots * 24f);
    }

    static float WindFromAngle(Vector3 ahead, Vector3 fromDirection)
    {
        fromDirection.y = 0f;
        if (ahead.sqrMagnitude < 0.0001f || fromDirection.sqrMagnitude < 0.0001f) return 0f;
        return Vector3.SignedAngle(ahead, fromDirection, Vector3.up);
    }

    public void onAwaAngleChange(Vector3 vector) {
        awaVector = vector;
    }

    public void onHeadSailAngleChange(Vector3 vector) {
        headSailVector = vector;
    }

    public void onMainSailAngleChange(Vector3 vector) {
        mainSailVector = vector;
    }
}
