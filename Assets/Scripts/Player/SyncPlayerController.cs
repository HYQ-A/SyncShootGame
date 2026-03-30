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

    // 子弹预制体缓存（从 Resources 加载）
    private GameObject bulletPrefabCache;

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

    // ===== 玩家颜色同步 =====
    // SyncVar：服务器设置后自动同步给所有客户端
    // hook：客户端收到新值时调用 OnColorIndexChanged，在里面修改材质颜色
    [SyncVar(hook = nameof(OnColorIndexChanged))]
    private int playerColorIndex = -1;

    // 100 个颜色，用 HSV 色彩空间均匀生成，确保相邻颜色区分度高
    private static Color[] playerColors;

    /// <summary>
    /// 静态构造：生成 100 个颜色，使用黄金比例间距
    ///
    /// 黄金比例 ≈ 0.618034 的数学特性：
    /// 它是"最无理"的无理数，意味着用它做乘数取模后，
    /// 产生的序列点之间的间距永远是最大化的，不会聚集。
    ///
    /// 效果对比：
    ///   顺序排列：0.00, 0.01, 0.02 → 红, 红, 红（几乎一样）
    ///   黄金比例：0.00, 0.62, 0.24 → 红, 蓝紫, 绿（差异最大）
    /// </summary>
    static SyncPlayerController()
    {
        playerColors = new Color[100];
        for (int i = 0; i < 100; i++)
        {
            // 黄金比例间距：每个新颜色都和已有颜色差异最大化
            float hue = (i * 0.618034f) % 1f;
            float saturation = 0.7f;     // 饱和度：不太灰，也不太刺眼
            float value = 0.9f;          // 明度：足够亮，在深色地面上容易看清
            playerColors[i] = Color.HSVToRGB(hue, saturation, value);
        }
    }

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

        // 缓存子弹预制体（从 Resources/Prefabs/Bullet 加载一次）
        if (bulletPrefab == null)
        {
            bulletPrefabCache = Resources.Load<GameObject>("Prefabs/Bullet");
        }
        else
        {
            bulletPrefabCache = bulletPrefab;
        }

        // 初始化逻辑位置
        logicPositionPrev = transform.position;
        logicPositionCurr = transform.position;

        // 远程玩家初始化
        remoteTargetPosition = transform.position;
        remoteTargetRotationY = transform.eulerAngles.y;

        // 应用颜色（SyncVar 在 Spawn 时已同步初始值，Start 中直接应用）
        ApplyColor(playerColorIndex);
    }

    /// <summary>
    /// 服务器调用：设置玩家颜色索引
    /// 修改 SyncVar 字段后，Mirror 自动同步给所有客户端并触发 hook
    /// [Server] 标记确保此方法只能在服务器端调用
    /// </summary>
    [Server]
    public void SetColorIndex(int index)
    {
        playerColorIndex = index;
    }

    /// <summary>
    /// 获取此玩家的颜色（供外部获取，如子弹染色）
    /// </summary>
    public Color GetPlayerColor()
    {
        if (playerColorIndex >= 0 && playerColorIndex < playerColors.Length)
            return playerColors[playerColorIndex];
        return Color.white;
    }

    /// <summary>
    /// SyncVar hook：服务器修改 playerColorIndex 后，Mirror 自动调用此方法
    /// 参数签名必须是 (旧值, 新值)，这是 Mirror SyncVar hook 的要求
    /// </summary>
    void OnColorIndexChanged(int oldIndex, int newIndex)
    {
        ApplyColor(newIndex);
    }

    /// <summary>
    /// 将颜色应用到玩家的 MeshRenderer 上
    /// 使用 MaterialPropertyBlock 而非直接修改 material，避免创建材质实例（节省内存）
    /// </summary>
    void ApplyColor(int colorIndex)
    {
        if (colorIndex < 0 || colorIndex >= playerColors.Length) return;

        // 获取玩家身上的 MeshRenderer（包括子对象）
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor("_Color", playerColors[colorIndex]);

        foreach (MeshRenderer rend in renderers)
        {
            rend.SetPropertyBlock(block);
        }
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

        // 4. 本地射击预测：立即生成视觉子弹，不等服务器
        //    这样玩家点击鼠标后零延迟看到子弹飞出
        //    服务器的 SpawnBulletMessage 到达后，客户端通过 OnClientSpawnBullet 再生成一颗
        //    （两颗子弹会同时飞行，但间距极小，视觉上可接受）
        if (input.isShooting)
        {
            SpawnLocalPredictedBullet();
        }
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
    /// 本地射击预测：立即在客户端生成一颗视觉子弹
    /// 让玩家点击鼠标后零延迟看到子弹飞出（不等服务器往返）
    /// </summary>
    void SpawnLocalPredictedBullet()
    {
        if (bulletPrefabCache == null) return;

        Vector3 forward = transform.forward;
        Vector3 firePos = firePoint != null ? firePoint.position : transform.position + transform.forward * 0.8f;

        Quaternion rotation = Quaternion.LookRotation(forward);
        GameObject bulletObj = Instantiate(bulletPrefabCache, firePos, rotation);

        SimpleBullet bullet = bulletObj.GetComponent<SimpleBullet>();
        if (bullet != null)
        {
            bullet.direction = forward;
            bullet.speed = bulletSpeed;
            // 子弹颜色 = 自己的玩家颜色
            if (playerColorIndex >= 0 && playerColorIndex < playerColors.Length)
            {
                bullet.SetColor(playerColors[playerColorIndex]);
            }
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
