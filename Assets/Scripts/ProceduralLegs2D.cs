using UnityEngine;

public enum BalanceState { Standing, Stumbling, Ragdoll, GettingUp }

public class ProceduralLegs2D : MonoBehaviour
{
    [Header("Body References")]
    public Rigidbody2D hipBody;
    public Rigidbody2D leftFoot;
    public Rigidbody2D rightFoot;
    [Tooltip("Torso segments for upright torque (Hip, MiddleTorso, UpperTorso, Head).")]
    public Rigidbody2D[] uprightBodies;
    [Tooltip("All rigidbodies in the physics rig (for impact sensors + ragdoll state).")]
    public Rigidbody2D[] allBodies;

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
    public float hipBobAmount = 0.05f;

    [Header("Foot Spring")]
    public float footFrequency = 8f;
    public float footDampingRatio = 0.8f;
    public float footMaxForce = 500f;
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
    public float moveInput;
    public float moveForce = 30f;

    [Header("Impact Response")]
    [Tooltip("Impact velocity to trigger a stumble.")]
    public float stumbleThreshold = 3f;
    [Tooltip("Impact velocity to trigger full ragdoll.")]
    public float knockdownThreshold = 8f;
    public float stumbleDuration = 0.6f;
    [Tooltip("Strength multiplier during stumble (0-1).")]
    [Range(0f, 1f)]
    public float stumbleStrengthMult = 0.35f;
    [Tooltip("Seconds to wait on the ground before getting up.")]
    public float getUpDelay = 1f;
    [Tooltip("Seconds to transition from ragdoll to standing.")]
    public float getUpDuration = 0.8f;
    [Tooltip("Body must be below this speed to start getting up.")]
    public float settleSpeed = 1f;

    // Runtime state
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
    float _leftGroundY;
    float _rightGroundY;

    BalanceState _state = BalanceState.Standing;
    float _stateTimer;
    float _strengthScale = 1f;

    public BalanceState CurrentState => _state;

    void Start()
    {
        _hipJoint = AddJoint(hipBody, hipFrequency, hipDampingRatio, hipMaxForce);
        _leftFootJoint = AddJoint(leftFoot, footFrequency, footDampingRatio, footMaxForce);
        _rightFootJoint = AddJoint(rightFoot, footFrequency, footDampingRatio, footMaxForce);

        _leftPlantPos = leftFoot.position;
        _rightPlantPos = rightFoot.position;
        _leftGroundY = _leftPlantPos.y;
        _rightGroundY = _rightPlantPos.y;

        AttachImpactSensors();
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

    void AttachImpactSensors()
    {
        if (allBodies == null) return;
        foreach (Rigidbody2D rb in allBodies)
        {
            if (rb == null) continue;
            var sensor = rb.gameObject.AddComponent<ImpactSensor2D>();
            sensor.owner = this;
            sensor.ignoreLayer = groundLayer;
        }
    }

    // --- Impact callback from ImpactSensor2D ---

    public void OnImpact(float force, Vector2 relativeVelocity, Vector2 point)
    {
        if (_state == BalanceState.Ragdoll || _state == BalanceState.GettingUp) return;

        if (force >= knockdownThreshold)
            EnterState(BalanceState.Ragdoll);
        else if (force >= stumbleThreshold && _state != BalanceState.Stumbling)
            EnterState(BalanceState.Stumbling);
    }

    void EnterState(BalanceState newState)
    {
        _state = newState;
        _stateTimer = 0f;

        switch (newState)
        {
            case BalanceState.Standing:
                _strengthScale = 1f;
                break;

            case BalanceState.Stumbling:
                _strengthScale = stumbleStrengthMult;
                _isStepping = false;
                break;

            case BalanceState.Ragdoll:
                _strengthScale = 0f;
                _isStepping = false;
                break;

            case BalanceState.GettingUp:
                _strengthScale = 0f;
                _leftPlantPos = leftFoot.position;
                _rightPlantPos = rightFoot.position;
                break;
        }
    }

    // --- Ground detection per foot ---

    float RaycastGroundY(float x)
    {
        Vector2 origin = new Vector2(x, hipBody.position.y);
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundRayLength, groundLayer);
        if (hit.collider != null)
            return hit.point.y;
        return (_leftPlantPos.y + _rightPlantPos.y) * 0.5f;
    }

    void UpdateGroundAtFeet()
    {
        _leftGroundY = RaycastGroundY(_leftPlantPos.x);
        _rightGroundY = RaycastGroundY(_rightPlantPos.x);
    }

    float GroundYAtX(float x)
    {
        return RaycastGroundY(x);
    }

    // --- Main update ---

    void FixedUpdate()
    {
        _stateTimer += Time.fixedDeltaTime;
        UpdateGroundAtFeet();

        switch (_state)
        {
            case BalanceState.Standing:
                UpdateStanding();
                break;
            case BalanceState.Stumbling:
                UpdateStumbling();
                break;
            case BalanceState.Ragdoll:
                UpdateRagdoll();
                break;
            case BalanceState.GettingUp:
                UpdateGettingUp();
                break;
        }
    }

