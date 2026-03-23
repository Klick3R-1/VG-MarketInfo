using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace VGMarketInfo;

public class MarketUI : MonoBehaviour
{
    private bool _visible = false;
    private Rect _windowRect = new Rect(20, 20, 860, 560);
    private Vector2 _scrollPos;
    private ConfigEntry<KeyCode> _toggleKey;

    private string _selectedStation = "";
    private bool _showDropdown = false;
    private Vector2 _dropdownScroll;

    private int _sortColumn = 0;
    private bool _sortAscending = true;

    public void Initialize(ConfigEntry<KeyCode> toggleKey)
    {
        _toggleKey = toggleKey;
    }

    private void Update()
    {
        if (Input.GetKeyDown(_toggleKey.Value))
            _visible = !_visible;
    }

    private void OnGUI()
    {
        if (!_visible) return;
        _windowRect = GUILayout.Window(0xB00B, _windowRect, DrawWindow, "VG Market Info Panel");
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Target Station:", GUILayout.Width(95));
        string btnLabel = string.IsNullOrEmpty(_selectedStation) ? "No Target  ▼" : _selectedStation + "  ▼";
        if (GUILayout.Button(btnLabel, GUILayout.Width(340)))
            _showDropdown = !_showDropdown;
        GUILayout.EndHorizontal();

        if (_showDropdown)
        {
            var stations = MarketDatabase.Instance.GetAllStations().ToList();
            _dropdownScroll = GUILayout.BeginScrollView(_dropdownScroll, GUILayout.MaxHeight(130));
            if (GUILayout.Button("No Target", GUILayout.Width(340)))
            {
                _selectedStation = "";
                _showDropdown = false;
                _sortColumn = 0;
                _sortAscending = true;
            }
            foreach (var station in stations)
            {
                if (GUILayout.Button(station, GUILayout.Width(340)))
                {
                    _selectedStation = station;
                    _showDropdown = false;
                    _sortColumn = 0;
                    _sortAscending = false;
                }
            }
            GUILayout.EndScrollView();
        }

        DrawTableHeader();
        _scrollPos = GUILayout.BeginScrollView(_scrollPos);
        if (string.IsNullOrEmpty(_selectedStation))
            DrawOverviewRows();
        else
            DrawTargetRows();
        GUILayout.EndScrollView();

        GUILayout.Label(
            $"{MarketDatabase.Instance.Items.Count} items tracked   |   " +
            $"{MarketDatabase.Instance.GetAllStations().Count()} stations visited   |   " +
            $"{_toggleKey.Value} to toggle",
            GUILayout.ExpandWidth(true));

        GUI.DragWindow();
    }

    private static readonly int[] OverviewWidths = { 190, 90, 185, 90, 185, 80 };
    private static readonly string[] OverviewLabels = { "Item", "Best Buy", "Buy At", "Best Sell", "Sell At", "Updated" };

    private static readonly int[] TargetWidths = { 200, 100, 100, 270, 80 };
    private static readonly string[] TargetLabels = { "Item", "Sell Price", "Buy Price", "Buy From", "Profit" };

    private void DrawTableHeader()
    {
        int[] widths = string.IsNullOrEmpty(_selectedStation) ? OverviewWidths : TargetWidths;
        string[] labels = string.IsNullOrEmpty(_selectedStation) ? OverviewLabels : TargetLabels;

        var sortableOverview = new HashSet<int> { 0, 1, 3 };
        var sortableTarget = new HashSet<int> { 0, 1, 4 };
        var sortable = string.IsNullOrEmpty(_selectedStation) ? sortableOverview : sortableTarget;

        GUILayout.BeginHorizontal();
        for (int i = 0; i < labels.Length; i++)
        {
            if (sortable.Contains(i))
            {
                string arrow = _sortColumn == i ? (_sortAscending ? " ▲" : " ▼") : "";
                if (GUILayout.Button(labels[i] + arrow, GUILayout.Width(widths[i])))
                {
                    if (_sortColumn == i) _sortAscending = !_sortAscending;
                    else { _sortColumn = i; _sortAscending = true; }
                }
            }
            else
            {
                GUILayout.Label(labels[i], GUILayout.Width(widths[i]));
            }
        }
        GUILayout.EndHorizontal();
    }

