using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using Source.Player;
using Source.Galaxy.POI;
using Source.Simulation.Story;

namespace VGMarketInfo;

// Color palette matching the Vanguard Galaxy UI
internal static class Pal
{
    // #0a111a — near-black navy window background
    internal static readonly Color Background = new Color(0.039f, 0.067f, 0.102f, 1f);
    // #1a5870 — teal border / separator
    internal static readonly Color Separator   = new Color(0.102f, 0.345f, 0.439f, 0.9f);
    // #b8ccd8 — cool off-white body text
    internal static readonly Color Text        = new Color(0.722f, 0.800f, 0.847f, 1f);
    // #20b8d8 — teal-cyan (buy prices, secondary info)
    internal static readonly Color Cyan        = new Color(0.125f, 0.722f, 0.847f, 1f);
    // #00c860 — bright green (sell prices, positive profit, purchased)
    internal static readonly Color Green       = new Color(0.000f, 0.784f, 0.376f, 1f);
    // #f0a010 — amber gold (costs, upgrade button)
    internal static readonly Color Gold        = new Color(0.941f, 0.627f, 0.063f, 1f);
    // #de3020 — orange-red (negative profit, out of stock)
    internal static readonly Color Red         = new Color(0.871f, 0.188f, 0.125f, 1f);
    // #7a909e — blue-gray (muted / no-data / age text) — must be readable on dark bg
    internal static readonly Color Muted       = new Color(0.478f, 0.565f, 0.620f, 1f);
}

public class MarketUI : MonoBehaviour
{
    private bool _visible = false;
    private Rect _windowRect = new Rect(20, 20, 860, 560);
    private ConfigEntry<KeyCode> _toggleKey;
    private ConfigEntry<float> _uiScale;
    private ConfigEntry<float> _uiAlpha;
    private ConfigEntry<float> _uiWidth;
    private ConfigEntry<float> _uiHeight;

    // Tab: 0=Price Board, 1=Export, 2=Import, 3=Upgrades, 4=Settings
    private int _activeTab = 0;
    private int _prevTab   = -1;

    // Settings — pending values (applied only on Save)
    private float _pendingScale;
    private float _pendingAlpha;
    private float _pendingWidth;
    private float _pendingHeight;

    // Custom styles — built once inside OnGUI (requires GUI.skin access)
    private static GUIStyle _windowStyle;
    private static GUIStyle _btnStyle;        // dark button (sort headers, dropdowns)
    private static GUIStyle _upgradeBtnStyle; // buy button — gold text
    private static GUIStyle _tabStyle;        // inactive tab toggle
    private static GUIStyle _tabOnStyle;      // active tab toggle
    private static GUIStyle _boxStyle;        // upgrade card box
    private static GUIStyle _labelStyle;     // white textColor — color driven by GUI.contentColor
    private static GUIStyle _sectorStyle;    // bold gold — upgrade card / section headers
    private static Texture2D _bgTex;

    // Price Board
    private Vector2 _boardScroll;
    private int _boardSortCol = 0;
    private bool _boardSortAsc = true;

    // Export (From Station)
    private Vector2 _exportScroll;
    private string _exportStation = "";
    private bool _showExportDropdown = false;
    private Vector2 _exportDropdownScroll;
    private int _exportSortCol = 0;
    private bool _exportSortAsc = false;

    // Import (To Station)
    private Vector2 _importScroll;
    private string _importStation = "";
    private bool _showImportDropdown = false;
    private Vector2 _importDropdownScroll;
    private int _importSortCol = 0;
    private bool _importSortAsc = false;

    public void Initialize(
        ConfigEntry<KeyCode> toggleKey,
        ConfigEntry<float> uiScale,
        ConfigEntry<float> uiAlpha,
        ConfigEntry<float> uiWidth,
        ConfigEntry<float> uiHeight)
    {
        _toggleKey = toggleKey;
        _uiScale   = uiScale;
        _uiAlpha   = uiAlpha;
        _uiWidth   = uiWidth;
        _uiHeight  = uiHeight;
        _windowRect   = new Rect(20, 20, _uiWidth.Value, _uiHeight.Value);
        _pendingScale  = _uiScale.Value;
        _pendingAlpha  = _uiAlpha.Value;
        _pendingWidth  = _uiWidth.Value;
        _pendingHeight = _uiHeight.Value;
    }

    private void Update()
    {
        if (Input.GetKeyDown(_toggleKey.Value))
            _visible = !_visible;
    }

