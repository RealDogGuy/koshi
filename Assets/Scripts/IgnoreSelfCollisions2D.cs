using UnityEngine;

public class IgnoreSelfCollisions2D : MonoBehaviour
{
    void Start()
    {
        Collider2D[] cols = GetComponentsInChildren<Collider2D>();

        for (int i = 0; i < cols.Length; i++)
        {
            for (int j = i + 1; j < cols.Length; j++)
            {
                Physics2D.IgnoreCollision(cols[i], cols[j], true);
            }
        }
    }
}