    void UpdateStanding()
    {
        _strengthScale = 1f;
        ApplyUprightTorque(_strengthScale);
        ApplyHip(_strengthScale);
        ApplyMovement();
        UpdateFeet(_strengthScale);
    }

    void UpdateStumbling()
    {
        ApplyUprightTorque(_strengthScale);
        ApplyHip(_strengthScale);
        UpdateFeet(_strengthScale);

        if (_stateTimer >= stumbleDuration)
            EnterState(BalanceState.Standing);
    }

    void UpdateRagdoll()
    {
        ZeroAllForces();

        if (_stateTimer >= getUpDelay && hipBody.linearVelocity.magnitude < settleSpeed)
            EnterState(BalanceState.GettingUp);
    }

    void UpdateGettingUp()
    {
        float t = Mathf.Clamp01(_stateTimer / getUpDuration);
        _strengthScale = t * t;

        _leftPlantPos = leftFoot.position;
        _rightPlantPos = rightFoot.position;

        ApplyUprightTorque(_strengthScale);
        ApplyHip(_strengthScale);
        SetFoot(_leftFootJoint, _leftPlantPos, false, _strengthScale);
        SetFoot(_rightFootJoint, _rightPlantPos, false, _strengthScale);

        if (t >= 1f)
            EnterState(BalanceState.Standing);
    }

    // --- Subsystems ---

    void ApplyUprightTorque(float scale)
    {
        if (uprightBodies == null || uprightBodies.Length == 0) return;

        float share = 1f / uprightBodies.Length;
        for (int i = 0; i < uprightBodies.Length; i++)
        {
            Rigidbody2D body = uprightBodies[i];
            if (body == null) continue;

            float err = Mathf.DeltaAngle(body.rotation, targetAngle);
            float torque = (err * uprightForce - body.angularVelocity * uprightDamping) * share * scale;
            body.AddTorque(Mathf.Clamp(torque, -maxUprightTorque * scale, maxUprightTorque * scale));
        }
    }

    void ApplyHip(float scale)
    {
        float footMidX = (_leftPlantPos.x + _rightPlantPos.x) * 0.5f;
        float avgGroundY = (_leftGroundY + _rightGroundY) * 0.5f;

        float bobOffset = 0f;
        if (_isStepping)
        {
            float t = Mathf.Clamp01(_stepTimer / stepDuration);
            bobOffset = -hipBobAmount * Mathf.Sin(t * Mathf.PI);
        }

        _hipJoint.target = new Vector2(footMidX, avgGroundY + standHeight + bobOffset);
        _hipJoint.frequency = hipFrequency;
        _hipJoint.dampingRatio = hipDampingRatio;
        _hipJoint.maxForce = hipMaxForce * scale;
    }

    void ApplyMovement()
    {
        if (Mathf.Abs(moveInput) > 0.1f)
            hipBody.AddForce(Vector2.right * moveInput * moveForce);
    }

    void UpdateFeet(float scale)
    {
        float hipX = hipBody.position.x;
        float footMidX = (_leftPlantPos.x + _rightPlantPos.x) * 0.5f;
        bool hasInput = Mathf.Abs(moveInput) > 0.1f;

        if (_cooldownTimer > 0f)
            _cooldownTimer -= Time.fixedDeltaTime;

        if (!_isStepping)
        {
            SetFoot(_leftFootJoint, _leftPlantPos, false, scale);
            SetFoot(_rightFootJoint, _rightPlantPos, false, scale);

            if (_cooldownTimer <= 0f)
            {
                float drift = hipX - footMidX;
                float threshold = _state == BalanceState.Stumbling
                    ? stepThreshold * 0.5f
                    : stepThreshold;

                if (Mathf.Abs(drift) > threshold || (hasInput && _state == BalanceState.Standing))
                    BeginStep(hipX, drift, hasInput);
            }
        }
        else
        {
            bool stepLeft = !_lastStepWasLeft;
            TargetJoint2D plantedJoint = stepLeft ? _rightFootJoint : _leftFootJoint;
            Vector2 plantPos = stepLeft ? _rightPlantPos : _leftPlantPos;
            SetFoot(plantedJoint, plantPos, true, scale);

            TargetJoint2D swingJoint = stepLeft ? _leftFootJoint : _rightFootJoint;
            UpdateStep(swingJoint, scale);
        }
    }

    void SetFoot(TargetJoint2D joint, Vector2 target, bool boosted, float scale)
    {
        joint.target = target;
        joint.frequency = footFrequency;
        joint.dampingRatio = footDampingRatio;
        float force = boosted ? footMaxForce * plantForceMult : footMaxForce;
        joint.maxForce = force * scale;
    }

