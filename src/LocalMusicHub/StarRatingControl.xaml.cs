using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace LocalMusicHub;

public partial class StarRatingControl
{
    public static readonly DependencyProperty RatingProperty =
        DependencyProperty.Register(
            nameof(Rating),
            typeof(int),
            typeof(StarRatingControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRatingChanged));

    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.Register(
            nameof(IsInteractive),
            typeof(bool),
            typeof(StarRatingControl),
            new PropertyMetadata(true));

    private readonly TextBlock[] _stars = new TextBlock[5];
    private int _hoverRating;
    private int _lastPaintedHover = -1;
    private int _lastPaintedRating = -1;
    private Brush? _mutedBrush;
    private Brush? _accentBrush;
    private Brush? _previewBrush;

    public StarRatingControl()
    {
        InitializeComponent();
        BuildStars();
        Loaded += (_, _) =>
        {
            CacheBrushes();
            ForcePaint();
        };
    }

    public int Rating
    {
        get => (int)GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <summary>True while the pointer is over the control previewing a rating.</summary>
    public bool IsHovering => _hoverRating > 0;

    public event EventHandler<int>? RatingChanged;

    /// <summary>Update rating from outside without fighting an active hover preview.</summary>
    public void SyncRating(int rating)
    {
        var clamped = Math.Clamp(rating, 0, 5);
        if (Rating == clamped)
            return;
        if (IsHovering)
            return;
        Rating = clamped;
    }

    private static void OnRatingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StarRatingControl control)
            control.ApplyVisual(force: false);
    }

    private void BuildStars()
    {
        StarsHost.Children.Clear();
        for (var i = 1; i <= 5; i++)
        {
            var star = new TextBlock
            {
                Text = "★",
                FontSize = 16,
                Width = 18,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Tag = i,
            };
            _stars[i - 1] = star;
            StarsHost.Children.Add(star);
        }
    }

    private void CacheBrushes()
    {
        _mutedBrush = TryFindResource("HubTextMutedBrush") as Brush ?? Brushes.Gray;
        _accentBrush = TryFindResource("HubAccentBrush") as Brush ?? Brushes.Goldenrod;
        _previewBrush = TryFindResource("HubTextPrimaryBrush") as Brush ?? Brushes.White;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsInteractive)
            return;

        var pos = e.GetPosition(StarsHost);
        var next = pos.X < 0 ? 0 : Math.Clamp((int)(pos.X / 18) + 1, 1, 5);
        if (next == _hoverRating)
            return;
        _hoverRating = next;
        ApplyVisual(force: false);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsInteractive)
            return;

        var rating = _hoverRating > 0 ? _hoverRating : RatingFromPoint(e.GetPosition(StarsHost));
        if (rating <= 0)
            return;

        var next = Rating == rating ? 0 : rating;
        Rating = next;
        ApplyVisual(force: true);
        RatingChanged?.Invoke(this, next);
        e.Handled = true;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_hoverRating == 0)
            return;
        _hoverRating = 0;
        ApplyVisual(force: false);
    }

    private static int RatingFromPoint(Point pos) =>
        pos.X < 0 ? 0 : Math.Clamp((int)(pos.X / 18) + 1, 1, 5);

    private void ForcePaint()
    {
        _lastPaintedHover = -1;
        _lastPaintedRating = -1;
        ApplyVisual(force: true);
    }

    private void ApplyVisual(bool force)
    {
        var committed = Math.Clamp(Rating, 0, 5);
        if (!force && _hoverRating == _lastPaintedHover && committed == _lastPaintedRating)
            return;

        _lastPaintedHover = _hoverRating;
        _lastPaintedRating = committed;
        CacheBrushes();

        var muted = _mutedBrush ?? Brushes.Gray;
        var accent = _accentBrush ?? Brushes.Goldenrod;
        var preview = _previewBrush ?? Brushes.White;
        var hover = _hoverRating;

        for (var i = 0; i < _stars.Length; i++)
        {
            var starIndex = i + 1;
            Brush fill;
            double opacity;

            if (hover <= 0)
            {
                // Idle: committed stars in accent.
                var on = starIndex <= committed;
                fill = on ? accent : muted;
                opacity = on ? 1.0 : 0.4;
            }
            else if (hover >= committed)
            {
                // Raising: keep existing accent, highlight new stars up to hover.
                if (starIndex <= committed)
                {
                    fill = accent;
                    opacity = 1.0;
                }
                else if (starIndex <= hover)
                {
                    fill = preview;
                    opacity = 1.0;
                }
                else
                {
                    fill = muted;
                    opacity = 0.4;
                }
            }
            else
            {
                // Lowering: preview keeps accent through hover; dim stars being cleared.
                if (starIndex <= hover)
                {
                    fill = accent;
                    opacity = 1.0;
                }
                else if (starIndex <= committed)
                {
                    fill = accent;
                    opacity = 0.35;
                }
                else
                {
                    fill = muted;
                    opacity = 0.4;
                }
            }

            if (!ReferenceEquals(_stars[i].Foreground, fill))
                _stars[i].Foreground = fill;
            if (Math.Abs(_stars[i].Opacity - opacity) > 0.01)
                _stars[i].Opacity = opacity;
        }
    }
}