    private void OnGUI()
    {
        if (!_visible) return;

        var oldColor  = GUI.color;
        var oldMatrix = GUI.matrix;

        GUIUtility.ScaleAroundPivot(Vector2.one * _uiScale.Value, new Vector2(_windowRect.x, _windowRect.y));

        _windowRect.width  = _uiWidth.Value;
        _windowRect.height = _uiHeight.Value;

        // Build custom styles once (must happen inside OnGUI for GUI.skin access)
        if (_windowStyle == null)
        {
            _bgTex = MakeTex(Pal.Background);

            // Window
            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background   = _bgTex;
            _windowStyle.onNormal.background = _bgTex;
            _windowStyle.normal.textColor    = Pal.Text;
            _windowStyle.onNormal.textColor  = Pal.Text;

            // Dark button (sort headers, dropdowns, general buttons)
            var btnNorm   = MakeTex(new Color(0.08f, 0.14f, 0.21f, 1f));  // #142236
            var btnHover  = MakeTex(new Color(0.12f, 0.20f, 0.30f, 1f));  // #1e3349
            var btnActive = MakeTex(new Color(0.16f, 0.27f, 0.40f, 1f));  // #294466
            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.normal.background  = btnNorm;  _btnStyle.normal.textColor  = Pal.Text;
            _btnStyle.hover.background   = btnHover; _btnStyle.hover.textColor   = Pal.Text;
            _btnStyle.active.background  = btnActive; _btnStyle.active.textColor = Pal.Text;
            _btnStyle.onNormal.background = btnActive; _btnStyle.onNormal.textColor = Pal.Text;

            // Upgrade buy button — gold text so it stands out against the dark background
            _upgradeBtnStyle = new GUIStyle(_btnStyle);
            _upgradeBtnStyle.normal.textColor  = Pal.Gold;
            _upgradeBtnStyle.hover.textColor   = Pal.Gold;
            _upgradeBtnStyle.active.textColor  = Pal.Gold;

            // Tab (inactive)
            _tabStyle = new GUIStyle(_btnStyle);

            // Tab (active) — brighter teal highlight, cyan text
            _tabOnStyle = new GUIStyle(_btnStyle);
            _tabOnStyle.normal.background  = btnActive;
            _tabOnStyle.normal.textColor   = Pal.Cyan;
            _tabOnStyle.hover.textColor    = Pal.Cyan;

            // Box (upgrade cards)
            var boxBg = MakeTex(new Color(0.08f, 0.14f, 0.21f, 1f));
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = boxBg;
            _boxStyle.normal.textColor  = Pal.Text;

            // Label style — MUST use Color.white so GUI.contentColor drives all coloring
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.normal.textColor = Color.white;
            _labelStyle.richText = true;

            _sectorStyle = new GUIStyle(_labelStyle);
            _sectorStyle.fontStyle = FontStyle.Bold;
        }

        GUI.color = new Color(1f, 1f, 1f, _uiAlpha.Value);
        _windowRect = GUILayout.Window(0xB00B, _windowRect, DrawWindow, "VG Market Info Panel", _windowStyle);

        GUI.color  = oldColor;
        GUI.matrix = oldMatrix;
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(_activeTab == 0, "Price Board", _activeTab == 0 ? _tabOnStyle : _tabStyle, GUILayout.Width(90))) _activeTab = 0;
        if (GUILayout.Toggle(_activeTab == 1, "Export",      _activeTab == 1 ? _tabOnStyle : _tabStyle, GUILayout.Width(70))) _activeTab = 1;
        if (GUILayout.Toggle(_activeTab == 2, "Import",      _activeTab == 2 ? _tabOnStyle : _tabStyle, GUILayout.Width(70))) _activeTab = 2;
        if (GUILayout.Toggle(_activeTab == 3, "Upgrades",    _activeTab == 3 ? _tabOnStyle : _tabStyle, GUILayout.Width(80))) _activeTab = 3;
        if (GUILayout.Toggle(_activeTab == 4, "Settings",    _activeTab == 4 ? _tabOnStyle : _tabStyle, GUILayout.Width(80))) _activeTab = 4;
        GUILayout.EndHorizontal();

        // Reload pending settings whenever the user navigates to the Settings tab
        if (_activeTab == 4 && _prevTab != 4)
        {
            _pendingScale  = _uiScale.Value;
            _pendingAlpha  = _uiAlpha.Value;
            _pendingWidth  = _uiWidth.Value;
            _pendingHeight = _uiHeight.Value;
        }
        _prevTab = _activeTab;

        switch (_activeTab)
        {
            case 0: DrawPriceBoardTab(); break;
            case 1: DrawExportTab();     break;
            case 2: DrawImportTab();     break;
            case 3: DrawUpgradesTab();   break;
            case 4: DrawSettingsTab();   break;
        }

