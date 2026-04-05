using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ============================================
/// Tick管理器 - 逻辑帧管理
/// ============================================
/// 游戏逻辑以固定频率（如30Hz或60Hz）运行
/// 与渲染帧率分离，保证逻辑一致性
/// </summary>
public class TickManager : MonoBehaviour
{
    public static TickManager Instance { get; private set; }

    [Header("===== Tick设置 =====")]
    [Tooltip("每秒逻辑帧数")]
    public int tickRate = 30;  // 30Hz，每秒30个逻辑帧

    /// <summary>
    /// 当前逻辑帧号
    /// </summary>
    [Header("===== 运行状态 =====")]
    [Tooltip("当前帧数")]
    [SerializeField] private uint currentTick;

    public uint CurrentTick => currentTick;

    /// <summary>
    /// 每个Tick的时间间隔（秒）
    /// </summary>
    public float TickInterval => 1f / tickRate;

    /// <summary>
    /// Tick事件 - 其他脚本订阅此事件来执行逻辑
    /// </summary>
    public event System.Action<uint> OnTick;

    // 计时器
    private float tickTimer;

    void Awake()
    {
        // 单例
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Update()
    {
        tickTimer += Time.deltaTime;

        // 执行所有累积的Tick（处理掉帧情况）
        while (tickTimer >= TickInterval)
        {
            tickTimer -= TickInterval;
            currentTick++;

            // 触发Tick事件
            OnTick?.Invoke(CurrentTick);
        }
    }

    /// <summary>
    /// 获取当前Tick的插值比例（用于渲染平滑）
    /// 返回 0-1 之间的值，表示当前渲染时刻在两个Tick之间的位置
    /// </summary>
    public float GetTickAlpha()
    {
        return tickTimer / TickInterval;
    }

    /// <summary>
    /// 同步服务端Tick（客户端使用）
    /// </summary>
    public void SyncTick(uint serverTick)
    {
        // 简单同步：直接采用服务端Tick
        // 实际项目中需要更复杂的时钟同步算法
        currentTick = serverTick;
    }
}
