using Microsoft.AspNetCore.SignalR;
using PokemonHelper.Hubs;
using PokemonHelper.Models;
using PokemonHelper.Utils;
using PokemonHelper.Services.Recognition;
using System.Text.Json;
using System.IO;
using OpenCvSharp.Extensions;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;

namespace PokemonHelper.Services
{
    public class ScreenCaptureService : IScreenCapturer
    {
        private readonly IHubContext<PokemonHub> _hubContext;
        private List<Pokemon> _pokemonList = new();
        private List<UsagePokemon> _usageList = new();
        private Dictionary<int, IReadOnlyList<IReadOnlyList<PokemonType>>> _dexTypes = new();

        public static bool IsRunning { get; set; } = false;
        public static IntPtr TargetHwnd { get; set; } = IntPtr.Zero;

        public static RegionSettings Settings { get; set; } = new JsonRegionSettingsRepository().Load();
        
        public static object? LastPartyData { get; set; }

        private CancellationTokenSource? _pickCts;

        private Dictionary<string, (long Timestamp, string Description)> _lastEventMap = new Dictionary<string, (long Timestamp, string Description)>();
        


        private CancellationTokenSource _cts = new CancellationTokenSource();
        private string _lastSentJson = "";
        private string _lastDebugRaw = "";
        private ulong _lastLogFingerprint = 0;
        private string _lastEmittedLog = "";
        private string _lastHpNameLog = "";
        
        public static string? TestVideoPath { get; set; }
        private long _virtualTimeMs = 0;
        public long GetCurrentTimeMs()
        {
            if (TestVideoPath != null) return _virtualTimeMs;
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private LogCascadeEmitGate _logEmitGate = new LogCascadeEmitGate();
        private int _logCascadeInFlight = 0;

        private int _myActiveIndex = 0;
        private int _oppActiveIndex = 0;
        
        private Dictionary<int, List<double>> _myHpHistories = new Dictionary<int, List<double>>();
        private Dictionary<int, List<double>> _oppHpHistories = new Dictionary<int, List<double>>();
        private Dictionary<int, double> _lastBroadcastMyHpMap = new Dictionary<int, double>();
        private Dictionary<int, double> _lastBroadcastOppHpMap = new Dictionary<int, double>();

        private Dictionary<string, string> _hpOcrCorrections = new Dictionary<string, string> {
            {"T", "7"}, {"!", "1"}, {"f", "1"}, {"I", "1"}, {"O", "0"}
        };
        private string _hpOcrWhitelist = "0123456789";

        private void LoadHpOcrConfig()
        {
            try
            {
                string path = "Data/hp_ocr_config.json";
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("corrections", out var corrProp))
                    {
                        var newCorr = new Dictionary<string, string>();
                        foreach (var prop in corrProp.EnumerateObject())
                        {
                            newCorr[prop.Name] = prop.Value.GetString() ?? "";
                        }
                        _hpOcrCorrections = newCorr;
                    }
                    
                    if (root.TryGetProperty("whitelist", out var whiteProp))
                    {
                        _hpOcrWhitelist = whiteProp.GetString() ?? "0123456789";
                    }
                    
                    Console.WriteLine("[OCR] HP Config loaded from JSON");
                }
            }
            catch (Exception ex) { Console.WriteLine("HP OCR Config Load Error: " + ex.Message); }
        }

        private double GetMode(List<double> history)
        {
            if (history.Count == 0) return double.NaN;
            return history.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
        }
        private readonly object _logEmitLock = new object();

        
        public static bool IsPartyRecognitionEnabled { get; set; } = true; // 배틀 초기화 시 true로 변경됨

        // P/Invoke
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        private const uint SRCCOPY = 13369376u;
        private const uint MONITOR_DEFAULTTONEAREST = 2u;
        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint PW_RENDERFULLCONTENT = 2u;
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        private PickEntryDetector _pickDetector;
        private OpponentPartyRecognizer _partyRecognizer;
        private IOcrEngine _ocrEngine;
        private JsonSpritesProvider _spritesProvider;
        
