using cfg.tower;
using UnityEngine;

namespace Game.HotUpdate.TowerDefense
{
    /// <summary>防御塔：索敌（射程内最前方）、按攻速发射子弹</summary>
    public class TDTower : MonoBehaviour
    {
        public Tower Config { get; private set; }

        private Transform _head;
        private Transform _muzzle;
        private float _nextFireTime;

        public void Init(Tower config)
        {
            Config = config;
            Color color = TDUtil.Hex(config.TowerColor);

            // Q 版造型：圆柱底座 + 方形炮身 + 球形炮口
            var baseCyl = TDUtil.Primitive(PrimitiveType.Cylinder, new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f), "base");
            baseCyl.transform.SetParent(transform, false);
            baseCyl.transform.localScale = new Vector3(0.9f, 0.15f, 0.9f);
            baseCyl.transform.localPosition = new Vector3(0, 0.15f, 0);

            var head = new GameObject("head");
            head.transform.SetParent(transform, false);
            head.transform.localPosition = new Vector3(0, 0.55f, 0);
            _head = head.transform;

            var body = TDUtil.Primitive(PrimitiveType.Cube, color, "body");
            body.transform.SetParent(_head, false);
            body.transform.localScale = new Vector3(0.55f, 0.45f, 0.55f);

            var muzzle = TDUtil.Primitive(PrimitiveType.Sphere, new Color(0.15f, 0.15f, 0.15f), "muzzle");
            muzzle.transform.SetParent(_head, false);
            muzzle.transform.localScale = Vector3.one * 0.18f;
            muzzle.transform.localPosition = new Vector3(0, 0, 0.35f);
            _muzzle = muzzle.transform;

            // 射程圈提示
            var rangeRing = TDUtil.Primitive(PrimitiveType.Sphere, new Color(1f, 1f, 1f, 0.08f), "range");
            rangeRing.transform.SetParent(transform, false);
            rangeRing.transform.localScale = new Vector3(config.AttackRange * 2, 0.02f, config.AttackRange * 2);
            rangeRing.transform.localPosition = new Vector3(0, 0.05f, 0);
            Object.Destroy(rangeRing.GetComponent<Collider>());
            var ringMat = rangeRing.GetComponent<MeshRenderer>().material;
            ringMat.SetFloat("_Mode", 3);
            ringMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ringMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ringMat.SetInt("_ZWrite", 0);
            ringMat.renderQueue = 3000;
        }

        private void Update()
        {
            if (TDGame.Instance == null || TDGame.Instance.State == TDGameState.Win || TDGame.Instance.State == TDGameState.Lose)
            {
                return;
            }

            TDEnemy target = FindTarget();
            if (target == null)
            {
                return;
            }

            // 炮身转向目标
            Vector3 aim = target.transform.position - transform.position;
            aim.y = 0;
            if (aim != Vector3.zero)
            {
                _head.rotation = Quaternion.Slerp(_head.rotation, Quaternion.LookRotation(aim), 12f * Time.deltaTime);
            }

            if (Time.time >= _nextFireTime)
            {
                _nextFireTime = Time.time + Config.AttackInterval;
                Fire(target);
            }
        }

        /// <summary>优先攻击走得最远的敌人</summary>
        private TDEnemy FindTarget()
        {
            TDEnemy best = null;
            foreach (TDEnemy enemy in TDGame.Instance.Enemies)
            {
                if (enemy.IsDead)
                {
                    continue;
                }
                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist > Config.AttackRange)
                {
                    continue;
                }
                if (best == null || enemy.Progress > best.Progress)
                {
                    best = enemy;
                }
            }
            return best;
        }

        private void Fire(TDEnemy target)
        {
            var bulletGo = new GameObject("bullet");
            bulletGo.transform.position = _muzzle.position;
            var bullet = bulletGo.AddComponent<TDBullet>();
            bullet.Init(this, target);
        }
    }
}