        GUI.DragWindow();
    }

    // ── Price Board ──────────────────────────────────────────────────────────

    private static readonly int[]    OverviewWidths      = { 170, 80, 155, 45, 80, 155, 45 };
    private static readonly string[] OverviewLabels      = { "Item", "Best Buy", "Buy At", "Age", "Best Sell", "Sell At", "Age" };
    private static readonly int[]    OverviewLiveWidths  = { 170, 80, 155, 35, 80, 190 };
    private static readonly string[] OverviewLiveLabels  = { "Item", "Best Buy", "Buy At", "Qty", "Best Sell", "Sell At" };

    private bool IsLive => MarketDatabase.Instance.MarketFeedUnlocked;

    private void DrawPriceBoardTab()
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (IsLive)
        {
            GUI.contentColor = Pal.Green;
            var econ = GamePlayer.current?.GetStoryteller<Economy>();
            string countdown = "";
            if (econ != null)
            {
                int secs = Mathf.CeilToInt(econ.economyTimer);
                countdown = $"  {secs / 60:D2}:{secs % 60:D2}";
            }
            GUILayout.Label($"● LIVE{countdown}", _labelStyle, GUILayout.Width(110));
            GUI.contentColor = Color.white;
        }
        GUILayout.EndHorizontal();

        int[] colWidths = ComputeBoardWidths();
        DrawPriceBoardHeader(colWidths);
        _boardScroll = GUILayout.BeginScrollView(_boardScroll);
        DrawPriceBoardRows(colWidths);
        GUILayout.EndScrollView();

        GUI.contentColor = Pal.Muted;
        GUILayout.Label(
            $"{MarketDatabase.Instance.Items.Count} items tracked   |   " +
            $"{MarketDatabase.Instance.GetAllStations().Count()} stations visited   |   " +
            $"{_toggleKey.Value} to toggle",
            _labelStyle, GUILayout.ExpandWidth(true));
        GUI.contentColor = Color.white;
    }

    private int[] ComputeBoardWidths()
    {
        int[] baseWidths = IsLive ? OverviewLiveWidths : OverviewWidths;
        float available  = _windowRect.width - 34f; // window chrome (~8px each side) + scrollbar (16px) + margin
        float baseTotal  = 0f;
        foreach (int bw in baseWidths) baseTotal += bw;
        float scale = available / baseTotal;
        var result = new int[baseWidths.Length];
        for (int i = 0; i < baseWidths.Length; i++)
            result[i] = Mathf.Max(20, Mathf.FloorToInt(baseWidths[i] * scale));
        return result;
    }

    private void DrawPriceBoardHeader(int[] widths)
    {
        bool     live    = IsLive;
        string[] labels  = live ? OverviewLiveLabels : OverviewLabels;
        var      sortable = new HashSet<int> { 0, 1, 4 };

        GUILayout.BeginHorizontal();
        for (int i = 0; i < labels.Length; i++)
        {
            if (sortable.Contains(i))
            {
                string arrow = _boardSortCol == i ? (_boardSortAsc ? " ▲" : " ▼") : "";
                if (GUILayout.Button(labels[i] + arrow, _btnStyle, GUILayout.Width(widths[i])))
                {
                    if (_boardSortCol == i) _boardSortAsc = !_boardSortAsc;
                    else { _boardSortCol = i; _boardSortAsc = true; }
                }
            }
            else
            {
                GUI.contentColor = Pal.Muted;
                GUILayout.Label(labels[i], _labelStyle, GUILayout.Width(widths[i]));
                GUI.contentColor = Color.white;
            }
        }
        GUILayout.EndHorizontal();
    }

    private void DrawPriceBoardRows(int[] w)
    {
        var items = MarketDatabase.Instance.Items.Values.ToList();
        bool live = IsLive;
        int sellSortCol = 4;

        items = (_boardSortCol, _boardSortAsc) switch
        {
            (0, true)  => items.OrderBy(i => i.ItemName).ToList(),
            (0, false) => items.OrderByDescending(i => i.ItemName).ToList(),
            (1, true)  => items.OrderBy(i => i.BestBuyPrice).ToList(),
            (1, false) => items.OrderByDescending(i => i.BestBuyPrice).ToList(),
            var (col, asc) when col == sellSortCol && asc  => items.OrderBy(i => i.BestSellPrice).ToList(),
            var (col, asc) when col == sellSortCol && !asc => items.OrderByDescending(i => i.BestSellPrice).ToList(),
            _ => items.OrderBy(i => i.ItemName).ToList(),
        };

        if (items.Count == 0)
        {
            GUILayout.Label("No data yet. Dock at stations to collect prices.");
            return;
        }

        var visited = MarketDatabase.Instance.StationLastVisited;

        foreach (var item in items)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(item.ItemName, _labelStyle, GUILayout.Width(w[0]));

            GUI.contentColor = item.BestBuyPrice == float.MaxValue ? Pal.Muted : Pal.Cyan;
            GUILayout.Label(item.BestBuyPrice == float.MaxValue ? "-" : $"{item.BestBuyPrice:N0}", _labelStyle, GUILayout.Width(w[1]));
            GUI.contentColor = Pal.Cyan;
            GUILayout.Label(item.BestBuyLocation, _labelStyle, GUILayout.Width(w[2]));
            GUI.contentColor = Color.white;

            if (live)
            {
                // Qty column (w[3])
                int supply = item.StationSupply != null && item.StationSupply.TryGetValue(item.BestBuyLocation, out var s) ? s : -1;
                GUI.contentColor = supply == 0 ? Pal.Red : supply < 0 ? Pal.Muted : Pal.Text;
                GUILayout.Label(supply < 0 ? "-" : supply.ToString(), _labelStyle, GUILayout.Width(w[3]));
                GUI.contentColor = Color.white;
            }
            else
            {
                GUI.contentColor = Pal.Muted;
                GUILayout.Label(visited.TryGetValue(item.BestBuyLocation, out var buyDt) ? AgeString(buyDt) : "-", _labelStyle, GUILayout.Width(w[3]));
                GUI.contentColor = Color.white;
            }

            GUI.contentColor = item.BestSellPrice == float.MinValue ? Pal.Muted : Pal.Green;
            GUILayout.Label(item.BestSellPrice == float.MinValue ? "-" : $"{item.BestSellPrice:N0}", _labelStyle, GUILayout.Width(w[4]));
            GUI.contentColor = Pal.Cyan;
            GUILayout.Label(item.BestSellLocation, _labelStyle, GUILayout.Width(w[5]));
            GUI.contentColor = Color.white;
            if (!live)
            {
                GUI.contentColor = Pal.Muted;
                GUILayout.Label(visited.TryGetValue(item.BestSellLocation, out var sellDt) ? AgeString(sellDt) : "-", _labelStyle, GUILayout.Width(w[6]));
                GUI.contentColor = Color.white;
            }

            GUILayout.EndHorizontal();
        }
    }

    // ── Export (From Station) ────────────────────────────────────────────────

    private static readonly int[]    ExportWidths     = { 200, 100,      100, 270, 80 };
    private static readonly string[] ExportLabels     = { "Item", "Buy Here", "Sell Price", "Sell At", "Profit" };
    private static readonly int[]    ExportLiveWidths = { 175, 90, 50,   90, 240, 80 };
    private static readonly string[] ExportLiveLabels = { "Item", "Buy Here", "Qty", "Sell Price", "Sell At", "Profit" };

    private void DrawExportTab()
    {
        if (!MarketDatabase.Instance.TradeRoutesUnlocked)
        {
            GUILayout.Space(20);
            GUILayout.Label("Requires the Trade Routes upgrade. Unlock it in the Upgrades tab.");
            return;
        }

        if (string.IsNullOrEmpty(_exportStation))
        {
            var current = GetCurrentStationName();
            if (!string.IsNullOrEmpty(current) && MarketDatabase.Instance.IsKnownStation(current))
                _exportStation = current;
        }

        GUILayout.BeginHorizontal();
        GUI.contentColor = Pal.Muted;
        GUILayout.Label("Export From:", _labelStyle, GUILayout.Width(85));
        GUI.contentColor = Color.white;
        string btnLabel = string.IsNullOrEmpty(_exportStation) ? "Select Station  ▼" : _exportStation + "  ▼";
        if (GUILayout.Button(btnLabel, _btnStyle, GUILayout.Width(340)))
            _showExportDropdown = !_showExportDropdown;
        GUILayout.EndHorizontal();

        if (_showExportDropdown)
        {
            var stations = MarketDatabase.Instance.GetAllStations().ToList();
            _exportDropdownScroll = GUILayout.BeginScrollView(_exportDropdownScroll, GUILayout.MaxHeight(130));
            foreach (var station in stations)
            {
                if (GUILayout.Button(station, _btnStyle, GUILayout.Width(340)))
                {
                    _exportStation = station;
                    _showExportDropdown = false;
                    _exportSortCol = 0;
                    _exportSortAsc = false;
                }
            }
            GUILayout.EndScrollView();
        }

        bool exportLive = IsLive;
        int[] exportW   = exportLive ? ExportLiveWidths : ExportWidths;

        if (!string.IsNullOrEmpty(_exportStation))
            DrawExportHeader(exportW, exportLive);

        _exportScroll = GUILayout.BeginScrollView(_exportScroll);
        if (string.IsNullOrEmpty(_exportStation))
            GUILayout.Label("Select a source station to view export opportunities.");
        else
            DrawExportRows(exportW, exportLive);
        GUILayout.EndScrollView();
    }

    private void DrawExportHeader(int[] w, bool live)
    {
        string[] labels  = live ? ExportLiveLabels : ExportLabels;
        int      profitI = live ? 5 : 4;
        var sortable = new HashSet<int> { 0, 1, profitI };
        GUILayout.BeginHorizontal();
        for (int i = 0; i < labels.Length; i++)
        {
            if (sortable.Contains(i))
            {
                string arrow = _exportSortCol == i ? (_exportSortAsc ? " ▲" : " ▼") : "";
                if (GUILayout.Button(labels[i] + arrow, _btnStyle, GUILayout.Width(w[i])))
                {
                    if (_exportSortCol == i) _exportSortAsc = !_exportSortAsc;
                    else { _exportSortCol = i; _exportSortAsc = true; }
                }
            }
            else
            {
                GUI.contentColor = Pal.Muted;
                GUILayout.Label(labels[i], _labelStyle, GUILayout.Width(w[i]));
                GUI.contentColor = Color.white;
            }
        }
        GUILayout.EndHorizontal();
    }

    private void DrawExportRows(int[] w, bool live)
    {
        bool filterStock = live;

        var rows = MarketDatabase.Instance.Items.Values
            .Where(i => i.StationPrices != null && i.StationPrices.ContainsKey(_exportStation))
            .Where(i => !filterStock || !(i.StationSupply != null && i.StationSupply.TryGetValue(_exportStation, out int es) && es == 0))
            .Select(i =>
            {
                float buyPrice = i.StationPrices[_exportStation];
                int   supply   = live && i.StationSupply != null && i.StationSupply.TryGetValue(_exportStation, out int sq) ? sq : -1;
                var destinations = i.StationPrices
                    .Where(kvp => kvp.Key != _exportStation)
                    .Select(kvp => (station: kvp.Key, sellPrice: kvp.Value, profit: kvp.Value - buyPrice))
                    .OrderByDescending(s => s.profit)
                    .Take(3)
                    .ToList();
                float bestProfit = destinations.Count > 0 ? destinations[0].profit : float.MinValue;
                return (data: i, buyPrice, supply, destinations, bestProfit);
            })
            .ToList();

        rows = (_exportSortCol, _exportSortAsc) switch
        {
            (0, true)  => rows.OrderBy(r => r.data.ItemName).ToList(),
            (0, false) => rows.OrderByDescending(r => r.data.ItemName).ToList(),
            (1, true)  => rows.OrderBy(r => r.buyPrice).ToList(),
            (1, false) => rows.OrderByDescending(r => r.buyPrice).ToList(),
            (4, true) or (5, true)   => rows.OrderBy(r => r.bestProfit).ToList(),
            (4, false) or (5, false) => rows.OrderByDescending(r => r.bestProfit).ToList(),
            _ => rows.OrderByDescending(r => r.bestProfit).ToList(),
        };

        if (rows.Count == 0)
        {
            GUILayout.Label($"No items recorded for {_exportStation}.");
            return;
        }

        // Column index helpers
        int iItem = 0, iBuyHere = 1, iQty = live ? 2 : -1, iSellPrice = live ? 3 : 2, iSellAt = live ? 4 : 3, iProfit = live ? 5 : 4;

        foreach (var (data, buyPrice, supply, destinations, bestProfit) in rows)
        {
            if (destinations.Count == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(data.ItemName, _labelStyle, GUILayout.Width(w[iItem]));
                GUI.contentColor = Pal.Cyan;
                GUILayout.Label($"{buyPrice:N0}", _labelStyle, GUILayout.Width(w[iBuyHere]));
                if (live)
                {
                    GUI.contentColor = supply == 0 ? Pal.Red : supply < 0 ? Pal.Muted : Pal.Text;
                    GUILayout.Label(supply < 0 ? "-" : supply.ToString(), _labelStyle, GUILayout.Width(w[iQty]));
                }
                GUI.contentColor = Pal.Muted;
                GUILayout.Label("-", _labelStyle, GUILayout.Width(w[iSellPrice]));
                GUILayout.Label("-", _labelStyle, GUILayout.Width(w[iSellAt]));
                GUILayout.Label("-", _labelStyle, GUILayout.Width(w[iProfit]));
                GUI.contentColor = Color.white;
                GUILayout.EndHorizontal();
                DrawSeparator();
                continue;
            }

            bool first = true;
            foreach (var (station, sellPrice, profit) in destinations)
            {
                GUILayout.BeginHorizontal();
                if (first)
                {
                    GUILayout.Label(data.ItemName, _labelStyle, GUILayout.Width(w[iItem]));
                    GUI.contentColor = Pal.Cyan;
                    GUILayout.Label($"{buyPrice:N0}", _labelStyle, GUILayout.Width(w[iBuyHere]));
                    if (live)
                    {
                        GUI.contentColor = supply == 0 ? Pal.Red : supply < 0 ? Pal.Muted : Pal.Text;
                        GUILayout.Label(supply < 0 ? "-" : supply.ToString(), _labelStyle, GUILayout.Width(w[iQty]));
                    }
                    GUI.contentColor = Color.white;
                }
                else
                {
                    GUILayout.Label("", _labelStyle, GUILayout.Width(w[iItem]));
                    GUILayout.Label("", _labelStyle, GUILayout.Width(w[iBuyHere]));
                    if (live) GUILayout.Label("", _labelStyle, GUILayout.Width(w[iQty]));
                }
                GUI.contentColor = Pal.Green;
                GUILayout.Label($"{sellPrice:N0}", _labelStyle, GUILayout.Width(w[iSellPrice]));
                GUI.contentColor = Pal.Cyan;
                GUILayout.Label(station, _labelStyle, GUILayout.Width(w[iSellAt]));
                GUI.contentColor = profit > 0 ? Pal.Green : Pal.Red;
                GUILayout.Label($"{profit:N0}", _labelStyle, GUILayout.Width(w[iProfit]));
                GUI.contentColor = Color.white;
                GUILayout.EndHorizontal();
                first = false;
            }
            DrawSeparator();
        }
    }

    // ── Import (To Station) ──────────────────────────────────────────────────

    private static readonly int[]    ImportWidths     = { 200, 100, 100,      270, 80 };
    private static readonly string[] ImportLabels     = { "Item", "Sell Price", "Buy Price", "Buy From", "Profit" };
    private static readonly int[]    ImportLiveWidths = { 175, 90, 90, 50,   240, 80 };
    private static readonly string[] ImportLiveLabels = { "Item", "Sell Price", "Buy Price", "Qty", "Buy From", "Profit" };

    private void DrawImportTab()
    {
        if (!MarketDatabase.Instance.TradeRoutesUnlocked)
        {
            GUILayout.Space(20);
            GUILayout.Label("Requires the Trade Routes upgrade. Unlock it in the Upgrades tab.");
            return;
        }

        GUILayout.BeginHorizontal();
        GUI.contentColor = Pal.Muted;
        GUILayout.Label("Import To:", _labelStyle, GUILayout.Width(85));
        GUI.contentColor = Color.white;
        string btnLabel = string.IsNullOrEmpty(_importStation) ? "Select Station  ▼" : _importStation + "  ▼";
        if (GUILayout.Button(btnLabel, _btnStyle, GUILayout.Width(340)))
            _showImportDropdown = !_showImportDropdown;
        GUILayout.EndHorizontal();

        if (_showImportDropdown)
        {
            var stations = MarketDatabase.Instance.GetAllStations().ToList();
            _importDropdownScroll = GUILayout.BeginScrollView(_importDropdownScroll, GUILayout.MaxHeight(130));
            if (GUILayout.Button("Clear", _btnStyle, GUILayout.Width(340)))
            {
                _importStation = "";
                _showImportDropdown = false;
                _importSortCol = 0;
                _importSortAsc = true;
            }
            foreach (var station in stations)
            {
                if (GUILayout.Button(station, _btnStyle, GUILayout.Width(340)))
                {
                    _importStation = station;
                    _showImportDropdown = false;
                    _importSortCol = 0;
                    _importSortAsc = false;
                }
            }
            GUILayout.EndScrollView();
        }

        bool importLive = IsLive;
        int[] importW   = importLive ? ImportLiveWidths : ImportWidths;

        if (!string.IsNullOrEmpty(_importStation))
            DrawImportHeader(importW, importLive);

        _importScroll = GUILayout.BeginScrollView(_importScroll);
        if (string.IsNullOrEmpty(_importStation))
            GUILayout.Label("Select a destination station to view import opportunities.");
        else
            DrawImportRows(importW, importLive);
        GUILayout.EndScrollView();
    }

    private void DrawImportHeader(int[] w, bool live)
    {
        string[] labels  = live ? ImportLiveLabels : ImportLabels;
        int      profitI = live ? 5 : 4;
        var sortable = new HashSet<int> { 0, 1, profitI };
        GUILayout.BeginHorizontal();
        for (int i = 0; i < labels.Length; i++)
        {
            if (sortable.Contains(i))
            {
                string arrow = _importSortCol == i ? (_importSortAsc ? " ▲" : " ▼") : "";
                if (GUILayout.Button(labels[i] + arrow, _btnStyle, GUILayout.Width(w[i])))
                {
                    if (_importSortCol == i) _importSortAsc = !_importSortAsc;
                    else { _importSortCol = i; _importSortAsc = true; }
                }
            }
            else
            {
                GUI.contentColor = Pal.Muted;
                GUILayout.Label(labels[i], _labelStyle, GUILayout.Width(w[i]));
                GUI.contentColor = Color.white;
            }
        }
        GUILayout.EndHorizontal();
    }

    private void DrawImportRows(int[] w, bool live)
    {
        bool filterStock = live;

        var rows = MarketDatabase.Instance.Items.Values
            .Where(i => i.StationPrices != null && i.StationPrices.ContainsKey(_importStation))
            .Select(i =>
            {
                float sellPrice = i.StationPrices[_importStation];
                var sources = i.StationPrices
                    .Where(kvp => kvp.Key != _importStation)
                    .Where(kvp => !filterStock || !(i.StationSupply != null && i.StationSupply.TryGetValue(kvp.Key, out int ss) && ss == 0))
                    .Select(kvp =>
                    {
                        int sq = live && i.StationSupply != null && i.StationSupply.TryGetValue(kvp.Key, out int s) ? s : -1;
                        return (station: kvp.Key, buyPrice: kvp.Value, supply: sq, profit: sellPrice - kvp.Value);
                    })
                    .OrderByDescending(s => s.profit)
                    .Take(3)
                    .ToList();
                float bestProfit = sources.Count > 0 ? sources[0].profit : float.MinValue;
                return (data: i, sellPrice, sources, bestProfit);
            })
            .ToList();

        rows = (_importSortCol, _importSortAsc) switch
        {
            (0, true)  => rows.OrderBy(r => r.data.ItemName).ToList(),
            (0, false) => rows.OrderByDescending(r => r.data.ItemName).ToList(),
            (1, true)  => rows.OrderBy(r => r.sellPrice).ToList(),
            (1, false) => rows.OrderByDescending(r => r.sellPrice).ToList(),
            (4, true) or (5, true)   => rows.OrderBy(r => r.bestProfit).ToList(),
            (4, false) or (5, false) => rows.OrderByDescending(r => r.bestProfit).ToList(),
            _ => rows.OrderByDescending(r => r.bestProfit).ToList(),
        };

        if (rows.Count == 0)
        {
            GUILayout.Label($"No items recorded for {_importStation}.");
            return;
        }

        // Column index helpers
        int iItem = 0, iSellPrice = 1, iBuyPrice = 2, iQty = live ? 3 : -1, iBuyFrom = live ? 4 : 3, iProfit = live ? 5 : 4;

        foreach (var (data, sellPrice, sources, bestProfit) in rows)
        {
            if (sources.Count == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(data.ItemName, _labelStyle, GUILayout.Width(w[iItem]));
                GUI.contentColor = Pal.Green;
                GUILayout.Label($"{sellPrice:N0}", _labelStyle, GUILayout.Width(w[iSellPrice]));
                GUI.contentColor = Pal.Muted;
                GUILayout.Label("-", _labelStyle, GUILayout.Width(w[iBuyPrice]));
                if (live) GUILayout.Label("-", _labelStyle, GUILayout.Width(w[iQty]));
                GUILayout.Label("-", _labelStyle, GUILayout.Width(w[iBuyFrom]));
                GUILayout.Label("-", _labelStyle, GUILayout.Width(w[iProfit]));
                GUI.contentColor = Color.white;
                GUILayout.EndHorizontal();
                DrawSeparator();
                continue;
            }

            bool first = true;
            foreach (var (station, buyPrice, supply, profit) in sources)
            {
                GUILayout.BeginHorizontal();
                if (first)
                {
                    GUILayout.Label(data.ItemName, _labelStyle, GUILayout.Width(w[iItem]));
                    GUI.contentColor = Pal.Green;
                    GUILayout.Label($"{sellPrice:N0}", _labelStyle, GUILayout.Width(w[iSellPrice]));
                    GUI.contentColor = Color.white;
                }
                else
                {
                    GUILayout.Label("", _labelStyle, GUILayout.Width(w[iItem]));
                    GUILayout.Label("", _labelStyle, GUILayout.Width(w[iSellPrice]));
                }
                GUI.contentColor = Pal.Cyan;
                GUILayout.Label($"{buyPrice:N0}", _labelStyle, GUILayout.Width(w[iBuyPrice]));
                if (live)
                {
                    GUI.contentColor = supply == 0 ? Pal.Red : supply < 0 ? Pal.Muted : Pal.Text;
                    GUILayout.Label(supply < 0 ? "-" : supply.ToString(), _labelStyle, GUILayout.Width(w[iQty]));
                }
                GUI.contentColor = Pal.Cyan;
                GUILayout.Label(station, _labelStyle, GUILayout.Width(w[iBuyFrom]));
                GUI.contentColor = profit > 0 ? Pal.Green : Pal.Red;
                GUILayout.Label($"{profit:N0}", _labelStyle, GUILayout.Width(w[iProfit]));
                GUI.contentColor = Color.white;
                GUILayout.EndHorizontal();
                first = false;
            }
            DrawSeparator();
        }
    }

    // ── Upgrades ─────────────────────────────────────────────────────────────

