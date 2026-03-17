using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Target : MonoBehaviour
{
    public int health = 30;
    
    public void TakeDamage(int damage)
    {
        health -= damage;
        Debug.Log($"靶子受到 {damage} 伤害，剩余血量：{health}");
        
        if (health <= 0)
        {
            Destroy(gameObject);
        }
    }
}
