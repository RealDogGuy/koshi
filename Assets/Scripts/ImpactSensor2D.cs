using UnityEngine;

public class ImpactSensor2D : MonoBehaviour
{
    [HideInInspector] public ProceduralLegs2D owner;
    [HideInInspector] public LayerMask ignoreLayer;

    void OnCollisionEnter2D(Collision2D col)
    {
        if (owner == null) return;
        if (((1 << col.gameObject.layer) & ignoreLayer) != 0) return;

        float impact = col.relativeVelocity.magnitude;
        owner.OnImpact(impact, col.relativeVelocity, col.GetContact(0).point);
    }
}
