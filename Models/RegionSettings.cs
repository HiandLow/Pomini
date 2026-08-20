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
            MyHp = new RectangleF(0.06273437f, 0.04402778f, 0.08171875f, 0.052916665f), // Placeholder for now
            OpponentHp = new RectangleF(0.06273437f, 0.04402778f, 0.08171875f, 0.052916665f), // Placeholder for now
            Log = new RectangleF(0.13960937f, 0.7190278f, 0.54484373f, 0.14027779f),
            PickEntry = new RectangleF(0.41828123f, 0.015416667f, 0.16328126f, 0.05f),
            StatsDisplay = new RectangleF(0.84296876f, 0.9291666f, 0.10359375f, 0.054166667f),

            OpponentPartySlots = new RectangleF[6]
            {
                new RectangleF(0.83781254f, 0.1425f, 0.06367187f, 0.10752315f),
                new RectangleF(0.8374219f, 0.25905094f, 0.06367187f, 0.106828704f),
                new RectangleF(0.83781254f, 0.3762963f, 0.06367187f, 0.106828704f),
                new RectangleF(0.8374219f, 0.4928472f, 0.06601562f, 0.10474537f),
                new RectangleF(0.83781254f, 0.6114814f, 0.06523437f, 0.10405093f),
                new RectangleF(0.83781254f, 0.7266435f, 0.065625f, 0.10613426f)
            },
            OpponentPartyTypeSlots = new RectangleF[6]
            {
                new RectangleF(0.90734375f, 0.15152778f, 0.055546876f, 0.051388897f),
                new RectangleF(0.90695316f, 0.2688889f, 0.05515625f, 0.05069445f),
                new RectangleF(0.90695316f, 0.3841667f, 0.055546876f, 0.051388897f),
                new RectangleF(0.90695316f, 0.5001389f, 0.056328125f, 0.05347223f),
                new RectangleF(0.9077344f, 0.61819446f, 0.054765623f, 0.05069445f),
                new RectangleF(0.9077344f, 0.7341667f, 0.054765623f, 0.052777786f)
            }
        };
    }
}
