using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

using Source.Player;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.Simulation.Economy;
using Source.Simulation.Story;
using Source.Util;
using Behaviour.Item;

namespace VGMarketInfo;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("VanguardGalaxy.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;

    private ConfigEntry<KeyCode> _toggleKey;
    private ConfigEntry<float> _uiScale;
    private ConfigEntry<float> _uiAlpha;
    private ConfigEntry<float> _uiWidth;
    private ConfigEntry<float> _uiHeight;
    private string _lastStationName = "";
    private float _lastEconomyTimer = 0f;
    private float _supplyRescanTimer = 0f;

    private void Awake()
    {
        Log = Logger;

        _toggleKey = Config.Bind("General", "ToggleKey",  KeyCode.F8, "Key to show/hide the market info panel.");
        _uiScale   = Config.Bind("UI", "Scale",  1.0f, new ConfigDescription("Window scale factor.", new AcceptableValueRange<float>(0.5f, 2.0f)));
        _uiAlpha   = Config.Bind("UI", "Alpha",  1.0f, new ConfigDescription("Window transparency.", new AcceptableValueRange<float>(0.1f, 1.0f)));
        _uiWidth   = Config.Bind("UI", "Width",  860f, new ConfigDescription("Window width.",  new AcceptableValueRange<float>(400f, 1600f)));
        _uiHeight  = Config.Bind("UI", "Height", 560f, new ConfigDescription("Window height.", new AcceptableValueRange<float>(300f, 1200f)));

        MarketDatabase.Instance.Load();
#if DEVBUILD
        DevDiary.Log(Log);
#endif

        var ui = gameObject.AddComponent<MarketUI>();
        ui.Initialize(_toggleKey, _uiScale, _uiAlpha, _uiWidth, _uiHeight);

        Instance = this;
        Log.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} loaded. Press {_toggleKey.Value} to open the market panel.");
    }

    private void Update()
    {
        if (GamePlayer.current != null)
            MarketDatabase.Instance.CurrentElapsedTime = GamePlayer.current.elapsedTime;

        TryLocationKeyMigration();
        TryReadMarketData();
        TryLiveFeedUpdate();
        TrySupplyRescan();
    }

    private void OnDestroy()
    {
        MarketDatabase.Instance.Save();
    }

    private void TryLocationKeyMigration()
    {
        if (!MarketDatabase.Instance.NeedsLocationKeyMigration) return;
        if (GamePlayer.current?.map == null) return;

        var systemToSector = new Dictionary<string, string>();
        foreach (MapPointOfInterest poi in GamePlayer.current.map.allPointsOfInterest)
        {
            if (poi is SpaceStation station && station.system?.name != null && station.system.sector?.name != null)
                systemToSector[station.system.name] = station.system.sector.name;
        }

        MarketDatabase.Instance.MigrateLocationKeys(systemToSector);
    }

    private void TryReadMarketData()
    {
        if (GamePlayer.current == null) return;

        var poi = GamePlayer.current.currentPointOfInterest;
        if (poi is not SpaceStation station) return;

        string sectorName  = station.system?.sector?.name ?? "Unknown Sector";
        string stationName = station.name ?? "Unknown Station";
        string location    = $"{stationName} ({sectorName})";

        if (location == _lastStationName) return;
        _lastStationName = location;

        Log.LogInfo($"Docked at {location} — scanning market data...");
        ScanStationMarket(station, location);
    }

    private void TryLiveFeedUpdate()
    {
        if (!MarketDatabase.Instance.MarketFeedUnlocked) return;
        if (GamePlayer.current == null) return;

        var econ = GamePlayer.current.GetStoryteller<Economy>();
        if (econ == null) return;

        float timer = econ.economyTimer;

        if (_lastEconomyTimer != 0f && timer > _lastEconomyTimer)
        {
            Log.LogInfo("Economy tick detected — refreshing known stations...");
            ScanAllKnownStations();
        }

        _lastEconomyTimer = timer;
    }

    private void TrySupplyRescan()
    {
        if (!MarketDatabase.Instance.MarketFeedUnlocked) return;
        if (GamePlayer.current?.currentPointOfInterest is not SpaceStation station) return;
        if (station.economy == null) return;

        _supplyRescanTimer -= Time.deltaTime;
        if (_supplyRescanTimer > 0f) return;
        _supplyRescanTimer = 1f;

        string location = $"{station.name} ({station.system?.sector?.name ?? "Unknown Sector"})";
        bool changed = false;

        foreach (LocalEconomyItem economyItem in station.economy.allItems)
        {
            string itemName = Translation.Translate(economyItem.item.displayName);
            if (!MarketDatabase.Instance.Items.TryGetValue(itemName, out var data)) continue;

            int known = (data.StationSupply != null && data.StationSupply.TryGetValue(location, out int ks)) ? ks : -1;
            if (known == economyItem.currentSupply) continue;

            MarketDatabase.Instance.RecordPrice(itemName, economyItem.cost, economyItem.currentSupply, location, GamePlayer.current?.elapsedTime ?? 0);
            changed = true;
        }

        if (changed)
            MarketDatabase.Instance.Save();
    }

    internal static Plugin Instance { get; private set; }

    internal void TriggerLiveScan() => ScanAllKnownStations();

    private void ScanAllKnownStations()
    {
        if (GamePlayer.current?.map == null) return;

        int stations = 0;
        int items = 0;

        foreach (MapPointOfInterest poi in GamePlayer.current.map.allPointsOfInterest)
        {
            if (poi is not SpaceStation station || station.economy == null) continue;

            string sectorName = station.system?.sector?.name ?? "Unknown Sector";
            string location   = $"{station.name} ({sectorName})";

            if (!MarketDatabase.Instance.IsKnownStation(location)) continue;

            foreach (LocalEconomyItem economyItem in station.economy.allItems)
            {
                string itemName = Translation.Translate(economyItem.item.displayName);
                MarketDatabase.Instance.RecordPrice(itemName, economyItem.cost, economyItem.currentSupply, location, GamePlayer.current?.elapsedTime ?? 0);
                items++;
            }

            stations++;
        }

        MarketDatabase.Instance.Save();
        Log.LogInfo($"Live feed updated {stations} stations, {items} prices.");
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
            MarketDatabase.Instance.RecordPrice(itemName, economyItem.cost, economyItem.currentSupply, location, GamePlayer.current?.elapsedTime ?? 0);
            count++;
        }

        MarketDatabase.Instance.Save();
        Log.LogInfo($"Scanned {count} trade items at {location}.");
    }
}
