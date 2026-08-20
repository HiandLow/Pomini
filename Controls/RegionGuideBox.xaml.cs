using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PokemonHelper.Controls
{
    public partial class RegionGuideBox : System.Windows.Controls.UserControl
    {
        private enum DragMode { None, Move, NW, NE, SW, SE }
        private DragMode _dragMode = DragMode.None;
        private System.Windows.Point _dragStart;
        private Rect _dragStartRect;

        public event Action? RegionChanged;

        public RegionGuideBox()
        {
            InitializeComponent();
            
            BodyBorder.MouseLeftButtonDown += (s, e) => StartDrag(DragMode.Move, e);
            HandleNW.MouseLeftButtonDown += (s, e) => StartDrag(DragMode.NW, e);
            HandleNE.MouseLeftButtonDown += (s, e) => StartDrag(DragMode.NE, e);
            HandleSW.MouseLeftButtonDown += (s, e) => StartDrag(DragMode.SW, e);
            HandleSE.MouseLeftButtonDown += (s, e) => StartDrag(DragMode.SE, e);
            
            MouseMove += OnMouseMoveCapture;
            MouseLeftButtonUp += OnMouseUpCapture;
        }

        public void SetLabel(string text)
        {
            LabelText.Text = text;
        }

        private void StartDrag(DragMode mode, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Parent is Canvas canvas)
            {
                _dragMode = mode;
                _dragStart = e.GetPosition(canvas);
                _dragStartRect = new Rect(Canvas.GetLeft(this), Canvas.GetTop(this), Math.Max(20, ActualWidth), Math.Max(20, ActualHeight));
                CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnMouseMoveCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragMode == DragMode.None || !(Parent is Canvas canvas)) return;

            var pos = e.GetPosition(canvas);
            double dx = pos.X - _dragStart.X;
            double dy = pos.Y - _dragStart.Y;

            double newL = _dragStartRect.Left;
            double newT = _dragStartRect.Top;
            double newW = _dragStartRect.Width;
            double newH = _dragStartRect.Height;

            switch (_dragMode)
            {
                case DragMode.Move:
                    newL += dx;
                    newT += dy;
                    break;
                case DragMode.NW:
                    newL += dx; newT += dy;
                    newW -= dx; newH -= dy;
                    break;
                case DragMode.NE:
                    newT += dy;
                    newW += dx; newH -= dy;
                    break;
                case DragMode.SW:
                    newL += dx;
                    newW -= dx; newH += dy;
                    break;
                case DragMode.SE:
                    newW += dx; newH += dy;
                    break;
            }

            if (newW < 20) newW = 20;
            if (newH < 20) newH = 20;

            Canvas.SetLeft(this, newL);
            Canvas.SetTop(this, newT);
            Width = newW;
            Height = newH;

            RegionChanged?.Invoke();
        }

        private void OnMouseUpCapture(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_dragMode != DragMode.None)
            {
                _dragMode = DragMode.None;
                ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        // Canvas 좌표를 부모(화면) 기준 0.0~1.0 비율로 변환
        public RectangleF ToRatio(double parentW, double parentH)
        {
            if (parentW <= 0 || parentH <= 0) return new RectangleF(0, 0, 0, 0);
            
            double left = double.IsNaN(Canvas.GetLeft(this)) ? 0 : Canvas.GetLeft(this);
            double top = double.IsNaN(Canvas.GetTop(this)) ? 0 : Canvas.GetTop(this);
            double w = double.IsNaN(Width) ? ActualWidth : Width;
            double h = double.IsNaN(Height) ? ActualHeight : Height;

            return new RectangleF(
                (float)Math.Round(left / parentW, 4),
                (float)Math.Round(top / parentH, 4),
                (float)Math.Round(w / parentW, 4),
                (float)Math.Round(h / parentH, 4)
            );
        }

        // 비율을 Canvas 좌표로 적용
        public void ApplyRatio(RectangleF ratio, double parentW, double parentH)
        {
            Canvas.SetLeft(this, ratio.X * parentW);
            Canvas.SetTop(this, ratio.Y * parentH);
            Width = ratio.Width * parentW;
            Height = ratio.Height * parentH;
        }
    }
}
