using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VGMarketInfo;

[Serializable]
public class ItemMarketData
{
    public string ItemName;

    public float BestBuyPrice = float.MaxValue;
    public string BestBuyLocation = "";

    public float BestSellPrice = float.MinValue;
    public string BestSellLocation = "";

    public float LastSeenPrice;
    public string LastSeenLocation = "";
    public double LastUpdated;  // in-game elapsed seconds (GamePlayer.elapsedTime)

    public Dictionary<string, float> StationPrices = new();
    public Dictionary<string, int>   StationSupply = new();
}

public class SaveFile
{
    public string Version { get; set; }
    public Dictionary<string, ItemMarketData> Items { get; set; } = new();
    public Dictionary<string, double> StationLastVisited { get; set; } = new();
    public bool MarketFeedUnlocked { get; set; } = false;
    public bool TradeRoutesUnlocked { get; set; } = false;
#if DEVBUILD
    public string DevBuildId { get; set; } = "";
#endif
}

public static class DatabaseMigration
{
    public const string CurrentVersion = "1.1.0";

    // Each entry: target version + action that brings the save up to that version.
    // Always append new entries — never edit existing ones.
    // New fields must use null/NaN — no invented values.
    private static readonly (string To, Action<SaveFile> Apply)[] Migrations =
    {
        ("1.0.1", f =>
        {
            // Added StationLastVisited. Leave empty — timestamps fill in on next visit.
            f.StationLastVisited ??= new Dictionary<string, double>();
        }),
        ("1.1.0", f =>
        {
            // Added MarketFeedUnlocked and TradeRoutesUnlocked. Must default to false — player has not paid for it.
            f.MarketFeedUnlocked = false;
            f.TradeRoutesUnlocked = false;
            // Timestamps switched from DateTime to double (in-game seconds).
            // Old DateTime values were zeroed during JObject pre-processing in Load() —
            // zero means "unknown age", which is safe and honest.
        }),
    };

    public static void Run(SaveFile file, ManualLogSource log)
    {
        var saveVer = Parse(file.Version ?? "0.0.0");
        var currentVer = Parse(CurrentVersion);
        if (saveVer >= currentVer) return;

        foreach (var (to, apply) in Migrations)
        {
            var targetVer = Parse(to);
            if (saveVer < targetVer)
            {
                log.LogInfo($"[VGMarketInfo] Migrating save {file.Version ?? "pre-versioned"} → {to}");
                apply(file);
                file.Version = to;
                saveVer = targetVer;
            }
        }
    }

    // Before v1.1.0, timestamps were DateTime strings. Replace them with 0.0 so
    // Newtonsoft.Json can deserialize the new double fields without errors.
    public static void ZeroLegacyTimestamps(JObject jobj)
    {
        if (jobj["StationLastVisited"] is JObject slv)
            foreach (var prop in slv.Properties().ToList())
                prop.Value = 0.0;

        if (jobj["Items"] is JObject items)
            foreach (var itemProp in items.Properties())
                if (itemProp.Value is JObject itemObj && itemObj["LastUpdated"] is JToken lt
                    && lt.Type == JTokenType.String)
                    itemObj["LastUpdated"] = 0.0;
    }

    private static Version Parse(string v)
        => Version.TryParse(v, out var ver) ? ver : new Version(0, 0, 0);
}

public class MarketDatabase
{
    private static MarketDatabase _instance;
    public static MarketDatabase Instance => _instance ??= new MarketDatabase();

    public Dictionary<string, ItemMarketData> Items { get; private set; } = new();
    public Dictionary<string, double> StationLastVisited { get; private set; } = new();
    public bool MarketFeedUnlocked { get; set; } = false;
    public bool TradeRoutesUnlocked { get; set; } = false;

    // Updated every frame by Plugin.Update when GamePlayer is available.
    // Used by AgeString so it never needs to call GamePlayer.current at render time.
    public double CurrentElapsedTime { get; set; } = 0;

