using UnityEngine;

public class ProceduralLegs2D : MonoBehaviour
{
    [Header("Body References")]
    public Rigidbody2D hipBody;
    public Rigidbody2D leftFoot;
    public Rigidbody2D rightFoot;
    [Tooltip("Torso segments for upright torque (Hip, MiddleTorso, UpperTorso, Head).")]
    public Rigidbody2D[] uprightBodies;

    [Header("Ground")]
    public LayerMask groundLayer;
    public float groundRayLength = 5f;

    [Header("Standing")]
    public float standHeight = 1.5f;
    public float stanceHalfWidth = 0.2f;

    [Header("Hip Spring")]
    public float hipFrequency = 5f;
    public float hipDampingRatio = 0.8f;
    public float hipMaxForce = 600f;
    [Tooltip("Vertical bob amplitude during a step.")]
    public float hipBobAmount = 0.05f;

    [Header("Foot Spring")]
    public float footFrequency = 8f;
    public float footDampingRatio = 0.8f;
    public float footMaxForce = 500f;
    [Tooltip("Extra force multiplier on planted foot during a step.")]
    public float plantForceMult = 1.6f;

    [Header("Upright Balance")]
    public float targetAngle = 0f;
    public float uprightForce = 15f;
    public float uprightDamping = 0.5f;
    public float maxUprightTorque = 20f;

    [Header("Stepping")]
    public float stepThreshold = 0.3f;
    public float strideLength = 0.5f;
    public float stepHeight = 0.2f;
    public float stepDuration = 0.2f;
    public float stepCooldown = 0.08f;

    [Header("Movement")]
    [Tooltip("Set from your input script. Negative = left, positive = right.")]
    public float moveInput;
    public float moveForce = 30f;

    TargetJoint2D _hipJoint;
    TargetJoint2D _leftFootJoint;
    TargetJoint2D _rightFootJoint;

    bool _isStepping;
    bool _lastStepWasLeft;
    float _stepTimer;
    float _cooldownTimer;
    Vector2 _stepStart;
    Vector2 _stepTarget;
    Vector2 _leftPlantPos;
    Vector2 _rightPlantPos;
    float _groundY;

    void Start()
    {
        _hipJoint = AddJoint(hipBody, hipFrequency, hipDampingRatio, hipMaxForce);
        _leftFootJoint = AddJoint(leftFoot, footFrequency, footDampingRatio, footMaxForce);
        _rightFootJoint = AddJoint(rightFoot, footFrequency, footDampingRatio, footMaxForce);

        _leftPlantPos = leftFoot.position;
        _rightPlantPos = rightFoot.position;
        _groundY = Mathf.Min(_leftPlantPos.y, _rightPlantPos.y);
    }

    TargetJoint2D AddJoint(Rigidbody2D body, float freq, float damp, float maxF)
    {
        var tj = body.gameObject.AddComponent<TargetJoint2D>();
        tj.autoConfigureTarget = false;
        tj.frequency = freq;
        tj.dampingRatio = damp;
        tj.maxForce = maxF;
        return tj;
    }

    void UpdateGroundY()
    {
        RaycastHit2D hit = Physics2D.Raycast(hipBody.position, Vector2.down, groundRayLength, groundLayer);
        if (hit.collider != null)
            _groundY = hit.point.y;
    }

    void FixedUpdate()
    {
        UpdateGroundY();
        ApplyUprightTorque();

        float hipX = hipBody.position.x;
        float footMidX = (_leftPlantPos.x + _rightPlantPos.x) * 0.5f;
        bool hasInput = Mathf.Abs(moveInput) > 0.1f;

        // --- Hip: standing height + bob ---
        float bobOffset = 0f;
        if (_isStepping)
        {
            float t = Mathf.Clamp01(_stepTimer / stepDuration);
            bobOffset = -hipBobAmount * Mathf.Sin(t * Mathf.PI);
        }

        _hipJoint.target = new Vector2(footMidX, _groundY + standHeight + bobOffset);
        _hipJoint.frequency = hipFrequency;
        _hipJoint.dampingRatio = hipDampingRatio;
        _hipJoint.maxForce = hipMaxForce;

        // --- Movement force ---
        if (hasInput)
            hipBody.AddForce(Vector2.right * moveInput * moveForce);

        // --- Cooldown ---
        if (_cooldownTimer > 0f)
            _cooldownTimer -= Time.fixedDeltaTime;

        // --- Feet ---
        if (!_isStepping)
        {
            SetFoot(_leftFootJoint, _leftPlantPos, false);
            SetFoot(_rightFootJoint, _rightPlantPos, false);

            if (_cooldownTimer <= 0f)
            {
                float drift = hipX - footMidX;
                bool balanceStep = Mathf.Abs(drift) > stepThreshold;

                if (balanceStep || hasInput)
                    BeginStep(hipX, drift, hasInput);
            }
        }
        else
        {
            // Planted foot — extra stiff
            bool stepLeft = !_lastStepWasLeft;
            TargetJoint2D plantedJoint = stepLeft ? _rightFootJoint : _leftFootJoint;
            Vector2 plantPos = stepLeft ? _rightPlantPos : _leftPlantPos;
            SetFoot(plantedJoint, plantPos, true);

            // Swinging foot
            TargetJoint2D swingJoint = stepLeft ? _leftFootJoint : _rightFootJoint;
            UpdateStep(swingJoint);
        }
    }

