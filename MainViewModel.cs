using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using ReactiveUI;
using StudioLog.Core;
using StudioLog.Models;

namespace StudioLog.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly NTPTimecodeClient _ntpClient;
        private readonly TimecodeDatabase _database;
        private readonly AppSettings _settings;
        private readonly LTCAudioManager _audioManager;
        private readonly SessionManager _sessionManager;
        private readonly ExportManager _exportManager;
        private System.Threading.Timer? _displayTimer;
        private Session? _currentSession;
        private TimecodeLogEntry? _currentEntry;
        private bool _disposed;
        private bool _hasUnsavedChanges = false;
        private readonly Stack<List<TimecodeLogEntry>> _undoStack = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        private string _currentTimecode = "00:00:00:00";
        public string CurrentTimecode
        {
            get => _currentTimecode;
            set { _currentTimecode = value; OnPropertyChanged(); }
        }

        private string _sessionName = string.Empty;
        public string SessionName
        {
            get => _sessionName;
            set { _sessionName = value; OnPropertyChanged(); }
        }

        private string _date = DateTime.Now.ToString("yyyy-MM-dd");
        public string Date
        {
            get => _date;
            set { _date = value; OnPropertyChanged(); }
        }

        private string _location = string.Empty;
        public string Location
        {
            get => _location;
            set { _location = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _isGeneratorRunning = false;
        public bool IsGeneratorRunning
        {
            get => _isGeneratorRunning;
            set 
            { 
                _isGeneratorRunning = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratorButtonText));
                OnPropertyChanged(nameof(GeneratorButtonColor));
                OnPropertyChanged(nameof(GeneratorButtonHoverColor));
                OnPropertyChanged(nameof(GeneratorButtonPressedColor));
            }
        }

        private bool _isTimecodeInActive = false;
        public bool IsTimecodeInActive
        {
            get => _isTimecodeInActive;
            set 
            { 
                _isTimecodeInActive = value; 
                OnPropertyChanged();
            }
        }

        public string GeneratorButtonText 
        {
            get
            {
                if (_isGeneratorRunning) return "STOP";
                return _audioManager?.IsPassthroughMode == true ? "READ" : "GENERATE";
            }
        }
        public string GeneratorButtonColor => _isGeneratorRunning ? "#dc2626" : "#22c55e";
        public string GeneratorButtonHoverColor => _isGeneratorRunning ? "#b91c1c" : "#1a9e4a";
        public string GeneratorButtonPressedColor => _isGeneratorRunning ? "#991b1b" : "#158a3f";

        // Diagnostic Properties
        private float _outputAmplitude = 1.0f;
        public float OutputAmplitude
        {
            get => _outputAmplitude;
            set
            {
                _outputAmplitude = Math.Clamp(value, 0.5f, 1.0f);
                _audioManager?.SetAmplitude(_outputAmplitude);
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutputAmplitudePercent));
            }
        }
        
        public int OutputAmplitudePercent => (int)(_outputAmplitude * 100);
        
        private bool _isTestToneActive = false;
        public bool IsTestToneActive
        {
            get => _isTestToneActive;
            set
            {
                _isTestToneActive = value;
                OnPropertyChanged();
            }
        }

        // Frame Rate Selection Indicators
        public bool IsFrameRate24 => _settings.SelectedFrameRate == "24 fps";
        public bool IsFrameRate25 => _settings.SelectedFrameRate == "25 fps";
        public bool IsFrameRate2997DF => _settings.SelectedFrameRate == "29.97 fps DF";
        public bool IsFrameRate2997NDF => _settings.SelectedFrameRate == "29.97 fps NDF";
        public bool IsFrameRate30 => _settings.SelectedFrameRate == "30 fps";

        // Audio Output Selection Indicators
        public bool IsAudioOutputNone => _settings.SelectedAudioOutput == "None";
        public bool IsAudioOutputSystemDefault => _settings.SelectedAudioOutput == "System Default";
        public bool IsAudioOutputASIO => _settings.SelectedAudioOutput == "ASIO";
        public bool IsAsioDriver(string driverName) => 
            _settings.SelectedAudioOutput == "ASIO" && _settings.SelectedAsioDriver == driverName;
        
        // Audio Input Selection Indicators
        public bool IsAudioInputNone => _settings.SelectedAudioInput == "None";
        public bool IsAudioInputSystemDefault => _settings.SelectedAudioInput == "System Default";
        public bool IsAudioInputASIO => _settings.SelectedAudioInput == "ASIO";
        public bool IsAudioInputNDI => _settings.SelectedAudioInput == "NDI Receive";
        public bool IsAsioInputDriver(string driverName) => 
            _settings.SelectedAudioInput == "ASIO" && _settings.SelectedAsioInputDriver == driverName;
        
        // NDI Properties
        public bool IsNDIAvailable
        {
            get
            {
                bool available = _audioManager.IsNDIAvailable;
                Console.WriteLine($"[MainViewModel] IsNDIAvailable queried: {available}");
                return available;
            }
        }
        public bool IsNDIOutputEnabled => _audioManager.IsNDIOutputEnabled;

        // Clock Source Selection Indicators
        public bool IsClockSystemClock => _settings.SelectedClockSource == "System Clock";
        public bool IsClockNtp => _settings.SelectedClockSource == "NTP";
        public bool IsClockFreeRun => _settings.SelectedClockSource == "Free Run";

        public ObservableCollection<TimecodeLogEntry> LogEntries { get; }

        public bool CanUndo => _undoStack.Count > 0;

        public ObservableCollection<string> AvailableAsioDrivers { get; } = new ObservableCollection<string>();
        
        private bool _hasAsioDrivers;
        public bool HasAsioDrivers
        {
            get => _hasAsioDrivers;
            set { _hasAsioDrivers = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> AvailableNDISources { get; } = new ObservableCollection<string>();
        
        private bool _hasNDISources;
        public bool HasNDISources
        {
            get => _hasNDISources;
            set { _hasNDISources = value; OnPropertyChanged(); }
        }
        
        public bool IsNDISource(string sourceName) => 
            _settings.SelectedAudioInput == "NDI Receive" && _settings.SelectedNDISource == sourceName;

        public ICommand ToggleGeneratorCommand { get; }
        public ICommand NewSessionCommand { get; }
        public ICommand SaveSessionCommand { get; }
        public ICommand OpenSessionCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportPngCommand { get; }
        public ICommand TimeCodeInCommand { get; }
        public ICommand TimeCodeOutCommand { get; }
        public ICommand TimeCodeMarkCommand { get; }
        public ICommand SetFrameRateCommand { get; }
        public ICommand SetAudioOutputCommand { get; }
        public ICommand SetAudioInputCommand { get; }
        public ICommand SetAsioDriverCommand { get; }
        public ICommand SetAsioInputDriverCommand { get; }
        public ICommand SetNDISourceCommand { get; }
        public ICommand ToggleNDIOutputCommand { get; }
        public ICommand SetClockSourceCommand { get; }
        public ICommand SetNtpTimezoneCommand { get; }
        public ICommand ToggleTestToneCommand { get; }
        public ICommand OpenManualCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand OpenContactCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand DeleteEntryCommand { get; }
        public ICommand UndoDeleteCommand { get; }

        public MainViewModel()
        {
            _settings = AppSettings.Load();
            _ntpClient = new NTPTimecodeClient();
            
            Console.WriteLine($"[MainViewModel] Loaded clock source from settings: '{_settings.SelectedClockSource}', Timezone: {_settings.SelectedTimezoneId}");
            
            // Apply saved clock source and timezone ID
            _ntpClient.SetClockSource(_settings.SelectedClockSource, _settings.SelectedTimezoneId);
            
            // Set display frame rate for timecode counting
            var displayFrameRate = GetDisplayFrameRate(_settings.SelectedFrameRate);
            _ntpClient.SetFrameRate(displayFrameRate);
            
            _database = new TimecodeDatabase(_settings.DatabasePath);
            _sessionManager = new SessionManager(_database);
            _exportManager = new ExportManager();
            
            // Initialize LTC audio (but don't start yet - user must click Generate)
            _audioManager = new LTCAudioManager();
            var frameRate = GetFrameRateValue(_settings.SelectedFrameRate);
            _audioManager.Initialize(frameRate, _settings.SelectedAudioOutput, _settings.SelectedAudioInput, _settings.SelectedNDISource);
            // Don't start - wait for user to click Generate button
            
            LogEntries = new ObservableCollection<TimecodeLogEntry>();

            ToggleGeneratorCommand = ReactiveCommand.Create(ToggleGenerator);
            NewSessionCommand = ReactiveCommand.Create(NewSession);
            SaveSessionCommand = ReactiveCommand.Create(SaveSession);
            OpenSessionCommand = ReactiveCommand.Create(OpenSession);
            ExportPdfCommand = ReactiveCommand.Create(ExportPdf);
            ExportCsvCommand = ReactiveCommand.Create(ExportCsv);
            ExportPngCommand = ReactiveCommand.Create(ExportPng);
            TimeCodeInCommand = ReactiveCommand.Create(TimeCodeIn);
            TimeCodeOutCommand = ReactiveCommand.Create(TimeCodeOut);
            TimeCodeMarkCommand = ReactiveCommand.Create(TimeCodeMark);
            SetFrameRateCommand = ReactiveCommand.Create<string>(SetFrameRate);
            SetAudioOutputCommand = ReactiveCommand.Create<string>(SetAudioOutput);
            SetAudioInputCommand = ReactiveCommand.Create<string>(SetAudioInput);
            SetAsioDriverCommand = ReactiveCommand.Create<string>(SetAsioDriver);
            SetAsioInputDriverCommand = ReactiveCommand.Create<string>(SetAsioInputDriver);
            SetNDISourceCommand = ReactiveCommand.Create<string>(SetNDISource);
            ToggleNDIOutputCommand = ReactiveCommand.Create(ToggleNDIOutput);
            SetClockSourceCommand = ReactiveCommand.Create<string>(SetClockSource);
            SetNtpTimezoneCommand = ReactiveCommand.Create<string>(SetNtpTimezone);
            ToggleTestToneCommand = ReactiveCommand.Create(ToggleTestTone);
            OpenManualCommand = ReactiveCommand.Create(OpenManual);
            OpenAboutCommand = ReactiveCommand.Create(OpenAbout);
            OpenContactCommand = ReactiveCommand.Create(OpenContact);
            ExitCommand = ReactiveCommand.Create(Exit);
            DeleteEntryCommand = ReactiveCommand.Create<TimecodeLogEntry>(DeleteEntry);
            UndoDeleteCommand = ReactiveCommand.Create(UndoDelete);

            // Create timer but don't start it - will start when user clicks Generate
            _displayTimer = new System.Threading.Timer(UpdateDisplay, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

            // Enumerate available ASIO drivers
            EnumerateAsioDrivers();
            
            // Enumerate available NDI sources and subscribe to updates
            EnumerateNDISources();
            if (_audioManager.NDI != null)
            {
                _audioManager.NDI.SourcesUpdated += OnNDISourcesUpdated;
            }

            // Initialize asynchronously with error observation
            InitializeAsync().ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    Console.WriteLine($"[MainViewModel] InitializeAsync failed: {t.Exception.InnerException?.Message}");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        StatusMessage = $"Initialization error: {t.Exception.InnerException?.Message}";
                    });
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task InitializeAsync()
        {
            try
            {
                // Create a blank session on startup
                int sessionId = await _database.CreateSession("Untitled", DateTime.Now.ToString("yyyy-MM-dd"), string.Empty);
                _currentSession = await _database.GetActiveSession();
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Initialize with blank fields
                    SessionName = string.Empty;
                    Date = DateTime.Now.ToString("yyyy-MM-dd");
                    Location = string.Empty;

                    UnsubscribeAllEntries();
                    LogEntries.Clear();
                    _hasUnsavedChanges = false;
                    
                    // Notify UI of NDI availability
                    OnPropertyChanged(nameof(IsNDIAvailable));
                    
                    // Auto-enable NDI output if available
                    if (_audioManager.IsNDIAvailable)
                    {
                        _audioManager.EnableNDIOutput(true);
                        OnPropertyChanged(nameof(IsNDIOutputEnabled));
                        Console.WriteLine("[MainViewModel] NDI output auto-enabled");
                    }
                    
                    StatusMessage = "Ready - Click GENERATE to start generator";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Error: {ex.Message}";
                });
            }
        }

        private void UpdateDisplay(object? state)
        {
            try
            {
                // Only update if generator is running
                if (!_isGeneratorRunning)
                {
                    return;
                }

                var timecode = _ntpClient.GetTimecodeString();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CurrentTimecode = timecode;
                });
                
                // Parse timecode and update LTC audio
                var parts = timecode.Split(':');
                if (parts.Length == 4)
                {
                    int hours = int.Parse(parts[0]);
                    int minutes = int.Parse(parts[1]);
                    int seconds = int.Parse(parts[2]);
                    int frames = int.Parse(parts[3]);
                    
                    _audioManager.SetTime(hours, minutes, seconds, frames);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Display] Update error: {ex.Message}");
            }
        }

        public async void NewSession()
        {
            try
            {
                // Check if there are unsaved changes
                if (_hasUnsavedChanges && _currentSession != null)
                {
                    bool shouldSave = await PromptToSaveSession();
                    if (shouldSave)
                    {
                        await SaveCurrentSession();
                    }
                }

                // Close current session in database
                if (_currentSession != null)
                {
                    await _database.CloseSession(_currentSession.Id);
                }

                // Create new blank session in database with default values
                int sessionId = await _database.CreateSession(
                    string.IsNullOrWhiteSpace(SessionName) ? "Untitled" : SessionName, 
                    Date, 
                    Location
                );
                _currentSession = await _database.GetActiveSession();

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Clear fields for new session
                    SessionName = string.Empty;
                    Date = DateTime.Now.ToString("yyyy-MM-dd");
                    Location = string.Empty;
                    UnsubscribeAllEntries();
                    LogEntries.Clear();
                    _undoStack.Clear();
                    OnPropertyChanged(nameof(CanUndo));

                    _hasUnsavedChanges = false;
                    StatusMessage = "New blank session created - Enter Artist Name and start logging";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Error: {ex.Message}";
                });
            }
        }

        public async void SaveSession()
        {
            try
            {
                if (_currentSession == null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = "No active session to save";
                    });
                    return;
                }

                _currentSession.SessionName = SessionName;
                _currentSession.Date = Date;
                _currentSession.Location = Location;
                await _database.UpdateSessionInfo(_currentSession);

                string safeSessionName = string.Join("_", SessionName.Split(Path.GetInvalidFileNameChars()));
                string safeDate = Date.Replace("/", "-").Replace("\\", "-");
                string defaultFilename = $"{safeSessionName}_{safeDate}.tcsession";

                string? filepath = await ShowSaveFileDialog(defaultFilename, "Timecode Sessions", "tcsession", "Save Session");
                if (string.IsNullOrEmpty(filepath)) return;

                var entries = LogEntries.ToList();
                await _sessionManager.SaveSessionToPath(_currentSession, entries, filepath);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _hasUnsavedChanges = false;
                    StatusMessage = $"Session saved: {Path.GetFileName(filepath)}";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Error saving session: {ex.Message}";
                });
            }
        }

        public async void ExportPdf()
        {
            try
            {
                string defaultFilename = $"{SessionName}_{Date}_Log.pdf".Replace("/", "-").Replace("\\", "-");
                
                string? filepath = await ShowSaveFileDialog(defaultFilename, "PDF Files", "pdf");
                if (string.IsNullOrEmpty(filepath)) return;
                
                await _exportManager.ExportToPdf(filepath, SessionName, Date, Location, LogEntries.ToList());
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"PDF exported: {Path.GetFileName(filepath)}";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                });
            }
        }

        public async void ExportCsv()
        {
            try
            {
                string defaultFilename = $"{SessionName}_{Date}_Log.csv".Replace("/", "-").Replace("\\", "-");
                
                string? filepath = await ShowSaveFileDialog(defaultFilename, "CSV Files", "csv");
                if (string.IsNullOrEmpty(filepath)) return;
                
                await _exportManager.ExportToCsv(filepath, SessionName, Date, Location, LogEntries.ToList());
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"CSV exported: {Path.GetFileName(filepath)}";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                });
            }
        }

        public async void ExportPng()
        {
            try
            {
                string defaultFilename = $"{SessionName}_{Date}_Log.png".Replace("/", "-").Replace("\\", "-");
                
                string? filepath = await ShowSaveFileDialog(defaultFilename, "PNG Images", "png");
                if (string.IsNullOrEmpty(filepath)) return;
                
                await _exportManager.ExportToPng(filepath, SessionName, Date, Location, LogEntries.ToList());
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"PNG exported: {Path.GetFileName(filepath)}";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                });
            }
        }

        public async void OpenSession()
        {
            try
            {
                // Check for unsaved changes
                if (_hasUnsavedChanges && _currentSession != null)
                {
                    bool shouldSave = await PromptToSaveSession();
                    if (shouldSave)
                    {
                        await SaveCurrentSession();
                    }
                }

                // Get list of saved sessions
                var sessionFiles = _sessionManager.GetSavedSessionFiles();
                if (sessionFiles.Count == 0)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = "No saved sessions found";
                    });
                    return;
                }

                // Show file picker (simple approach - use first file for now)
                // TODO: Implement proper file picker dialog
                string selectedFile = await ShowOpenSessionDialog(sessionFiles);
                if (string.IsNullOrEmpty(selectedFile))
                {
                    return;
                }

                // Load session from file
                var sessionData = await _sessionManager.LoadSessionFromFile(selectedFile);
                if (sessionData == null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = "Failed to load session";
                    });
                    return;
                }

                // Close current session
                if (_currentSession != null)
                {
                    await _database.CloseSession(_currentSession.Id);
                }

                // Create new session in database with loaded data
                int sessionId = await _database.CreateSession(
                    sessionData.SessionName,
                    sessionData.Date,
                    sessionData.Location
                );
                _currentSession = await _database.GetActiveSession();

                // Add all entries to database
                foreach (var entry in sessionData.Entries)
                {
                    entry.SessionId = sessionId;
                    await _database.AddEntry(entry, sessionId);
                }

                // Update UI
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SessionName = sessionData.SessionName;
                    Date = sessionData.Date;
                    Location = sessionData.Location;
                    
                    UnsubscribeAllEntries();
                    LogEntries.Clear();
                    _undoStack.Clear();
                    OnPropertyChanged(nameof(CanUndo));
                    foreach (var entry in sessionData.Entries)
                    {
                        LogEntries.Add(entry);
                        SubscribeToEntry(entry);
                    }
                    
                    _hasUnsavedChanges = false;
                    StatusMessage = $"Session loaded: {sessionData.SessionName}";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Error opening session: {ex.Message}";
                });
            }
        }

        private async Task SaveCurrentSession()
        {
            if (_currentSession == null) return;

            var entries = LogEntries.ToList();
            string filepath = await _sessionManager.SaveSessionToFile(_currentSession, entries);
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _hasUnsavedChanges = false;
                StatusMessage = $"Session saved: {Path.GetFileName(filepath)}";
            });
        }

        private async Task<bool> PromptToSaveSession()
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is 
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;
                    
                    if (mainWindow == null)
                    {
                        tcs.SetResult(true); // Default to saving if no window
                        return;
                    }
                    
                    var dialog = new Avalonia.Controls.Window
                    {
                        Title = "Save Session?",
                        Width = 350,
                        Height = 150,
                        WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                        Background = Avalonia.Media.Brush.Parse("#2d2d2d"),
                        CanResize = false
                    };
                    
                    bool result = false;
                    
                    var yesBtn = new Avalonia.Controls.Button 
                    { 
                        Content = "Save", Width = 80, Height = 32,
                        Background = Avalonia.Media.Brush.Parse("#22c55e"),
                        Foreground = Avalonia.Media.Brushes.White,
                        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
                    };
                    var noBtn = new Avalonia.Controls.Button 
                    { 
                        Content = "Don't Save", Width = 100, Height = 32,
                        Background = Avalonia.Media.Brush.Parse("#dc2626"),
                        Foreground = Avalonia.Media.Brushes.White,
                        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
                    };
                    
                    yesBtn.Click += (_, _) => { result = true; dialog.Close(); };
                    noBtn.Click += (_, _) => { result = false; dialog.Close(); };
                    
                    var panel = new Avalonia.Controls.StackPanel
                    {
                        Margin = new Avalonia.Thickness(20),
                        Spacing = 15
                    };
                    panel.Children.Add(new Avalonia.Controls.TextBlock 
                    { 
                        Text = "You have unsaved changes. Save before continuing?",
                        Foreground = Avalonia.Media.Brushes.White,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    });
                    
                    var btnPanel = new Avalonia.Controls.StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
                    };
                    btnPanel.Children.Add(noBtn);
                    btnPanel.Children.Add(yesBtn);
                    panel.Children.Add(btnPanel);
                    
                    dialog.Content = panel;
                    await dialog.ShowDialog(mainWindow);
                    
                    tcs.SetResult(result);
                });
                
                return await tcs.Task;
            }
            catch
            {
                return true; // Default to saving on error
            }
        }

        private async Task<string> ShowOpenSessionDialog(List<string> sessionFiles)
        {
            try
            {
                var tcs = new TaskCompletionSource<string>();
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is 
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;
                    
                    if (mainWindow == null)
                    {
                        tcs.SetResult(string.Empty);
                        return;
                    }
                    
                    var storageProvider = mainWindow.StorageProvider;
                    
                    // Try to get the sessions folder as a starting location
                    IStorageFolder? startFolder = null;
                    try
                    {
                        startFolder = await storageProvider.TryGetFolderFromPathAsync(_sessionManager.SessionsFolderPath);
                    }
                    catch { }
                    
                    var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "Open Session",
                        AllowMultiple = false,
                        SuggestedStartLocation = startFolder,
                        FileTypeFilter = new[]
                        {
                            new FilePickerFileType("Timecode Sessions") { Patterns = new[] { "*.tcsession" } },
                            new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                        }
                    });
                    
                    var selectedFile = files.FirstOrDefault();
                    tcs.SetResult(selectedFile?.TryGetLocalPath() ?? string.Empty);
                });
                
                return await tcs.Task;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task<string?> ShowSaveFileDialog(string defaultFilename, string filterName, string extension, string? title = null)
        {
            try
            {
                var tcs = new TaskCompletionSource<string?>();

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;

                    if (mainWindow == null)
                    {
                        tcs.SetResult(null);
                        return;
                    }

                    var storageProvider = mainWindow.StorageProvider;

                    // Try to get Documents folder as starting location
                    IStorageFolder? startFolder = null;
                    try
                    {
                        startFolder = await storageProvider.TryGetFolderFromPathAsync(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                    }
                    catch { }

                    var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = title ?? $"Export {filterName}",
                        SuggestedFileName = defaultFilename,
                        SuggestedStartLocation = startFolder,
                        FileTypeChoices = new[]
                        {
                            new FilePickerFileType(filterName) { Patterns = new[] { $"*.{extension}" } }
                        }
                    });

                    tcs.SetResult(file?.TryGetLocalPath());
                });

                return await tcs.Task;
            }
            catch
            {
                return null;
            }
        }

        public void ToggleGenerator()
        {
            try
            {
                if (_isGeneratorRunning)
                {
                    // Stop the generator
                    _audioManager.Stop();
                    _displayTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                    IsGeneratorRunning = false;
                    StatusMessage = "Generator stopped - Click GENERATE to restart";
                    Console.WriteLine("[Generator] Stopped");
                }
                else
                {
                    // Get current timecode and initialize LTC generator BEFORE starting
                    var timecode = _ntpClient.GetTimecodeString();
                    var parts = timecode.Split(':');
                    if (parts.Length == 4)
                    {
                        int hours = int.Parse(parts[0]);
                        int minutes = int.Parse(parts[1]);
                        int seconds = int.Parse(parts[2]);
                        int frames = int.Parse(parts[3]);
                        
                        // Set the timecode BEFORE starting audio
                        _audioManager.SetTime(hours, minutes, seconds, frames);
                    }
                    
                    // Now start the generator with correct timecode
                    _audioManager.Start();
                    _displayTimer?.Change(0, 100);
                    IsGeneratorRunning = true;
                    StatusMessage = "Generator running - Creating LTC timecode";
                    Console.WriteLine("[Generator] Started");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Console.WriteLine($"[Generator] Error: {ex.Message}");
            }
        }

        public async void TimeCodeIn()
        {
            try
            {
                if (_currentSession == null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = "No active session - create new session first";
                    });
                    return;
                }

                string timecode = CurrentTimecode;

                if (_currentEntry != null)
                {
                    _currentEntry.TimeCodeOut = timecode;
                    _currentEntry.Duration = CalculateDuration(_currentEntry.TimeCodeIn, timecode);
                    await _database.UpdateEntry(_currentEntry);

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsTimecodeInActive = false;
                    });

                    _currentEntry = null;
                }

                _currentEntry = new TimecodeLogEntry
                {
                    TimeCodeIn = timecode,
                    SessionId = _currentSession.Id
                };

                int entryId = await _database.AddEntry(_currentEntry, _currentSession.Id);
                _currentEntry.Id = entryId;

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LogEntries.Add(_currentEntry);
                    SubscribeToEntry(_currentEntry);
                    IsTimecodeInActive = true;
                    _hasUnsavedChanges = true;
                    StatusMessage = $"TC IN: {timecode}";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Error: {ex.Message}";
                });
            }
        }

        public async void TimeCodeOut()
        {
            try
            {
                if (_currentEntry == null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = "No active entry - press TC IN first";
                    });
                    return;
                }

                _currentEntry.TimeCodeOut = CurrentTimecode;
                _currentEntry.Duration = CalculateDuration(_currentEntry.TimeCodeIn, CurrentTimecode);

                await _database.UpdateEntry(_currentEntry);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsTimecodeInActive = false; // Stop flashing
                    StatusMessage = $"TC OUT: {CurrentTimecode} (Duration: {_currentEntry.Duration})";
                });
                
                _currentEntry = null;
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Error: {ex.Message}";
                });
            }
        }

        public async void TimeCodeMark()
        {
            try
            {
                if (_currentSession == null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = "No active session - create new session first";
                    });
                    return;
                }

                string currentTimecode = CurrentTimecode;

                if (_currentEntry != null)
                {
                    var markEntry = new TimecodeLogEntry
                    {
                        ParentEntryId = _currentEntry.Id,
                        MarkTimecode = currentTimecode,
                        Notes = "",
                        SessionId = _currentSession.Id
                    };

                    int entryId = await _database.AddEntry(markEntry, _currentSession.Id);
                    markEntry.Id = entryId;

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        LogEntries.Add(markEntry);
                        SubscribeToEntry(markEntry);
                        _hasUnsavedChanges = true;
                        StatusMessage = $"MARK added: {currentTimecode}";
                    });
                }
                else
                {
                    var markEntry = new TimecodeLogEntry
                    {
                        TimeCodeIn = currentTimecode,
                        MarkTimecode = currentTimecode,
                        Notes = "MARK",
                        SessionId = _currentSession.Id
                    };

                    int entryId = await _database.AddEntry(markEntry, _currentSession.Id);
                    markEntry.Id = entryId;

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        LogEntries.Add(markEntry);
                        SubscribeToEntry(markEntry);
                        _hasUnsavedChanges = true;
                        StatusMessage = $"MARK created: {currentTimecode}";
                    });
                }
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Error: {ex.Message}";
                });
            }
        }

        private async void DeleteEntry(TimecodeLogEntry entry)
        {
            try
            {
                if (_currentSession == null) return;

                // Collect everything being deleted: children first, then the entry itself
                var toDelete = new List<TimecodeLogEntry>();
                if (!entry.IsMarkSubRow)
                {
                    var children = LogEntries.Where(e => e.ParentEntryId == entry.Id).ToList();
                    toDelete.AddRange(children);
                }
                toDelete.Add(entry);

                // Delete from DB (children before parent)
                if (!entry.IsMarkSubRow)
                    await _database.DeleteChildEntries(entry.Id);
                await _database.DeleteEntry(entry.Id);

                // Remove from UI
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var e in toDelete)
                    {
                        e.PropertyChanged -= OnEntryPropertyChanged;
                        LogEntries.Remove(e);
                    }

                    // Clear _currentEntry if it was deleted
                    if (_currentEntry != null && toDelete.Contains(_currentEntry))
                    {
                        _currentEntry = null;
                        IsTimecodeInActive = false;
                    }

                    _undoStack.Push(toDelete);
                    OnPropertyChanged(nameof(CanUndo));
                    _hasUnsavedChanges = true;
                    StatusMessage = $"Deleted {toDelete.Count} row(s). Use File > Undo Delete to restore.";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Delete error: {ex.Message}";
                });
            }
        }

        private async void UndoDelete()
        {
            try
            {
                if (_undoStack.Count == 0) return;
                if (_currentSession == null) return;

                var toRestore = _undoStack.Peek();

                foreach (var entry in toRestore)
                    await _database.RestoreEntry(entry);

                // Reload entries from DB so ordering is correct
                var entries = await _database.GetSessionEntries(_currentSession.Id);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _undoStack.Pop();
                    UnsubscribeAllEntries();
                    LogEntries.Clear();
                    foreach (var e in entries)
                    {
                        LogEntries.Add(e);
                        SubscribeToEntry(e);
                    }
                    // Re-anchor _currentEntry to the new object instance from the DB reload
                    if (_currentEntry != null)
                        _currentEntry = LogEntries.FirstOrDefault(e => e.Id == _currentEntry.Id);
                    OnPropertyChanged(nameof(CanUndo));
                    _hasUnsavedChanges = true;
                    StatusMessage = $"Undo: restored {toRestore.Count} row(s).";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Undo error: {ex.Message}";
                });
            }
        }

        private string CalculateDuration(string tcIn, string tcOut)
        {
            try
            {
                var inParts = tcIn.Split(':');
                var outParts = tcOut.Split(':');

                if (inParts.Length == 4 && outParts.Length == 4)
                {
                    bool isDropFrame = _settings.SelectedFrameRate == "29.97 fps DF";
                    double fps = GetFrameRateValue(_settings.SelectedFrameRate);
                    int fpsInt = (int)Math.Round(fps);
                    
                    long inFrames = ParseTimecode(inParts, fpsInt, isDropFrame);
                    long outFrames = ParseTimecode(outParts, fpsInt, isDropFrame);
                    long durationFrames = outFrames - inFrames;

                    if (durationFrames < 0) durationFrames += TotalFramesInDay(fpsInt, isDropFrame);

                    return FormatTimecode(durationFrames, fpsInt, isDropFrame);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Duration] Calculation error: {ex.Message}");
            }

            return "00:00:00:00";
        }

        private long ParseTimecode(string[] parts, int fps, bool dropFrame)
        {
            int hours = int.Parse(parts[0]);
            int minutes = int.Parse(parts[1]);
            int seconds = int.Parse(parts[2]);
            int frames = int.Parse(parts[3]);

            if (!dropFrame)
            {
                return (hours * 60 * 60 * fps) + (minutes * 60 * fps) + (seconds * fps) + frames;
            }
            
            // Drop-frame: 29.97 DF skips frames 0 and 1 at the start of each minute
            // except every 10th minute (00, 10, 20, 30, 40, 50)
            // Total dropped frames = 2 * (totalMinutes - totalMinutes/10)
            long totalMinutes = (hours * 60) + minutes;
            long droppedFrames = 2 * (totalMinutes - totalMinutes / 10);
            long linearFrames = (hours * 3600L * fps) + (minutes * 60L * fps) + (seconds * fps) + frames;
            return linearFrames - droppedFrames;
        }

        private string FormatTimecode(long totalFrames, int fps, bool dropFrame)
        {
            if (!dropFrame)
            {
                int frames = (int)(totalFrames % fps);
                totalFrames /= fps;
                int seconds = (int)(totalFrames % 60);
                totalFrames /= 60;
                int minutes = (int)(totalFrames % 60);
                int hours = (int)(totalFrames / 60);
                return $"{hours:D2}:{minutes:D2}:{seconds:D2}:{frames:D2}";
            }
            
            // Reverse drop-frame calculation
            // Frames per 10 minutes = 10*60*30 - 9*2 = 17982
            int framesPerMin = 30 * 60; // 1800
            int framesPer10Min = framesPerMin * 10 - 9 * 2; // 17982
            
            int d = (int)(totalFrames / framesPer10Min);
            int m = (int)(totalFrames % framesPer10Min);
            
            int extraFrames;
            if (m < framesPerMin) // first minute of 10-minute block (no drop)
            {
                extraFrames = 0;
            }
            else
            {
                // Remaining minutes drop 2 frames each
                extraFrames = (m - framesPerMin) / (framesPerMin - 2);
                extraFrames = Math.Min(extraFrames, 8); // max 8 more minutes
            }
            
            totalFrames += 18 * d + 2 * extraFrames; // add back dropped frames
            
            int fr = (int)(totalFrames % fps);
            totalFrames /= fps;
            int sec = (int)(totalFrames % 60);
            totalFrames /= 60;
            int min = (int)(totalFrames % 60);
            int hr = (int)(totalFrames / 60);
            return $"{hr:D2}:{min:D2}:{sec:D2}:{fr:D2}";
        }
        
        private long TotalFramesInDay(int fps, bool dropFrame)
        {
            if (!dropFrame)
                return 24L * 60 * 60 * fps;
            // 24 hours * 6 ten-minute blocks * 17982 frames per 10-min block
            return 24L * 6 * 17982;
        }

        private double GetFrameRateValue(string frameRate)
        {
            // Returns the LTC AUDIO generation frame rate
            // For 29.97 fps modes, LTC audio always runs at 30 fps (2400 Hz)
            return frameRate switch
            {
                "24 fps" => 24.0,
                "25 fps" => 25.0,
                "29.97 fps DF" => 30.0,   // Audio at 30 fps
                "29.97 fps NDF" => 30.0,  // Audio at 30 fps
                "30 fps" => 30.0,
                _ => 30.0
            };
        }
        
        private double GetDisplayFrameRate(string frameRate)
        {
            // Returns the actual display/counting frame rate
            // For 29.97 modes, frames count at 29.97 fps for video sync
            return frameRate switch
            {
                "24 fps" => 24.0,
                "25 fps" => 25.0,
                "29.97 fps DF" => 29.97,   // Display at 29.97 fps
                "29.97 fps NDF" => 29.97,  // Display at 29.97 fps
                "30 fps" => 30.0,
                _ => 30.0
            };
        }

        public void SetFrameRate(string frameRate)
        {
            _settings.SelectedFrameRate = frameRate;
            _settings.Save();
            StatusMessage = $"Frame rate set to: {frameRate}";
            
            // Update NTP client with DISPLAY frame rate (for timecode values)
            var displayRate = GetDisplayFrameRate(frameRate);
            _ntpClient.SetFrameRate(displayRate);
            
            // Update LTC audio with AUDIO frame rate (for waveform generation)
            var audioRate = GetFrameRateValue(frameRate);
            _audioManager.SetFrameRate(audioRate);

            // Notify UI of frame rate changes
            OnPropertyChanged(nameof(IsFrameRate24));
            OnPropertyChanged(nameof(IsFrameRate25));
            OnPropertyChanged(nameof(IsFrameRate2997DF));
            OnPropertyChanged(nameof(IsFrameRate2997NDF));
            OnPropertyChanged(nameof(IsFrameRate30));
        }

        public void SetAudioOutput(string audioOutput)
        {
            _settings.SelectedAudioOutput = audioOutput;
            _settings.SelectedAsioDriver = null; // Clear ASIO driver when switching to System Default
            _settings.Save();
            StatusMessage = $"Audio output set to: {audioOutput}";

            // Reinitialize audio with new output
            var frameRate = GetFrameRateValue(_settings.SelectedFrameRate);
            _audioManager.Initialize(frameRate, audioOutput, _settings.SelectedAudioInput, _settings.SelectedNDISource);
            
            // Only restart if generator was already running
            if (_isGeneratorRunning)
            {
                _audioManager.Start();
            }

            // Notify UI of audio changes
            OnPropertyChanged(nameof(IsAudioOutputNone));
            OnPropertyChanged(nameof(IsAudioOutputSystemDefault));
            OnPropertyChanged(nameof(IsAudioOutputASIO));
        }

        public void SetAudioInput(string audioInput)
        {
            _settings.SelectedAudioInput = audioInput;
            _settings.Save();
            StatusMessage = $"Audio input set to: {audioInput}";

            // Reinitialize audio with new input
            var frameRate = GetFrameRateValue(_settings.SelectedFrameRate);
            _audioManager.Initialize(frameRate, _settings.SelectedAudioOutput, audioInput, _settings.SelectedNDISource);
            
            // Start if generator was running
            if (_isGeneratorRunning)
            {
                _audioManager.Start();
            }

            // Notify UI of audio changes
            OnPropertyChanged(nameof(IsAudioInputNone));
            OnPropertyChanged(nameof(IsAudioInputSystemDefault));
            OnPropertyChanged(nameof(IsAudioInputASIO));
            OnPropertyChanged(nameof(IsAudioInputNDI));
            OnPropertyChanged(nameof(GeneratorButtonText)); // Update button text
        }

        private void EnumerateAsioDrivers()
        {
            try
            {
                AvailableAsioDrivers.Clear();
                
                // NAudio's AsioOut can enumerate installed ASIO drivers
                var asioDriverNames = NAudio.Wave.AsioOut.GetDriverNames();
                
                foreach (var driverName in asioDriverNames)
                {
                    AvailableAsioDrivers.Add(driverName);
                }
                
                HasAsioDrivers = AvailableAsioDrivers.Count > 0;
                
                if (HasAsioDrivers)
                {
                    StatusMessage = $"Found {AvailableAsioDrivers.Count} ASIO driver(s)";
                }
                else
                {
                    StatusMessage = "No ASIO drivers found";
                }
            }
            catch (Exception ex)
            {
                HasAsioDrivers = false;
                StatusMessage = $"ASIO enumeration failed: {ex.Message}";
            }
        }

        public void SetAsioDriver(string asioDriver)
        {
            _settings.SelectedAudioOutput = "ASIO";
            _settings.SelectedAsioDriver = asioDriver;
            _settings.Save();
            StatusMessage = $"ASIO output driver set to: {asioDriver}";
        }
        
        public void SetAsioInputDriver(string asioDriver)
        {
            _settings.SelectedAudioInput = "ASIO";
            _settings.SelectedAsioInputDriver = asioDriver;
            _settings.Save();
            StatusMessage = $"ASIO input driver set to: {asioDriver}";
            
            // Reinitialize audio with new ASIO input
            var frameRate = GetFrameRateValue(_settings.SelectedFrameRate);
            _audioManager.Initialize(frameRate, _settings.SelectedAudioOutput, "ASIO", _settings.SelectedNDISource);
            
            if (_isGeneratorRunning)
            {
                _audioManager.Start();
            }
            
            // Notify UI of audio input changes
            OnPropertyChanged(nameof(IsAudioInputNone));
            OnPropertyChanged(nameof(IsAudioInputSystemDefault));
            OnPropertyChanged(nameof(IsAudioInputASIO));
            OnPropertyChanged(nameof(IsAudioInputNDI));
            OnPropertyChanged(nameof(GeneratorButtonText));
        }
        
        private void EnumerateNDISources()
        {
            try
            {
                AvailableNDISources.Clear();
                
                if (_audioManager?.NDI == null)
                {
                    HasNDISources = false;
                    return;
                }
                
                // Get discovered NDI sources
                var sources = _audioManager.NDI.DiscoveredSources;
                
                foreach (var source in sources)
                {
                    AvailableNDISources.Add(source);
                }
                
                HasNDISources = AvailableNDISources.Count > 0;
                
                if (HasNDISources)
                {
                    StatusMessage = $"Found {AvailableNDISources.Count} NDI source(s)";
                }
            }
            catch (Exception ex)
            {
                HasNDISources = false;
                StatusMessage = $"NDI source enumeration failed: {ex.Message}";
            }
        }
        
        private void OnNDISourcesUpdated()
        {
            // Re-enumerate sources when NDI detects changes
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                EnumerateNDISources();
            });
        }
        
        public void SetNDISource(string ndiSource)
        {
            _settings.SelectedAudioInput = "NDI Receive";
            _settings.SelectedNDISource = ndiSource;
            _settings.Save();
            StatusMessage = $"NDI source set to: {ndiSource}";
            
            // Reinitialize audio with new NDI source
            var frameRate = GetFrameRateValue(_settings.SelectedFrameRate);
            _audioManager.Initialize(frameRate, _settings.SelectedAudioOutput, _settings.SelectedAudioInput, ndiSource);
            
            // Start if generator was running
            if (_isGeneratorRunning)
            {
                _audioManager.Start();
            }
            
            // Notify UI of all audio input changes
            OnPropertyChanged(nameof(IsAudioInputNone));
            OnPropertyChanged(nameof(IsAudioInputSystemDefault));
            OnPropertyChanged(nameof(IsAudioInputASIO));
            OnPropertyChanged(nameof(IsAudioInputNDI));
            OnPropertyChanged(nameof(GeneratorButtonText));
        }
        
        public void ToggleNDIOutput()
        {
            if (!_audioManager.IsNDIAvailable)
            {
                StatusMessage = "NDI not available - install NDI Tools from https://ndi.tv/tools/";
                return;
            }
            
            if (_audioManager.IsNDIOutputEnabled)
            {
                _audioManager.EnableNDIOutput(false);
                StatusMessage = "NDI output disabled";
            }
            else
            {
                _audioManager.EnableNDIOutput(true);
                // Check if it actually enabled
                if (_audioManager.IsNDIOutputEnabled)
                {
                    StatusMessage = "NDI output enabled - broadcasting as 'StudioLog LTC'";
                }
                else
                {
                    StatusMessage = "Failed to enable NDI output";
                }
            }
            
            // Notify UI of NDI state change
            OnPropertyChanged(nameof(IsNDIOutputEnabled));
        }
        
        public void ToggleTestTone()
        {
            IsTestToneActive = !IsTestToneActive;
            _audioManager?.SetTestTone(IsTestToneActive);
            
            if (IsTestToneActive)
            {
                StatusMessage = "Test tone enabled (1kHz sine wave)";
            }
            else
            {
                StatusMessage = "Test tone disabled";
            }
        }

        public void SetClockSource(string clockSource)
        {
            _settings.SelectedClockSource = clockSource;
            
            // Reset timezone to UTC when switching to non-NTP sources
            if (clockSource != "NTP")
            {
                _settings.SelectedTimezoneId = "UTC";
            }
            
            _settings.Save();
            
            // Reinitialize clock with new source and timezone ID
            _ntpClient.SetClockSource(clockSource, _settings.SelectedTimezoneId);
            
            StatusMessage = $"Clock source set to: {clockSource}";

            // Notify UI of clock source changes
            OnPropertyChanged(nameof(IsClockSystemClock));
            OnPropertyChanged(nameof(IsClockNtp));
            OnPropertyChanged(nameof(IsClockFreeRun));
        }

        public void SetNtpTimezone(string timezoneId)
        {
            // timezoneId is a Windows TimeZoneInfo ID like "Central Standard Time"
            _settings.SelectedClockSource = "NTP";
            _settings.SelectedTimezoneId = timezoneId;
            _settings.Save();
            
            // Reinitialize clock with NTP and timezone ID
            _ntpClient.SetClockSource("NTP", timezoneId);
            
            // Get friendly display name
            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                StatusMessage = $"Timezone set to: {tz.DisplayName} (DST-aware)";
            }
            catch
            {
                StatusMessage = $"Timezone set to: {timezoneId}";
            }

            // Notify UI of clock source changes
            OnPropertyChanged(nameof(IsClockSystemClock));
            OnPropertyChanged(nameof(IsClockNtp));
            OnPropertyChanged(nameof(IsClockFreeRun));
        }

        public void OpenManual()
        {
            StatusMessage = "Opening user manual...";
            
            // Try to open manual file or URL
            try
            {
                var manualPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Manual.pdf"
                );

                if (System.IO.File.Exists(manualPath))
                {
                    // Open PDF with default application
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = manualPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    StatusMessage = "Manual opened";
                }
                else
                {
                    // No local manual found — inform user
                    StatusMessage = "Manual not found. Visit studiolog.app for documentation.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not open manual: {ex.Message}";
            }
        }

        public void OpenAbout()
        {
            var aboutWindow = new AboutWindow();
            
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
                && desktop.MainWindow != null)
            {
                aboutWindow.ShowDialog(desktop.MainWindow);
            }
            else
            {
                aboutWindow.Show();
            }
        }

        public void OpenContact()
        {
            StatusMessage = "Opening contact information...";
            
            try
            {
                // Open email client with pre-filled email
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "mailto:support@timecodelogger.com?subject=StudioLog Support",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                StatusMessage = "Email client opened";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not open email: {ex.Message}";
            }
        }

        public async void Exit()
        {
            try
            {
                // Check for unsaved changes
                if (_hasUnsavedChanges && _currentSession != null)
                {
                    bool shouldSave = await PromptToSaveSession();
                    if (shouldSave)
                    {
                        await SaveCurrentSession();
                    }
                }

                _settings.Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Exit] Error during shutdown: {ex.Message}");
                _settings.Save();
            }
            finally
            {
                // Use Avalonia's proper shutdown instead of Environment.Exit
                // This allows Dispose, OnClosed, and other lifecycle handlers to run
                if (Avalonia.Application.Current?.ApplicationLifetime is 
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown(0);
                }
                else
                {
                    Environment.Exit(0); // Fallback only if no desktop lifetime
                }
            }
        }

        private void SubscribeToEntry(TimecodeLogEntry entry)
        {
            entry.PropertyChanged += OnEntryPropertyChanged;
        }

        private void UnsubscribeAllEntries()
        {
            foreach (var entry in LogEntries)
                entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        private async void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not TimecodeLogEntry entry) return;
            if (entry.Id <= 0) return;
            if (e.PropertyName != nameof(TimecodeLogEntry.TimeCodeIn) &&
                e.PropertyName != nameof(TimecodeLogEntry.TimeCodeOut) &&
                e.PropertyName != nameof(TimecodeLogEntry.Notes)) return;

            if ((e.PropertyName == nameof(TimecodeLogEntry.TimeCodeIn) ||
                 e.PropertyName == nameof(TimecodeLogEntry.TimeCodeOut)) &&
                !string.IsNullOrEmpty(entry.TimeCodeIn) && !string.IsNullOrEmpty(entry.TimeCodeOut))
            {
                entry.Duration = CalculateDuration(entry.TimeCodeIn, entry.TimeCodeOut);
            }

            try
            {
                await _database.UpdateEntry(entry);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Entry updated: TC In {entry.TimeCodeIn}  TC Out {entry.TimeCodeOut}";
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Entry] Auto-save error: {ex.Message}");
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Auto-save failed: {ex.Message}";
                });
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            UnsubscribeAllEntries();

            // Unsubscribe from NDI events
            if (_audioManager?.NDI != null)
            {
                _audioManager.NDI.SourcesUpdated -= OnNDISourcesUpdated;
            }

            _displayTimer?.Dispose();
            _database?.Dispose();
            _audioManager?.Dispose();
            
            // Clean up shared NDI singleton on app exit
            LTCAudioManager.DisposeSharedNDI();

            _disposed = true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