    private static string SavePath => Path.Combine(Paths.ConfigPath, "VGMarketInfo_data.json");

    public void RecordPrice(string itemName, float price, int supply, string location, double elapsedTime)
    {
        if (string.IsNullOrWhiteSpace(itemName) || price <= 0 || string.IsNullOrWhiteSpace(location))
            return;

        if (!Items.TryGetValue(itemName, out var data))
        {
            data = new ItemMarketData
            {
                ItemName = itemName,
                BestBuyPrice = price,
                BestBuyLocation = location,
                BestSellPrice = price,
                BestSellLocation = location,
            };
            Items[itemName] = data;
        }

        if (price < data.BestBuyPrice)
        {
            data.BestBuyPrice = price;
            data.BestBuyLocation = location;
        }

        if (price > data.BestSellPrice)
        {
            data.BestSellPrice = price;
            data.BestSellLocation = location;
        }

        data.StationPrices ??= new();
        data.StationPrices[location] = price;

        data.StationSupply ??= new();
        data.StationSupply[location] = supply;

        if (elapsedTime > 0)
            StationLastVisited[location] = elapsedTime;

        data.LastSeenPrice = price;
        data.LastSeenLocation = location;
        if (elapsedTime > 0)
            data.LastUpdated = elapsedTime;
    }

    public bool IsKnownStation(string location)
    {
        return Items.Values.Any(i => i.StationPrices != null && i.StationPrices.ContainsKey(location));
    }

    public IEnumerable<string> GetAllStations()
    {
        return Items.Values
            .SelectMany(i => i.StationPrices?.Keys ?? Enumerable.Empty<string>())
            .Distinct()
            .OrderBy(s => s);
    }

    public void RemoveItem(string itemName)
    {
        Items.Remove(itemName);
    }

    public void Save()
    {
        try
        {
            var file = new SaveFile
            {
                Version = DatabaseMigration.CurrentVersion,
                Items = Items,
                StationLastVisited = StationLastVisited,
                MarketFeedUnlocked = MarketFeedUnlocked,
                TradeRoutesUnlocked = TradeRoutesUnlocked,
#if DEVBUILD
                DevBuildId = DevDiary.BuildId,
#endif
            };
            File.WriteAllText(SavePath, JsonConvert.SerializeObject(file, Formatting.Indented));
            Plugin.Log.LogInfo($"Market data saved ({Items.Count} items).");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Failed to save market data: {e.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                Plugin.Log.LogInfo("No existing market data found, starting fresh.");
                return;
            }

            string json = File.ReadAllText(SavePath);
            var jobj = JObject.Parse(json);
            SaveFile file;

            if (jobj.ContainsKey("Items"))
            {
                // Pre-process: zero out old DateTime timestamp strings so the
                // new double fields deserialize cleanly (saves before v1.1.0).
                var rawVersion = jobj["Version"]?.ToString() ?? "0.0.0";
                if (Version.TryParse(rawVersion, out var sv) && sv < new Version(1, 1, 0))
                    DatabaseMigration.ZeroLegacyTimestamps(jobj);
                file = jobj.ToObject<SaveFile>() ?? new SaveFile();
            }
            else
            {
                // Pre-versioned plain dict format (before 1.0.0)
                file = new SaveFile
                {
                    Version = "0.0.0",
                    Items = jobj.ToObject<Dictionary<string, ItemMarketData>>() ?? new()
                };
            }

            DatabaseMigration.Run(file, Plugin.Log);

            Items = file.Items ?? new();
            StationLastVisited = file.StationLastVisited ?? new();
            MarketFeedUnlocked = file.MarketFeedUnlocked;
            TradeRoutesUnlocked = file.TradeRoutesUnlocked;

            Plugin.Log.LogInfo($"Market data loaded ({Items.Count} items, save v{file.Version}).");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Failed to load market data: {e.Message}");
            Items = new();
            StationLastVisited = new();
        }
    }
}
