using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StudioLog.Core;

namespace StudioLog
{
    public partial class DriveFolderPickerDialog : Window
    {
        private readonly GoogleDriveManager _driveManager;

        private class BreadcrumbEntry
        {
            public string Name { get; set; } = string.Empty;
            public string? FolderId { get; set; } // null = Shared Drives root
            public string? DriveId { get; set; }
        }

        private readonly List<BreadcrumbEntry> _breadcrumb = new();
        private List<GoogleDriveManager.DriveFolder> _currentItems = new();

        public string? SelectedFolderId { get; private set; }
        public string? SelectedDriveId { get; private set; }
        public bool LoadFailed { get; private set; }
        public string? LoadError { get; private set; }

        public DriveFolderPickerDialog(GoogleDriveManager driveManager)
        {
            InitializeComponent();
            _driveManager = driveManager;
            Opened += async (_, _) => await InitializeAsync();
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            _breadcrumb.Clear();
            _breadcrumb.Add(new BreadcrumbEntry { Name = "Shared Drives", FolderId = null, DriveId = null });

            StatusText.Text = "Loading...";

            try
            {
                var production = await _driveManager.FindSharedDriveByNameAsync("Production");
                if (production != null)
                {
                    var artists = await _driveManager.FindChildFolderByNameAsync(production.Id, production.DriveId, "Artists");
                    if (artists != null)
                    {
                        _breadcrumb.Add(new BreadcrumbEntry { Name = production.Name, FolderId = production.Id, DriveId = production.DriveId });
                        _breadcrumb.Add(new BreadcrumbEntry { Name = artists.Name, FolderId = artists.Id, DriveId = artists.DriveId });
                    }
                }

                await LoadCurrentLevelAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load Google Drive: {ex.Message}";
                LoadFailed = true;
                LoadError = ex.Message;
            }
        }

        private async System.Threading.Tasks.Task LoadCurrentLevelAsync()
        {
            var current = _breadcrumb[^1];
            BreadcrumbText.Text = string.Join(" / ", _breadcrumb.Select(b => b.Name));
            Console.WriteLine($"[DriveFolderPickerDialog] Now browsing: {BreadcrumbText.Text} (FolderId={current.FolderId}, DriveId={current.DriveId})");
            UpButton.IsEnabled = _breadcrumb.Count > 1;
            SelectButton.IsEnabled = current.FolderId != null;
            StatusText.Text = "Loading...";

            try
            {
                _currentItems = current.FolderId == null
                    ? await _driveManager.ListSharedDrivesAsync()
                    : await _driveManager.ListChildFoldersAsync(current.FolderId, current.DriveId);

                FolderListBox.ItemsSource = _currentItems.Select(f => f.Name).ToList();
                StatusText.Text = _currentItems.Count == 0 ? "No folders here." : string.Empty;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load folder: {ex.Message}";
                _currentItems = new List<GoogleDriveManager.DriveFolder>();
                FolderListBox.ItemsSource = null;
                LoadFailed = true;
                LoadError = ex.Message;
            }
        }

        private async void FolderListBox_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (FolderListBox.SelectedIndex < 0 || FolderListBox.SelectedIndex >= _currentItems.Count) return;

            var selected = _currentItems[FolderListBox.SelectedIndex];
            var atSharedDrivesRoot = _breadcrumb[^1].FolderId == null;

            _breadcrumb.Add(new BreadcrumbEntry
            {
                Name = selected.Name,
                FolderId = atSharedDrivesRoot ? selected.DriveId : selected.Id,
                DriveId = selected.DriveId
            });

            await LoadCurrentLevelAsync();
        }

        private async void UpButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_breadcrumb.Count <= 1) return;
            _breadcrumb.RemoveAt(_breadcrumb.Count - 1);
            await LoadCurrentLevelAsync();
        }

        private void FolderListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Selecting a child folder does not affect whether "Select This Folder" is
            // enabled — that always applies to the current folder, not the highlighted child.
            SelectButton.IsEnabled = _breadcrumb[^1].FolderId != null;
        }

        private void SelectButton_Click(object? sender, RoutedEventArgs e)
        {
            // If a subfolder is highlighted in the list (but not double-clicked into), select
            // that highlighted folder directly rather than the folder currently being browsed —
            // otherwise a single click + Select silently targets the wrong (parent) folder.
            var atSharedDrivesRoot = _breadcrumb[^1].FolderId == null;
            if (!atSharedDrivesRoot && FolderListBox.SelectedIndex >= 0 && FolderListBox.SelectedIndex < _currentItems.Count)
            {
                var highlighted = _currentItems[FolderListBox.SelectedIndex];
                SelectedFolderId = highlighted.Id;
                SelectedDriveId = highlighted.DriveId;
            }
            else
            {
                var current = _breadcrumb[^1];
                SelectedFolderId = current.FolderId;
                SelectedDriveId = current.DriveId;
            }

            Console.WriteLine($"[DriveFolderPickerDialog] SelectButton_Click: SelectedFolderId={SelectedFolderId} SelectedDriveId={SelectedDriveId}");
            Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
    }
}