    void SetFoot(TargetJoint2D joint, Vector2 target, bool boosted)
    {
        joint.target = target;
        joint.frequency = footFrequency;
        joint.dampingRatio = footDampingRatio;
        joint.maxForce = boosted ? footMaxForce * plantForceMult : footMaxForce;
    }

    void BeginStep(float hipX, float drift, bool hasInput)
    {
        _isStepping = true;
        _stepTimer = 0f;

        // Strict alternation
        bool stepLeft = !_lastStepWasLeft;

        Rigidbody2D swingFoot = stepLeft ? leftFoot : rightFoot;
        _stepStart = swingFoot.position;

        float dir = hasInput ? Mathf.Sign(moveInput) : Mathf.Sign(drift);

        float targetX;
        if (hasInput)
        {
            float predictedX = hipX + hipBody.linearVelocity.x * stepDuration;
            targetX = predictedX + dir * strideLength;
        }
        else
        {
            targetX = hipX;
        }

        // Offset so feet don't cross — left foot lands left of center, right lands right
        float sideOffset = stepLeft ? -stanceHalfWidth : stanceHalfWidth;
        targetX += sideOffset;

        Vector2 plantRef = stepLeft ? _rightPlantPos : _leftPlantPos;
        _stepTarget = new Vector2(targetX, plantRef.y);
    }

    void UpdateStep(TargetJoint2D swingJoint)
    {
        _stepTimer += Time.fixedDeltaTime;
        float raw = Mathf.Clamp01(_stepTimer / stepDuration);

        // Smoothstep for natural ease-in-out
        float t = raw * raw * (3f - 2f * raw);

        Vector2 pos = Vector2.Lerp(_stepStart, _stepTarget, t);
        pos.y += stepHeight * Mathf.Sin(raw * Mathf.PI);

        swingJoint.target = pos;
        swingJoint.frequency = footFrequency;
        swingJoint.dampingRatio = footDampingRatio;
        swingJoint.maxForce = footMaxForce;

        if (raw >= 1f)
        {
            bool stepLeft = !_lastStepWasLeft;

            if (stepLeft)
                _leftPlantPos = _stepTarget;
            else
                _rightPlantPos = _stepTarget;

            _lastStepWasLeft = stepLeft;
            _cooldownTimer = stepCooldown;

            // Auto-chain: if still moving, begin next step immediately
            bool hasInput = Mathf.Abs(moveInput) > 0.1f;
            if (hasInput)
            {
                float hipX = hipBody.position.x;
                float footMidX = (_leftPlantPos.x + _rightPlantPos.x) * 0.5f;
                float drift = hipX - footMidX;
                BeginStep(hipX, drift, true);
            }
            else
            {
                _isStepping = false;
            }
        }
    }

    void ApplyUprightTorque()
    {
        if (uprightBodies == null || uprightBodies.Length == 0) return;

        float share = 1f / uprightBodies.Length;
        for (int i = 0; i < uprightBodies.Length; i++)
        {
            Rigidbody2D body = uprightBodies[i];
            if (body == null) continue;

            float err = Mathf.DeltaAngle(body.rotation, targetAngle);
            float torque = (err * uprightForce - body.angularVelocity * uprightDamping) * share;
            body.AddTorque(Mathf.Clamp(torque, -maxUprightTorque, maxUprightTorque));
        }
    }

    void OnDestroy()
    {
        if (_hipJoint != null) Destroy(_hipJoint);
        if (_leftFootJoint != null) Destroy(_leftFootJoint);
        if (_rightFootJoint != null) Destroy(_rightFootJoint);
    }

    void OnDrawGizmos()
    {
        if (hipBody == null) return;
        Vector2 hipPos = hipBody.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(hipPos, 0.08f);

        if (!Application.isPlaying)
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(hipPos, hipPos + Vector2.down * groundRayLength);
            return;
        }

        // Standing height target
        Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
        float standY = _groundY + standHeight;
        Gizmos.DrawLine(new Vector3(hipPos.x - 0.3f, standY), new Vector3(hipPos.x + 0.3f, standY));

        // Foot plant positions
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(_leftPlantPos, 0.06f);
        Gizmos.DrawWireSphere(_rightPlantPos, 0.06f);

        // Stance width markers
        float midX = (_leftPlantPos.x + _rightPlantPos.x) * 0.5f;
        float midY = (_leftPlantPos.y + _rightPlantPos.y) * 0.5f;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(midX, midY - 0.2f), new Vector3(midX, midY + 0.2f));

        // Threshold zone
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawLine(new Vector3(midX - stepThreshold, midY - 0.15f), new Vector3(midX - stepThreshold, midY + 0.15f));
        Gizmos.DrawLine(new Vector3(midX + stepThreshold, midY - 0.15f), new Vector3(midX + stepThreshold, midY + 0.15f));

        // Hip drift
        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(midX, hipPos.y), hipPos);

        // Step arc
        if (_isStepping)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(_stepTarget, 0.1f);

            // Draw the full arc preview
            Vector2 prev = _stepStart;
            for (int i = 1; i <= 10; i++)
            {
                float r = i / 10f;
                float s = r * r * (3f - 2f * r);
                Vector2 p = Vector2.Lerp(_stepStart, _stepTarget, s);
                p.y += stepHeight * Mathf.Sin(r * Mathf.PI);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        // Ground ray
        Gizmos.color = Color.gray;
        Gizmos.DrawLine(hipPos, hipPos + Vector2.down * groundRayLength);
    }
}
