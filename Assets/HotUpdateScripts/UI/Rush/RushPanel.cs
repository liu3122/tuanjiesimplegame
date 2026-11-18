using System.Collections.Generic;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.ChestGame
{
    /// <summary>
    /// 抢宝房（红包玩法）：房卡选择 → 加入 → 实时抢 20 格 → 结算。
    /// prefab: RushScreen（room_list 容器 + game_area[slot_0..19/status/feed]）。
    /// </summary>
    public class RushPanel : MonoBehaviour
    {
        private RushWsClient _ws;
        private readonly GameObject[] _slots = new GameObject[20];
        private int[] _amounts;
        private readonly long[] _slotOwner = new long[20];
        private readonly Dictionary<long, string> _names = new Dictionary<long, string>();
        private Text _status;
        private Text _feed;
        private Transform _gameArea;
        private readonly List<string> _feedLines = new List<string>();
        /// <summary>好友私人房房间码（null=公共档位房）</summary>
        private string _privateCode;
        /// <summary>好友房房主"提前开局"按钮（快照 isHost 时显示）</summary>
        private Button _startBtn;

        public static async void Show()
        {
            var root = await UIManager.ShowAsync("RushScreen");
            if (root == null) { UIRoot.ToastStatic(L10n.Get("界面加载失败")); return; }
            var panel = root.GetComponent<RushPanel>() ?? root.AddComponent<RushPanel>();
            panel.Bind(root);
        }

        /// <summary>凭码进入好友私人抢宝房（已在好友面板完成 join，这里只连房）</summary>
        public static async void ShowPrivate(string code)
        {
            var root = await UIManager.ShowAsync("RushScreen");
            if (root == null) { UIRoot.ToastStatic(L10n.Get("界面加载失败")); return; }
            var panel = root.GetComponent<RushPanel>() ?? root.AddComponent<RushPanel>();
            panel._privateCode = code;
            panel.Bind(root);
            var list = root.transform.Find("room_list");
            if (list != null) list.gameObject.SetActive(false); // 私人房不展示档位选择
            var room = GameConfig.Tables.TbRushRoom.GetOrDefault(1); // 展示用默认档位文案（标题/颜色）
            panel.EnterGame(room);
        }

        private void Bind(GameObject root)
        {
            var back = UIManager.Find<Button>(root.transform, "btn_back");
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => { _ws?.Close(); Screens.ShowMain(); });

            _gameArea = root.transform.Find("game_area");
            _gameArea.gameObject.SetActive(false);
            _status = UIManager.Find<Text>(_gameArea, "status");
            _feed = UIManager.Find<Text>(_gameArea, "feed");
            for (int i = 0; i < 20; i++)
            {
                int idx = i;
                var slot = _gameArea.Find("slot_" + i);
                if (slot == null) continue;
                _slots[i] = slot.gameObject;
                var img = slot.GetComponent<Image>();
                img.sprite = UITheme.Load("slot_locked");
                var txt = slot.GetComponentInChildren<Text>();
                txt.text = "?"; txt.fontSize = 34; txt.color = Color.white;
                slot.GetComponent<Button>().onClick.RemoveAllListeners();
                slot.GetComponent<Button>().onClick.AddListener(() => _ws?.Claim(idx));
            }

            BuildRoomCards(root.transform.Find("room_list"));
        }

        private void BuildRoomCards(Transform list)
        {
            foreach (Transform child in list) Destroy(child.gameObject);
            float y = 80;
            foreach (var room in GameConfig.Tables.TbRushRoom.DataList)
            {
                var c = room;
                long poolPreview = (long)(c.Ticket * c.PlayerCount * c.PoolScale);
                var card = UITheme.CreatePanel(list, "panel_dark", new Vector2(0, y), new Vector2(940, 300));

                var name = TDUIHelper.MakeText(card.transform, new Vector2(-290, 80), TextAnchor.MiddleCenter, 36);
                name.text = "<color=#" + c.ColorHex + ">" + c.Name + (c.Blind ? L10n.Get("（盲抢）") : "") + "</color>";
                TDUIHelper.SetSize(name.rectTransform, 420, 70);
                var info = TDUIHelper.MakeText(card.transform, new Vector2(-280, -30), TextAnchor.UpperLeft, 24);
                info.color = UITheme.TextSub;
                // 每日限次视图（服务端 rushDaily 下发；limit=0 不限次不显示）
                var rd = OnlineState.State != null && OnlineState.State["data"] != null
                    ? OnlineState.State["data"]["rushDaily"][c.Id.ToString()] : null;
                int used = rd != null ? rd["used"].AsInt : 0;
                int limit = rd != null ? rd["limit"].AsInt : c.DailyLimit;
                string limitNote = limit > 0 ? "\n" + L10n.Get("今日剩余") + " " + Mathf.Max(0, limit - used) : "";
                info.text = "门票 " + c.Ticket + " 金币\n" + c.PlayerCount + " 人同抢 · " + c.DurationSec + " 秒\n奖池约 " + poolPreview.ToString("N0")
                    + limitNote
                    + (c.Blind ? "\n<color=#6EC6FF>" + L10n.Get("金额隐藏，抢到才揭示") + "</color>" : "");
                var infoRt = info.rectTransform;
                infoRt.anchorMin = infoRt.pivot = new Vector2(0, 0.5f);
                infoRt.anchoredPosition = new Vector2(-450, -20);
                TDUIHelper.SetSize(infoRt, 560, 220);

                var join = UITheme.ThemedButton(card.transform, new Vector2(330, 0), new Vector2(240, 90), L10n.Get("加 入"), "green", 30);
                if (limit > 0 && used >= limit)
                {
                    join.interactable = false;
                    join.GetComponentInChildren<Text>().text = L10n.Get("今日已达上限");
                }
                join.onClick.AddListener(async () =>
                {
                    join.interactable = false;
                    var r = await ApiClient.JoinRushAsync(c.Id);
                    if (r == null || !r["ok"].AsBool)
                    {
                        join.interactable = true;
                        UIRoot.ToastStatic(L10n.Get("加入失败：") + Screens.ErrText(r));
                        return;
                    }
                    EnterGame(c);
                });
                y -= 340;
            }
        }

        private void EnterGame(cfg.rush.RushRoom room)
        {
            _gameArea.gameObject.SetActive(true);
            var title = UIManager.Find<Text>(_gameArea, "title");
            title.text = "<color=#" + room.ColorHex + ">" + room.Name + "</color>";
            _status.text = L10n.Get("等待玩家中…");
            _feedLines.Clear();
            _feed.text = "";
            for (int i = 0; i < 20; i++)
            {
                _slotOwner[i] = 0;
                _slots[i].GetComponent<Image>().sprite = UITheme.Load("slot_locked");
                var t = _slots[i].GetComponentInChildren<Text>();
                t.text = "?"; t.color = Color.white;
                _slots[i].GetComponent<Button>().interactable = true;
            }
            _ws = new RushWsClient();
            if (_privateCode != null) _ws.ConnectByRoom(_privateCode);
            else _ws.Connect(room.Id);
        }

        private void Update()
        {
            _ws?.Pump(OnEvent);
        }

        private void OnEvent(JSONNode e)
        {
            string type = e["type"].Value;
            switch (type)
            {
                case "snapshot":
                {
                    // 好友私人房：展示房码；房主且等待中给"提前开局"按钮
                    var code = e["roomCode"];
                    if (code != null && !code.IsNull)
                    {
                        _privateCode = code.Value;
                        PushFeed(L10n.Get("好友房房间码") + " <color=#F5D061>" + _privateCode + "</color>（分享给好友凭码加入）");
                        if (e["isHost"].AsBool && e["phase"].Value == "matching" && _startBtn == null)
                        {
                            _startBtn = UITheme.ThemedButton(_gameArea, new Vector2(0, -460), new Vector2(320, 64),
                                L10n.Get("开始游戏（不足员可开）"), "green", 22);
                            _startBtn.onClick.AddListener(() => _ws?.Start());
                        }
                    }
                    break;
                }
                case "closed":
                    // 好友房解散（等待超时未开局）：已退款；销毁残留的开局按钮（防误点）
                    _status.text = L10n.Get("好友房已解散（入场费已退回）");
                    _ws?.Close();
                    if (_startBtn != null) { Destroy(_startBtn.gameObject); _startBtn = null; }
                    break;
                case "joined":
                    PushFeed(e["name"].Value + " 进入房间（" + e["count"].AsInt + "/" + e["need"].AsInt + "）");
                    break;
                case "phase":
                {
                    string phase = e["phase"].Value;
                    if (phase == "countdown")
                    {
                        if (_startBtn != null) { Destroy(_startBtn.gameObject); _startBtn = null; }
                        _status.text = "<color=#E05D5D>准备！</color>";
                        PushFeed(L10n.Get("房间已满，即将开始…"));
                    }
                    else if (phase == "rush")
                    {
                        if (_startBtn != null) { Destroy(_startBtn.gameObject); _startBtn = null; }
                        var arr = e["amounts"];
                        _amounts = new int[arr.Count];
                        for (int i = 0; i < arr.Count; i++) _amounts[i] = arr[i].AsInt;
                        _status.text = "<color=#4DD0A5>开抢！</color>";
                        PushFeed(L10n.Get("奖池已生成，点击格子抢夺！"));
                    }
                    break;
                }
                case "countdown":
                {
                    int sec = e["remainSec"].AsInt;
                    if (sec > 0) _status.text = "<color=#E05D5D>" + sec + "</color>";
                    break;
                }
                case "remainSec":
                {
                    int sec = e["remainSec"].AsInt;
                    _status.text = "抢夺中… 剩余 <color=#F5D061>" + sec + "</color> 秒";
                    break;
                }
                case "claimed":
                {
                    int slot = e["slot"].AsInt;
                    long pid = e["playerId"].AsLong;
                    _slotOwner[slot] = pid;
                    var img = _slots[slot].GetComponent<Image>();
                    img.sprite = UITheme.Load("slot_open");
                    var t = _slots[slot].GetComponentInChildren<Text>();
                    t.text = e["amount"].AsInt.ToString("N0");
                    t.fontSize = 26;
                    bool mine = ApiClient.PlayerId == pid;
                    t.color = mine ? UITheme.GoldLight : new Color(0.6f, 0.65f, 0.72f);
                    _slots[slot].GetComponent<Button>().interactable = false;
                    PushFeed((mine ? "<color=#F5D061>你</color>" : e["name"].Value) + " 抢到 <color=#F5D061>" + e["amount"].AsInt.ToString("N0") + "</color>");
                    StartCoroutine(Pop(_slots[slot].transform));
                    break;
                }
                case "settled":
                {
                    _status.text = L10n.Get("本局结束");
                    var sb = new System.Text.StringBuilder("—— 结算 ——\n");
                    int rank = 1;
                    foreach (JSONNode row in e["board"])
                    {
                        sb.Append(rank).Append(". ").Append(row["name"].Value).Append("  ").Append(row["earned"].AsLong.ToString("N0")).Append('\n');
                        rank++;
                    }
                    PushFeed(sb.ToString());
                    _ws?.Close();
                    break;
                }
                case "__disconnected":
                    // 断线（切后台/网络抖动；结算后的主动 Close 不提示）——抢宝房无重连，提示后回大厅
                    if (!_status.text.Contains(L10n.Get("本局结束")))
                    {
                        UIRoot.ToastStatic(L10n.Get("连接已断开"));
                        Screens.ShowMain();
                    }
                    break;
            }
        }

        private void PushFeed(string line)
        {
            _feedLines.Add(line);
            if (_feedLines.Count > 9) _feedLines.RemoveAt(0);
            _feed.text = string.Join("\n", _feedLines.ToArray());
        }

        private System.Collections.IEnumerator Pop(Transform tr)
        {
            float t = 0;
            Vector3 origin = Vector3.one;
            tr.localScale = origin * 1.4f;
            while (t < 0.2f)
            {
                t += Time.deltaTime;
                tr.localScale = Vector3.Lerp(origin * 1.4f, origin, t / 0.2f);
                yield return null;
            }
            tr.localScale = origin;
        }

        private void OnDestroy()
        {
            _ws?.Close();
        }
    }
}
