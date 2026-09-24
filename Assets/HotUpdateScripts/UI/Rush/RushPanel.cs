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

        public static async void Show()
        {
            var root = await UIManager.ShowAsync("RushScreen");
            if (root == null) { UIRoot.ToastStatic(L10n.Get("界面加载失败")); return; }
            var panel = root.GetComponent<RushPanel>() ?? root.AddComponent<RushPanel>();
            panel.Bind(root);
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
                var card = UITheme.CreatePanel(list, "panel_dark", new Vector2(0, y), new Vector2(1200, 320));

                var name = TDUIHelper.MakeText(card.transform, new Vector2(-380, 80), TextAnchor.MiddleCenter, 40);
                name.text = "<color=#" + c.ColorHex + ">" + c.Name + "</color>";
                TDUIHelper.SetSize(name.rectTransform, 420, 70);
                var info = TDUIHelper.MakeText(card.transform, new Vector2(-360, -30), TextAnchor.UpperLeft, 26);
                info.color = UITheme.TextSub;
                info.text = "门票 " + c.Ticket + " 金币\n" + c.PlayerCount + " 人同抢 · " + c.DurationSec + " 秒\n奖池约 " + poolPreview.ToString("N0");
                var infoRt = info.rectTransform;
                infoRt.anchorMin = infoRt.pivot = new Vector2(0, 0.5f);
                infoRt.anchoredPosition = new Vector2(-560, -20);
                TDUIHelper.SetSize(infoRt, 600, 220);

                var join = UITheme.ThemedButton(card.transform, new Vector2(420, 0), new Vector2(280, 100), L10n.Get("加 入"), "green", 34);
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
                y -= 360;
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
            _ws.Connect(room.Id);
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
                    break; // 快照信息由后续事件增量补齐
                case "joined":
                    PushFeed(e["name"].Value + " 进入房间（" + e["count"].AsInt + "/" + e["need"].AsInt + "）");
                    break;
                case "phase":
                {
                    string phase = e["phase"].Value;
                    if (phase == "countdown")
                    {
                        _status.text = "<color=#E05D5D>准备！</color>";
                        PushFeed(L10n.Get("房间已满，即将开始…"));
                    }
                    else if (phase == "rush")
                    {
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
