using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace QuickNoteApp.Windows;

public partial class FloatingRestoreWindow : Window
{
    private readonly Action _restoreAction;
    private readonly string _positionFilePath;
    private System.Windows.Point _mouseDownScreenPosition;
    private System.Windows.Point _windowStartPosition;
    private bool _isDragging;
    private bool _suppressNextRestore;

    public FloatingRestoreWindow(string iconText, Action restoreAction)
    {
        InitializeComponent();
        IconText.Text = iconText;
        _restoreAction = restoreAction;
        _positionFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickNoteApp",
            "floating-icon-position.txt");
        Loaded += (_, _) => MoveToSavedPositionOrBottomRight();
    }

    public void ShowIcon()
    {
        MoveToSavedPositionOrBottomRight();
        Show();
        Activate();
    }

    private void RestoreButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        _suppressNextRestore = false;
        _mouseDownScreenPosition = PointToScreen(e.GetPosition(this));
        _windowStartPosition = new System.Windows.Point(Left, Top);
        RestoreButton.CaptureMouse();
        e.Handled = true;
    }

    private void RestoreButton_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!RestoreButton.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentScreenPosition = PointToScreen(e.GetPosition(this));
        var offsetX = currentScreenPosition.X - _mouseDownScreenPosition.X;
        var offsetY = currentScreenPosition.Y - _mouseDownScreenPosition.Y;

        if (!_isDragging &&
            Math.Abs(offsetX) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(offsetY) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        _suppressNextRestore = true;
        SetClampedPosition(_windowStartPosition.X + offsetX, _windowStartPosition.Y + offsetY);
        e.Handled = true;
    }

    private void RestoreButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (RestoreButton.IsMouseCaptured)
        {
            RestoreButton.ReleaseMouseCapture();
        }

        if (_isDragging || _suppressNextRestore)
        {
            SaveCurrentPosition();
            _isDragging = false;
            _suppressNextRestore = false;
            e.Handled = true;
            return;
        }

        Hide();
        _restoreAction();
    }

    private void RestoreButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        Hide();
    }

    private void MoveToSavedPositionOrBottomRight()
    {
        if (TryLoadSavedPosition(out var left, out var top))
        {
            SetClampedPosition(left, top);
            return;
        }

        MoveToBottomRight();
    }

    private void MoveToBottomRight()
    {
        var area = SystemParameters.WorkArea;
        SetClampedPosition(area.Right - Width - 18, area.Bottom - Height - 18);
    }

    private void SetClampedPosition(double left, double top)
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Min(Math.Max(left, area.Left), area.Right - Width);
        Top = Math.Min(Math.Max(top, area.Top), area.Bottom - Height);
    }

    private bool TryLoadSavedPosition(out double left, out double top)
    {
        left = 0;
        top = 0;

        if (!File.Exists(_positionFilePath))
        {
            return false;
        }

        var parts = File.ReadAllText(_positionFilePath).Split(';');
        return parts.Length == 2 &&
               double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left) &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top);
    }

    private void SaveCurrentPosition()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_positionFilePath)!);
        var content = string.Create(CultureInfo.InvariantCulture, $"{Left};{Top}");
        File.WriteAllText(_positionFilePath, content);
    }
}