    void BeginStep(float hipX, float drift, bool hasInput)
    {
        _isStepping = true;
        _stepTimer = 0f;

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

        float sideOffset = stepLeft ? -stanceHalfWidth : stanceHalfWidth;
        targetX += sideOffset;

        float targetGroundY = GroundYAtX(targetX);
        Vector2 plantRef = stepLeft ? _rightPlantPos : _leftPlantPos;
        float footHeight = plantRef.y - (stepLeft ? _rightGroundY : _leftGroundY);
        _stepTarget = new Vector2(targetX, targetGroundY + footHeight);
    }

    void UpdateStep(TargetJoint2D swingJoint, float scale)
    {
        _stepTimer += Time.fixedDeltaTime;
        float raw = Mathf.Clamp01(_stepTimer / stepDuration);
        float t = raw * raw * (3f - 2f * raw);

        Vector2 pos = Vector2.Lerp(_stepStart, _stepTarget, t);
        pos.y += stepHeight * Mathf.Sin(raw * Mathf.PI);

        swingJoint.target = pos;
        swingJoint.frequency = footFrequency;
        swingJoint.dampingRatio = footDampingRatio;
        swingJoint.maxForce = footMaxForce * scale;

        if (raw >= 1f)
        {
            bool stepLeft = !_lastStepWasLeft;

            if (stepLeft)
                _leftPlantPos = _stepTarget;
            else
                _rightPlantPos = _stepTarget;

            _lastStepWasLeft = stepLeft;
            _cooldownTimer = stepCooldown;

            bool hasInput = Mathf.Abs(moveInput) > 0.1f;
            if (hasInput && _state == BalanceState.Standing)
            {
                float hipX = hipBody.position.x;
                float footMidX = (_leftPlantPos.x + _rightPlantPos.x) * 0.5f;
                BeginStep(hipX, hipX - footMidX, true);
            }
            else
            {
                _isStepping = false;
            }
        }
    }

    void ZeroAllForces()
    {
        _hipJoint.maxForce = 0f;
        _leftFootJoint.maxForce = 0f;
        _rightFootJoint.maxForce = 0f;
    }

    // --- Public API ---

    public void ForceRagdoll()
    {
        EnterState(BalanceState.Ragdoll);
    }

    public void ForceStumble()
    {
        EnterState(BalanceState.Stumbling);
    }

    // --- Cleanup ---

    void OnDestroy()
    {
        if (_hipJoint != null) Destroy(_hipJoint);
        if (_leftFootJoint != null) Destroy(_leftFootJoint);
        if (_rightFootJoint != null) Destroy(_rightFootJoint);
    }

    // --- Gizmos ---

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

        // State label color
        switch (_state)
        {
            case BalanceState.Standing:  Gizmos.color = Color.green;   break;
            case BalanceState.Stumbling: Gizmos.color = Color.yellow;  break;
            case BalanceState.Ragdoll:   Gizmos.color = Color.red;     break;
            case BalanceState.GettingUp: Gizmos.color = new Color(1f, 0.5f, 0f); break;
        }
        Gizmos.DrawWireSphere(hipPos, 0.15f);

        // Standing height target
        float avgGroundY = (_leftGroundY + _rightGroundY) * 0.5f;
        float standY = avgGroundY + standHeight;
        Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
        Gizmos.DrawLine(new Vector3(hipPos.x - 0.3f, standY), new Vector3(hipPos.x + 0.3f, standY));

        // Foot plant positions
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(_leftPlantPos, 0.06f);
        Gizmos.DrawWireSphere(_rightPlantPos, 0.06f);

        // Per-foot ground level
        Gizmos.color = new Color(0.5f, 0.3f, 0f, 0.5f);
        Gizmos.DrawLine(new Vector3(_leftPlantPos.x - 0.1f, _leftGroundY), new Vector3(_leftPlantPos.x + 0.1f, _leftGroundY));
        Gizmos.DrawLine(new Vector3(_rightPlantPos.x - 0.1f, _rightGroundY), new Vector3(_rightPlantPos.x + 0.1f, _rightGroundY));

        // Midpoint + threshold
        float midX = (_leftPlantPos.x + _rightPlantPos.x) * 0.5f;
        float midY = (_leftPlantPos.y + _rightPlantPos.y) * 0.5f;
        float threshold = _state == BalanceState.Stumbling ? stepThreshold * 0.5f : stepThreshold;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(midX, midY - 0.2f), new Vector3(midX, midY + 0.2f));
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawLine(new Vector3(midX - threshold, midY - 0.15f), new Vector3(midX - threshold, midY + 0.15f));
        Gizmos.DrawLine(new Vector3(midX + threshold, midY - 0.15f), new Vector3(midX + threshold, midY + 0.15f));

        // Hip drift
        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(midX, hipPos.y), hipPos);

        // Step arc
        if (_isStepping)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(_stepTarget, 0.1f);
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

        // Ground rays from feet
        Gizmos.color = Color.gray;
        Gizmos.DrawLine(new Vector3(_leftPlantPos.x, hipPos.y), new Vector3(_leftPlantPos.x, hipPos.y - groundRayLength));
        Gizmos.DrawLine(new Vector3(_rightPlantPos.x, hipPos.y), new Vector3(_rightPlantPos.x, hipPos.y - groundRayLength));
    }
}
