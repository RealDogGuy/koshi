using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ActiveJoint2D
{
    public string name;
    public HingeJoint2D physicsJoint;
    public Transform animLimb;

    [Header("Spring Settings")]
    public float frequency = 5f;
    public float dampingRatio = 0.7f;
    public float maxForce = 500f;

    [HideInInspector]
    public TargetJoint2D targetJoint;
}

public class ActiveRagdoll2D : MonoBehaviour
{
    [Header("Joint Tracking")]
    public ActiveJoint2D[] joints;

    [Header("Root Body Tracking")]
    public Rigidbody2D hipBody;
    public Transform animHip;
    public float hipFrequency = 4f;
    public float hipDampingRatio = 0.7f;
    public float hipMaxForce = 400f;

    [Header("Global Blend")]
    [Range(0f, 1f)]
    public float ragdollBlend = 1f;

    [Header("Auto-Wire References")]
    public Transform physicsRigRoot;
    public Transform animRigRoot;
    [Tooltip("Limb names to skip during auto-wire (e.g. legs managed by ProceduralLegs2D).")]
    public string[] excludeNames;

    TargetJoint2D _hipTargetJoint;

    void Start()
    {
        if (hipBody != null)
        {
            _hipTargetJoint = hipBody.gameObject.AddComponent<TargetJoint2D>();
            _hipTargetJoint.autoConfigureTarget = false;
            _hipTargetJoint.frequency = hipFrequency;
            _hipTargetJoint.dampingRatio = hipDampingRatio;
            _hipTargetJoint.maxForce = hipMaxForce;
        }

        for (int i = 0; i < joints.Length; i++)
        {
            var j = joints[i];
            if (j.physicsJoint == null || j.animLimb == null) continue;

            Rigidbody2D rb = j.physicsJoint.attachedRigidbody;
            if (rb == null) continue;

            j.physicsJoint.useMotor = false;

            var tj = rb.gameObject.AddComponent<TargetJoint2D>();
            tj.autoConfigureTarget = false;
            tj.frequency = j.frequency;
            tj.dampingRatio = j.dampingRatio;
            tj.maxForce = j.maxForce;

            j.targetJoint = tj;
        }
    }

    void FixedUpdate()
    {
        float blend = ragdollBlend;

        if (_hipTargetJoint != null && animHip != null)
        {
            _hipTargetJoint.target = animHip.position;
            _hipTargetJoint.maxForce = hipMaxForce * blend;
        }

        for (int i = 0; i < joints.Length; i++)
        {
            var j = joints[i];
            if (j.targetJoint == null || j.animLimb == null) continue;

            j.targetJoint.target = j.animLimb.position;
            j.targetJoint.maxForce = j.maxForce * blend;
        }
    }

    void OnDestroy()
    {
        if (_hipTargetJoint != null)
            Destroy(_hipTargetJoint);

        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i].targetJoint != null)
                Destroy(joints[i].targetJoint);
        }
    }

    [ContextMenu("Auto Wire Joints")]
    void AutoWireJoints()
    {
        if (physicsRigRoot == null || animRigRoot == null)
        {
            Debug.LogError("ActiveRagdoll2D: Assign physicsRigRoot and animRigRoot before auto-wiring.");
            return;
        }

        HingeJoint2D[] hinges = physicsRigRoot.GetComponentsInChildren<HingeJoint2D>();
        var result = new List<ActiveJoint2D>();
        var animTransforms = animRigRoot.GetComponentsInChildren<Transform>();

        foreach (HingeJoint2D hinge in hinges)
        {
            string limbName = hinge.gameObject.name;

            if (IsExcluded(limbName))
            {
                Debug.Log($"ActiveRagdoll2D: Skipping excluded limb '{limbName}'");
                continue;
            }

            Transform match = null;

            foreach (Transform t in animTransforms)
            {
                if (t.name == limbName)
                {
                    match = t;
                    break;
                }
            }

            if (match == null)
            {
                Debug.LogWarning($"ActiveRagdoll2D: No AnimRig match found for '{limbName}', skipping.");
                continue;
            }

            result.Add(new ActiveJoint2D
            {
                name = limbName,
                physicsJoint = hinge,
                animLimb = match,
                frequency = 5f,
                dampingRatio = 0.7f,
                maxForce = 500f
            });

            Debug.Log($"ActiveRagdoll2D: Wired '{limbName}'");
        }

        joints = result.ToArray();
        Debug.Log($"ActiveRagdoll2D: Auto-wired {joints.Length} joints.");

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    bool IsExcluded(string limbName)
    {
        if (excludeNames == null) return false;
        for (int i = 0; i < excludeNames.Length; i++)
        {
            if (!string.IsNullOrEmpty(excludeNames[i]) && limbName.Contains(excludeNames[i]))
                return true;
        }
        return false;
    }
}
