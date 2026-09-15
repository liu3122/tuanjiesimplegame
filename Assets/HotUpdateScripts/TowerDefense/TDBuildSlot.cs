using UnityEngine;

namespace Game.HotUpdate.TowerDefense
{
    /// <summary>可建塔的点位</summary>
    public class TDBuildSlot : MonoBehaviour
    {
        public bool IsOccupied { get; private set; }
        public int SlotIndex { get; private set; }

        private Material _mat;
        private static readonly Color FreeColor = new Color(0.4f, 0.9f, 0.5f, 0.35f);
        private static readonly Color HoverColor = new Color(1f, 1f, 0.4f, 0.55f);

        public void Init(int index)
        {
            SlotIndex = index;
            var plate = TDUtil.Primitive(PrimitiveType.Cube, FreeColor, "slot");
            plate.transform.SetParent(transform, false);
            plate.transform.localScale = new Vector3(1f, 0.15f, 1f);
            plate.transform.localPosition = new Vector3(0, 0.08f, 0);
            _mat = plate.GetComponent<MeshRenderer>().material;
            _mat.SetFloat("_Mode", 3);
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_ZWrite", 0);
            _mat.renderQueue = 3000;

            // OnMouseEnter 需要 Collider 与脚本同物体
            Object.Destroy(plate.GetComponent<Collider>());
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, 0.08f, 0);
            collider.size = new Vector3(1f, 0.15f, 1f);
        }

        private void OnMouseEnter()
        {
            if (!IsOccupied && TDGame.Instance != null && TDGame.Instance.SelectedTowerId > 0)
            {
                _mat.color = HoverColor;
            }
        }

        private void OnMouseExit()
        {
            _mat.color = FreeColor;
        }

        /// <summary>在此点位建造防御塔</summary>
        public bool TryBuild()
        {
            if (IsOccupied)
            {
                return false;
            }
            var tower = TDGame.Instance.BuildTower(transform.position);
            if (tower == null)
            {
                return false;
            }
            tower.transform.SetParent(transform, true); // 保持世界位置（BuildTower 已按塔位定位）
            IsOccupied = true;
            _mat.color = new Color(0, 0, 0, 0);
            return true;
        }
    }
}
