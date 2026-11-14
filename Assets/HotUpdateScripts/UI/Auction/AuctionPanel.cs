using System.Linq;
using System.Threading.Tasks;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>拍卖行（prefab Bind 模式）：AuctionScreen 加载后挂本组件。列表自动刷新（失败自动退避）。
    /// 行对象池化：3 秒轮询只改文本，不再 Destroy/重建整列表（旧实现是小游戏最大 GC 源）。</summary>
    public class AuctionPanel : MonoBehaviour
    {
        private JSONNode _lots;
        private long _selectedLot = -1;
        // 行池（Button + label 缓存；行数变化才增删对象）
        private readonly System.Collections.Generic.List<Button> _rowBtns = new System.Collections.Generic.List<Button>();
        private readonly System.Collections.Generic.List<Text> _rowLabels = new System.Collections.Generic.List<Text>();
        private Text _detail;
        private Button _bidBtn;
        private Text _bidText;
        private Button _buyoutBtn;
        private Text _buyoutText;
        private float _refreshTimer = 99f;
        /// <summary>轮询间隔：成功 3s；连续失败指数退避至 15s（防网络抖动时空转刷请求）</summary>
        private float _refreshInterval = 3f;
        private bool _refreshInFlight;
        private Transform _listRoot;

        public static async void Show()
        {
            UITheme.PreloadAtlas("main");
            var root = await UIManager.ShowAsync("AuctionScreen");
            if (root == null) { UIRoot.ToastStatic(L10n.Get("界面加载失败")); return; }

            var panel = root.GetComponent<AuctionPanel>();
            if (panel == null) panel = root.AddComponent<AuctionPanel>();
            await panel.Bind(root);
        }

        private async Task Bind(GameObject root)
        {
            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => Screens.ShowMain(true));

            _listRoot = root.transform.Find("list_bg/list");
            _detail = UIManager.Find<Text>(root.transform, "detail_panel/detail");
            _bidBtn = UIManager.Find<Button>(root.transform, "detail_panel/btn_bid");
            _bidText = UIManager.Find<Text>(root.transform, "detail_panel/btn_bid/label");
            _bidBtn.onClick.RemoveAllListeners();
            _bidBtn.onClick.AddListener(OnBid);
            _buyoutBtn = UIManager.Find<Button>(root.transform, "detail_panel/btn_buyout");
            _buyoutText = UIManager.Find<Text>(root.transform, "detail_panel/btn_buyout/label");
            _buyoutBtn.onClick.RemoveAllListeners();
            _buyoutBtn.onClick.AddListener(OnBuyout);

            await RefreshData();
            if (_lots == null)
            {
                UIRoot.ToastStatic(L10n.Get("获取拍品失败"));
                return;
            }
            Redraw();
        }

        private async Task<bool> RefreshData()
        {
            var r = await ApiClient.GetAuctionsAsync();
            bool ok = r != null && r["ok"].AsBool;
            if (ok) _lots = r["data"];
            return ok;
        }

        private void Update()
        {
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < _refreshInterval || _refreshInFlight) return;
            _refreshTimer = 0f;
            _ = AutoRefresh();
        }

        private async Task AutoRefresh()
        {
            _refreshInFlight = true;
            try
            {
                bool ok = await RefreshData();
                _refreshInterval = ok ? 3f : System.Math.Min(15f, _refreshInterval * 1.7f); // 失败退避，成功恢复
                if (_lots != null && this != null) Redraw();
            }
            finally { _refreshInFlight = false; }
        }

        private void Redraw()
        {
            int count = _lots.Count;

            // 行池：只增删差额行，其余复用（每 3 秒重建 N 行 GameObject 是小游戏 GC 大头）
            while (_rowBtns.Count < count)
            {
                int idx = _rowBtns.Count;
                var row = UITheme.ThemedButton(_listRoot, Vector2.zero, new Vector2(940, 96), "", "blue", 20);
                UITheme.SkinButton(row, "gold");
                var label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.fontSize = 21;
                row.onClick.AddListener(() => { _selectedLot = _lots[idx]["lotId"].AsLong; Redraw(); });
                _rowBtns.Add(row);
                _rowLabels.Add(label);
            }
            while (_rowBtns.Count > count)
            {
                int last = _rowBtns.Count - 1;
                if (_rowBtns[last] != null) Destroy(_rowBtns[last].gameObject);
                _rowBtns.RemoveAt(last);
                _rowLabels.RemoveAt(last);
            }

            float y = 340;
            for (int i = 0; i < count; i++)
            {
                var lot = _lots[i];
                var row = _rowBtns[i];
                var label = _rowLabels[i];
                if (row == null) continue; // 池外销毁（理论不发生）
                var rt = row.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(0, y);
                var config = GameConfig.Tables.TbChest.GetOrDefault(lot["configId"].AsInt);

                long currentBid = lot["currentBid"].AsLong;
                long startPrice = lot["startPrice"].AsLong;
                long buyoutPrice = lot["buyoutPrice"].AsLong;
                string seller = lot["isMine"].AsBool ? L10n.Get("你") : lot["seller"].Value;
                string price = currentBid > 0
                    ? "<b>" + currentBid.ToString("N0") + "</b>（" + lot["currentBidder"].Value + "）"
                    : "<b>" + startPrice.ToString("N0") + "</b> 起";
                string buyout = buyoutPrice > 0 ? "  一口 " + buyoutPrice.ToString("N0") : "";
                string identTag = lot["identified"].AsBool ? "[已鉴定] " : "";
                label.text = "[" + seller + "] " + identTag + (config != null ? config.Name : "?") + "  剩" + lot["remainingCount"].AsInt
                    + "格  EV " + lot["expectedValue"].AsLong.ToString("N0") + "  技能" + lot["skillUses"].AsInt + "/" + lot["skillUsesMax"].AsInt
                    + "  " + price + buyout + "  剩" + lot["endInSec"].AsInt + "s";
                row.image.color = lot["lotId"].AsLong == _selectedLot
                    ? new Color(0.35f, 0.42f, 0.55f, 0.95f)
                    : new Color(0.16f, 0.2f, 0.28f, 0.95f);
                y -= 104;
            }

            JSONNode sel = null;
            foreach (JSONNode n in _lots) { if (n["lotId"].AsLong == _selectedLot) { sel = n; break; } }
            if (sel == null)
            {
                _detail.text = "选择左侧拍品\n\n规则：出价至少 +5%，预扣金币；\n被超出价自动退回；到期最高价成交（卖家抽成 5%）；一口价直接成交";
                _bidBtn.interactable = false;
                _buyoutBtn.interactable = false;
                return;
            }

            var selConfig = GameConfig.Tables.TbChest.GetOrDefault(sel["configId"].AsInt);
            long cur = sel["currentBid"].AsLong > 0 ? sel["currentBid"].AsLong : sel["startPrice"].AsLong;
            long next = cur + System.Math.Max(1, (long)(cur * 0.05));
            long selBuyout = sel["buyoutPrice"].AsLong;
            long selBid = sel["currentBid"].AsLong;
            string curBidText = selBid > 0 ? selBid.ToString("N0") + "（" + sel["currentBidder"].Value + "）" : L10n.Get("无");
            _detail.text = "<b>" + (selConfig != null ? selConfig.Name : "?") + "</b>\n卖家 " + sel["seller"].Value
                + "\n\n剩余 " + sel["remainingCount"].AsInt + " 格 · EV " + sel["expectedValue"].AsLong.ToString("N0")
                + "\n技能额度 " + sel["skillUses"].AsInt + "/" + sel["skillUsesMax"].AsInt
                + "\n\n当前出价 " + curBidText + "\n剩余 " + sel["endInSec"].AsInt + " 秒";
            // 已鉴定拍品：展示完整剩余金额分布（信息透明）
            string distText = "";
            if (sel["identified"].AsBool && sel["remainingAmounts"] != null && !sel["remainingAmounts"].IsNull)
            {
                var amounts = new System.Collections.Generic.List<long>();
                foreach (JSONNode a in sel["remainingAmounts"]) amounts.Add(a.AsLong);
                amounts.Sort((a, b) => b.CompareTo(a));
                distText = "\n\n<color=#F5D061>剩余金额分布（已鉴定）：</color>\n" + string.Join("  ", amounts.ConvertAll(a => a.ToString("N0")));
            }

            bool canBid = sel["endInSec"].AsInt > 0 && !sel["isMine"].AsBool && !sel["iAmTop"].AsBool;
            _bidBtn.interactable = canBid;
            _bidText.text = "出价 " + next.ToString("N0");
            _buyoutBtn.interactable = canBid && selBuyout > 0;
            _buyoutText.text = selBuyout > 0 ? "一口价 " + selBuyout.ToString("N0") : L10n.Get("无一口价");
        }

        private async void OnBid()
        {
            if (_bidBtn != null) _bidBtn.interactable = false; // 防双击重复出价（两次预扣）
            var r = await ApiClient.BidAsync(_selectedLot);
            if (this == null) return;
            if (r == null || !r["ok"].AsBool) { UIRoot.ToastStatic(L10n.Get("出价失败：") + Screens.ErrText(r)); }
            else UIRoot.ToastStatic(r["data"]["buyout"].AsBool ? L10n.Get("以一口价拿下！") : "出价成功 " + r["data"]["bid"].AsLong.ToString("N0"));
            if (UIRoot.Instance != null) UIRoot.Instance.RefreshTop(); // 金币变动只在操作后刷，不随 3s 轮询
            await AutoRefresh();
        }

        private async void OnBuyout()
        {
            if (_buyoutBtn != null) _buyoutBtn.interactable = false; // 防双击重复买断
            var r = await ApiClient.BuyoutAsync(_selectedLot);
            if (this == null) return;
            if (r == null || !r["ok"].AsBool) { UIRoot.ToastStatic(L10n.Get("失败：") + Screens.ErrText(r)); }
            if (UIRoot.Instance != null) UIRoot.Instance.RefreshTop();
            await AutoRefresh();
        }

        /// <summary>挂拍对话框（从背包发起，动态生成）</summary>
        public static void OpenListDialog(JSONNode chest)
        {
            long uid = chest["uid"].AsLong;
            long ev = (long)chest["expectedValue"].AsDouble;

            var dialog = UITheme.CreatePanel(UIRoot.Instance.CanvasTransform, "panel_gold", Vector2.zero, new Vector2(660, 460));
            var rt = dialog.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var title = TDUIHelper.MakeText(dialog.transform, new Vector2(0, -30), TextAnchor.MiddleCenter, 30);
            title.text = "挂拍（EV " + ev.ToString("N0") + " · 技能 " + chest["skillUsesRemaining"].AsInt + "/3）";
            TDUIHelper.SetSize(title.rectTransform, 620, 50);

            var l1 = TDUIHelper.MakeText(dialog.transform, new Vector2(-250, -110), TextAnchor.MiddleLeft, 24);
            l1.text = L10n.Get("起拍价");
            var startInput = TDUIHelper.MakeInputField(dialog.transform, new Vector2(60, -110), new Vector2(280, 56), ((long)(ev * 0.8)).ToString());

            var l2 = TDUIHelper.MakeText(dialog.transform, new Vector2(-250, -190), TextAnchor.MiddleLeft, 24);
            l2.text = L10n.Get("一口价");
            var buyoutInput = TDUIHelper.MakeInputField(dialog.transform, new Vector2(60, -190), new Vector2(280, 56), ((long)(ev * 1.2)).ToString());

            var tip = TDUIHelper.MakeText(dialog.transform, new Vector2(0, -260), TextAnchor.MiddleCenter, 20);
            tip.color = new Color(0.7f, 0.75f, 0.8f);
            tip.text = L10n.Get("时长 60 秒，流拍退回；一口价填 0 表示不启用");
            TDUIHelper.SetSize(tip.rectTransform, 620, 40);

            var ok = UITheme.ThemedButton(dialog.transform, new Vector2(-100, -360), new Vector2(160, 56), L10n.Get("挂 拍"), "green", 24);
            ok.onClick.AddListener(async () =>
            {
                long start, buyout;
                if (!long.TryParse(startInput.text, out start) || start <= 0) { UIRoot.ToastStatic(L10n.Get("起拍价无效")); return; }
                long.TryParse(buyoutInput.text, out buyout);
                var r = await ApiClient.ListAuctionAsync(uid, start, buyout);
                if (r == null || !r["ok"].AsBool) { UIRoot.ToastStatic(L10n.Get("挂拍失败：") + Screens.ErrText(r)); return; }
                Object.Destroy(dialog);
                UIRoot.ToastStatic(L10n.Get("已挂拍，可到拍卖行围观"));
                UIRoot.Instance.RefreshTop();
                await OnlineState.Refresh();
                Screens.ShowBag();
            });

            var cancel = UITheme.ThemedButton(dialog.transform, new Vector2(100, -360), new Vector2(160, 56), L10n.Get("取消"), "red", 24);
            cancel.onClick.AddListener(() => Object.Destroy(dialog));
        }
    }
}