        private WindowsOcrEngine _windowsOcrEngine;
        private ActiveBattleDetector _battleDetector;
        private LogOcr _logOcr;
        private LogFusionVoter _logVoter;
        private BattleLogAnalyzer _logAnalyzer;
        private LogTextCorrector _logCorrector;

        public static ScreenCaptureService? Instance { get; private set; }

        public void ResetBattleState()
        {
            IsPartyRecognitionEnabled = true;
            _logAnalyzer.Reset();
        }

        public ScreenCaptureService(IHubContext<PokemonHub> hubContext)
        {
            Instance = this;
            _hubContext = hubContext;
            LoadPokemonData();
            LoadHpOcrConfig();
            
            _ocrEngine = new PaddleOcrEngine();
            
            _spritesProvider = new JsonSpritesProvider("sprites.json");
            var nccMatcher = new NccMatcher(_spritesProvider);
            var typeMatcher = new TypeIconMatcher("type-icons");

            _pickDetector = new PickEntryDetector(this, _ocrEngine);
            _partyRecognizer = new OpponentPartyRecognizer(this, nccMatcher, typeMatcher, _spritesProvider);

            _battleDetector = new ActiveBattleDetector(this);
            _windowsOcrEngine = new WindowsOcrEngine();
            _logOcr = new LogOcr(this, _windowsOcrEngine, Settings.Log);
            _logOcr.UseThresholdPreprocess = true;
            _logVoter = new LogFusionVoter();
            _logVoter.CascadeEnabled = true;
            _logVoter.VocabProvider = () => _pokemonList.Select(x => x.NameKo).ToList();
            _logAnalyzer = new BattleLogAnalyzer();
            
            _logCorrector = new LogTextCorrector();
            _logCorrector.SetSpeciesVocabulary(_pokemonList.Select(x => x.NameKo));
        }

        public static Dictionary<int, string> PokemonNames { get; } = new Dictionary<int, string>();
        