#if DEVBUILD
    private const long MarketFeedCost   = 1000L;
    private const long TradeRoutesCost  = 1000L;
#else
    private const long MarketFeedCost   = 1_000_000L;
    private const long TradeRoutesCost  = 500_000L;
#endif

    private void DrawUpgradesTab()
    {
        var db = MarketDatabase.Instance;

        GUILayout.Space(8);

        // --- Market Feed ---
        GUILayout.BeginVertical(_boxStyle);
        GUILayout.BeginHorizontal();
        GUI.contentColor = Pal.Gold;
        GUILayout.Label("Market Feed", _sectorStyle, GUILayout.Width(200));
        GUILayout.Label($"{MarketFeedCost:N0} cr", _labelStyle, GUILayout.ExpandWidth(true));
        GUI.contentColor = Color.white;
        GUILayout.EndHorizontal();
        GUILayout.Label("Automatically refreshes prices at all known stations on each economy tick.", GUILayout.ExpandWidth(true));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (db.MarketFeedUnlocked)
        {
            GUI.contentColor = Pal.Green;
            GUILayout.Label("● Purchased", _labelStyle, GUILayout.Width(100));
            GUI.contentColor = Color.white;
        }
        else
        {
            bool canAfford = GamePlayer.current != null && GamePlayer.current.credits >= MarketFeedCost;
            GUI.enabled = canAfford;
            if (GUILayout.Button($"Buy — {MarketFeedCost:N0} cr", _upgradeBtnStyle, GUILayout.Width(180)))
            {
                GamePlayer.current.RemoveCredits(MarketFeedCost);
                db.MarketFeedUnlocked = true;
                db.Save();
                Plugin.Log.LogInfo("Market Feed upgrade purchased.");
                Plugin.Instance.TriggerLiveScan();
            }
            GUI.enabled = true;
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.Space(6);

        // --- Trade Routes ---
        GUILayout.BeginVertical(_boxStyle);
        GUILayout.BeginHorizontal();
        GUI.contentColor = Pal.Gold;
        GUILayout.Label("Trade Routes", _sectorStyle, GUILayout.Width(200));
        GUILayout.Label($"{TradeRoutesCost:N0} cr", _labelStyle, GUILayout.ExpandWidth(true));
        GUI.contentColor = Color.white;
        GUILayout.EndHorizontal();
        GUILayout.Label("Unlocks the Export and Import tabs. Export shows the best selling destinations for items at any known station. Import shows what to haul to a destination and where to source it cheapest.", GUILayout.ExpandWidth(true));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (db.TradeRoutesUnlocked)
        {
            GUI.contentColor = Pal.Green;
            GUILayout.Label("● Purchased", _labelStyle, GUILayout.Width(100));
            GUI.contentColor = Color.white;
        }
        else
        {
            bool canAfford = GamePlayer.current != null && GamePlayer.current.credits >= TradeRoutesCost;
            GUI.enabled = canAfford;
            if (GUILayout.Button($"Buy — {TradeRoutesCost:N0} cr", _upgradeBtnStyle, GUILayout.Width(180)))
            {
                GamePlayer.current.RemoveCredits(TradeRoutesCost);
                db.TradeRoutesUnlocked = true;
                db.Save();
                Plugin.Log.LogInfo("Trade Routes upgrade purchased.");
            }
            GUI.enabled = true;
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    // ── Settings ─────────────────────────────────────────────────────────────

    private void DrawSettingsTab()
    {
        GUILayout.Space(8);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Scale", GUILayout.Width(80));
        _pendingScale = GUILayout.HorizontalSlider(_pendingScale, 0.5f, 2.0f, GUILayout.Width(220));
        GUILayout.Label($"{_pendingScale:F2}x", GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Transparency", GUILayout.Width(80));
        _pendingAlpha = GUILayout.HorizontalSlider(_pendingAlpha, 0.1f, 1.0f, GUILayout.Width(220));
        GUILayout.Label($"{_pendingAlpha:F2}", GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Width", GUILayout.Width(80));
        _pendingWidth = Mathf.Round(GUILayout.HorizontalSlider(_pendingWidth, 400f, 1600f, GUILayout.Width(220)));
        GUILayout.Label($"{(int)_pendingWidth}px", GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Height", GUILayout.Width(80));
        _pendingHeight = Mathf.Round(GUILayout.HorizontalSlider(_pendingHeight, 300f, 1200f, GUILayout.Width(220)));
        GUILayout.Label($"{(int)_pendingHeight}px", GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", _btnStyle, GUILayout.Width(80)))
        {
            _uiScale.Value  = _pendingScale;
            _uiAlpha.Value  = _pendingAlpha;
            _uiWidth.Value  = _pendingWidth;
            _uiHeight.Value = _pendingHeight;
        }
        GUILayout.Label("Changes apply on Save and persist across sessions.");
        GUILayout.EndHorizontal();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private static void DrawSeparator()
    {
        var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1), GUILayout.ExpandWidth(true));
        var saved = GUI.color;
        GUI.color = Pal.Separator;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = saved;
    }

    private static string GetCurrentStationName()
    {
        if (GamePlayer.current?.currentPointOfInterest is SpaceStation station)
            return $"{station.name} ({station.system?.sector?.name ?? "Unknown Sector"})";
        return null;
    }

    private static string AgeString(double recordedTime)
    {
        if (recordedTime <= 0) return "-";
        double now = MarketDatabase.Instance.CurrentElapsedTime;
        if (now <= 0) return "-";
        double age = now - recordedTime;
        if (age < 60)    return "now";
        if (age < 3600)  return $"{(int)(age / 60)}m";
        if (age < 86400) return $"{(int)(age / 3600)}h";
        return $"{(int)(age / 86400)}d";
    }
}
