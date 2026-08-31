using System.Collections.Generic;
using System.Drawing;

namespace PokemonHelper.Models
{
    public class RegionSettings
    {
        public RectangleF MyHp { get; set; }
        public RectangleF OpponentHp { get; set; }
        public RectangleF Log { get; set; }
        public RectangleF PickEntry { get; set; }
        public RectangleF StatsDisplay { get; set; }

        public IReadOnlyList<RectangleF> OpponentPartySlots { get; set; }
        public IReadOnlyList<RectangleF> OpponentPartyTypeSlots { get; set; }

        public static RegionSettings Default => new RegionSettings
        {
            MyHp = new RectangleF(0.1183f, 0.7163f, 0.0614f, 0.0282f),
            OpponentHp = new RectangleF(0.8922f, 0.297f, 0.0609f, 0.0254f),
            Log = new RectangleF(0.155f, 0.6169f, 0.5519f, 0.0575f),
            PickEntry = new RectangleF(0.41828123f, 0.015416667f, 0.16328126f, 0.05f),
            StatsDisplay = new RectangleF(0.5958f, 0.7122f, 0.1543f, 0.0211f),

            OpponentPartySlots = new RectangleF[6]
            {
                new RectangleF(0.8291f, 0.3159f, 0.0666f, 0.0519f),
                new RectangleF(0.8292f, 0.3775f, 0.0666f, 0.0519f),
                new RectangleF(0.8302f, 0.4351f, 0.0666f, 0.0519f),
                new RectangleF(0.8313f, 0.4964f, 0.0666f, 0.0519f),
                new RectangleF(0.8323f, 0.5555f, 0.0666f, 0.0519f),
                new RectangleF(0.8318f, 0.6160f, 0.0666f, 0.0519f)
            },
            OpponentPartyTypeSlots = new RectangleF[6]
            {
                new RectangleF(0.9026f, 0.3157f, 0.0564f, 0.0323f),
                new RectangleF(0.9022f, 0.3741f, 0.0564f, 0.0323f),
                new RectangleF(0.9022f, 0.4343f, 0.0564f, 0.0323f),
                new RectangleF(0.9023f, 0.4951f, 0.0564f, 0.0323f),
                new RectangleF(0.9030f, 0.5547f, 0.0564f, 0.0323f),
                new RectangleF(0.9019f, 0.6154f, 0.0564f, 0.0323f)
            }
        };
    }

    public class RegionSettingsConfig
    {
        public int ActivePreset { get; set; } = 1;
        public System.Collections.Generic.Dictionary<int, RegionSettings> Presets { get; set; } = new System.Collections.Generic.Dictionary<int, RegionSettings>();
    }
}
