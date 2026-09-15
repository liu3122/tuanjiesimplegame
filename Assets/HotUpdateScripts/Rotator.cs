using UnityEngine;

namespace Game.HotUpdate
{
    /// <summary>
    /// 热更脚本示例：挂在资源 prefab 上，随资源一起被加载实例化。
    /// HybridCLR 支持资源挂载热更新脚本，这是它相对其他热更方案的招牌能力。
    /// </summary>
    public class Rotator : MonoBehaviour
    {
        /// <summary>旋转速度（由配置表 TbGlobal 驱动）</summary>
        public float RotateSpeed = 90f;

        private void Update()
        {
            transform.Rotate(Vector3.up, RotateSpeed * Time.deltaTime, Space.World);
        }
    }
}
