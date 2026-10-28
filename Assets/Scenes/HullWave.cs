using UnityEngine;

// Surface displacement of the hull's own wave. Added on the near tiles only.
// FloatCube keeps sampling the swell, so the boat does not lift itself.
public static class HullWave
{
    const float KelvinTan = 0.353f;
    // 0.24 m at 2 m/s, still under the 0.45 m swell that fills the color scale.
    const float AmpPerSpeed2 = 0.06f;
    const float AmpCap = 0.32f;

    static bool measured;
    static float length = 5.2f;
    static float beam = 1.6f;
    static float bow = 2.6f;
    static float stern = -2.6f;

    public static float Height(Vector3 world, BoatForces boat)
    {
        if (boat == null) return 0f;
        Transform hull = boat.transform;
        Rigidbody body = boat.yachtRigidbody;
        if (body == null) return 0f;
        if (!measured) Measure(hull);

        Vector3 forward = hull.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) return 0f;
        forward.Normalize();
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);

        Vector3 vel = body.velocity;
        vel.y = 0f;
        float speed = Vector3.Dot(vel, forward);
        if (speed < 0.3f) return 0f;
        float amp = Mathf.Min(AmpCap, AmpPerSpeed2 * speed * speed);

        Vector3 delta = world - hull.position;
        delta.y = 0f;
        float ahead = Vector3.Dot(delta, forward);
        float side = Vector3.Dot(delta, right);

        float bowCrest = Blob(ahead, side, bow + 0.15f * length, 0f, 0.45f * length, 0.5f * beam);
        float shoulder =
            Blob(ahead, side, bow - 0.05f * length, 0.7f * beam, 0.35f * length, 0.4f * beam) +
            Blob(ahead, side, bow - 0.05f * length, -0.7f * beam, 0.35f * length, 0.4f * beam);
        float mid = 0.5f * (bow + stern);
        float trough =
            Blob(ahead, side, mid, 0.85f * beam, 0.55f * length, 0.35f * beam) +
            Blob(ahead, side, mid, -0.85f * beam, 0.55f * length, 0.35f * beam);
        float sternCrest = Blob(ahead, side, stern - 0.3f * length, 0f, 0.4f * length, 0.55f * beam);

        float height = amp * (bowCrest + 0.5f * shoulder - 0.7f * trough + 0.75f * sternCrest);
        height += WakeArms(delta, forward, vel, amp);
        return Mathf.Clamp(height, -AmpCap, AmpCap);
    }

    static float WakeArms(Vector3 delta, Vector3 forward, Vector3 vel, float amp)
    {
        Vector3 track = vel.sqrMagnitude > 0.01f ? vel.normalized : forward;
        Vector3 astern = -track;
        Vector3 across = new Vector3(astern.z, 0f, -astern.x);
        // Arms leave the transom and open at the Kelvin angle.
        Vector3 fromStern = delta - forward * stern;
        float back = Vector3.Dot(fromStern, astern);
        if (back < 0f) return 0f;
        float side = Vector3.Dot(fromStern, across);
        float arm = back * KelvinTan;
        float width = Mathf.Max(1.5f, 0.28f * length);
        float left = Mathf.Exp(-Mathf.Pow(side - arm, 2f) / (width * width));
        float right = Mathf.Exp(-Mathf.Pow(side + arm, 2f) / (width * width));
        float decay = Mathf.Exp(-back / (3.5f * length));
        return amp * 0.85f * decay * (left + right);
    }

    static float Blob(float ahead, float side, float at, float beside, float alongRadius, float sideRadius)
    {
        float da = (ahead - at) / Mathf.Max(alongRadius, 0.5f);
        float ds = (side - beside) / Mathf.Max(sideRadius, 0.4f);
        return Mathf.Exp(-(da * da + ds * ds));
    }

    static void Measure(Transform boat)
    {
        measured = true;
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
        Vector3 forward = boat.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) return;
        forward.Normalize();
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);

        float minA = float.PositiveInfinity, maxA = float.NegativeInfinity;
        float minS = float.PositiveInfinity, maxS = float.NegativeInfinity;
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
                Vector3 world = filter.transform.TransformPoint(corner) - boat.position;
                world.y = 0f;
                float along = Vector3.Dot(world, forward);
                float across = Vector3.Dot(world, right);
                if (along < minA) minA = along;
                if (along > maxA) maxA = along;
                if (across < minS) minS = across;
                if (across > maxS) maxS = across;
            }
        }
        if (float.IsInfinity(minA) || maxA - minA < 1f) return;
        stern = minA;
        bow = maxA;
        length = maxA - minA;
        beam = Mathf.Max(0.8f, maxS - minS);
    }
}
