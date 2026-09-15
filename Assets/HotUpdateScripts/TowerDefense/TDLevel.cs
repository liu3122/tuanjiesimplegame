using UnityEngine;

namespace Game.HotUpdate.TowerDefense
{
    /// <summary>关卡静态数据：敌人路径、塔位坐标（以后可迁移到配置表）</summary>
    public static class TDLevel
    {
        /// <summary>敌人行进路径拐点（y 为地面高度 0）</summary>
        public static readonly Vector3[] Waypoints =
        {
            new Vector3(-14, 0, 8),
            new Vector3(6, 0, 8),
            new Vector3(6, 0, 3),
            new Vector3(-8, 0, 3),
            new Vector3(-8, 0, -2),
            new Vector3(8, 0, -2),
            new Vector3(8, 0, -7),
            new Vector3(-6, 0, -7),
        };

        /// <summary>可建造塔的点位</summary>
        public static readonly Vector3[] BuildSlots =
        {
            new Vector3(-12, 0, 6.6f),
            new Vector3(-4, 0, 6.6f),
            new Vector3(2, 0, 6.6f),
            new Vector3(7.4f, 0, 6),
            new Vector3(7.4f, 0, 4.4f),
            new Vector3(4, 0, 1.4f),
            new Vector3(-2, 0, 1.4f),
            new Vector3(-9.4f, 0, 0.5f),
            new Vector3(-5.5f, 0, -0.6f),
            new Vector3(0, 0, -0.6f),
            new Vector3(5.5f, 0, -0.6f),
            new Vector3(6.6f, 0, -3.5f),
            new Vector3(3, 0, -8.4f),
        };

    /// <summary>关卡包围盒（路径 + 塔位）</summary>
    public static Bounds GetLevelBounds()
    {
        var b = new Bounds(Waypoints[0], Vector3.zero);
        foreach (var p in Waypoints) b.Encapsulate(p);
        foreach (var p in BuildSlots) b.Encapsulate(p);
        b.Expand(2f);
        return b;
    }

    /// <summary>
    /// 自适应相机：迭代拉远，直到关卡包围盒的四个地面角点都落入视锥。
    /// 底部预留 13% 视口高度给建塔按钮，适配任意屏幕比例。
    /// </summary>
    public static void FitCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }
        Bounds b = GetLevelBounds();
        var rot = Quaternion.Euler(70f, 0, 0);
        float dist = 8f;
        for (int i = 0; i < 300; i++)
        {
            cam.transform.position = b.center - rot * Vector3.forward * dist;
            cam.transform.rotation = rot;
            if (AllCornersVisible(cam, b))
            {
                break;
            }
            dist += 0.4f;
        }
    }

    private static bool AllCornersVisible(Camera cam, Bounds b)
    {
        var corners = new Vector3[]
        {
            new Vector3(b.min.x, 0, b.min.z),
            new Vector3(b.max.x, 0, b.min.z),
            new Vector3(b.min.x, 0, b.max.z),
            new Vector3(b.max.x, 0, b.max.z),
        };
        foreach (var c in corners)
        {
            Vector3 vp = cam.WorldToViewportPoint(c);
            if (vp.z <= 0f || vp.x < 0.02f || vp.x > 0.98f || vp.y < 0.13f || vp.y > 0.99f)
            {
                return false;
            }
        }
        return true;
    }
    }

    /// <summary>小工具：hex 颜色、创建基本几何体</summary>
    public static class TDUtil
    {
        public static Color Hex(string hex)
        {
            return new Color32(
                System.Convert.ToByte(hex.Substring(0, 2), 16),
                System.Convert.ToByte(hex.Substring(2, 2), 16),
                System.Convert.ToByte(hex.Substring(4, 2), 16),
                255);
        }

        public static Material SolidMaterial(Color color)
        {
            var shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            mat.SetFloat("_Glossiness", 0.15f);
            return mat;
        }

        /// <summary>创建一个带纯色材质的几何体</summary>
        public static GameObject Primitive(PrimitiveType type, Color color, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = SolidMaterial(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
