using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("===== 移动设置 =====")]
    [Tooltip("移动速度")]
    public float moveSpeed = 8f;

    [Tooltip("重力")]
    public float gravity = -20f;

    [Header("===== 射击设置 =====")]
    [Tooltip("子弹预制体")]
    public GameObject bulletPrefab;

    [Tooltip("枪口位置（子弹生成点）")]
    public Transform firePoint;

    [Tooltip("子弹速度")]
    public float bulletSpeed = 0.5f;

    [Tooltip("射击间隔（秒）")]
    public float fireRate = 0.2f;

    [Tooltip("子弹伤害")]
    public int bulletDamage = 10;

    [Header("===== 瞄准设置 =====")]
    [Tooltip("瞄准平面高度")]
    public float aimHeight = 0.5f;

    // 私有变量
    private CharacterController controller;
    private Camera mainCamera;
    private Vector3 velocity;
    private float nextFireTime;
    private Plane aimPlane;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        mainCamera = Camera.main;
        aimPlane = new Plane(Vector3.up, new Vector3(0, aimHeight, 0));

        // 检查必要组件
        if (bulletPrefab == null)
            Debug.LogWarning("[PlayerController] 未设置子弹预制体！");

        if (firePoint == null)
        {
            Debug.LogWarning("[PlayerController] 未设置枪口位置，将使用玩家位置作为发射点");
            // 自动创建一个发射点
            GameObject fp = new GameObject("FirePoint");
            fp.transform.SetParent(transform);
            fp.transform.localPosition = new Vector3(0, 0.5f, 0.8f);
            firePoint = fp.transform;
        }
    }

    void Update()
    {
        if (!isLocalPlayer)
            return;

        HandleMovement();
        HandleAiming();
        HandleShooting();
    }

    /// <summary>
    /// 处理WASD移动
    /// </summary>
    void HandleMovement()
    {
        // 获取输入
        float horizontal = 0f;
        float vertical = 0f;

        if (Input.GetKey(KeyCode.W)) vertical += 1f;
        if (Input.GetKey(KeyCode.S)) vertical -= 1f;
        if (Input.GetKey(KeyCode.A)) horizontal -= 1f;
        if (Input.GetKey(KeyCode.D)) horizontal += 1f;

        // 计算移动方向（世界坐标）
        Vector3 moveDirection = new Vector3(horizontal, 0, vertical);

        // 归一化防止斜向移动过快
        if (moveDirection.magnitude > 1f)
            moveDirection.Normalize();

        // 应用移动
        controller.Move(moveDirection * moveSpeed * Time.deltaTime);

        // 应用重力
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    /// <summary>
    /// 处理鼠标瞄准（角色朝向鼠标方向）
    /// </summary>
    void HandleAiming()
    {
        if (mainCamera == null) return;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (aimPlane.Raycast(ray, out float distance))
        {
            Vector3 hitPoint = ray.GetPoint(distance);
            Vector3 lookDirection = hitPoint - transform.position;
            lookDirection.y = 0;

            if (lookDirection.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection);
            }
        }
    }

    /// <summary>
    /// 处理鼠标左键射击
    /// </summary>
    void HandleShooting()
    {
        // 检查是否按下鼠标左键且冷却完成
        if (Input.GetMouseButton(0) && Time.time >= nextFireTime)
        {
            Shoot();
            nextFireTime = Time.time + fireRate;
        }
    }

    /// <summary>
    /// 发射子弹
    /// </summary>
    void Shoot()
    {
        if (bulletPrefab == null) return;

        // 生成子弹
        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);

        // 设置子弹速度
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = firePoint.forward * bulletSpeed;
        }

        // 如果子弹有SimpleBullet脚本，初始化它
        // SimpleBullet simpleBullet = bullet.GetComponent<SimpleBullet>();
        // if (simpleBullet != null)
        // {
        //     simpleBullet.damage = bulletDamage;
        // }

        // 3秒后自动销毁子弹
        Destroy(bullet, 5f);
    }
}
