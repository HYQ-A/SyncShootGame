using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 服务端玩家数据（服务端内部使用）
/// </summary>
public struct ServerPlayerData
{
    public uint netId;
    public int connectionId;
    public Vector3 position;
    public float rotationY;
    public int health;
    public bool isAlive;
    public float moveSpeed;
    public uint lastProcessedInput;
}

/// <summary>
/// 服务端子弹数据（服务端内部使用）
/// 服务器权威：子弹的位置、碰撞检测全部由服务器计算，
/// 客户端只负责视觉渲染。
/// </summary>
public struct ServerBulletData
{
    public uint bulletId;       // 唯一ID，用于服务器↔客户端对应同一颗子弹
    public uint ownerNetId;     // 发射者，用于判定"不打自己"
    public Vector3 position;    // 当前位置（服务器每Tick更新）
    public Vector3 direction;   // 飞行方向（单位向量，生成后不变）
    public float speed;         // 飞行速度
    public float spawnTime;     // 生成时间，用于超时销毁
}

/// <summary>
/// 客户端状态接收器（静态事件，供其他脚本订阅）
/// </summary>
public static class ClientStateReceiver
{
    public static System.Action<ServerStateMessage> OnReceiveState;
}

/// <summary>
/// ============================================
/// 游戏网络管理器
/// ============================================
/// 继承Mirror的NetworkManager，添加自定义消息处理
/// </summary>
public class GameNetworkManager : NetworkManager
{
    public static new GameNetworkManager singleton { get; private set; }

    [Header("===== 游戏设置 =====")]
    public GameObject tickManagerPrefab;

    // 服务端：玩家数据
    private Dictionary<uint, ServerPlayerData> serverPlayers = new Dictionary<uint, ServerPlayerData>();

    // 服务端：输入队列
    private Dictionary<uint, Queue<ClientInputMessage>> inputQueues = new Dictionary<uint, Queue<ClientInputMessage>>();

    // ===== 服务端：子弹管理 =====
    // 自增ID，每颗子弹唯一，用于服务器↔客户端对应同一颗子弹
    private uint nextBulletId = 1;
    // 所有飞行中的子弹（服务器权威数据）
    private Dictionary<uint, ServerBulletData> serverBullets = new Dictionary<uint, ServerBulletData>();
    // 待销毁列表（遍历字典后统一删除，避免遍历中修改字典的老问题）
    private List<uint> bulletsToRemove = new List<uint>();

    [Header("===== 子弹设置 =====")]
    public float bulletMaxLifetime = 3f;
    public float defaultBulletSpeed = 30f;
    public int bulletDamage = 10;

    // ===== 客户端：子弹视觉对象 =====
    // bulletId → 客户端的子弹 GameObject，收到 DestroyBulletMessage 时按 ID 找到并销毁
    private Dictionary<uint, GameObject> clientBullets = new Dictionary<uint, GameObject>();
    // 子弹预制体缓存（从 Resources/Prefabs/Bullet 加载一次）
    private GameObject bulletPrefabCache;

    public override void Awake()
    {
        base.Awake();
        Application.runInBackground = true;
        // 关闭垂直同步 + 设置帧率下限，防止 Windows 对非活动窗口降帧
        // 导致 TickManager 一帧内补偿大量 Tick 造成消息突发
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        singleton = this;
    }

    /// <summary>
    /// 服务端启动时
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();

        // 注册消息处理器：接收客户端输入
        NetworkServer.RegisterHandler<ClientInputMessage>(OnServerReceiveInput);

        // 创建TickManager
        CreateTickManager();

