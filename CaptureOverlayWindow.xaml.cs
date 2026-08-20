using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using PokemonHelper.Controls;
using PokemonHelper.Models;
using PokemonHelper.Services;
using System.Drawing;
using System.Collections.Generic;

namespace PokemonHelper
{
    public partial class CaptureOverlayWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        private IntPtr _targetHwnd;
        private RegionSettings _settings;
        private JsonRegionSettingsRepository _repo;


        private RegionGuideBox _boxLog;
        private RegionGuideBox _boxMyHp;
        private RegionGuideBox _boxOpponentHp;
        private RegionGuideBox _boxPickEntry;
        private RegionGuideBox _boxStatsDisplay;
        private RegionGuideBox[] _boxOpponentPartySlots = new RegionGuideBox[6];
        private RegionGuideBox[] _boxOpponentPartyTypeSlots = new RegionGuideBox[6];

        public CaptureOverlayWindow(IntPtr targetHwnd)
        {
            InitializeComponent();
            _targetHwnd = targetHwnd;
            
            _repo = new JsonRegionSettingsRepository();
            _settings = _repo.Load();


            // _boxOpponentHp and _boxMyHp are NOT linked because their text formats differ.
            _boxPickEntry = new RegionGuideBox(); _boxPickEntry.SetLabel("선택 화면 확인");
            _boxStatsDisplay = new RegionGuideBox(); _boxStatsDisplay.SetLabel("능력치 표시 확인");

            // RegionGuideBox 생성
            _boxLog = new RegionGuideBox(); _boxLog.SetLabel("게임 로그");
            _boxMyHp = new RegionGuideBox(); _boxMyHp.SetLabel("내 현재 HP");
            _boxOpponentHp = new RegionGuideBox(); _boxOpponentHp.SetLabel("상대 HP");


            OverlayCanvas.Children.Add(_boxLog);
            OverlayCanvas.Children.Add(_boxMyHp);
            OverlayCanvas.Children.Add(_boxOpponentHp);
            OverlayCanvas.Children.Add(_boxPickEntry);
            OverlayCanvas.Children.Add(_boxStatsDisplay);

            for (int i = 0; i < 6; i++)
            {
                int index = i;
                _boxOpponentPartySlots[i] = new RegionGuideBox();
                _boxOpponentPartySlots[i].SetLabel($"파티 슬롯 {i + 1}");
                _boxOpponentPartySlots[i].RegionChanged += () => SyncSizes(_boxOpponentPartySlots, index);
                OverlayCanvas.Children.Add(_boxOpponentPartySlots[i]);

                _boxOpponentPartyTypeSlots[i] = new RegionGuideBox();
                _boxOpponentPartyTypeSlots[i].SetLabel($"타입 {i + 1}");
                _boxOpponentPartyTypeSlots[i].RegionChanged += () => SyncSizes(_boxOpponentPartyTypeSlots, index);
                OverlayCanvas.Children.Add(_boxOpponentPartyTypeSlots[i]);
            }

            this.Loaded += (s, e) => PositionWindowOverTarget();
        }

        private bool _isSyncing = false;
        private void SyncSizes(RegionGuideBox[] boxes, int sourceIndex)
        {
            if (_isSyncing) return;
            _isSyncing = true;
            try
            {
                var source = boxes[sourceIndex];
                double w = source.Width;
                double h = source.Height;
                if (double.IsNaN(w) || double.IsNaN(h)) return;

                for (int i = 0; i < boxes.Length; i++)
                {
                    if (i != sourceIndex && boxes[i] != null)
                    {
                        boxes[i].Width = w;
                        boxes[i].Height = h;
                    }
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

        private void PositionWindowOverTarget()
        {
            if (GetWindowRect(_targetHwnd, out RECT rect))
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.EnsureHandle();
                if (helper.Handle != IntPtr.Zero)
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    SetWindowPos(helper.Handle, IntPtr.Zero, rect.Left, rect.Top, width, height, 0x0014); // SWP_NOZORDER | SWP_NOACTIVATE
                }
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.ActualWidth > 0 && this.ActualHeight > 0)
            {

                _boxLog.ApplyRatio(_settings.Log, this.ActualWidth, this.ActualHeight);
                _boxMyHp.ApplyRatio(_settings.MyHp, this.ActualWidth, this.ActualHeight);
                _boxOpponentHp.ApplyRatio(_settings.OpponentHp, this.ActualWidth, this.ActualHeight);
                _boxPickEntry.ApplyRatio(_settings.PickEntry, this.ActualWidth, this.ActualHeight);
                _boxStatsDisplay.ApplyRatio(_settings.StatsDisplay, this.ActualWidth, this.ActualHeight);

                for (int i = 0; i < 6; i++)
                {
                    if (_settings.OpponentPartySlots != null && _settings.OpponentPartySlots.Count > i)
                        _boxOpponentPartySlots[i].ApplyRatio(_settings.OpponentPartySlots[i], this.ActualWidth, this.ActualHeight);
                        
                    if (_settings.OpponentPartyTypeSlots != null && _settings.OpponentPartyTypeSlots.Count > i)
                        _boxOpponentPartyTypeSlots[i].ApplyRatio(_settings.OpponentPartyTypeSlots[i], this.ActualWidth, this.ActualHeight);
                }
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {

                _settings.Log = _boxLog.ToRatio(this.ActualWidth, this.ActualHeight);
                _settings.MyHp = _boxMyHp.ToRatio(this.ActualWidth, this.ActualHeight);
                _settings.OpponentHp = _boxOpponentHp.ToRatio(this.ActualWidth, this.ActualHeight);
                _settings.PickEntry = _boxPickEntry.ToRatio(this.ActualWidth, this.ActualHeight);
                _settings.StatsDisplay = _boxStatsDisplay.ToRatio(this.ActualWidth, this.ActualHeight);

                var newPartySlots = new List<RectangleF>();
                var newTypeSlots = new List<RectangleF>();
                for (int i = 0; i < 6; i++)
                {
                    newPartySlots.Add(_boxOpponentPartySlots[i].ToRatio(this.ActualWidth, this.ActualHeight));
                    newTypeSlots.Add(_boxOpponentPartyTypeSlots[i].ToRatio(this.ActualWidth, this.ActualHeight));
                }
                
                _settings.OpponentPartySlots = newPartySlots;
                _settings.OpponentPartyTypeSlots = newTypeSlots;
                
                _repo.Save(_settings);
                ScreenCaptureService.Settings = _settings;
                
                this.DialogResult = true;
                this.Close();
            }
        }
    }
}
