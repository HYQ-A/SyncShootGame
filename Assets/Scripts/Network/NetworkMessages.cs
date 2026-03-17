using Mirror;
using UnityEngine;

/// <summary>
/// ============================================
/// 网络消息定义
/// ============================================
/// 定义客户端和服务端之间通信的数据结构
/// </summary>

// ==========================================
// 客户端 → 服务端：玩家输入消息
// ==========================================
public struct ClientInputMessage : NetworkMessage
{
    public uint tick;           // 逻辑帧号
    public uint sequence;       // 输入序号（用于确认和回滚）
    public float moveX;         // 移动输入 X（-1 到 1）
    public float moveY;         // 移动输入 Y（-1 到 1）
    public float aimAngle;      // 瞄准角度（0-360度）
    public bool isShooting;     // 是否射击
}

// ==========================================
// 服务端 → 客户端：游戏状态消息
// ==========================================
public struct ServerStateMessage : NetworkMessage
{
    public uint serverTick;             // 服务端当前帧号
    public uint yourLastProcessedInput; // 服务端处理的你的最后一个输入序号
    public PlayerStateData[] players;   // 所有玩家的状态
}

// ==========================================
// 玩家状态数据
// ==========================================
[System.Serializable]
public struct PlayerStateData
{
    public uint netId;          // 网络ID（区分不同玩家）
    public Vector3 position;    // 位置
    public float rotationY;     // Y轴旋转
    public int health;          // 生命值
    public bool isAlive;        // 是否存活
}

// ==========================================
// 子弹生成消息（服务端 → 客户端）
// ==========================================
public struct SpawnBulletMessage : NetworkMessage
{
    public uint bulletId;       // 子弹ID
    public uint ownerNetId;     // 发射者网络ID
    public Vector3 position;    // 生成位置
    public Vector3 direction;   // 飞行方向
    public float speed;         // 速度
}

// ==========================================
// 子弹销毁消息（服务端 → 客户端）
// ==========================================
public struct DestroyBulletMessage : NetworkMessage
{
    public uint bulletId;       // 子弹ID
    public Vector3 hitPosition; // 命中位置（用于特效）
}
