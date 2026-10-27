using System;
using System.Collections.Generic;
using System.Globalization;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// Order ticket UI (amber). Buy/sell tabs with sell locked, five order-type buttons (disabled ones dimmed; pressing
    /// one shows its reason), price and the locked 1-share quantity, the amber "매수 준비" button, and the preflight
    /// preview card with the values that would be sent. "확인" sends nothing anywhere: it only shows the "S4에서 연결"
    /// badge. Every enable, lock and reason comes from <see cref="OrderView"/>.
    /// </summary>
    public sealed class OrderPanel : HoloPanel
    {
        public const string ConnectLaterBadge = "S4에서 연결";
        private const float ToastSeconds = 2.2f;
        private const float RowGap = 6f;
        private const float TabH = 36f;
        private const float TypeH = 38f;
        private const float ValueH = 40f;
        private const float ActionH = 42f;
        private const float CardH = 124f;

        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private SpatialCardGraphic _badge;
        [SerializeField] private TextMeshProUGUI _badgeText;
        [SerializeField] private SpatialCardGraphic _buyTab;
        [SerializeField] private TextMeshProUGUI _buyText;
        [SerializeField] private SpatialCardGraphic _sellTab;
        [SerializeField] private TextMeshProUGUI _sellText;
        [SerializeField] private RectTransform _sellLock;
        [SerializeField] private List<Button> _types = new List<Button>();
        [SerializeField] private TextMeshProUGUI _priceKey;
        [SerializeField] private TextMeshProUGUI _price;
        [SerializeField] private TextMeshProUGUI _qtyKey;
        [SerializeField] private TextMeshProUGUI _qty;
        [SerializeField] private RectTransform _qtyLock;
        [SerializeField] private Button _prepare;
        [SerializeField] private TextMeshProUGUI _prepareText;
        [SerializeField] private SpatialCardGraphic _card;
        [SerializeField] private SpatialCardGraphic _cardBadge;
        [SerializeField] private TextMeshProUGUI _cardBadgeText;
        [SerializeField] private SpatialCardGraphic _s4;
        [SerializeField] private TextMeshProUGUI _s4Text;
        [SerializeField] private TextMeshProUGUI _cardName;
        [SerializeField] private TextMeshProUGUI _cardLine;
        [SerializeField] private TextMeshProUGUI _cardPrice;
        [SerializeField] private TextMeshProUGUI _cardQty;
        [SerializeField] private Button _confirm;
        [SerializeField] private TextMeshProUGUI _confirmText;
        [SerializeField] private SpatialCardGraphic _toast;
        [SerializeField] private TextMeshProUGUI _toastText;

        private OrderView _view;
        private OrderPreview _preview;
        private PanelRect _rect;
        private bool _confirmed;
        private float _toastUntil = -1f;

        /// <summary>An enabled order-type button was pressed.</summary>
        public event Action<OrderType> TypeChosen;
        /// <summary>"매수 준비" was pressed while the view allows preparing; the owner calls <c>OrderTicket.TryPrepare</c>.</summary>
        public event Action PrepareRequested;

        protected override Color Accent => HoloStyle.Amber;
        protected override Color Fill => HoloStyle.OrderFill;

        /// <summary>True after "확인" on the current preview. Nothing is sent; the badge is the whole effect.</summary>
        public bool ConfirmShown => _confirmed;

        protected override void Build()
        {
            _title = Holo.Text(transform, "Title", Font, TitleSize, HoloStyle.Amber);
            _title.text = "주문";
            _badge = Holo.Card(transform, "Badge", HoloStyle.Alpha(HoloStyle.Amber, .12f), HoloStyle.Amber, 1.5f, 5f);
            _badgeText = Chip(_badge, "BadgeText", HoloStyle.Amber);

            _buyTab = Holo.Card(transform, "Buy Tab", HoloStyle.Alpha(HoloStyle.Cyan, .2f), HoloStyle.Cyan, 1.5f, 6f);
            _buyText = Chip(_buyTab, "Buy", HoloStyle.Ink);
            _buyText.text = "매수";
            _sellTab = Holo.Card(transform, "Sell Tab", Color.clear, HoloStyle.Alpha(HoloStyle.Cyan, .3f), 1.5f, 6f);
            _sellText = Chip(_sellTab, "Sell", HoloStyle.Alpha(HoloStyle.Cyan, .4f));
            _sellLock = Holo.LockIcon(_sellTab.transform, "Sell Lock", HoloStyle.Alpha(HoloStyle.Cyan, .4f));

            for (var i = 0; i < 5; i++) AddTypeButton();

            _priceKey = Holo.Text(transform, "Key Won", Font, HoloStyle.MinText, HoloStyle.Muted);
            _priceKey.text = "가격";
            _price = Holo.Text(transform, "Price", Font, HoloStyle.MinNumber, HoloStyle.Ink);
            _qtyKey = Holo.Text(transform, "Key Shares", Font, HoloStyle.MinText, HoloStyle.Muted);
            _qtyKey.text = "수량";
            _qty = Holo.Text(transform, "Qty", Font, HoloStyle.MinNumber, HoloStyle.Ink);
            _qtyLock = Holo.LockIcon(transform, "Qty Lock", HoloStyle.Amber);

            _prepare = Holo.Button(transform, "Prepare");
            _prepareText = Chip((SpatialCardGraphic)_prepare.targetGraphic, "PrepareText", HoloStyle.Amber, 20f);
            _prepareText.text = "매수 준비";

            _card = Holo.Card(transform, "Preview Card", HoloStyle.Alpha(HoloStyle.Amber, .07f), HoloStyle.Alpha(HoloStyle.Amber, .7f), 1.5f, 8f);
            _cardBadge = Holo.Card(_card.transform, "Preview Badge", HoloStyle.Alpha(HoloStyle.Amber, .14f), HoloStyle.Amber, 1.5f, 5f);
            _cardBadgeText = Chip(_cardBadge, "PreviewBadgeText", HoloStyle.Amber);
            _s4 = Holo.Card(_card.transform, "Connect Later", HoloStyle.Alpha(HoloStyle.Amber, .3f), HoloStyle.Amber, 1.5f, 5f);
            _s4Text = Chip(_s4, "ConnectLaterText", HoloStyle.Ink);
            _s4Text.text = ConnectLaterBadge;
            _cardName = Holo.Text(_card.transform, "Preview Name", Font, 18f, HoloStyle.Ink, TextAlignmentOptions.MidlineRight);
            _cardLine = Holo.Text(_card.transform, "Preview Fields", Font, 18f, HoloStyle.Ink);
            _cardPrice = Holo.Text(_card.transform, "PreviewPrice", Font, HoloStyle.MinNumber, HoloStyle.Ink);
            _cardQty = Holo.Text(_card.transform, "PreviewQty", Font, HoloStyle.MinNumber, HoloStyle.Ink);
            _confirm = Holo.Button(_card.transform, "Confirm");
            _confirmText = Chip((SpatialCardGraphic)_confirm.targetGraphic, "ConfirmText", HoloStyle.Amber, 20f);
            _confirmText.text = "확인";

            _toast = Holo.Card(transform, "Toast", new Color(.03f, .03f, .03f, .94f), HoloStyle.Amber, 1.5f, 8f);
            _toastText = Chip(_toast, "ToastText", HoloStyle.Ink, 18f);
            Holo.Show(_toast, false);
        }

        private TextMeshProUGUI Chip(SpatialCardGraphic card, string name, Color color, float size = HoloStyle.MinText)
        {
            var t = Holo.Text(card.transform, name, Font, size, color, TextAlignmentOptions.Midline);
            Holo.Stretch(t);
            t.margin = new Vector4(6f, 0f, 6f, 0f);
            return t;
        }

        private void AddTypeButton()
        {
            var b = Holo.Button(transform, "Order Type");
            Chip((SpatialCardGraphic)b.targetGraphic, "TypeLabel", HoloStyle.Cyan);
            _types.Add(b);
            if (_wiredTypes > 0) WireType(_types.Count - 1);
        }

        private int _wiredTypes;
        private Component[] _idleParts;

        private void WireType(int index)
        {
            Holo.OnClick(_types[index], () => OnType(index));
            _wiredTypes = Math.Max(_wiredTypes, index + 1);
        }

        protected override void Wire()
        {
            for (var i = 0; i < _types.Count; i++) WireType(i);
            Holo.OnClick(_prepare, OnPrepare);
            Holo.OnClick(_confirm, OnConfirm);
        }

        /// <param name="preview">The ticket's preflight preview, or null when the ticket is not in preflight.</param>
        public void Render(OrderView view, PanelRect rect, OrderPreview preview = null)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            EnsureBuilt();
            if (!ReferenceEquals(preview, _preview)) _confirmed = false;
            _view = view;
            _preview = preview;
            _rect = rect;
            Draw();
        }

        private void Draw()
        {
            var view = _view;
            var size = Size(_rect);
            float w = size.x, h = size.y, inner = w - 2 * Pad;
            var preflight = _preview != null;
            var compact = _rect.Height < 6;
            var tiny = _rect.Height < 5;

            // Header: 주문 [매수|매도🔒 when compact] …… badge
            var titleW = Holo.Width(_title, _title.text) + 4f;
            Holo.Place(_title, Pad, Top, titleW, HeaderH);
            _sellText.text = view.SellTabLabel;
            var sellTextW = Holo.Width(_sellText, view.SellTabLabel);
            var headerRight = Pad + titleW;
            if (compact)
            {
                // Slim tab indicator in the header so the sell lock stays visible at every legal size.
                var buyW = Holo.Width(_buyText, _buyText.text) + 16f;
                var sellW = sellTextW + 16f + (view.SellLocked ? 22f : 0f);
                PlaceTabs(headerRight + 8f, Top + 2f, buyW, sellW, HeaderH - 4f, 4f, sellTextW, view.SellLocked);
                headerRight += 8f + buyW + 4f + sellW;
            }
            _badgeText.text = view.Badge;
            var badgeW = Mathf.Max(0f, Mathf.Min(Holo.Width(_badgeText, view.Badge) + 16f, w - Pad - headerRight - 8f));
            Holo.Place(_badge, w - Pad - badgeW, Top + 2f, badgeW, HeaderH - 4f);
            Rule(w, Top + HeaderH + 3f);
            var y = Top + HeaderH + 10f;

            // Full tabs row: 매수 | 매도 (locked)
            if (!compact)
            {
                var half = (inner - 8f) / 2f;
                PlaceTabs(Pad, y, half, half, TabH, 8f, sellTextW, view.SellLocked);
                y += TabH + RowGap;
            }

            // Order types
            var showTypes = !(tiny && preflight);
            while (_types.Count < view.Types.Count) AddTypeButton();
            var n = view.Types.Count;
            var typeW = n == 0 ? 0f : (inner - (n - 1) * 6f) / n;
            for (var i = 0; i < _types.Count; i++)
            {
                var on = showTypes && i < n;
                Holo.Show(_types[i], on);
                if (!on) continue;
                Holo.Place(_types[i], Pad + i * (typeW + 6f), y, typeW, TypeH);
                StyleType(_types[i], view.Types[i]);
            }
            if (showTypes) y += TypeH + RowGap;

            // Idle: price · quantity, then 매수 준비. Preflight: the preview card replaces both.
            if (_idleParts == null) _idleParts = new Component[] { _priceKey, _price, _qtyKey, _qty, _prepare };
            foreach (var c in _idleParts) Holo.Show(c, !preflight);
            Holo.Show(_card, preflight);
            if (!preflight)
            {
                var valueH = compact ? ValueH - 2f : ValueH;
                var priceW = inner * .6f;
                Holo.Place(_priceKey, Pad, y, 52f, valueH);
                Holo.Place(_price, Pad + 52f, y, priceW - 60f, valueH);
                _price.text = view.PriceText;
                Holo.Place(_qtyKey, Pad + priceW, y, 52f, valueH);
                _qty.text = view.QuantityLabel;
                var qtyLocked = view.QuantityLocked;
                var qtyW = Mathf.Min(Holo.Width(_qty, _qty.text) + 4f, inner - priceW - 52f - 24f);
                Holo.Place(_qty, Pad + priceW + 52f, y, qtyW, valueH);
                Holo.Show(_qtyLock, qtyLocked);  // hidden with the row in preflight (below)
                Holo.Place(_qtyLock, Pad + priceW + 52f + qtyW + 6f, y + (valueH - 20f) / 2f, 16f, 20f);
                y += valueH + RowGap;

                var actionH = compact ? ActionH - 4f : ActionH;
                Holo.Place(_prepare, Pad, Mathf.Min(y, h - Top - actionH), inner, actionH);
                StyleAction(_prepare, _prepareText, view.PrepareEnabled);
            }
            else
            {
                Holo.Show(_qtyLock, false);
                DrawCard(inner, Mathf.Min(CardH, h - Top - y), y);
            }

            if (_toast.gameObject.activeSelf) PlaceToast(w, h);
        }

        private void DrawCard(float inner, float cardH, float y)
        {
            var p = _preview;
            Holo.Place(_card, Pad, y, inner, cardH);
            const float cp = 10f;
            var cw = inner - 2 * cp;

            // Line 1: PREFLIGHT [S4에서 연결] …… 삼성전자 005930
            _cardBadgeText.text = p.Badge;
            var badgeW = Holo.Width(_cardBadgeText, p.Badge) + 16f;
            Holo.Place(_cardBadge, cp, 8f, badgeW, 28f);
            Holo.Show(_s4, _confirmed);
            var s4W = Holo.Width(_s4Text, ConnectLaterBadge) + 16f;
            Holo.Place(_s4, cp + badgeW + 6f, 8f, s4W, 28f);
            var nameX = cp + badgeW + 6f + (_confirmed ? s4W + 6f : 0f);
            Holo.Place(_cardName, nameX, 6f, inner - cp - nameX, 32f);
            _cardName.text = p.Name + " " + p.Code;

            // Line 2: 매수 · 시장가 · 매매구분 3 · KRX
            Holo.Place(_cardLine, cp, 40f, cw, 30f);
            _cardLine.text = ViewFormat.Join(p.Side, OrderTypeTable.DisplayName(p.Type), "매매구분 " + p.KiwoomTradeType, p.Market);

            // Line 3: price · quantity …… 확인
            var confirmW = Mathf.Min(110f, cw * .3f);
            const float line3Y = 74f;
            _cardQty.text = p.Quantity.ToString("#,##0", CultureInfo.InvariantCulture) + "주";
            var qtyW = Holo.Width(_cardQty, _cardQty.text) + 8f;
            var priceW = Mathf.Max(0f, cw - confirmW - qtyW - 20f);
            Holo.Place(_cardPrice, cp, line3Y, priceW, 40f);
            _cardPrice.text = "가격 " + ViewFormat.Number(p.Price);
            Holo.Place(_cardQty, cp + priceW + 10f, line3Y, qtyW, 40f);
            Holo.Place(_confirm, inner - cp - confirmW, line3Y, confirmW, 40f);
            StyleAction(_confirm, _confirmText, _view.PrepareEnabled);
        }

        private void PlaceTabs(float x, float y, float buyW, float sellW, float h, float gap, float sellTextW, bool sellLocked)
        {
            Holo.Place(_buyTab, x, y, buyW, h);
            Holo.Place(_sellTab, x + buyW + gap, y, sellW, h);
            Holo.Show(_sellLock, sellLocked);
            // With a lock, the text centres in the tab minus 24 mm on the right and the lock follows the text.
            var rightMargin = sellLocked ? 24f : 6f;
            _sellText.margin = new Vector4(6f, 0f, rightMargin, 0f);
            var textRight = (6f + sellW - rightMargin) / 2f + sellTextW / 2f;
            Holo.Place(_sellLock, textRight + 6f, (h - 20f) / 2f, 16f, 20f);
        }

        private void OnType(int index)
        {
            if (_view == null || index >= _view.Types.Count) return;
            var b = _view.Types[index];
            if (!b.Enabled) { ShowToast(b.Reason); return; }
            TypeChosen?.Invoke(b.Type);
        }

        private void OnPrepare()
        {
            if (_view == null) return;
            if (!_view.PrepareEnabled) { ShowToast(_view.LockReason); return; }
            PrepareRequested?.Invoke();
        }

        /// <summary>No order, no network: the confirmation only reveals the "S4에서 연결" badge.</summary>
        private void OnConfirm()
        {
            if (_view == null || _preview == null) return;
            if (!_view.PrepareEnabled) { ShowToast(_view.LockReason); return; }
            _confirmed = true;
            Draw();
        }

        private void ShowToast(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return;
            _toastText.text = reason;
            _toastUntil = Time.unscaledTime + ToastSeconds;
            Holo.Show(_toast, true);
            _toast.transform.SetAsLastSibling();
            var size = HoloStyle.SizeFor(_rect);
            PlaceToast(size.x, size.y);
        }

        private void PlaceToast(float w, float h)
        {
            var tw = Mathf.Min(Holo.Width(_toastText, _toastText.text) + 32f, w - 2 * Pad);
            Holo.Place(_toast, (w - tw) / 2f, h - Top - ActionH - RowGap - 44f, tw, 44f);
        }

        private void Update()
        {
            if (_toastUntil > 0f && Time.unscaledTime >= _toastUntil)
            {
                _toastUntil = -1f;
                Holo.Show(_toast, false);
            }
        }

        private static void StyleType(Button b, OrderTypeButtonView v)
        {
            var card = (SpatialCardGraphic)b.targetGraphic;
            var label = b.GetComponentInChildren<TMP_Text>(true);
            label.text = v.Label;
            if (v.Selected && v.Enabled)
            {
                Holo.Paint(card, HoloStyle.Alpha(HoloStyle.Cyan, .24f), HoloStyle.Cyan, HoloStyle.LineWidth);
                label.color = HoloStyle.Ink;
            }
            else if (v.Enabled)
            {
                Holo.Paint(card, HoloStyle.Alpha(HoloStyle.Cyan, .04f), HoloStyle.Alpha(HoloStyle.Cyan, .55f), 1.5f);
                label.color = HoloStyle.Cyan;
            }
            else
            {
                Holo.Paint(card, Color.clear, HoloStyle.Alpha(HoloStyle.Cyan, v.Selected ? .4f : .2f), 1.5f);
                label.color = HoloStyle.Alpha(HoloStyle.Muted, .5f);
            }
        }

        private static void StyleAction(Button b, TMP_Text label, bool enabled)
        {
            Holo.Paint((SpatialCardGraphic)b.targetGraphic, HoloStyle.Alpha(HoloStyle.Amber, enabled ? .16f : .03f),
                HoloStyle.Alpha(HoloStyle.Amber, enabled ? 1f : .3f), enabled ? HoloStyle.LineWidth : 1.5f);
            label.color = HoloStyle.Alpha(HoloStyle.Amber, enabled ? 1f : .4f);
        }
    }
}
