using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleBullet : MonoBehaviour
{
    [Tooltip("子弹伤害")]
    public int damage = 10;

    [Tooltip("击中特效（可选）")]
    public GameObject hitEffect;

    void OnTriggerEnter(Collider other)
    {
        // 忽略与玩家的碰撞
        if (other.CompareTag("Player")) return;

        // 尝试对靶子造成伤害
        Target target = other.GetComponent<Target>();
        if (target != null)
        {
            target.TakeDamage(damage);
        }

        Debug.Log("销毁子弹");
        // 销毁子弹
        Destroy(gameObject);
    }
}
