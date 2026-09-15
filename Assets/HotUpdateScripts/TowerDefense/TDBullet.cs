using UnityEngine;

namespace Game.HotUpdate.TowerDefense
{
    /// <summary>子弹：追踪目标飞行，命中结算伤害/减速</summary>
    public class TDBullet : MonoBehaviour
    {
        private TDTower _owner;
        private TDEnemy _target;
        private Vector3 _lastDir;
        private float _life = 3f;

        private float Speed => _owner.Config.BulletSpeed;

        public void Init(TDTower owner, TDEnemy target)
        {
            _owner = owner;
            _target = target;

            // 炮塔的子弹更大更沉
            float scale = _owner.Config.Damage >= 30 ? 0.32f : 0.16f;
            var ball = TDUtil.Primitive(PrimitiveType.Sphere, TDUtil.Hex(owner.Config.TowerColor), "ball");
            ball.transform.SetParent(transform, false);
            ball.transform.localScale = Vector3.one * scale;
            Object.Destroy(ball.GetComponent<Collider>());
        }

        private void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 dir;
            if (_target != null && !_target.IsDead)
            {
                Vector3 to = _target.transform.position + Vector3.up * 0.5f - transform.position;
                dir = to.normalized;
                _lastDir = dir;

                if (to.magnitude < 0.25f)
                {
                    Hit(_target);
                    return;
                }
            }
            else
            {
                // 目标已死：沿最后方向直飞直至超时
                dir = _lastDir;
            }

            transform.position += dir * Speed * Time.deltaTime;
        }

        private void Hit(TDEnemy enemy)
        {
            var tower = _owner.Config;
            enemy.TakeDamage(tower.Damage);
            if (tower.SlowRate > 0)
            {
                enemy.ApplySlow(tower.SlowRate, tower.SlowDuration);
            }
            Destroy(gameObject);
        }
    }
}
