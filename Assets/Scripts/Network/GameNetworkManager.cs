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

        // 2. 广播游戏状态给所有客户端
        BroadcastGameState(tick);
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
            // TODO: 在服务端生成子弹，广播给所有客户端
        }

        serverPlayers[netId] = player;
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
    /// </summary>
    private void OnClientSpawnBullet(SpawnBulletMessage msg)
    {
        // TODO: 在客户端生成子弹
    }

    /// <summary>
    /// 客户端：收到子弹销毁消息
    /// </summary>
    private void OnClientDestroyBullet(DestroyBulletMessage msg)
    {
        // TODO: 在客户端销毁子弹，播放特效
    }
}
