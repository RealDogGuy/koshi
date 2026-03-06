using UnityEngine;

[System.Serializable]
public class ActiveJoint2D
{
    public string name;
    public HingeJoint2D physicsJoint;   // on PhysicsRig
    public Transform animLimb;          // matching limb on AnimRig
    public float strength = 800f;
    public float speedGain = 8f;
}

public class ActiveRagdoll2D : MonoBehaviour
{
    public ActiveJoint2D[] joints;

    void FixedUpdate()
    {
        for (int i = 0; i < joints.Length; i++)
        {
            var j = joints[i];
            if (j.physicsJoint == null || j.animLimb == null) continue;

            // target world Z rotation from hidden animated limb
            float targetWorldZ = j.animLimb.eulerAngles.z;

            // connected body rotation, so we can convert into joint-local target
            Rigidbody2D connected = j.physicsJoint.connectedBody;
            float connectedWorldZ = connected ? connected.rotation : 0f;

            // desired relative angle for this hinge
            float targetJointAngle = Mathf.DeltaAngle(connectedWorldZ, targetWorldZ);

            // current relative hinge angle
            float currentJointAngle = j.physicsJoint.jointAngle;

            // error
            float error = Mathf.DeltaAngle(currentJointAngle, targetJointAngle);

            JointMotor2D motor = j.physicsJoint.motor;
            motor.motorSpeed = error * j.speedGain;
            motor.maxMotorTorque = j.strength;

            j.physicsJoint.motor = motor;
            j.physicsJoint.useMotor = true;
        }
    }
}