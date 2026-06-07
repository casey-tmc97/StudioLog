using Avalonia.Controls;
using Avalonia.Input;
using StudioLog.ViewModels;
using System;
using System.Collections.Specialized;
using System.Windows.Input;

namespace StudioLog
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Handle Enter key on any TextBox in the window — commits edit and moves focus away
            AddHandler(KeyDownEvent, OnGlobalKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            // Populate ASIO menus when drivers are available
            _viewModel.AvailableAsioDrivers.CollectionChanged += OnAsioDriversChanged;
            PopulateAsioOutputMenu();
            PopulateAsioInputMenu();
            
            // Populate NDI sources menu when sources are available
            _viewModel.AvailableNDISources.CollectionChanged += OnNDISourcesChanged;
            PopulateNDIMenu();
            
            // Repopulate NDI menu when selection changes (to update checkmarks)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsAudioInputNDI))
            {
                PopulateNDIMenu();
            }
            // Refresh ASIO input menu checkmarks when input selection changes
            if (e.PropertyName == nameof(MainViewModel.IsAudioInputASIO))
            {
                PopulateAsioInputMenu();
            }
        }

        private void OnAsioDriversChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PopulateAsioOutputMenu();
            PopulateAsioInputMenu();
        }
        
        private void OnNDISourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PopulateNDIMenu();
        }

        private void PopulateAsioOutputMenu()
        {
            if (_viewModel == null) return;

            var asioMenuItem = this.FindControl<MenuItem>("AsioOutputMenuItem");
            if (asioMenuItem == null) return;

            asioMenuItem.Items.Clear();

            foreach (var driverName in _viewModel.AvailableAsioDrivers)
            {
                var menuItem = new MenuItem
                {
                    Header = driverName,
                    Command = _viewModel.SetAsioDriverCommand,
                    CommandParameter = driverName
                };
                asioMenuItem.Items.Add(menuItem);
            }
        }
        
        private void PopulateAsioInputMenu()
        {
            if (_viewModel == null) return;

            var asioMenuItem = this.FindControl<MenuItem>("AsioInputMenuItem");
            if (asioMenuItem == null) return;

            asioMenuItem.Items.Clear();

            foreach (var driverName in _viewModel.AvailableAsioDrivers)
            {
                var menuItem = new MenuItem
                {
                    Header = driverName,
                    Command = _viewModel.SetAsioInputDriverCommand,
                    CommandParameter = driverName,
                    Icon = _viewModel.IsAsioInputDriver(driverName) 
                        ? new Avalonia.Controls.TextBlock 
                          { 
                              Text = "✓", 
                              Foreground = Avalonia.Media.Brushes.White, 
                              FontWeight = Avalonia.Media.FontWeight.Bold, 
                              FontSize = 14 
                          } 
                        : null
                };
                asioMenuItem.Items.Add(menuItem);
            }
        }
        
        private void PopulateNDIMenu()
        {
            if (_viewModel == null) return;

            var ndiMenuItem = this.FindControl<MenuItem>("NDIReceiveMenuItem");
            if (ndiMenuItem == null) return;

            ndiMenuItem.Items.Clear();

            foreach (var sourceName in _viewModel.AvailableNDISources)
            {
                var menuItem = new MenuItem
                {
                    Header = sourceName,
                    Command = _viewModel.SetNDISourceCommand,
                    CommandParameter = sourceName,
                    Icon = _viewModel.IsNDISource(sourceName) ? "✓" : ""
                };
                
                ndiMenuItem.Items.Add(menuItem);
            }
        }

        private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                if (e.Source is TextBox textBox)
                {
                    // Force binding update by moving focus away
                    this.FocusManager?.ClearFocus();
                    e.Handled = true;
                }
            }
            else if (e.KeyModifiers == KeyModifiers.Control)
            {
                switch (e.Key)
                {
                    case Key.O:
                        _viewModel?.OpenSessionCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.S:
                        _viewModel?.SaveSessionCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.Z:
                        if (e.Source is not TextBox)
                        {
                            _viewModel?.UndoDeleteCommand.Execute(null);
                            e.Handled = true;
                        }
                        break;
                    case Key.E:
                        var fileMenuItem = this.FindControl<MenuItem>("FileMenuItem");
                        var exportMenuItem = this.FindControl<MenuItem>("ExportMenuItem");
                        if (fileMenuItem != null && exportMenuItem != null)
                        {
                            fileMenuItem.IsSubMenuOpen = true;
                            exportMenuItem.IsSubMenuOpen = true;
                        }
                        e.Handled = true;
                        break;
                    case Key.Q:
                        _viewModel?.ExitCommand.Execute(null);
                        e.Handled = true;
                        break;
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel?.Dispose();
            base.OnClosed(e);
        }
    }
}