        private void LoadPokemonData()
        {
            try
            {
                if (File.Exists(@"Data\master.json"))
                {
                    var json = File.ReadAllText(@"Data\master.json");
                    using var doc = JsonDocument.Parse(json);
                    var array = doc.RootElement.GetProperty("species").EnumerateArray();
                    foreach (var el in array)
                    {
                        int id = el.GetProperty("id").GetInt32();
                        var pokemon = new Pokemon
                        {
                            Id = id,
                            NameKo = el.GetProperty("nameKo").GetString() ?? ""
                        };
                        _pokemonList.Add(pokemon);
                        PokemonNames[id] = pokemon.NameKo;

                        if (el.TryGetProperty("types", out var typesProp) && typesProp.ValueKind == JsonValueKind.Array)
                        {
                            var typesList = new List<PokemonType>();
                            foreach (var t in typesProp.EnumerateArray())
                            {
                                var tStr = t.GetString();
                                if (Enum.TryParse<PokemonType>(tStr, true, out var pt))
                                {
                                    typesList.Add(pt);
                                    pokemon.Types.Add(tStr);
                                }
                            }
                            if (typesList.Count > 0)
                            {
                                _dexTypes[id] = new List<IReadOnlyList<PokemonType>> { typesList };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"포켓몬 마스터 데이터 로드 오류: {ex.Message}");
            }
        }

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();
            Task.Run(CaptureLoop, _cts.Token);
        }

        public void Stop()
        {
            IsRunning = false;
            _cts.Cancel();
        }

        public Bitmap CaptureWindowRegion(IntPtr hwnd, RectangleF normalized)
        {
            if (TestVideoPath != null && _currentVideoFrame != null)
            {
                var clone = new Bitmap(_currentVideoFrame);
                return CropNormalized(clone, normalized);
            }

            if (!GetWindowRect(hwnd, out RECT rect))
                return new Bitmap(1, 1);

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return new Bitmap(1, 1);

            int captureW = width;
            int captureH = height;

            try
            {
                uint dpiForWindow = GetDpiForWindow(hwnd);
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (dpiForWindow != 0 && monitor != IntPtr.Zero &&
                    GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint _) == 0 &&
                    dpiX != 0 && dpiForWindow != dpiX)
                {
                    captureW = Math.Max(1, (int)Math.Round((double)width * dpiForWindow / dpiX));
                    captureH = Math.Max(1, (int)Math.Round((double)height * dpiForWindow / dpiX));
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            var bitmap = new Bitmap(captureW, captureH, PixelFormat.Format32bppArgb);
            try
            {
                bool flag;
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = graphics.GetHdc();
                    try
                    {
                        flag = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }

                if (!flag)
                {
                    if (captureW != width || captureH != height)
                    {
                        bitmap.Dispose();
                        bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    }
                    using var graphics2 = Graphics.FromImage(bitmap);
                    IntPtr hdc2 = graphics2.GetHdc();
                    try
                    {
                        IntPtr dC = GetDC(IntPtr.Zero);
                        try
                        {
                            BitBlt(hdc2, 0, 0, width, height, dC, rect.Left, rect.Top, SRCCOPY);
                        }
                        finally
                        {
                            ReleaseDC(IntPtr.Zero, dC);
                        }
                    }
                    finally
                    {
                        graphics2.ReleaseHdc(hdc2);
                    }
                }

                return CropNormalized(bitmap, normalized);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static Bitmap CropNormalized(Bitmap full, RectangleF normalized)
        {
            float nx = Clamp01(normalized.X);
            float ny = Clamp01(normalized.Y);
            float nw = Clamp01(normalized.Width);
            float nh = Clamp01(normalized.Height);

            if (nx + nw > 1f) nw = 1f - nx;
            if (ny + nh > 1f) nh = 1f - ny;

            int x = (int)Math.Round(full.Width * nx);
            int y = (int)Math.Round(full.Height * ny);
            int w = (int)Math.Round(full.Width * nw);
            int h = (int)Math.Round(full.Height * nh);

            if (w <= 0 || h <= 0) return full;
            
            var cropped = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cropped))
            {
                g.DrawImage(full, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
            }
            full.Dispose();
            return cropped;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        public WindowBounds GetWindowBounds(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return new WindowBounds(0, 0, 0, 0);
            if (!GetWindowRect(hwnd, out RECT rect)) return new WindowBounds(0, 0, 0, 0);
            return new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        public static List<object> TestModeBattleLogs = new();
        public static object? TestModePartyData = null;
        private Bitmap? _currentVideoFrame = null;

        public static void SaveTestResult()
        {
            if (string.IsNullOrEmpty(TestVideoPath)) return;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(TestVideoPath);
                if (string.IsNullOrEmpty(dir)) dir = ".";
                var fileName = System.IO.Path.GetFileNameWithoutExtension(TestVideoPath) + ".json";
                var resultPath = System.IO.Path.Combine(dir, fileName);
                var result = new { party = TestModePartyData, logs = TestModeBattleLogs };
                System.IO.File.WriteAllText(resultPath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save test result: {ex.Message}");
            }
        }

        private async Task CaptureLoop()
        {
            int loopCount = 0;
            
            OpenCvSharp.VideoCapture? videoCapture = null;
            if (!string.IsNullOrEmpty(TestVideoPath))
            {
                videoCapture = new OpenCvSharp.VideoCapture(TestVideoPath);
                TargetHwnd = new IntPtr(-1);
                IsRunning = true;
                Console.WriteLine($"Starting Test Mode with video: {TestVideoPath}");
                if (!Directory.Exists(".agents/temp")) Directory.CreateDirectory(".agents/temp");
            }

            while (!_cts.Token.IsCancellationRequested)
            {
                if (videoCapture != null)
                {
                    using var mat = new OpenCvSharp.Mat();
                    if (!videoCapture.Read(mat) || mat.Empty())
                    {
                        SaveTestResult();
                        Console.WriteLine("Test mode finished, results saved.");
                        Environment.Exit(0);
                        return;
                    }
                    var old = _currentVideoFrame;
                    _currentVideoFrame = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);
                    old?.Dispose();
                    _virtualTimeMs += 33;
                }
                else
                {
                    await Task.Delay(33, _cts.Token);
                }

                if (!IsRunning || TargetHwnd == IntPtr.Zero) continue;
                
                loopCount++;
                try
                {
                    // 설정값을 매 루프마다 반영
                    _pickDetector.Region = Settings.PickEntry;
                    _pickDetector.StatsRegion = Settings.StatsDisplay;
                    _logOcr.LogRegion = Settings.Log;

                    if (loopCount == 150)
                    {
                        if (_currentVideoFrame != null)
                        {
                            _currentVideoFrame.Save(".agents/temp/frame150.png", System.Drawing.Imaging.ImageFormat.Png);
                            using var pickBmp = CaptureWindowRegion(TargetHwnd, Settings.PickEntry);
                            pickBmp.Save(".agents/temp/pick150.png", System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }

                    if (loopCount % 3 == 0)
                    {
                        if (IsPartyRecognitionEnabled)
                        {
                            var pickEntry = _pickDetector.Analyze(TargetHwnd);
                            
                            if (!string.IsNullOrWhiteSpace(pickEntry.Raw) && pickEntry.Raw != _lastDebugRaw)
                            {
                                _lastDebugRaw = pickEntry.Raw;
                                Console.WriteLine($"[OCR DEBUG] {pickEntry.Raw}");
                            }

                            if (pickEntry.Shown)
                            {
                                Console.WriteLine("Pick Entry Detected!");
                                
                                var slots = Settings.OpponentPartySlots.ToList();
                                var typeSlots = Settings.OpponentPartyTypeSlots.ToList();

                                var allDexIds = _pokemonList.Select(x => x.Id).ToList();
                                
                                var result = _partyRecognizer.Recognize(TargetHwnd, slots, allDexIds, typeSlots, _dexTypes);
                                
                                var partyData = result.Slots.Select((s, index) => {
                                    var sprite = _spritesProvider.AllSprites.FirstOrDefault(x => x.DexId == s.DexId && x.FormKey == s.FormKey);
                                    var pkmn = _pokemonList.FirstOrDefault(x => x.Id == s.DexId);
                                    var dict = new Dictionary<string, object>();
                                    dict["index"] = index + 1;
                                    dict["name"] = s.DisplayName;
                                    dict["dexId"] = s.DexId ?? 0;
                                    dict["formKey"] = s.FormKey;
                                    dict["score"] = s.Score;
                                    dict["iconFile"] = sprite?.IconFile ?? $"{s.DexId}-default.png";
                                    dict["types"] = pkmn?.Types ?? new List<string>();
                                    return dict;
                                }).ToList();

                                var json = System.Text.Json.JsonSerializer.Serialize(partyData);
                                if (json != _lastSentJson)
                                {
                                    _lastSentJson = json;
                                    LastPartyData = partyData;
                                    BattleStateCache.Initialize(LastPartyData);
                                    TestModePartyData = partyData;
                                    SaveTestResult();
                                    Console.WriteLine($"상대 파티 인식 완료: {json}");
                                    
                                    IsPartyRecognitionEnabled = false;
                                    Console.WriteLine("[Party] 파티 인식 완료, 재인식 차단됨 (배틀 상태 초기화 필요)");
                                    
                                    await _hubContext.Clients.All.SendAsync("UpdateOpponentParty", partyData);
                                }
                            }
                        }
                        else if (!IsPartyRecognitionEnabled)
                        {
                            long nowMs = GetCurrentTimeMs();
                            using var logBmp = _logOcr.CaptureLogRegion(TargetHwnd);
                            
                            ulong fp = LogOcr.ComputeFingerprint(logBmp);
                            bool changed = (fp != _lastLogFingerprint);
                            if (changed) _lastLogFingerprint = fp;

                            var results = _logVoter.Advance(changed ? logBmp : null, changed, nowMs, bmp =>
                            {
                                var raw = _logOcr.RecognizeLogRaw(bmp);
                                if (!string.IsNullOrWhiteSpace(raw) && raw != _lastDebugRaw)
                                {
                                    _lastDebugRaw = raw;
                                    Console.WriteLine($"[LogOCR_RAW] '{raw}'");
                                }
                                return raw;
                            });
                            
                            var pendingCascade = _logVoter.TakePendingCascade();
                            if (pendingCascade != null)
                            {
                                DispatchLogCascade(pendingCascade);
                            }

                            if (results != null)
                            {
                                foreach (var rawRes in results)
                                {
                                    var finals = _logEmitGate.Submit(rawRes, nowMs);
                                    EmitBatch(finals);
                                }
                            }
                        }
                    }

                    try
                    {
                        Bitmap PadAndEnhance(Bitmap src)
                        {
                            if (src.Width <= 0 || src.Height <= 0) return new Bitmap(src);
                            int padding = 10;
                            using var padded = new Bitmap(src.Width + padding * 2, src.Height + padding * 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            using (Graphics g = Graphics.FromImage(padded))
                            {
                                g.Clear(Color.Black);
                                g.DrawImage(src, padding, padding, src.Width, src.Height);
                            }
                            using var hsvFiltered = PokemonHelper.Services.Recognition.ImagePreprocessor.BinarizeByHsv(padded, new OpenCvSharp.Scalar(0, 0, 170), new OpenCvSharp.Scalar(180, 80, 255), 1);
                            var finalBmp = PokemonHelper.Services.Recognition.ImagePreprocessor.UpscaleAndEnhance(hsvFiltered, 3);
                            return finalBmp;
                        }
                        string myHpText = null;
                        if (Settings.MyHp.Width > 0 && Settings.MyHp.Height > 0)
                        {
                            using var myHpBmp = CaptureWindowRegion(TargetHwnd, Settings.MyHp);
                            using var enhanced = PadAndEnhance(myHpBmp);
                            myHpText = _windowsOcrEngine.Recognize(enhanced);
                        }

                        string oppHpText = null;
                        if (Settings.OpponentHp.Width > 0 && Settings.OpponentHp.Height > 0)
                        {
                            using var oppHpBmp = CaptureWindowRegion(TargetHwnd, Settings.OpponentHp);
                            using var enhanced = PadAndEnhance(oppHpBmp);
                            oppHpText = _windowsOcrEngine.Recognize(enhanced);
                            if (!string.IsNullOrWhiteSpace(oppHpText) && !oppHpText.Contains('%'))
                            {
                                oppHpText = null;
                            }
                        }
                        
                        if (!string.IsNullOrWhiteSpace(myHpText) || !string.IsNullOrWhiteSpace(oppHpText))
                        {
                            string currentHpNameLog = $"MyHP: '{myHpText}', OppHP: '{oppHpText}'";
                            if (currentHpNameLog != _lastHpNameLog)
                            {
                                _lastHpNameLog = currentHpNameLog;
                                Console.WriteLine($"[OCR HP] {currentHpNameLog}");
                            }

                            var hpDict = new Dictionary<string, string>();
                            
                            if (!string.IsNullOrWhiteSpace(myHpText))
                            {
                                string myHpStr = myHpText;
                                foreach (var kv in _hpOcrCorrections)
                                {
                                    myHpStr = System.Text.RegularExpressions.Regex.Replace(myHpStr, System.Text.RegularExpressions.Regex.Escape(kv.Key), kv.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                }
                                myHpStr = System.Text.RegularExpressions.Regex.Replace(myHpStr, @"\s", "");
                                myHpStr = myHpStr.TrimStart('-');
                                
                                bool isValid = true;
                                foreach (char c in myHpStr)
                                {
                                    if (!_hpOcrWhitelist.Contains(c)) { isValid = false; break; }
                                }
                                
                                if (isValid && double.TryParse(myHpStr, out double curRaw))
                                {
                                    if (!_myHpHistories.ContainsKey(_myActiveIndex)) _myHpHistories[_myActiveIndex] = new List<double>();
                                    var history = _myHpHistories[_myActiveIndex];
                                    
                                    history.Add(curRaw);
                                    if (history.Count > 5) history.RemoveAt(0);
                                    
                                    if (history.Count >= 3)
                                    {
                                        double mode = GetMode(history);
                                        _lastBroadcastMyHpMap.TryGetValue(_myActiveIndex, out double lastB);
                                        
                                        if (mode != lastB)
                                        {
                                            _lastBroadcastMyHpMap[_myActiveIndex] = mode;
                                            hpDict["myHpParsed"] = mode.ToString();

                                            var hpEv = new PokemonHelper.Models.BattleLogEvent {
                                                EventType = "HpChange",
                                                Source = "My",
                                                Name = "My",
                                                Description = $"내 HP 변경: {mode}",
                                                Payload = mode.ToString(),
                                                TargetIndex = _myActiveIndex
                                            };
                                            if (TestVideoPath != null) { TestModeBattleLogs.Add(hpEv); SaveTestResult(); }
                                            _ = _hubContext.Clients.All.SendAsync("BattleLogEvent", hpEv);
                                        }
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(oppHpText))
                            {
                                string oppStr = oppHpText;
                                foreach (var kv in _hpOcrCorrections)
                                {
                                    oppStr = System.Text.RegularExpressions.Regex.Replace(oppStr, System.Text.RegularExpressions.Regex.Escape(kv.Key), kv.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                }
                                oppStr = System.Text.RegularExpressions.Regex.Replace(oppStr, @"\s", "");
                                oppStr = oppStr.Replace("%", "").TrimStart('-');
                                
                                bool isValid = true;
                                foreach (char c in oppStr)
                                {
                                    if (!_hpOcrWhitelist.Contains(c)) { isValid = false; break; }
                                }
                                
                                if (isValid && double.TryParse(oppStr, out double pctRaw) && pctRaw <= 100)
                                {
                                    if (!_oppHpHistories.ContainsKey(_oppActiveIndex)) _oppHpHistories[_oppActiveIndex] = new List<double>();
                                    var history = _oppHpHistories[_oppActiveIndex];
                                    
                                    history.Add(pctRaw);
                                    if (history.Count > 5) history.RemoveAt(0);
                                    
                                    if (history.Count >= 3)
                                    {
                                        double mode = GetMode(history);
                                        _lastBroadcastOppHpMap.TryGetValue(_oppActiveIndex, out double lastB);
                                        
                                        if (mode != lastB)
                                        {
                                            _lastBroadcastOppHpMap[_oppActiveIndex] = mode;
                                            hpDict["opponentHpParsed"] = mode.ToString();

                                            var hpEv = new PokemonHelper.Models.BattleLogEvent {
                                                EventType = "HpChange",
                                                Source = "Opponent",
                                                Name = "Opponent",
                                                Description = $"상대 HP 변경: {mode}%",
                                                Payload = mode.ToString(),
                                                TargetIndex = _oppActiveIndex
                                            };
                                            if (TestVideoPath != null) { TestModeBattleLogs.Add(hpEv); SaveTestResult(); }
                                            _ = _hubContext.Clients.All.SendAsync("BattleLogEvent", hpEv);
                                        }
                                    }
                                }
                            }

                            if (hpDict.Count > 0)
                            {
                                _ = _hubContext.Clients.All.SendAsync("UpdateHpEvent", hpDict);
                            }
                        }
                    }
                    catch (Exception) { }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Capture loop error: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

private void DispatchLogCascade(LogCascadeRequest req)
        {
            if (Interlocked.CompareExchange(ref _logCascadeInFlight, 1, 0) != 0)
            {
                Console.WriteLine("[3층 재시도] skip(이전 진행 중) — 대상: " + req.SourceRaw);
                req.Dispose();
                return;
            }
            Console.WriteLine("[3층 재시도] 시작 — 원본: " + req.SourceRaw);
            long nowMs = GetCurrentTimeMs();
            int gen = _logEmitGate.BeginHold(nowMs);
            
            Action cascadeAction = () =>
            {
                try
                {
                    string text = _ocrEngine.Recognize(req.Frame) ?? string.Empty;
                    req.Dispose();
                    
                    string adopted = LogFusionVoter.ShouldAdoptSecondOpinion(text, req.BestScore, req.Vocab) ? text : null;
                    Console.WriteLine($"[3층 재시도] 완료 — {((adopted == null) ? "기각" : "채택")}");
                    var finals = _logEmitGate.Complete(gen, adopted);
                    EmitBatch(finals);
                }
                finally
                {
                    Interlocked.Exchange(ref _logCascadeInFlight, 0);
                }
            };

            if (TestVideoPath != null)
            {
                cascadeAction();
            }
            else
            {
                Task.Run(cascadeAction);
            }
        }

        private void EmitBatch(IReadOnlyList<string> raws)
        {
            if (raws == null || raws.Count == 0) return;
            lock (_logEmitLock)
            {
                foreach (var raw in raws)
                {
                    EmitLogRaw(raw);
                }
            }
        }

        private async void EmitLogRaw(string raw)
        {
            _lastEmittedLog = raw;
            if (!string.IsNullOrWhiteSpace(raw) && !LogOcr.IsTimerOnlyRaw(raw))
            {
                long now = GetCurrentTimeMs();

                string res = _logCorrector.Correct(raw);
                Console.WriteLine($"[BattleLog] {res}");
                
                var ev = _logAnalyzer.Analyze(res);
                if (ev != null)
                {
                    if (ev.EventType == "Switch")
                    {
                        if (ev.Source == "My" && ev.TargetIndex != -1)
                        {
                            _myActiveIndex = ev.TargetIndex;
                        }
                        else if (ev.Source == "Opponent" && ev.TargetIndex != -1)
                        {
                            _oppActiveIndex = ev.TargetIndex;
                        }
                    }

                    string payloadStr = "";
                    if (ev.Payload is PokemonHelper.Models.RankChangePayload rcp)
                    {
                        payloadStr = $"{rcp.Stat}_{rcp.Stages}";
                    }
                    else if (ev.Payload != null)
                    {
                        payloadStr = ev.Payload.ToString();
                    }

                    string sig = $"{ev.EventType}_{ev.Source}_{ev.Name}_{payloadStr}";
                    if (_lastEventMap.TryGetValue(sig, out var lastEvent) && now - lastEvent.Timestamp < 3000)
                    {
                        if (ev.EventType == "Switch" && ev.Description != null && ev.Description.Length > (lastEvent.Description?.Length ?? 0) + 1)
                        {
                            // Allow better Switch event to pass
                        }
                        else
                        {
                            Console.WriteLine($"[BattleLog Event Debounce] 중복 기각 — {sig}");
                            return;
                        }
                    }
                    _lastEventMap[sig] = (now, ev.Description ?? "");

                    Console.WriteLine($"[BattleLog Event] {ev.EventType} - {ev.Name}");

                    if (TestVideoPath != null)
                    {
                        TestModeBattleLogs.Add(ev);
                        SaveTestResult();
                    }

                    _ = _hubContext.Clients.All.SendAsync("BattleLogEvent", ev);
                }
            }
        }
    }
}
