using cfg.monster;
using UnityEngine;

namespace Game.HotUpdate.TowerDefense
{
    /// <summary>塔防敌人：沿路径行进、可被攻击、死亡给奖励</summary>
    public class TDEnemy : MonoBehaviour
    {
        public Monster Config { get; private set; }
        public long Hp { get; private set; }
        public bool IsDead { get; private set; }

        /// <summary>行进进度：已走过的拐点数（塔优先攻击最前面的敌人）</summary>
        public int SegmentIndex { get; private set; }
        public float Progress { get; private set; }

        private float _slowUntil;
        private float _slowRate;

        private Transform _hpBarFill;
        private Vector3 _hpBarFillScaleOrigin;

        public void Init(Monster config)
        {
            Config = config;
            Hp = config.Hp;

            // Q 版身体：胶囊 + 两颗眼睛
            float size = Mathf.Clamp(0.55f + config.Hp / 8000f, 0.55f, 1.1f);
            var body = TDUtil.Primitive(PrimitiveType.Capsule, TDUtil.Hex(config.BodyColor), "body");
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(size, size * 0.75f, size);
            body.transform.localPosition = new Vector3(0, size * 0.7f, 0);
            Object.Destroy(body.GetComponent<Collider>()); // 避免挡住塔位点击

            var eyeL = TDUtil.Primitive(PrimitiveType.Sphere, Color.black, "eyeL");
            eyeL.transform.SetParent(transform, false);
            eyeL.transform.localScale = Vector3.one * 0.09f;
            eyeL.transform.localPosition = new Vector3(-0.14f * size, size * 1.0f, 0.32f * size);
            Object.Destroy(eyeL.GetComponent<Collider>());

            var eyeR = TDUtil.Primitive(PrimitiveType.Sphere, Color.black, "eyeR");
            eyeR.transform.SetParent(transform, false);
            eyeR.transform.localScale = Vector3.one * 0.09f;
            eyeR.transform.localPosition = new Vector3(0.14f * size, size * 1.0f, 0.32f * size);
            Object.Destroy(eyeR.GetComponent<Collider>());

            // 头顶血条（红底 + 绿条）
            var barRoot = new GameObject("hpbar");
            barRoot.transform.SetParent(transform, false);
            barRoot.transform.localPosition = new Vector3(0, size * 1.5f + 0.3f, 0);
            barRoot.transform.localRotation = Quaternion.Euler(70, 0, 0); // 面向俯视相机

            var barBg = TDUtil.Primitive(PrimitiveType.Cube, new Color(0.25f, 0.08f, 0.08f), "bg");
            barBg.transform.SetParent(barRoot.transform, false);
            barBg.transform.localScale = new Vector3(0.8f, 0.09f, 0.02f);
            Object.Destroy(barBg.GetComponent<Collider>());

            var barFill = TDUtil.Primitive(PrimitiveType.Cube, new Color(0.3f, 0.9f, 0.35f), "fill");
            barFill.transform.SetParent(barRoot.transform, false);
            barFill.transform.localScale = new Vector3(0.8f, 0.11f, 0.03f);
            barFill.transform.localPosition = new Vector3(0, 0, 0.01f);
            Object.Destroy(barFill.GetComponent<Collider>());
            _hpBarFill = barFill.transform;
            _hpBarFillScaleOrigin = _hpBarFill.localScale;
        }

        private void Update()
        {
            if (IsDead || TDGame.Instance == null || TDGame.Instance.State != TDGameState.Running)
            {
                return;
            }

            float speed = Config.MoveSpeed;
            if (Time.time < _slowUntil)
            {
                speed *= 1f - _slowRate;
            }

            Vector3 pos = transform.position;
            Vector3 target = TDLevel.Waypoints[SegmentIndex + 1];
            target.y = pos.y;
            Vector3 dir = (target - pos);
            float move = speed * Time.deltaTime;

            if (dir.magnitude <= move)
            {
                // 走完当前段
                transform.position = target;
                SegmentIndex++;
                Progress = SegmentIndex;
                if (SegmentIndex >= TDLevel.Waypoints.Length - 1)
                {
                    TDGame.Instance.OnEnemyReachEnd(this);
                    return;
                }
            }
            else
            {
                transform.position = pos + dir.normalized * move;
                transform.rotation = Quaternion.LookRotation(dir.normalized);
                Progress = SegmentIndex + move / Mathf.Max(dir.magnitude, 0.0001f);
            }
        }

        public void ApplySlow(float rate, float duration)
        {
            if (rate <= 0)
            {
                return;
            }
            _slowRate = Mathf.Max(_slowRate, rate);
            _slowUntil = Mathf.Max(_slowUntil, Time.time + duration);
        }

        public void TakeDamage(int damage)
        {
            if (IsDead || TDGame.Instance == null || TDGame.Instance.State != TDGameState.Running)
            {
                return;
            }
            Hp -= damage;
            RefreshHpBar();
            if (Hp <= 0)
            {
                IsDead = true;
                TDGame.Instance.OnEnemyKilled(this);
                StartCoroutine(DeathAnimation());
            }
        }

        private void RefreshHpBar()
        {
            float pct = Mathf.Clamp01((float)Hp / Config.Hp);
            _hpBarFill.localScale = new Vector3(_hpBarFillScaleOrigin.x * pct, _hpBarFillScaleOrigin.y, _hpBarFillScaleOrigin.z);
            _hpBarFill.localPosition = new Vector3(-(1f - pct) * 0.4f, 0, 0.01f);
        }

        /// <summary>Q 版死亡动画：缩小上浮消失</summary>
        private System.Collections.IEnumerator DeathAnimation()
        {
            Vector3 baseScale = transform.localScale;
            float t = 0;
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                float k = 1 - t / 0.4f;
                transform.localScale = baseScale * k;
                transform.position += Vector3.up * Time.deltaTime * 2f;
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
