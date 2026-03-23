using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json;

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
    public DateTime LastUpdated;

    public Dictionary<string, float> StationPrices = new();
}

public class MarketDatabase
{
    private static MarketDatabase _instance;
    public static MarketDatabase Instance => _instance ??= new MarketDatabase();

    public Dictionary<string, ItemMarketData> Items { get; private set; } = new();

    private static string SavePath => Path.Combine(Paths.ConfigPath, "VGMarketInfo_data.json");

    public void RecordPrice(string itemName, float price, string location)
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

        data.LastSeenPrice = price;
        data.LastSeenLocation = location;
        data.LastUpdated = DateTime.Now;
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
            string json = JsonConvert.SerializeObject(Items, Formatting.Indented);
            File.WriteAllText(SavePath, json);
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
            Items = JsonConvert.DeserializeObject<Dictionary<string, ItemMarketData>>(json) ?? new();
            Plugin.Log.LogInfo($"Market data loaded ({Items.Count} items).");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Failed to load market data: {e.Message}");
            Items = new();
        }
    }
}
