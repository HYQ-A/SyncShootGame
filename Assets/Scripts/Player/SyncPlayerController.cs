using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// ============================================
/// 同步玩家控制器
/// ============================================
/// 替代原来的PlayerController和NetworkPlayer
/// 实现：本地输入采集 → 发送到服务端
/// 
/// 阶段3：仅实现输入上报
/// 阶段4：将添加客户端预测
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SyncPlayerController : NetworkBehaviour
{
    [Header("===== 移动设置 =====")]
    public float moveSpeed = 8f;

    [Header("===== 射击设置 =====")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float bulletSpeed = 30f;
    public float fireRate = 0.2f;

    [Header("===== 瞄准设置 =====")]
    public float aimHeight = 0.5f;

    // 组件引用
    private CharacterController controller;
    private Camera mainCamera;
    private Plane aimPlane;

    // 输入序号（每次输入递增）
    private uint inputSequence = 0;

    // 射击冷却
    private float nextFireTime;

    // ===== 本地预测相关 =====
    // 输入缓冲区（环形）
    private ClientInputMessage[] inputBuffer = new ClientInputMessage[128];
    // 最后被服务器确认的输入序号
    private uint lastAckedSequence = 0;

    // ===== 渲染插值相关 =====
    // 逻辑位置（Tick驱动，30Hz更新）
    private Vector3 logicPositionPrev;   // 上一Tick的逻辑位置
    private Vector3 logicPositionCurr;   // 当前Tick的逻辑位置
    // 远程玩家：指数平滑（只记录目标，每帧平滑逼近）
    private Vector3 remoteTargetPosition;
    private float remoteTargetRotationY;
    private bool hasRemoteState = false;

    [Header("===== 远程同步设置 =====")]
    [Tooltip("远程玩家平滑追赶速度（越大越快，10-20 为推荐值）")]
    public float remoteSmoothSpeed = 15f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        aimPlane = new Plane(Vector3.up, new Vector3(0, aimHeight, 0));

        if (isLocalPlayer)
        {
            // 获取摄像机
            mainCamera = Camera.main;

            // 设置摄像机跟随
            CameraFollow cameraFollow = mainCamera?.GetComponent<CameraFollow>();
            if (cameraFollow != null)
            {
                cameraFollow.target = transform;
            }

            // 订阅Tick事件
            if (TickManager.Instance != null)
            {
                TickManager.Instance.OnTick += OnLocalTick;
            }
        }

        // 所有客户端都订阅服务器状态（用于同步所有玩家位置）
        if (isClient)
        {
            ClientStateReceiver.OnReceiveState += OnServerStateReceived;
        }

        // 自动创建枪口
        if (firePoint == null)
        {
            GameObject fp = new GameObject("FirePoint");
            fp.transform.SetParent(transform);
            fp.transform.localPosition = new Vector3(0, 0.5f, 0.8f);
            firePoint = fp.transform;
        }

        // 初始化逻辑位置
        logicPositionPrev = transform.position;
        logicPositionCurr = transform.position;

        // 远程玩家初始化
        remoteTargetPosition = transform.position;
        remoteTargetRotationY = transform.eulerAngles.y;
    }

    void OnGUI()
    {
        if (!isLocalPlayer) return;

        string text = $"NetId: {netId}";
        Vector2 size = new Vector2(150, 30);
        Rect rect = new Rect(Screen.width - size.x - 10, 10, size.x, size.y);

        GUI.Label(rect, text);
    }

    void OnDestroy()
    {
        if (isLocalPlayer && TickManager.Instance != null)
        {
            TickManager.Instance.OnTick -= OnLocalTick;
        }

        if (isClient)
        {
            ClientStateReceiver.OnReceiveState -= OnServerStateReceived;
        }
    }

    /// <summary>
    /// 本地玩家每个Tick执行
    /// </summary>
    void OnLocalTick(uint tick)
    {
        if (!isLocalPlayer) return;

        // 1. 采集输入
        ClientInputMessage input = GatherInput(tick);

        // 2. 发送到服务端
        SendInputToServer(input);

        // 3. 本地预测执行（立即移动，不等服务器回包）
        ApplyInputLocally(input);
    }

    /// <summary>
    /// 每帧渲染：插值 + 瞄准
    /// </summary>
    void Update()
    {
        float alpha = TickManager.Instance?.GetTickAlpha() ?? 0f;

        if (isLocalPlayer)
        {
            // 本地玩家：在前后两个逻辑位置之间插值渲染
            Vector3 renderPos = Vector3.Lerp(logicPositionPrev, logicPositionCurr, alpha);
            controller.enabled = false;
            transform.position = renderPos;
            controller.enabled = true;

            // 瞄准需要每帧更新，不能只在Tick中
            HandleAiming();
        }
        else if (hasRemoteState)
        {
            // 远程玩家：指数平滑逼近目标
            // 每帧从当前位置向目标靠近一个比例，天然抗消息突发
            float t = remoteSmoothSpeed * Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, remoteTargetPosition, t);
            float currentRotY = transform.eulerAngles.y;
            float newRotY = Mathf.LerpAngle(currentRotY, remoteTargetRotationY, t);
            transform.rotation = Quaternion.Euler(0, newRotY, 0);
        }
    }

    /// <summary>
    /// 采集当前输入
    /// </summary>
    ClientInputMessage GatherInput(uint tick)
    {
        // 移动输入
        float moveX = 0, moveY = 0;
        if (Input.GetKey(KeyCode.W)) moveY += 1;
        if (Input.GetKey(KeyCode.S)) moveY -= 1;
        if (Input.GetKey(KeyCode.A)) moveX -= 1;
        if (Input.GetKey(KeyCode.D)) moveX += 1;

        // 归一化
        Vector2 moveInput = new Vector2(moveX, moveY);
        if (moveInput.magnitude > 1f)
            moveInput.Normalize();

        // 瞄准角度
        float aimAngle = GetAimAngle();

        // 射击判断
        bool isShooting = Input.GetMouseButton(0) && Time.time >= nextFireTime;
        if (isShooting)
        {
            nextFireTime = Time.time + fireRate;
        }

        return new ClientInputMessage
        {
            tick = tick,
            sequence = inputSequence++,
            moveX = moveInput.x,
            moveY = moveInput.y,
            aimAngle = aimAngle,
            isShooting = isShooting
        };
    }

    /// <summary>
    /// 获取瞄准角度
    /// </summary>
    float GetAimAngle()
    {
        if (mainCamera == null) return transform.eulerAngles.y;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (aimPlane.Raycast(ray, out float distance))
        {
            Vector3 hitPoint = ray.GetPoint(distance);
            Vector3 direction = hitPoint - transform.position;
            direction.y = 0;

            if (direction.sqrMagnitude > 0.01f)
            {
                return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            }
        }

        return transform.eulerAngles.y;
    }

    /// <summary>
    /// 处理瞄准（本地立即响应）
    /// </summary>
    void HandleAiming()
    {
        float aimAngle = GetAimAngle();
        transform.rotation = Quaternion.Euler(0, aimAngle, 0);
    }

    /// <summary>
    /// 发送输入到服务端
    /// </summary>
    void SendInputToServer(ClientInputMessage input)
    {
        if (NetworkClient.isConnected)
        {
            NetworkClient.Send(input);
        }
    }

    /// <summary>
    /// 本地预测执行：更新逻辑位置，不直接移动 Transform（由 Update 插值渲染）
    /// </summary>
    void ApplyInputLocally(ClientInputMessage input)
    {
        // 存储输入到环形缓冲区（用于服务器校正时重演）
        inputBuffer[input.sequence % inputBuffer.Length] = input;

        // 保存上一帧逻辑位置（用于插值）
        logicPositionPrev = logicPositionCurr;

        // 计算新的逻辑位置
        Vector3 moveDir = new Vector3(input.moveX, 0, input.moveY).normalized;
        float tickInterval = TickManager.Instance != null ? TickManager.Instance.TickInterval : (1f / 30f);
        logicPositionCurr += moveDir * moveSpeed * tickInterval;
    }

    /// <summary>
    /// 客户端：收到服务端状态广播，更新逻辑位置（由 Update 插值渲染）
    /// </summary>
    void OnServerStateReceived(ServerStateMessage msg)
    {
        if (msg.players == null) return;

        foreach (var playerState in msg.players)
        {
            if (playerState.netId == netId)
            {
                // Host 玩家（同时是服务端）：预测 = 权威，无需和解
                // Mirror loopback 有一帧延迟，和解反而会用过时位置拉回 curr
                if (isServer) continue;

                // === 纯客户端：服务端和解（Server Reconciliation） ===
                lastAckedSequence = msg.yourLastProcessedInput;

                // 1. 以服务端权威位置为基准
                Vector3 reconciledPos = playerState.position;

                // 2. 重演所有未被服务端确认的输入
                float tickInterval = TickManager.Instance != null ? TickManager.Instance.TickInterval : (1f / 30f);
                for (uint seq = lastAckedSequence + 1; seq < inputSequence; seq++)
                {
                    ClientInputMessage buffered = inputBuffer[seq % inputBuffer.Length];
                    if (buffered.sequence != seq) break;

                    Vector3 moveDir = new Vector3(buffered.moveX, 0, buffered.moveY).normalized;
                    reconciledPos += moveDir * moveSpeed * tickInterval;
                }

                // 3. 只修正 logicPositionCurr，不动 logicPositionPrev
                logicPositionCurr = reconciledPos;
            }
            else
            {
                // === 远程玩家：找到对应网络对象，更新其插值缓冲 ===
                if (!NetworkClient.spawned.TryGetValue(playerState.netId, out NetworkIdentity identity))
                    continue;
                if (identity == null) continue;

                SyncPlayerController otherCtrl = identity.GetComponent<SyncPlayerController>();
                if (otherCtrl != null)
                {
                    // 只更新目标，不重置任何计时器
                    // 指数平滑会自动从当前视觉位置平滑追赶到目标
                    otherCtrl.remoteTargetPosition = playerState.position;
                    otherCtrl.remoteTargetRotationY = playerState.rotationY;
                    otherCtrl.hasRemoteState = true;
                }
            }
        }
    }

}