        // 订阅Tick事件
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick += OnServerTick;
        }

        Debug.Log("[Server] 服务端启动，已注册消息处理器");
    }

    /// <summary>
    /// 客户端连接到服务端时
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();

        // 注册消息处理器：接收服务端状态
        NetworkClient.RegisterHandler<ServerStateMessage>(OnClientReceiveState);
        NetworkClient.RegisterHandler<SpawnBulletMessage>(OnClientSpawnBullet);
        NetworkClient.RegisterHandler<DestroyBulletMessage>(OnClientDestroyBullet);

        // 创建TickManager（如果还没有）
        CreateTickManager();

        Debug.Log("[Client] 客户端启动，已注册消息处理器");
    }

    /// <summary>
    /// 服务端停止时
    /// </summary>
    public override void OnStopServer()
    {
        base.OnStopServer();

        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick -= OnServerTick;
        }

        serverPlayers.Clear();
        inputQueues.Clear();
        serverBullets.Clear();
        nextBulletId = 1;
    }

    /// <summary>
    /// 创建TickManager
    /// </summary>
    private void CreateTickManager()
    {
        if (TickManager.Instance == null)
        {
            GameObject tickObj = new GameObject("TickManager");
            tickObj.AddComponent<TickManager>();
            DontDestroyOnLoad(tickObj);
        }
    }

    // ==========================================
    // 服务端逻辑
    // ==========================================

    /// <summary>
    /// 服务端：玩家加入
    /// </summary>
    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        base.OnServerAddPlayer(conn);

        // 获取生成的玩家对象
        if (conn.identity != null)
        {
            uint netId = conn.identity.netId;

            // 初始化玩家数据
            serverPlayers[netId] = new ServerPlayerData
            {
                netId = netId,
                connectionId = conn.connectionId,
                position = conn.identity.transform.position,
                rotationY = conn.identity.transform.eulerAngles.y,
                health = 100,
                isAlive = true,
                moveSpeed = 8f,
                lastProcessedInput = 0
            };

            inputQueues[netId] = new Queue<ClientInputMessage>();

            Debug.Log($"[Server] 玩家加入: NetId={netId}");
        }
    }

    /// <summary>
    /// 服务端：玩家离开
    /// </summary>
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (conn.identity != null)
        {
            uint netId = conn.identity.netId;
            serverPlayers.Remove(netId);
            inputQueues.Remove(netId);
            Debug.Log($"[Server] 玩家离开: NetId={netId}");
        }

        base.OnServerDisconnect(conn);
    }

    /// <summary>
    /// 服务端：收到客户端输入
    /// </summary>
    private void OnServerReceiveInput(NetworkConnectionToClient conn, ClientInputMessage msg)
    {
        if (conn.identity == null) return;

        uint netId = conn.identity.netId;

        if (inputQueues.ContainsKey(netId))
        {
            inputQueues[netId].Enqueue(msg);
        }
    }

    /// <summary>
    /// 服务端：每个Tick执行
    /// </summary>
    // 缓存key列表，避免每Tick分配
    private List<uint> tempNetIds = new List<uint>();

    private void OnServerTick(uint tick)
    {
        // 1. 处理所有玩家的输入
        // 注意：不能直接 foreach serverPlayers，因为 ProcessPlayerInput 会修改字典值，
        // C# Dictionary 在遍历期间被修改会抛出 InvalidOperationException
        tempNetIds.Clear();
        tempNetIds.AddRange(serverPlayers.Keys);

        foreach (uint netId in tempNetIds)
        {
            if (inputQueues.ContainsKey(netId))
            {
                while (inputQueues[netId].Count > 0)
                {
                    ClientInputMessage input = inputQueues[netId].Dequeue();
                    ProcessPlayerInput(netId, input);
                }
            }
        }

        // 2. 更新所有子弹（移动 + 碰撞检测 + 超时销毁）
        UpdateServerBullets();

        // 3. 广播游戏状态给所有客户端
        BroadcastGameState(tick);
    }

    /// <summary>
    /// 服务端：每Tick更新所有子弹
    /// 移动子弹 → Raycast碰撞检测 → 超时销毁
    /// </summary>
    // 缓存子弹key列表，避免每Tick分配（与 tempNetIds 同理）
    private List<uint> tempBulletIds = new List<uint>();

    private void UpdateServerBullets()
    {
        float tickInterval = TickManager.Instance?.TickInterval ?? (1f / 30f);
        bulletsToRemove.Clear();

        // 遍历 key 快照，避免遍历中修改字典抛 InvalidOperationException
        tempBulletIds.Clear();
        tempBulletIds.AddRange(serverBullets.Keys);

        foreach (uint bulletId in tempBulletIds)
        {
            ServerBulletData bullet = serverBullets[bulletId];

            // 计算本Tick的飞行距离
            float moveDistance = bullet.speed * tickInterval;
            Vector3 oldPos = bullet.position;
            Vector3 newPos = oldPos + bullet.direction * moveDistance;

            // Raycast碰撞检测：从旧位置向飞行方向发射射线，长度=飞行距离*1.2（留余量）
            // 这样即使子弹速度很快也不会"穿墙"
            bool hit = false;
            if (Physics.Raycast(oldPos, bullet.direction, out RaycastHit hitInfo, moveDistance * 1.2f))
            {
                // 忽略发射者自身（用 NetworkIdentity 判断）
                NetworkIdentity hitIdentity = hitInfo.collider.GetComponentInParent<NetworkIdentity>();
                if (hitIdentity == null || hitIdentity.netId != bullet.ownerNetId)
                {
                    hit = true;

                    // 对 Target 造成伤害
                    Target target = hitInfo.collider.GetComponent<Target>();
                    if (target != null)
                    {
                        target.TakeDamage(bulletDamage);
                    }

                    // 广播销毁消息给所有客户端
                    NetworkServer.SendToAll(new DestroyBulletMessage
                    {
                        bulletId = bullet.bulletId,
                        hitPosition = hitInfo.point
                    });

                    bulletsToRemove.Add(bullet.bulletId);
                    Debug.Log($"[Server] 子弹命中: id={bullet.bulletId}, hit={hitInfo.collider.name}");
                }
            }

            if (!hit)
            {
                // 未命中：更新子弹位置（现在安全，因为遍历的是 tempBulletIds 而非字典）
                bullet.position = newPos;
                serverBullets[bulletId] = bullet;

                // 超时销毁
                if (Time.time - bullet.spawnTime >= bulletMaxLifetime)
                {
                    NetworkServer.SendToAll(new DestroyBulletMessage
                    {
                        bulletId = bullet.bulletId,
                        hitPosition = newPos
                    });
                    bulletsToRemove.Add(bullet.bulletId);
                }
            }
        }

        // 统一删除已销毁的子弹（不在遍历中修改字典）
        foreach (uint id in bulletsToRemove)
        {
            serverBullets.Remove(id);
        }
    }

    /// <summary>
    /// 处理玩家输入
    /// </summary>
    private void ProcessPlayerInput(uint netId, ClientInputMessage input)
    {
        if (!serverPlayers.ContainsKey(netId)) return;

        ServerPlayerData player = serverPlayers[netId];

        // 计算移动
        Vector3 moveDir = new Vector3(input.moveX, 0, input.moveY).normalized;
        float tickInterval = TickManager.Instance?.TickInterval ?? (1f / 30f);
        player.position += moveDir * player.moveSpeed * tickInterval;

        // 更新旋转
        player.rotationY = input.aimAngle;

        // 更新最后处理的输入序号
        player.lastProcessedInput = input.sequence;

        // 处理射击
        if (input.isShooting)
        {
            ServerSpawnBullet(netId, player);
        }

        serverPlayers[netId] = player;

        // 高频日志已移除（每Tick每玩家都会触发，严重影响性能）
        // 调试时可取消注释：
        // Debug.Log($"[Server] 处理输入: NetId={netId}, seq={input.sequence}, pos={player.position}");
    }

    /// <summary>
    /// 服务端：生成一颗子弹并广播给所有客户端
    /// </summary>
    private void ServerSpawnBullet(uint ownerNetId, ServerPlayerData player)
    {
        // 1. 根据玩家朝向计算枪口位置和射击方向
        Quaternion rot = Quaternion.Euler(0, player.rotationY, 0);
        Vector3 forward = rot * Vector3.forward;                          // 玩家面朝方向
        Vector3 firePos = player.position + rot * new Vector3(0, 0.5f, 0.8f); // 枪口偏移（与SyncPlayerController中FirePoint一致）

        // 2. 创建服务器子弹数据
        uint bulletId = nextBulletId++;
        serverBullets[bulletId] = new ServerBulletData
        {
            bulletId = bulletId,
            ownerNetId = ownerNetId,
            position = firePos,
            direction = forward,
            speed = defaultBulletSpeed,
            spawnTime = Time.time
        };

        // 3. 广播 SpawnBulletMessage 给所有客户端
        SpawnBulletMessage msg = new SpawnBulletMessage
        {
            bulletId = bulletId,
            ownerNetId = ownerNetId,
            position = firePos,
            direction = forward,
            speed = defaultBulletSpeed
        };
        NetworkServer.SendToAll(msg);

        Debug.Log($"[Server] 生成子弹: id={bulletId}, owner={ownerNetId}, pos={firePos}");
    }

    /// <summary>
    /// 同步玩家Transform
    /// </summary>
    private void SyncPlayerTransform(uint netId, ServerPlayerData data)
    {
        if (NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity))
        {
            identity.transform.position = data.position;
            identity.transform.rotation = Quaternion.Euler(0, data.rotationY, 0);
        }
    }

    /// <summary>
    /// 广播游戏状态
    /// </summary>
    private void BroadcastGameState(uint tick)
    {
        // 构建所有玩家状态
        PlayerStateData[] playersArray = new PlayerStateData[serverPlayers.Count];
        int i = 0;
        foreach (var kvp in serverPlayers)
        {
            ServerPlayerData sp = kvp.Value;
            playersArray[i++] = new PlayerStateData
            {
                netId = sp.netId,
                position = sp.position,
                rotationY = sp.rotationY,
                health = sp.health,
                isAlive = sp.isAlive
            };
        }

        // 给每个客户端发送状态（包含该客户端的lastProcessedInput）
        foreach (var kvp in serverPlayers)
        {
            uint netId = kvp.Key;
            ServerPlayerData sp = kvp.Value;

            // 找到对应的连接
            if (NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity))
            {
                if (identity.connectionToClient != null)
                {
                    ServerStateMessage msg = new ServerStateMessage
                    {
                        serverTick = tick,
                        yourLastProcessedInput = sp.lastProcessedInput,
                        players = playersArray
                    };

                    identity.connectionToClient.Send(msg);
                }
            }
        }

        // 高频日志已移除（每Tick触发，30Hz = 每秒30条）
        // 调试时可取消注释：
        // Debug.Log($"[Server] 广播状态: Tick={tick}, 玩家数={serverPlayers.Count}");
    }

    // ==========================================
    // 客户端逻辑
    // ==========================================

    /// <summary>
    /// 客户端：收到服务端状态
    /// </summary>
    private void OnClientReceiveState(ServerStateMessage msg)
    {
        // 交给本地玩家的预测组件处理
        // 这个会在阶段4实现
        ClientStateReceiver.OnReceiveState?.Invoke(msg);
    }

    /// <summary>
    /// 客户端：收到子弹生成消息
    /// 从 Resources 加载 Bullet 预制体，实例化并设置飞行参数。
    /// 子弹的视觉飞行由 SimpleBullet.Update 驱动（纯客户端）。
    /// </summary>
    private void OnClientSpawnBullet(SpawnBulletMessage msg)
    {
        // 延迟加载预制体（只加载一次，缓存复用）
        if (bulletPrefabCache == null)
        {
            bulletPrefabCache = Resources.Load<GameObject>("Prefabs/Bullet");
            if (bulletPrefabCache == null)
            {
                Debug.LogError("[Client] 无法加载子弹预制体: Resources/Prefabs/Bullet");
                return;
            }
        }

        // 实例化子弹，朝向飞行方向
        Quaternion rotation = Quaternion.LookRotation(msg.direction);
        GameObject bulletObj = Instantiate(bulletPrefabCache, msg.position, rotation);

        // 设置飞行参数
        SimpleBullet bullet = bulletObj.GetComponent<SimpleBullet>();
        if (bullet != null)
        {
            bullet.direction = msg.direction;
            bullet.speed = msg.speed;
            bullet.bulletId = msg.bulletId;
        }

        // 记录到字典，收到 DestroyBulletMessage 时按 ID 销毁
        clientBullets[msg.bulletId] = bulletObj;
    }

    /// <summary>
    /// 客户端：收到子弹销毁消息
    /// 通过 bulletId 找到客户端的子弹 GameObject 并销毁，播放命中特效。
    /// </summary>
    private void OnClientDestroyBullet(DestroyBulletMessage msg)
    {
        if (clientBullets.TryGetValue(msg.bulletId, out GameObject bulletObj))
        {
            // 播放命中特效（如果预制体配置了 hitEffect）
            if (bulletObj != null)
            {
                SimpleBullet bullet = bulletObj.GetComponent<SimpleBullet>();
                if (bullet != null && bullet.hitEffect != null)
                {
                    GameObject fx = Instantiate(bullet.hitEffect, msg.hitPosition, Quaternion.identity);
                    Destroy(fx, 2f); // 特效2秒后自动清理
                }

                Destroy(bulletObj);
            }

            clientBullets.Remove(msg.bulletId);
        }
    }
}
