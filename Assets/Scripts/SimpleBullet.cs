using UnityEngine;

/// <summary>
/// ============================================
/// 子弹视觉脚本（纯客户端表现）
/// ============================================
/// 网络架构下，碰撞判定由服务器完成（Raycast），
/// 客户端只负责让子弹沿方向飞行 + 超时自毁。
///
/// 由 GameNetworkManager.OnClientSpawnBullet 实例化时设置 direction 和 speed。
/// </summary>
public class SimpleBullet : MonoBehaviour
{
    [Tooltip("子弹伤害（仅服务器使用）")]
    public int damage = 10;

    [Tooltip("击中特效（可选）")]
    public GameObject hitEffect;

    [Header("===== 飞行参数（由网络管理器设置）=====")]
    [HideInInspector] public Vector3 direction;   // 飞行方向（单位向量）
    [HideInInspector] public float speed;          // 飞行速度
    [HideInInspector] public uint bulletId;        // 服务器分配的子弹ID

    [Tooltip("子弹最大存活时间（秒），超时自动销毁，防止丢包导致子弹永存")]
    public float maxLifetime = 3f;

    private float spawnTime;

    void Start()
    {
        spawnTime = Time.time;
    }

    void Update()
    {
        // 沿方向匀速飞行
        transform.position += direction * speed * Time.deltaTime;

        // 超时自毁（兜底机制）
        if (Time.time - spawnTime >= maxLifetime)
        {
            Destroy(gameObject);
        }
    }
}
