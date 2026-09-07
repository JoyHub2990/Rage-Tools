using System;
using System.Collections.Generic;

namespace RageLightEditor.Editor
{
    public static class MloAssetCategories_P2
    {
        public static readonly string[] Names =
        {
            "All", "Lights", "Electronics", "Food & Drink", "Desks & Tables", "Seating",
            "Storage & Shelves", "Doors & Windows", "Kitchen & Bathroom", "Office", "Industrial",
            "Signs & Decals", "Vegetation", "Vehicles & Parts", "Misc",
        };
        public const int Count = 15;
        public const int Misc = 14;

        private sealed class Cat
        {
            public int Index;
            public string[] Tokens = Array.Empty<string>();
            public string[] Paths = Array.Empty<string>();
        }

        private static readonly Cat[] Order =
        {
            new Cat { Index = 1, Tokens = new[] { "light", "lights", "lighting", "lamp", "lamps", "lampshade", "chandelier",
                "neon", "lantern", "spotlight", "floodlight", "streetlight", "bulb", "candle", "candles", "sconce",
                "downlight", "lightbox", "luminaire", "lumin", "emissive",
                "lightshade", "lightbulb", "lightpole", "lightrig", "ceilinglight", "walllight", "wallight",
                "striplight", "lightstrip", "worklight", "sidelight", "uplight", "lightfitting" } },

            new Cat { Index = 12, Tokens = new[] { "tree", "trees", "bush", "bushes", "plant", "plants", "potplant",
                "flower", "flowers", "grass", "hedge", "palm", "fern", "shrub", "leaf", "leaves", "cactus", "vine",
                "moss", "planter", "veg", "vegetation", "sapling", "bonsai", "ivy" },
                Paths = new[] { "vegetation", "/veg_", "_veg/" } },

            new Cat { Index = 13, Tokens = new[] { "car", "cars", "vehicle", "vehicles", "truck", "van", "bike",
                "bicycle", "motorbike", "wheel", "wheels", "tyre", "tyres", "tire", "bumper", "exhaust", "bonnet",
                "hubcap", "chassis", "trailer", "boat", "plane", "heli", "helicopter", "spoiler", "carpart",
                "numberplate", "licenseplate" },
                Paths = new[] { "vehicles.rpf", "/vehicles/", "vehiclemods" } },

            new Cat { Index = 11, Tokens = new[] { "sign", "signs", "signage", "decal", "decals", "poster", "posters",
                "billboard", "banner", "logo", "sticker", "graffiti", "plaque", "notice", "roadsign", "exitsign",
                "label", "arrow", "arrows", "marking", "markings" } },

            new Cat { Index = 8, Tokens = new[] { "sink", "toilet", "wc", "urinal", "shower", "showers", "bath",
                "bathtub", "basin", "tap", "taps", "faucet", "fridge", "freezer", "oven", "cooker", "hob", "microwave",
                "dishwasher", "washer", "washingmachine", "mirror", "mirrors", "towel", "towels", "soap", "kitchen",
                "bathroom", "cistern", "bidet", "kettle", "toaster", "extractor", "cubicle" } },

            new Cat { Index = 7, Tokens = new[] { "door", "doors", "doorway", "window", "windows", "gate", "gates",
                "hatch", "shutter", "shutters", "blind", "blinds", "curtain", "curtains", "pane", "panes", "porthole",
                "doorframe", "windowframe" } },

            new Cat { Index = 3, Tokens = new[] { "food", "drink", "drinks", "bottle", "bottles", "can", "cans", "cup",
                "cups", "mug", "mugs", "plate", "plates", "bowl", "bowls", "burger", "pizza", "donut", "coffee", "beer",
                "wine", "whisky", "whiskey", "soda", "snack", "snacks", "fruit", "bread", "cake", "sandwich", "vending",
                "cutlery", "fork", "spoon", "tray", "trays", "cereal", "milk", "juice", "cocktail", "keg", "flask" } },

            new Cat { Index = 2, Tokens = new[] { "tv", "tvs", "television", "monitor", "monitors", "screen", "screens",
                "computer", "pc", "laptop", "keyboard", "printer", "scanner", "radio", "speaker", "speakers", "phone",
                "telephone", "cellphone", "server", "servers", "console", "cctv", "camera", "cameras", "projector",
                "stereo", "hifi", "dvd", "arcade", "jukebox", "atm", "till", "modem", "router", "antenna", "satdish",
                "remote", "controller" } },

            new Cat { Index = 5, Tokens = new[] { "chair", "chairs", "seat", "seats", "seating", "sofa", "couch",
                "stool", "stools", "bench", "benches", "armchair", "recliner", "pew", "bleacher", "beanbag" } },

            new Cat { Index = 4, Tokens = new[] { "desk", "desks", "table", "tables", "worktop", "counter", "counters",
                "countertop", "workbench", "sidetable", "coffeetable", "nightstand", "podium", "lectern" } },

            new Cat { Index = 6, Tokens = new[] { "shelf", "shelves", "shelving", "cabinet", "cabinets", "cupboard",
                "locker", "lockers", "drawer", "drawers", "wardrobe", "crate", "crates", "box", "boxes", "container",
                "containers", "bin", "bins", "rack", "racks", "chest", "safe", "filing", "storage", "pallet", "pallets",
                "barrel", "barrels", "toolbox", "trunk", "dresser", "sideboard", "bookcase", "basket", "sack", "sacks" } },

            new Cat { Index = 9, Tokens = new[] { "paper", "papers", "folder", "folders", "binder", "binders", "pen",
                "pens", "pencil", "notepad", "notebook", "book", "books", "magazine", "newspaper", "clipboard",
                "stapler", "whiteboard", "noticeboard", "corkboard", "calendar", "office", "document", "documents",
                "chart", "charts", "briefcase", "envelope", "mail", "postit", "map", "maps", "photo", "photos",
                "picture", "pictures", "frame", "frames", "painting", "wallart", "trophy" } },

            new Cat { Index = 10, Tokens = new[] { "pipe", "pipes", "generator", "machine", "machinery", "pump",
                "valve", "duct", "ducts", "vent", "vents", "aircon", "compressor", "tank", "tanks", "welder",
                "forklift", "scaffold", "ladder", "ladders", "cable", "cables", "wire", "wires", "fusebox", "elecbox",
                "switchbox", "junction", "motor", "turbine", "girder", "beam", "gantry", "conduit", "transformer",
                "meter", "tool", "tools", "drill", "hammer", "saw", "workshop", "industrial", "extinguisher",
                "hosereel", "hydrant", "cone", "cones", "barrier", "fence", "scaffolding", "toolchest", "jack" },
                Paths = new[] { "/ind_", "industrial" } },
        };

