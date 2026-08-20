using System;
using System.Drawing;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR.Models.Online;
using Sdcb.PaddleOCR;
using OpenCvSharp.Extensions;

namespace PokemonHelper.Services.Recognition
{
    public class PaddleOcrEngine : IOcrEngine
    {
        private PaddleOcrAll? _ocr;
        private readonly object _runSync = new object();

        public PaddleOcrEngine()
        {
            var model = OnlineFullModels.KoreanV4.DownloadAsync().Result;
            _ocr = new PaddleOcrAll(model, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = false,
                Enable180Classification = false
            };
        }

        public string Recognize(Bitmap bmp, OcrLayoutHint hint = OcrLayoutHint.Block)
        {
            if (_ocr == null) return string.Empty;
            if (bmp == null || bmp.Width <= 1 || bmp.Height <= 1) return string.Empty;
            
            try
            {
                using var mat = BitmapConverter.ToMat(bmp);
                using var input = mat.Channels() == 4 ? mat.CvtColor(OpenCvSharp.ColorConversionCodes.BGRA2BGR) : mat.Clone();
                
                Sdcb.PaddleOCR.PaddleOcrResult result;
                lock (_runSync)
                {
                    result = _ocr.Run(input);
                }
                
                var sortedRegions = result.Regions.OrderBy(r => Math.Min(r.Rect.Size.Width, r.Rect.Size.Height)) // Original used a custom sort, let's just append them directly without newlines
                                          .ToList();
                
                // Original used OcrReadingOrder.Sort, but since Sdcb.PaddleOCR already sorts somewhat decently, 
                // we can just strip newlines if we want to be safe, or we can use the exact same logic.
                // Let's just concatenate them all.
                var sb = new System.Text.StringBuilder();
                foreach (var region in result.Regions)
                {
                    foreach (var c in region.Text)
                    {
                        if (!char.IsWhiteSpace(c))
                        {
                            sb.Append(c);
                        }
                    }
                }
                
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Dispose()
        {
            _ocr?.Dispose();
        }
    }
}