    private void DrawOverviewRows()
    {
        var items = MarketDatabase.Instance.Items.Values.ToList();

        items = (_sortColumn, _sortAscending) switch
        {
            (0, true)  => items.OrderBy(i => i.ItemName).ToList(),
            (0, false) => items.OrderByDescending(i => i.ItemName).ToList(),
            (1, true)  => items.OrderBy(i => i.BestBuyPrice).ToList(),
            (1, false) => items.OrderByDescending(i => i.BestBuyPrice).ToList(),
            (3, true)  => items.OrderBy(i => i.BestSellPrice).ToList(),
            (3, false) => items.OrderByDescending(i => i.BestSellPrice).ToList(),
            _          => items.OrderBy(i => i.ItemName).ToList(),
        };

        if (items.Count == 0)
        {
            GUILayout.Label("No data yet. Dock at stations to collect prices.");
            return;
        }

        Color def = GUI.contentColor;
        foreach (var item in items)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(item.ItemName, GUILayout.Width(OverviewWidths[0]));

            GUI.contentColor = item.BestBuyPrice == float.MaxValue ? Color.gray : Color.cyan;
            GUILayout.Label(item.BestBuyPrice == float.MaxValue ? "-" : $"{item.BestBuyPrice:N0}", GUILayout.Width(OverviewWidths[1]));
            GUI.contentColor = def;
            GUILayout.Label(item.BestBuyLocation, GUILayout.Width(OverviewWidths[2]));

            GUI.contentColor = item.BestSellPrice == float.MinValue ? Color.gray : Color.green;
            GUILayout.Label(item.BestSellPrice == float.MinValue ? "-" : $"{item.BestSellPrice:N0}", GUILayout.Width(OverviewWidths[3]));
            GUI.contentColor = def;
            GUILayout.Label(item.BestSellLocation, GUILayout.Width(OverviewWidths[4]));

            GUI.contentColor = Color.gray;
            GUILayout.Label(AgeString(item.LastUpdated), GUILayout.Width(OverviewWidths[5]));
            GUI.contentColor = def;

            GUILayout.EndHorizontal();
        }
    }

    private void DrawTargetRows()
    {
        var rows = MarketDatabase.Instance.Items.Values
            .Where(i => i.StationPrices != null && i.StationPrices.ContainsKey(_selectedStation))
            .Select(i =>
            {
                float sellPrice = i.StationPrices[_selectedStation];
                var sources = i.StationPrices
                    .Where(kvp => kvp.Key != _selectedStation)
                    .Select(kvp => (station: kvp.Key, buyPrice: kvp.Value, profit: sellPrice - kvp.Value))
                    .OrderByDescending(s => s.profit)
                    .Take(3)
                    .ToList();
                float bestProfit = sources.Count > 0 ? sources[0].profit : float.MinValue;
                return (data: i, sellPrice, sources, bestProfit);
            })
            .ToList();

        rows = (_sortColumn, _sortAscending) switch
        {
            (0, true)  => rows.OrderBy(r => r.data.ItemName).ToList(),
            (0, false) => rows.OrderByDescending(r => r.data.ItemName).ToList(),
            (1, true)  => rows.OrderBy(r => r.sellPrice).ToList(),
            (1, false) => rows.OrderByDescending(r => r.sellPrice).ToList(),
            (4, true)  => rows.OrderBy(r => r.bestProfit).ToList(),
            (4, false) => rows.OrderByDescending(r => r.bestProfit).ToList(),
            _          => rows.OrderByDescending(r => r.bestProfit).ToList(),
        };

        if (rows.Count == 0)
        {
            GUILayout.Label($"No items recorded for {_selectedStation}.");
            return;
        }

        Color def = GUI.contentColor;
        foreach (var (data, sellPrice, sources, bestProfit) in rows)
        {
            if (sources.Count == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(data.ItemName, GUILayout.Width(TargetWidths[0]));
                GUI.contentColor = Color.green;
                GUILayout.Label($"{sellPrice:N0}", GUILayout.Width(TargetWidths[1]));
                GUI.contentColor = Color.gray;
                GUILayout.Label("-", GUILayout.Width(TargetWidths[2]));
                GUILayout.Label("-", GUILayout.Width(TargetWidths[3]));
                GUILayout.Label("-", GUILayout.Width(TargetWidths[4]));
                GUI.contentColor = def;
                GUILayout.EndHorizontal();
                continue;
            }

            bool first = true;
            foreach (var (station, buyPrice, profit) in sources)
            {
                GUILayout.BeginHorizontal();

                if (first)
                {
                    GUILayout.Label(data.ItemName, GUILayout.Width(TargetWidths[0]));
                    GUI.contentColor = Color.green;
                    GUILayout.Label($"{sellPrice:N0}", GUILayout.Width(TargetWidths[1]));
                    GUI.contentColor = def;
                }
                else
                {
                    GUILayout.Label("", GUILayout.Width(TargetWidths[0]));
                    GUILayout.Label("", GUILayout.Width(TargetWidths[1]));
                }

                GUI.contentColor = Color.cyan;
                GUILayout.Label($"{buyPrice:N0}", GUILayout.Width(TargetWidths[2]));
                GUI.contentColor = def;
                GUILayout.Label(station, GUILayout.Width(TargetWidths[3]));
                GUI.contentColor = profit > 0 ? Color.green : Color.red;
                GUILayout.Label($"{profit:N0}", GUILayout.Width(TargetWidths[4]));
                GUI.contentColor = def;

                GUILayout.EndHorizontal();
                first = false;
            }
        }
    }

    private static string AgeString(DateTime dt)
    {
        if (dt == default) return "-";
        var age = DateTime.Now - dt;
        if (age.TotalMinutes < 1) return "now";
        if (age.TotalHours < 1)   return $"{(int)age.TotalMinutes}m";
        if (age.TotalDays < 1)    return $"{(int)age.TotalHours}h";
        return $"{(int)age.TotalDays}d";
    }
}
