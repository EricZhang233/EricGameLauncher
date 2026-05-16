using System.ComponentModel;

namespace EricGameLauncher;

public class RunActionBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void NotifyPropertyChanged(string propertyName)
    {
        try { LogService.Write("Run", $"NotifyPropertyChanged {propertyName}"); } catch { }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string? _path;
    public string? Path
    {
        get => _path;
        set
        {
            if (_path != value)
            {
                LogService.Write("Run", $"Path changed from={_path ?? "null"} to={value ?? "null"}");
                _path = value;
                NotifyPropertyChanged(nameof(Path));
            }
        }
    }

    private bool _isAdmin;
    public bool IsAdmin
    {
        get => _isAdmin;
        set
        {
            if (_isAdmin != value)
            {
                LogService.Write("Run", $"IsAdmin changed from={_isAdmin} to={value}");
                _isAdmin = value;
                NotifyPropertyChanged(nameof(IsAdmin));
            }
        }
    }
}

public class AlternativeRunAction : RunActionBase
{
    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value) { LogService.Write("Run", $"Enabled changed from={_enabled} to={value}"); _enabled = value; NotifyPropertyChanged(nameof(Enabled)); }
        }
    }
}

public class AlongsideRunAction : RunActionBase
{
    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value) { LogService.Write("Run", $"Enabled changed from={_enabled} to={value}"); _enabled = value; NotifyPropertyChanged(nameof(Enabled)); }
        }
    }
}