        public static int Of(string name, string path)
        {
            var tokens = Tokenise(name);
            string p = (path ?? "").Replace('\\', '/').ToLowerInvariant();
            foreach (var cat in Order)
            {
                for (int t = 0; t < tokens.Count; t++)
                    for (int k = 0; k < cat.Tokens.Length; k++)
                        if (TokenIs(tokens[t], cat.Tokens[k])) return cat.Index;
                for (int k = 0; k < cat.Paths.Length; k++)
                    if (p.Length > 0 && p.Contains(cat.Paths[k], StringComparison.Ordinal)) return cat.Index;
            }
            return Misc;
        }

        private static List<string> Tokenise(string name)
        {
            var list = new List<string>(8);
            if (string.IsNullOrEmpty(name)) return list;
            name = name.ToLowerInvariant();
            int start = 0;
            for (int i = 0; i <= name.Length; i++)
            {
                if (i < name.Length && name[i] != '_' && name[i] != '-' && name[i] != '.' && name[i] != ' ') continue;
                if (i > start) list.Add(name.Substring(start, i - start));
                start = i + 1;
            }
            return list;
        }

        private static bool TokenIs(string token, string key)
        {
            if (token.Length < key.Length || !token.StartsWith(key, StringComparison.Ordinal)) return false;
            int extra = token.Length - key.Length;
            if (extra == 0) return true;
            if (extra == 1 && token[key.Length] == 's') return true;
            if (extra == 2 && token[key.Length] == 'e' && token[key.Length + 1] == 's') return true;
            bool sawDigit = false;
            for (int i = key.Length; i < token.Length; i++)
            {
                char c = token[i];
                if (c >= '0' && c <= '9') { sawDigit = true; continue; }
                if (sawDigit && i == token.Length - 1 && c >= 'a' && c <= 'z') continue;
                return false;
            }
            return sawDigit;
        }
    }

    public partial class MloAssetLibrary
    {

        public int Category_P2;
        public readonly int[] CategoryCounts_P2 = new int[MloAssetCategories_P2.Count];
        public bool CategoryCountsKnown_P2;

        public const int PageSize_P2 = 300;
        public const int MaxFetch_P2 = 60000;
        public int FetchLimit_P2 = PageSize_P2;
        public bool RequestMore_P2;
        public bool AllLoaded_P2 = true;

        public void ResetPaging_P2()
        {
            FetchLimit_P2 = PageSize_P2;
            RequestMore_P2 = false;
        }

        public int OverFetch_P2(int want) => Category_P2 == 0 ? want : Math.Min(want * 16, MaxFetch_P2);

        public bool Accepts_P2(MloAssetItem it) =>
            Category_P2 == 0 || it == null || MloAssetCategories_P2.Of(it.Name, it.Path) == Category_P2;

        public int FilterToCategory_P2(List<MloAssetItem> items)
        {
            if (Category_P2 != 0)
                for (int i = items.Count - 1; i >= 0; i--)
                    if (!Accepts_P2(items[i])) items.RemoveAt(i);
            return items.Count;
        }

        public int ApplyCategory_P2(List<MloAssetItem> items, int want)
        {
            int matched = FilterToCategory_P2(items);
            if (items.Count > want) items.RemoveRange(want, items.Count - want);
            return matched;
        }
    }
}

