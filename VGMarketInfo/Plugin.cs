using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

using Source.Player;
using Source.Galaxy.POI;
using Source.Simulation.Economy;
using Source.Util;
using Behaviour.Item;

namespace VGMarketInfo;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("VanguardGalaxy.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;

    private ConfigEntry<KeyCode> _toggleKey;
    private string _lastStationName = "";

    private void Awake()
    {
        Log = Logger;

        _toggleKey = Config.Bind(
            "General",
            "ToggleKey",
            KeyCode.F8,
            "Key to show/hide the market info panel."
        );

        MarketDatabase.Instance.Load();

        var ui = gameObject.AddComponent<MarketUI>();
        ui.Initialize(_toggleKey);

        Log.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} loaded. Press {_toggleKey.Value} to open the market panel.");
    }

    private void Update()
    {
        TryReadMarketData();
    }

    private void OnDestroy()
    {
        MarketDatabase.Instance.Save();
    }

    private void TryReadMarketData()
    {
        if (GamePlayer.current == null) return;

        var poi = GamePlayer.current.currentPointOfInterest;
        if (poi is not SpaceStation station) return;

        string systemName  = GamePlayer.current.currentSystem?.name ?? "Unknown System";
        string stationName = station.name ?? "Unknown Station";
        string location    = $"{stationName} ({systemName})";

        if (location == _lastStationName) return;
        _lastStationName = location;

        Log.LogInfo($"Docked at {location} — scanning market data...");
        ScanStationMarket(station, location);
    }

    private void ScanStationMarket(SpaceStation station, string location)
    {
        LocalEconomy economy = station.economy;
        if (economy == null)
        {
            Log.LogInfo($"{location} has no trade terminal — skipping.");
            return;
        }

        int count = 0;
        foreach (LocalEconomyItem economyItem in economy.allItems)
        {
            string itemName = Translation.Translate(economyItem.item.displayName);
            float price = economyItem.cost;
            MarketDatabase.Instance.RecordPrice(itemName, price, location);
            count++;
        }

        MarketDatabase.Instance.Save();
        Log.LogInfo($"Scanned {count} trade items at {location}.");
    }
}
