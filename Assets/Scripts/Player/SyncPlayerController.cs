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

        // 3. 【阶段4将添加】本地预测执行
        // ApplyInputLocally(input);
    }

    /// <summary>
    /// Update中处理瞄准（需要更平滑的响应）
    /// </summary>
    void Update()
    {
        if (!isLocalPlayer) return;

        // 瞄准需要每帧更新，不能只在Tick中
        HandleAiming();
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
    /// 客户端：收到服务端状态广播，应用到所有玩家对象
    /// </summary>
    void OnServerStateReceived(ServerStateMessage msg)
    {
        if (msg.players == null) return;

        foreach (var playerState in msg.players)
        {
            // 找到对应的网络对象
            if (!NetworkClient.spawned.TryGetValue(playerState.netId, out NetworkIdentity identity))
                continue;

            if (identity == null) continue;

            if (playerState.netId == netId)
            {
                // 本地玩家：应用服务器权威位置（Phase 3 直接覆盖）
                // Phase 4 启用后改为预测校验 + 回滚
                transform.position = playerState.position;
                // 旋转由 HandleAiming() 本地控制，不覆盖
            }
            else
            {
                // 其他玩家：应用位置和旋转
                identity.transform.position = playerState.position;
                identity.transform.rotation = Quaternion.Euler(0, playerState.rotationY, 0);
            }
        }
    }

    // ==========================================
    // 阶段4将添加的功能（先注释）
    // ==========================================

    /*
    // 输入缓冲区
    private ClientInputMessage[] inputBuffer = new ClientInputMessage[128];
    
    // 状态缓冲区
    private PlayerStateData[] stateBuffer = new PlayerStateData[128];
    
    // 最后确认的输入序号
    private uint lastAckedSequence = 0;

    /// <summary>
    /// 本地预测执行
    /// </summary>
    void ApplyInputLocally(ClientInputMessage input)
    {
        // 存储输入
        inputBuffer[input.sequence % inputBuffer.Length] = input;
        
        // 执行移动
        Vector3 moveDir = new Vector3(input.moveX, 0, input.moveY);
        controller.Move(moveDir * moveSpeed * TickManager.Instance.TickInterval);
        
        // 存储状态
        stateBuffer[input.sequence % stateBuffer.Length] = new PlayerStateData
        {
            position = transform.position,
            rotationY = transform.eulerAngles.y
        };
    }

    /// <summary>
    /// 收到服务端状态时校验
    /// </summary>
    void OnServerStateReceived(ServerStateMessage msg)
    {
        // 找到自己的状态
        foreach (var playerState in msg.players)
        {
            if (playerState.netId == netId)
            {
                // 获取对应的本地预测状态
                uint seq = msg.yourLastProcessedInput;
                PlayerStateData predicted = stateBuffer[seq % stateBuffer.Length];
                
                // 对比
                float error = Vector3.Distance(predicted.position, playerState.position);
                
                if (error > 0.01f)
                {
                    // 预测错误，执行回滚
                    Rollback(seq, playerState);
                }
                
                lastAckedSequence = seq;
                break;
            }
        }
    }

    /// <summary>
    /// 回滚并重演
    /// </summary>
    void Rollback(uint fromSequence, PlayerStateData correctState)
    {
        // 1. 恢复到正确状态
        transform.position = correctState.position;
        transform.rotation = Quaternion.Euler(0, correctState.rotationY, 0);
        
        // 2. 重演后续输入
        for (uint seq = fromSequence + 1; seq < inputSequence; seq++)
        {
            ClientInputMessage input = inputBuffer[seq % inputBuffer.Length];
            
            Vector3 moveDir = new Vector3(input.moveX, 0, input.moveY);
            controller.Move(moveDir * moveSpeed * TickManager.Instance.TickInterval);
            
            stateBuffer[seq % stateBuffer.Length] = new PlayerStateData
            {
                position = transform.position,
                rotationY = transform.eulerAngles.y
            };
        }
    }
    */
}
