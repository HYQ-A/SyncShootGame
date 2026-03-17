using UnityEngine;
using Mirror;

public class NetworkPlayer : NetworkBehaviour
{
    public float moveSpeed = 8f;
    
    void Update()
    {
        // 只有本地玩家才能控制
        if (!isLocalPlayer) return;
        
        // WASD移动
        float h = Input.GetKey(KeyCode.A) ? -1 : Input.GetKey(KeyCode.D) ? 1 : 0;
        float v = Input.GetKey(KeyCode.S) ? -1 : Input.GetKey(KeyCode.W) ? 1 : 0;
        
        Vector3 move = new Vector3(h, 0, v).normalized * moveSpeed * Time.deltaTime;
        transform.position += move;
    }
}