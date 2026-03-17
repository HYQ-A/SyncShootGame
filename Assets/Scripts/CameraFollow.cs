using UnityEngine;

/// <summary>
/// 俯视角摄像机跟随脚本
/// 摄像机只跟随玩家位置，不跟随旋转（避免循环依赖）
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("===== 跟随目标 =====")]
    public Transform target;

    [Header("===== 摄像机设置 =====")]
    [Tooltip("摄像机俯视角度")]
    [Range(30f, 90f)]
    public float pitchAngle = 60f;

    [Tooltip("摄像机与玩家的距离")]
    public float distance = 15f;

    [Tooltip("摄像机固定朝向（Y轴旋转）")]
    public float fixedYaw = 0f;

    [Header("===== 平滑设置 =====")]
    [Tooltip("跟随平滑度（值越大越快）")]
    public float smoothSpeed = 10f;

    void Start()
    {
        if (target == null)
        {
            Debug.LogError("[CameraFollow] 未设置跟随目标！");
            return;
        }
        
        // 初始化位置
        SnapToTarget();
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 1. 计算摄像机目标位置（基于固定角度，不跟随玩家旋转）
        float pitchRad = pitchAngle * Mathf.Deg2Rad;
        float yawRad = fixedYaw * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            distance * Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
            distance * Mathf.Sin(pitchRad),
            -distance * Mathf.Cos(pitchRad) * Mathf.Cos(yawRad)
        );

        Vector3 targetPosition = target.position + offset;

        // 2. 平滑移动
        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);

        // 3. 固定旋转（不跟随玩家）
        transform.rotation = Quaternion.Euler(pitchAngle, fixedYaw, 0f);
    }

    /// <summary>
    /// 立即跳转到目标位置
    /// </summary>
    public void SnapToTarget()
    {
        if (target == null) return;

        float pitchRad = pitchAngle * Mathf.Deg2Rad;
        float yawRad = fixedYaw * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            distance * Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
            distance * Mathf.Sin(pitchRad),
            -distance * Mathf.Cos(pitchRad) * Mathf.Cos(yawRad)
        );

        transform.position = target.position + offset;
        transform.rotation = Quaternion.Euler(pitchAngle, fixedYaw, 0f);
    }
}